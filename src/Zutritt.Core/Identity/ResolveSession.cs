using System.Security.Principal;
using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>Direkte Mitglieder einer Gruppe; null = nicht lesbar. <c>Incomplete</c>: nur ein Teil ließ sich lesen.</summary>
internal readonly record struct MembersAnswer(IReadOnlyList<string>? Members, bool Incomplete);

/// <summary>
/// Eine Auflösungsrunde mit fester Verzeichnis-Kette und eigenem Zwischenspeicher. Jeder Lauf bekommt eine
/// neue Runde, damit gelöschte oder deaktivierte Konten und geänderte Mitgliedschaften nie aus einem alten
/// Zwischenspeicher kommen.
/// </summary>
internal sealed class ResolveSession
{
    // Fehler, die den ganzen Server betreffen: weitere Anfragen dort würden nur jeweils in die Zeitüberschreitung laufen.
    private static readonly HashSet<int> ServerDownErrors =
    [
        Native.ERROR_ACCESS_DENIED, Native.ERROR_BAD_NETPATH, 67 /* ERROR_BAD_NET_NAME */, 1203 /* ERROR_NO_NET_OR_BAD_PATH */,
        1231 /* ERROR_NETWORK_UNREACHABLE */, 1232 /* ERROR_HOST_UNREACHABLE */, Native.RPC_S_SERVER_UNAVAILABLE,
        Native.RPC_S_CALL_FAILED_DNE, 1753 /* EPT_S_NOT_REGISTERED */,
    ];

    private readonly IReadOnlyList<IDirectory> _chain;
    private readonly IReadOnlyList<IDirectory> _machineDirectories;
    private readonly IReadOnlyList<IAccountLookup> _lookups;
    private readonly IReadOnlyList<string> _unaskedServers;
    private readonly HashSet<IAccountLookup> _deadLookups = [];
    private readonly Dictionary<string, Principal> _principals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MembersAnswer> _members = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScanError> _problems = [];
    private readonly HashSet<string> _problemKeys = new(StringComparer.Ordinal);

    /// <param name="machineDirectories">Verzeichnisse der gescannten Rechner (lokale Kontendatenbanken), vorne in der Kette.</param>
    /// <param name="otherDirectories">Alle übrigen Verzeichnisse (AD, Attrappen) in der gewünschten Reihenfolge.</param>
    /// <param name="lookups">LSA der gescannten Rechner (und dieses Rechners) als Rückfall.</param>
    /// <param name="unaskedServers">
    /// Gescannte Server, deren LSA nicht unter <paramref name="lookups"/> ist. Für ihre lokalen Konten und Dienste
    /// beweist ein „nicht zugeordnet“ dieses Rechners nichts – solche SIDs bleiben dann „Unbekannt“.
    /// </param>
    public ResolveSession(IReadOnlyList<IDirectory> machineDirectories, IReadOnlyList<IDirectory> otherDirectories,
        IReadOnlyList<IAccountLookup> lookups, IReadOnlyList<string>? unaskedServers = null)
    {
        _machineDirectories = machineDirectories;
        _chain = [.. machineDirectories, .. otherDirectories];
        _lookups = lookups;
        _unaskedServers = unaskedServers ?? [];
    }

    /// <summary>Probleme mit Quellen (Server, Domäne) und Hinweise, als Fehler für den Lauf.</summary>
    public IReadOnlyList<ScanError> Problems => _problems;

    /// <summary>SID in die übliche Schreibweise bringen (S-1-…, Großbuchstaben); ungültige bleiben, wie sie sind.</summary>
    public static string Normalize(string sid)
    {
        var s = sid.Trim();
        try { return new SecurityIdentifier(s).Value; }
        catch (ArgumentException) { return s; }
    }

    public void Note(string path, string message)
    {
        if (_problemKeys.Add(path + "\n" + message)) _problems.Add(new ScanError(path, message));
    }

    /// <summary>Konto zu einer SID, immer als neue Kopie (der Aufrufer darf sie verändern).</summary>
    public Principal Resolve(string sid)
    {
        var key = Normalize(sid);
        if (!_principals.TryGetValue(key, out var p))
        {
            p = ResolveCore(key);
            _principals[key] = p;
        }
        return Clone(p);
    }

    public MembersAnswer GetMembers(string groupSid)
    {
        var key = Normalize(groupSid);
        if (!_members.TryGetValue(key, out var answer))
        {
            answer = WellKnownSids.IsBuiltin(key) && _machineDirectories.Count > 1 ? MergedBuiltinMembers(key) : MembersCore(key);
            _members[key] = answer;
        }
        return answer;
    }

    private Principal ResolveCore(string sid)
    {
        if (!IsValidSid(sid)) return Make(sid, sid, PrincipalKind.Unbekannt);
        if (WellKnownSids.TryGet(sid, out var knownName, out var knownKind))
            return Make(sid, knownName, knownKind, isLocal: WellKnownSids.IsBuiltin(sid));

        bool missing = false, unreachable = false;
        foreach (var directory in _chain)
        {
            var outcome = Ask(directory, sid);
            switch (outcome.State)
            {
                case LookupState.Gefunden:
                    return Adopt(outcome.Principal!, sid);
                case LookupState.NichtVorhanden:
                    missing = true;
                    break;
                case LookupState.NichtErreichbar:
                    unreachable = true;
                    break;
            }
        }

        var (lsa, server) = LookupLsa(sid);
        // Auch wenn ein Verzeichnis die SID nicht kennt: Findet Windows einen Namen (z. B. über die SID-Historie
        // eines migrierten Kontos), existiert das Konto.
        if (lsa.Status == LookupStatus.Gefunden) return FromLsa(sid, lsa, server);

        var nonAccount = WellKnownSids.NonAccountName(sid);
        if (nonAccount is not null) return Make(sid, nonAccount, PrincipalKind.Bekannt);
        // VORDEFINIERT-Gruppe, die es auf diesem Rechner nicht gibt (z. B. nur auf Servern): von Windows vergeben,
        // kein gelöschtes Konto – ein Löschhinweis wäre falsch.
        if (WellKnownSids.IsBuiltin(sid)) return Make(sid, sid, PrincipalKind.Unbekannt, isLocal: true);

        // Die zuständige Kontendatenbank war erreichbar und kennt das Konto nicht: sicher verwaist.
        if (missing) return Make(sid, sid, PrincipalKind.Verwaist);
        // Sonst nur dann verwaist, wenn nichts dagegen spricht – lieber „unbekannt“ als ein falscher Löschhinweis.
        if (unreachable || WellKnownSids.IsUnverifiable(sid) || lsa.Status != LookupStatus.NichtZugeordnet)
            return Make(sid, sid, PrincipalKind.Unbekannt);
        if (_unaskedServers.Count > 0)
        {
            // Lokale Konten und Dienste eines Servers kennt nur dessen eigene LSA, nicht die dieses Rechners.
            foreach (var unasked in _unaskedServers)
                Note(@"\\" + unasked, "Konten wurden nicht beim Server selbst nachgeschlagen. Nicht auflösbare Konten bleiben "
                    + "deshalb „Unbekannt“ statt „Verwaist“ – lokale Konten des Servers kennt nur der Server.");
            return Make(sid, sid, PrincipalKind.Unbekannt);
        }
        return Make(sid, sid, PrincipalKind.Verwaist);
    }

    private LookupOutcome Ask(IDirectory directory, string sid)
    {
        try
        {
            if (directory is IAuthoritativeDirectory authoritative)
            {
                var outcome = authoritative.Find(sid);
                if (outcome.State == LookupState.NichtErreichbar) Note(authoritative.SourceName, outcome.Error ?? "Verzeichnis nicht erreichbar.");
                if (outcome.State == LookupState.Gefunden && outcome.Principal is null) return LookupOutcome.NotResponsible;
                return outcome;
            }
            var p = directory.Lookup(sid);
            return p is null ? LookupOutcome.NotResponsible : LookupOutcome.Found(p);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ein fehlerhaftes Verzeichnis darf den Lauf nicht abbrechen; das Konto bleibt dann „unbekannt“.
            Note(SourceOf(directory), $"Verzeichnisfehler beim Auflösen von Konten: {ex.Message}");
            return LookupOutcome.Unreachable(ex.Message);
        }
    }

    private MembersOutcome AskMembers(IDirectory directory, string groupSid)
    {
        try
        {
            if (directory is IAuthoritativeDirectory authoritative)
            {
                var outcome = authoritative.FindMembers(groupSid);
                if (outcome.State == LookupState.NichtErreichbar) Note(authoritative.SourceName, outcome.Error ?? "Verzeichnis nicht erreichbar.");
                if (outcome.State == LookupState.Gefunden && outcome.Members is null) return MembersOutcome.NotResponsible;
                return outcome;
            }
            var members = directory.GetMembers(groupSid);
            return members is null ? MembersOutcome.NotResponsible : MembersOutcome.Found(members);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Note(SourceOf(directory), $"Verzeichnisfehler beim Lesen von Gruppenmitgliedern: {ex.Message}");
            return MembersOutcome.Unreachable(ex.Message);
        }
    }

    private MembersAnswer MembersCore(string groupSid)
    {
        foreach (var directory in _chain)
        {
            var outcome = AskMembers(directory, groupSid);
            if (outcome.State == LookupState.Gefunden) return new MembersAnswer(Clean(outcome.Members!), false);
        }
        return new MembersAnswer(null, false);
    }

    /// <summary>
    /// VORDEFINIERT-Gruppen bei einem Lauf über mehrere Server: Eine SID kann im Ergebnis nur einmal vorkommen,
    /// deshalb werden die Mitglieder zusammengefasst. Lieber eine Berechtigung zu viel zeigen als eine verschweigen.
    /// </summary>
    private MembersAnswer MergedBuiltinMembers(string groupSid)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perServer = new List<HashSet<string>>();
        var incomplete = false;
        foreach (var directory in _machineDirectories)
        {
            var outcome = AskMembers(directory, groupSid);
            switch (outcome.State)
            {
                case LookupState.Gefunden:
                    var set = new HashSet<string>(Clean(outcome.Members!), StringComparer.OrdinalIgnoreCase);
                    perServer.Add(set);
                    all.UnionWith(set);
                    break;
                case LookupState.NichtErreichbar:
                    incomplete = true;
                    break;
            }
        }
        if (perServer.Count == 0) return new MembersAnswer(null, false);
        if (perServer.Count > 1 && perServer.Skip(1).Any(s => !s.SetEquals(perServer[0])))
        {
            var label = Resolve(groupSid).Label;
            Note(label, "Die Gruppe hat auf den gescannten Servern verschiedene Mitglieder. Weil jede SID im Lauf nur einmal vorkommt, "
                + "sind die Mitglieder aller Server zusammengefasst – ein Zugriff über diese Gruppe gilt womöglich nur auf einem der Server.");
        }
        return new MembersAnswer(Sorted(all), incomplete);
    }

    private (AccountLookupResult Result, string? Server) LookupLsa(string sid)
    {
        // Ohne irgendeine LSA lässt sich nichts prüfen – dann nie „verwaist“.
        var error = _lookups.Count == 0;
        var lastError = 0;
        foreach (var lookup in _lookups)
        {
            if (_deadLookups.Contains(lookup))
            {
                error = true;
                continue;
            }
            AccountLookupResult r;
            try
            {
                r = lookup.Lookup(sid);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Note(LsaSource(lookup.Server), $"Kontennamen nicht auflösbar: {ex.Message}. Betroffene Konten bleiben „Unbekannt“.");
                _deadLookups.Add(lookup);
                error = true;
                continue;
            }
            if (r.Status == LookupStatus.Gefunden) return (r, lookup.Server);
            if (r.Status == LookupStatus.Fehler)
            {
                error = true;
                lastError = r.Error;
                Note(LsaSource(lookup.Server), $"Kontennamen nicht auflösbar: {Native.Describe(r.Error)}. Betroffene Konten bleiben „Unbekannt“.");
                if (ServerDownErrors.Contains(r.Error)) _deadLookups.Add(lookup);
            }
        }
        var status = error ? LookupStatus.Fehler : LookupStatus.NichtZugeordnet;
        return (new AccountLookupResult(status, "", "", Native.SidNameUse.Unknown, lastError), null);
    }

    private static Principal FromLsa(string sid, AccountLookupResult r, string? server)
    {
        // Virtuelle Konten (Window Manager\DWM-1, Font Driver Host\UMFD-0 …) meldet Windows teils als Benutzer –
        // als Person gezählt, ergäben sie Fehlalarme wie „Recht direkt an Person vergeben“.
        var kind = WellKnownSids.IsVirtualAccount(sid) && r.Kind is PrincipalKind.Benutzer or PrincipalKind.Gruppe or PrincipalKind.Unbekannt
            ? PrincipalKind.Bekannt
            : r.Kind;
        var p = Make(sid, r.FullName, kind);
        p.DisplayName = r.Name;
        if (p.Kind == PrincipalKind.Verwaist)
        {
            p.Name = sid;
            p.DisplayName = "";
        }
        // Lokales Konto: Windows nennt als „Domäne“ den Rechnernamen der Kontendatenbank.
        var machine = (server ?? Environment.MachineName).Split('.')[0];
        p.IsLocal = WellKnownSids.IsBuiltin(sid) || string.Equals(r.Domain, machine, StringComparison.OrdinalIgnoreCase);
        return p;
    }

    private static Principal Adopt(Principal found, string sid) => new()
    {
        Sid = sid,
        Name = found.Name,
        DisplayName = found.DisplayName,
        Kind = found.Kind,
        IsLocal = found.IsLocal,
        IsDisabled = found.IsDisabled,
    };

    private static Principal Clone(Principal p) => Adopt(p, p.Sid);

    private static Principal Make(string sid, string name, PrincipalKind kind, bool isLocal = false) => new()
    {
        Sid = sid,
        Name = name,
        // Bei Verwaisten und Unbekannten steht die SID im Namen; einen Anzeigenamen gibt es dann nicht.
        DisplayName = kind is PrincipalKind.Verwaist or PrincipalKind.Unbekannt ? "" : name.Split('\\')[^1],
        Kind = kind,
        IsLocal = isLocal,
    };

    private static List<string> Clean(IEnumerable<string> members) =>
        Sorted(members.Where(m => !string.IsNullOrWhiteSpace(m)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase));

    private static List<string> Sorted(IEnumerable<string> sids)
    {
        var list = sids.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static bool IsValidSid(string sid)
    {
        try
        {
            _ = new SecurityIdentifier(sid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string LsaSource(string? server) => @"\\" + (server ?? Environment.MachineName);

    private static string SourceOf(IDirectory directory) => directory switch
    {
        IAuthoritativeDirectory a => a.SourceName,
        IMachineDirectory m => @"\\" + (m.Server ?? Environment.MachineName),
        _ => directory.GetType().Name,
    };
}
