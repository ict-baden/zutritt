using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Zugriffe einer Person zusammengefasst – ohne die einzelnen Ordner anzulegen.</summary>
/// <param name="Folders">Gespeicherte Ordner mit mindestens Auflisten.</param>
/// <param name="Highest">Höchstes effektives Recht.</param>
/// <param name="Via">Alle Gruppen, die in einer Gruppenkette vorkommen (ohne Reihenfolge, ohne Doppelte).</param>
/// <param name="HasDirect">Mindestens ein Zugriff kommt direkt (ohne Gruppe).</param>
internal readonly record struct PersonAccessSummary(int Folders, AccessLevel Highest, IReadOnlyCollection<string> Via, bool HasDirect);

/// <summary>
/// „Wo darf wer überall hin?“ für viele Personen auf einmal – dasselbe Ergebnis wie
/// <see cref="EffectiveAccess.ForPerson(ScanResult, string)"/> je Person, aber ohne Personen × Ordner durchzurechnen.
/// </summary>
/// <remarks>
/// Bei 3.000 Personen und 50.000 Ordnern wären das 150 Millionen Rechteprüfungen und rund 50 Millionen Zugriffe.
/// Zwei Abkürzungen machen daraus wenige Millionen Prüfungen ohne die Einzelzugriffe:
/// <list type="bullet">
/// <item>Ordner mit gleicher Wirkung (<see cref="AccessContext.EffectKey"/>: gleiche wirksame Einträge, Freigabe,
/// Zugriffsweg) geben jeder Person dasselbe Recht – sie werden einmal je Klasse gerechnet und dann gezählt. Die vielen
/// Unterordner, die nur eine Gruppe mehr eintragen als ihre Geschwister, fallen so zusammen.</item>
/// <item>Gerechnet wird je Klasse nur für Personen, in deren Token eine SID eines Erlauben-Eintrags steht
/// (<see cref="AccessContext.GrantingSids"/>). Alle anderen haben dort sicher keinen Zugriff.</item>
/// </list>
/// Die einzelnen Zugriffe einer Person entstehen erst auf Anfrage (<see cref="Grants"/>) – ein Bericht braucht sie
/// nur für einen Teil der Personen.
/// </remarks>
internal sealed class PeopleAccess
{
    private readonly AccessContext _ctx;
    private readonly List<string> _people;
    private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EffectClass> _classes = [];
    private readonly int[] _count;
    private readonly AccessLevel[] _highest;
    private readonly bool[] _direct;
    private readonly HashSet<string>?[] _via;
    /// <summary>Klassen, in denen die Person Zugriff hat (für <see cref="Grants"/>).</summary>
    private readonly List<int>?[] _granted;

    /// <summary>Ordner gleicher Wirkung: einer steht für alle (Indizes in <see cref="ScanResult.Folders"/>, aufsteigend).</summary>
    private sealed class EffectClass(FolderNode representative)
    {
        public FolderNode Representative { get; } = representative;
        public LogonKind Logon { get; } = AccessContext.LogonFor(representative);
        public List<int> Folders { get; } = [];
    }

    private PeopleAccess(AccessContext ctx, List<string> people)
    {
        _ctx = ctx;
        _people = people;
        for (int i = 0; i < people.Count; i++) _index.TryAdd(people[i], i);
        _count = new int[people.Count];
        _highest = new AccessLevel[people.Count];
        _direct = new bool[people.Count];
        _via = new HashSet<string>?[people.Count];
        _granted = new List<int>?[people.Count];
    }

    /// <summary>Rechnet die Zugriffe der angegebenen Personen (SIDs, Doppelte zählen einmal) auf alle gespeicherten Ordner.</summary>
    public static PeopleAccess Compute(AccessContext ctx, IEnumerable<string> people, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(people);
        var list = people.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new PeopleAccess(ctx, list);
        result.Run(ct);
        return result;
    }

    private void Run(CancellationToken ct)
    {
        var folders = _ctx.Scan.Folders;
        var classOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < folders.Count; i++)
        {
            // Nicht lesbare Ordner zählen nirgends (wie bei ForPerson)
            if (folders[i].Error is not null) continue;
            var key = _ctx.EffectKey(folders[i]);
            if (!classOf.TryGetValue(key, out int c))
            {
                classOf[key] = c = _classes.Count;
                _classes.Add(new EffectClass(folders[i]));
            }
            _classes[c].Folders.Add(i);
        }
        if (_people.Count == 0 || _classes.Count == 0) return;

        // Umgekehrter Index: welche Personen haben eine SID im Token – je Zugriffsweg, denn NETZWERK bzw. INTERAKTIV
        // stehen nur im passenden Token
        var holders = new Dictionary<LogonKind, Dictionary<string, List<int>>>();
        foreach (var logon in _classes.Select(c => c.Logon).Distinct())
        {
            var bySid = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int p = 0; p < _people.Count; p++)
            {
                foreach (var sid in _ctx.TokenOf(_people[p], logon).Sids)
                {
                    if (!bySid.TryGetValue(sid, out var who)) bySid[sid] = who = [];
                    who.Add(p);
                }
            }
            holders[logon] = bySid;
        }

        // Je Person die Klassen, in denen sie ein Erlauben-Eintrag treffen kann; „schon vorgemerkt“ per Stempel
        var candidates = new List<int>?[_people.Count];
        var stamp = new int[_people.Count];
        for (int c = 0; c < _classes.Count; c++)
        {
            ct.ThrowIfCancellationRequested();
            var bySid = holders[_classes[c].Logon];
            int generation = c + 1;
            foreach (var sid in _ctx.GrantingSids(_classes[c].Representative))
            {
                if (!bySid.TryGetValue(sid, out var who)) continue;
                foreach (var p in who)
                {
                    if (stamp[p] == generation) continue;
                    stamp[p] = generation;
                    (candidates[p] ??= []).Add(c);
                }
            }
        }

        // Personen unabhängig voneinander parallel rechnen. Alle Zwischenspeicher des Kontexts sind jetzt gefüllt (Token
        // oben, vorbereitete Einträge über EffectKey) – die Auswertung liest nur noch, und jede Person schreibt nur ihre Felder.
        Parallel.For(0, _people.Count, new ParallelOptions { CancellationToken = ct }, p =>
        {
            if (candidates[p] is { } classes) Evaluate(p, classes);
        });
    }

    private void Evaluate(int person, List<int> classes)
    {
        var sid = _people[person];
        int count = 0;
        var highest = AccessLevel.Keine;
        bool direct = false;
        HashSet<string>? via = null;
        List<int>? granted = null;
        foreach (var c in classes)
        {
            var cls = _classes[c];
            var grant = EffectiveAccess.ForToken(_ctx, cls.Representative, sid, cls.Logon);
            if (grant.Effective < AccessLevel.Auflisten) continue;
            count += cls.Folders.Count;
            if (grant.Effective > highest) highest = grant.Effective;
            if (grant.Via.Count == 0) direct = true;
            else (via ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(grant.Via);
            (granted ??= []).Add(c);
        }
        _count[person] = count;
        _highest[person] = highest;
        _direct[person] = direct;
        _via[person] = via;
        _granted[person] = granted;
    }

    /// <summary>Summen für die Person; eine unbekannte SID hat keinen Zugriff.</summary>
    public PersonAccessSummary Summary(string sid)
    {
        if (!_index.TryGetValue(sid, out int p)) return new PersonAccessSummary(0, AccessLevel.Keine, [], false);
        return new PersonAccessSummary(_count[p], _highest[p], (IReadOnlyCollection<string>?)_via[p] ?? [], _direct[p]);
    }

    /// <summary>
    /// Die einzelnen Zugriffe der Person (mindestens Auflisten), in der Reihenfolge von <see cref="ScanResult.Folders"/> –
    /// genau die Einträge, die <see cref="EffectiveAccess.ForPerson(ScanResult, string)"/> liefert.
    /// </summary>
    public IReadOnlyList<AccessGrant> Grants(string sid)
    {
        if (!_index.TryGetValue(sid, out int p) || _granted[p] is not { } classes) return [];
        var folders = _ctx.Scan.Folders;
        var grants = new List<(int Index, AccessGrant Grant)>(_count[p]);
        foreach (var c in classes)
        {
            var cls = _classes[c];
            var grant = EffectiveAccess.ForToken(_ctx, cls.Representative, _people[p], cls.Logon);
            foreach (var i in cls.Folders)
                grants.Add((i, ReferenceEquals(folders[i], cls.Representative) ? grant : grant with { Path = folders[i].Path }));
        }
        grants.Sort((a, b) => a.Index.CompareTo(b.Index));
        return grants.Select(g => g.Grant).ToList();
    }
}
