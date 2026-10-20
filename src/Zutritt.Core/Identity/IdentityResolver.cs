using Zutritt.Core.Model;

using Zutritt.Core.Resources;
namespace Zutritt.Core.Identity;

/// <summary>
/// Löst SIDs zu Konten auf, erkennt bekannte Prinzipale und verwaiste SIDs und füllt Gruppen bis zur
/// Person auf. Fragt die Verzeichnisse der Reihe nach und merkt sich Ergebnisse.
/// </summary>
/// <remarks>
/// Reihenfolge je SID: bekannte SIDs (feste deutsche Namen) → Verzeichnisse der Reihe nach → Kontenauflösung des
/// Systems (unter Windows die LSA, siehe <c>Zutritt.Platform.Windows.WindowsPlatform</c>).
/// „Verwaist“ heißt: die zuständige Stelle war erreichbar und kennt das Konto nicht (ERROR_NONE_MAPPED).
/// Ist eine Domäne oder ein Server nicht erreichbar, bleibt das Konto „Unbekannt“ – ein falsches „verwaist“
/// würde im Audit dazu verleiten, gültige Berechtigungen zu löschen.
/// </remarks>
public sealed class IdentityResolver : IDisposable
{
    /// <summary>Standard für die maximale Verschachtelungstiefe von Gruppen.</summary>
    public const int DefaultMaxGroupDepth = 20;

    // Einzelabfragen über Resolve() dürfen kurz zwischengespeichert werden; Läufe bekommen immer frische Daten.
    private static readonly TimeSpan AdHocCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly List<IDirectory> _directories;
    private readonly Func<string?, IAccountLookup>? _lookupFactory;
    private readonly Func<string?, IDirectory>? _machineDirectoryFactory;
    private readonly bool _askScannedServers;
    private readonly List<IDisposable> _owned;
    private readonly int _maxGroupDepth = DefaultMaxGroupDepth;
    private readonly object _gate = new();
    private ResolveSession? _adHoc;
    private DateTime _adHocCreatedUtc;

    /// <summary>
    /// Resolver mit genau diesen Verzeichnissen, ohne Kontenauflösung des Systems: SIDs, die kein Verzeichnis kennt,
    /// bleiben „Unbekannt“ (nie „Verwaist“). Für Läufe die Plattform fragen (<see cref="Platform.IScanPlatform.CreateIdentityResolver"/>) –
    /// unter Windows kommen dann die LSA dieses Rechners sowie Kontendatenbank und LSA jedes gescannten Servers dazu.
    /// </summary>
    public IdentityResolver(IEnumerable<IDirectory> directories)
        : this(directories, lookupFactory: null, machineDirectoryFactory: null, askScannedServers: false, owned: []) { }

    /// <param name="lookupFactory">Kontenauflösung eines Servers (null = dieser Rechner); ohne: keine.</param>
    /// <param name="machineDirectoryFactory">Kontendatenbank für gescannte Rechner, die kein Verzeichnis in der Liste haben.</param>
    /// <param name="askScannedServers">Auch die LSA der gescannten Server fragen (nicht nur dieses Rechners).</param>
    /// <param name="owned">Vom Resolver selbst angelegte Verzeichnisse, die er beim Dispose schließt.</param>
    internal IdentityResolver(IEnumerable<IDirectory> directories, Func<string?, IAccountLookup>? lookupFactory,
        Func<string?, IDirectory>? machineDirectoryFactory, bool askScannedServers, IEnumerable<IDisposable> owned)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _directories = [.. directories];
        if (_directories.Any(d => d is null)) throw new ArgumentException("Die Liste der Verzeichnisse enthält einen leeren Eintrag.", nameof(directories));
        _lookupFactory = lookupFactory;
        _machineDirectoryFactory = machineDirectoryFactory;
        _askScannedServers = askScannedServers;
        _owned = [.. owned];
    }

    /// <summary>Zeitlimit je Anfrage an ein Verzeichnis oder eine Kontenauflösung (siehe <see cref="ResolveSession.CallTimeout"/>).</summary>
    internal TimeSpan CallTimeout { get; init; } = Platform.TimeBudget.Directory;

    /// <summary>
    /// Maximale Verschachtelungstiefe beim Auflösen von Gruppen (Gruppen in der ACL = Tiefe 0). Tiefer liegende
    /// Gruppen werden mit <see cref="Principal.MembersUnknown"/> markiert und als Hinweis im Lauf vermerkt.
    /// </summary>
    public int MaxGroupDepth
    {
        get => _maxGroupDepth;
        init => _maxGroupDepth = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Die Tiefe darf nicht negativ sein.");
    }

    /// <summary>
    /// Konto zu einer SID, ohne Gruppenmitglieder. Ungültige oder nicht auflösbare SIDs ergeben keinen
    /// Fehler, sondern <see cref="PrincipalKind.Unbekannt"/> bzw. <see cref="PrincipalKind.Verwaist"/>.
    /// </summary>
    public Principal Resolve(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        lock (_gate)
        {
            if (_adHoc is null || DateTime.UtcNow - _adHocCreatedUtc > AdHocCacheLifetime)
            {
                var machine = _directories.Where(d => d is IMachineDirectory).ToList();
                var others = _directories.Where(d => d is not IMachineDirectory).ToList();
                _adHoc = new ResolveSession(machine, others, _lookupFactory is null ? [] : [_lookupFactory(null)]) { CallTimeout = CallTimeout };
                _adHocCreatedUtc = DateTime.UtcNow;
            }
            return _adHoc.Resolve(sid);
        }
    }

    /// <summary>Alle SIDs in <paramref name="result"/> auflösen und Gruppen rekursiv expandieren (in result.Principals).</summary>
    /// <remarks>
    /// Baut <see cref="ScanResult.Principals"/> neu auf (erst am Ende, ein Abbruch lässt das Ergebnis unverändert).
    /// Ohne <paramref name="expandGroups"/> sind alle Gruppen als <see cref="Principal.MembersUnknown"/> markiert –
    /// sie sind ja nicht leer, nur nicht gelesen. Probleme mit Servern oder Domänen landen in <see cref="ScanResult.Errors"/>.
    /// </remarks>
    public void ResolveAll(ScanResult result, bool expandGroups, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ct.ThrowIfCancellationRequested();

        var session = CreateSession(MachineNames.Of(result));
        var principals = new Dictionary<string, Principal>(StringComparer.OrdinalIgnoreCase);
        // Breitensuche: jede Gruppe wird auf ihrer kürzesten Tiefe genau einmal aufgeklappt – das erkennt Zyklen
        // und begrenzt die Tiefe ohne Rekursion.
        var pending = new Queue<(Principal Group, int Depth)>();

        void Add(string sid, int depth)
        {
            // Schneller Vorabtest: Bei 100.000 Ordnern kommt dieselbe SID hunderttausendfach vor, Normalize kostet je
            // einen Win32-Aufruf. Die Schlüssel vergleichen ohne Groß/Klein, das deckt die übliche Schreibweise ab.
            if (principals.ContainsKey(sid)) return;
            var key = ResolveSession.Normalize(sid);
            if (key.Length == 0 || principals.ContainsKey(key)) return;
            var p = session.Resolve(key);
            principals[key] = p;
            if (p.Kind == PrincipalKind.Gruppe) pending.Enqueue((p, depth));
        }

        foreach (var sid in CollectSids(result))
        {
            ct.ThrowIfCancellationRequested();
            Add(sid, 0);
        }

        while (pending.TryDequeue(out var item))
        {
            ct.ThrowIfCancellationRequested();
            var (group, depth) = item;
            if (!expandGroups)
            {
                group.MembersUnknown = true;
                continue;
            }
            if (depth >= MaxGroupDepth)
            {
                group.MembersUnknown = true;
                session.Note(group.Label, Texts.Format(Strings.Identity_GroupsTooDeep, MaxGroupDepth));
                continue;
            }

            var answer = session.GetMembers(group.Sid);
            if (answer.Members is null)
            {
                group.MembersUnknown = true;
                continue;
            }
            group.Members.AddRange(answer.Members);
            group.MembersUnknown = answer.Incomplete;
            foreach (var member in answer.Members)
            {
                ct.ThrowIfCancellationRequested();
                Add(member, depth + 1);
            }
        }

        result.Principals.Clear();
        foreach (var (sid, p) in principals) result.Principals[sid] = p;
        // Ein zweiter Lauf über dasselbe Ergebnis darf dieselben Hinweise nicht noch einmal anhängen.
        var known = new HashSet<ScanError>(result.Errors);
        foreach (var problem in session.Problems)
            if (known.Add(problem)) result.Errors.Add(problem);
    }

    /// <summary>Schließt Verzeichnisse, die der Resolver selbst angelegt hat (z. B. die AD-Verbindung).</summary>
    public void Dispose()
    {
        foreach (var d in _owned) d.Dispose();
    }

    private ResolveSession CreateSession(IReadOnlyList<string?> machines)
    {
        var wanted = new HashSet<string>(machines.Select(m => m ?? ""), StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var machineDirectories = new List<IDirectory>();
        var others = new List<IDirectory>();

        foreach (var directory in _directories)
        {
            if (directory is IMachineDirectory md)
            {
                // Kontendatenbanken anderer Rechner bleiben draußen: ihre VORDEFINIERT-Gruppen haben dieselben
                // SIDs, aber andere Mitglieder, und würden sonst falsche Berechtigungen vortäuschen.
                var key = MachineNames.Normalize(md.Server) ?? "";
                if (wanted.Contains(key) && covered.Add(key)) machineDirectories.Add(directory);
            }
            else
            {
                others.Add(directory);
            }
        }

        if (_machineDirectoryFactory is not null)
        {
            foreach (var machine in machines)
                if (covered.Add(machine ?? "")) machineDirectories.Add(_machineDirectoryFactory(machine));
        }

        var lookups = new List<IAccountLookup>();
        var unasked = new List<string>();
        foreach (var machine in machines)
        {
            if (machine is null) continue;
            if (_askScannedServers && _lookupFactory is not null) lookups.Add(_lookupFactory(machine));
            else unasked.Add(machine);
        }
        // Die LSA dieses Rechners zuletzt: Sie kennt die eigene Domäne auch dann, wenn ein Server nicht antwortet.
        if (_lookupFactory is not null) lookups.Add(_lookupFactory(null));

        return new ResolveSession(machineDirectories, others, lookups, unasked) { CallTimeout = CallTimeout };
    }

    private static IEnumerable<string> CollectSids(ScanResult result)
    {
        foreach (var folder in result.Folders)
        {
            if (!string.IsNullOrWhiteSpace(folder.OwnerSid)) yield return folder.OwnerSid;
            foreach (var ace in folder.Aces)
                if (!string.IsNullOrWhiteSpace(ace.Sid)) yield return ace.Sid;
        }
        foreach (var share in result.Shares)
            foreach (var ace in share.Permissions)
                if (!string.IsNullOrWhiteSpace(ace.Sid)) yield return ace.Sid;
    }
}
