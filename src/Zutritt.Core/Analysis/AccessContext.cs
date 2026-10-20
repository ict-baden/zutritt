// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Wie die Person zugreift – bestimmt, ob NETZWERK oder INTERAKTIV im Zugriffstoken steht.</summary>
internal enum LogonKind
{
    /// <summary>Am Rechner selbst (Ordner liegt unter keiner Freigabe).</summary>
    Local,
    /// <summary>Über eine SMB-Freigabe.</summary>
    Network,
}

/// <summary>Ergebnis einer Rechteprüfung auf einen Knoten.</summary>
/// <param name="Granted">Gewährte Dateirechte nach Verweigerungen.</param>
/// <param name="Allowed">Alle passenden Erlauben-Rechte vor Verweigerungen.</param>
/// <param name="BestAceSid">SID des Eintrags, der das höchste Recht beiträgt (für „über welche Gruppen“).</param>
internal readonly record struct AccessResult(FileRights Granted, FileRights Allowed, string? BestAceSid)
{
    public AccessLevel Level => Rights.ToLevel(Granted);
    public bool ReducedByDeny => (Allowed & ~Granted) != 0;
}

/// <summary>
/// Gruppen eines Kontos (transitiv) samt kürzestem Weg dorthin. Der Weg erklärt später „über welche Gruppen“.
/// </summary>
internal sealed class Membership
{
    private readonly Dictionary<string, (string? Parent, int Depth)> _nodes;

    public Membership(string start, Dictionary<string, (string? Parent, int Depth)> nodes)
    {
        Start = start;
        _nodes = nodes;
    }

    public string Start { get; }
    public IReadOnlyCollection<string> Sids => _nodes.Keys;
    public bool Contains(string sid) => _nodes.ContainsKey(sid);
    public int DistanceTo(string sid) => _nodes.TryGetValue(sid, out var n) ? n.Depth : int.MaxValue;

    /// <summary>Kette vom Konto (ausgeschlossen) bis zur Gruppe (eingeschlossen); leer, wenn die SID das Konto selbst ist.</summary>
    public IReadOnlyList<string> PathTo(string sid)
    {
        if (!_nodes.TryGetValue(sid, out var node) || node.Parent is null) return [];
        var chain = new List<string>();
        var current = sid;
        // Tiefe begrenzt die Schleife zusätzlich – Elternverweise bilden durch die Breitensuche nie einen Kreis
        for (int guard = node.Depth; guard > 0 && _nodes.TryGetValue(current, out node) && node.Parent is not null; guard--)
        {
            chain.Add(current);
            current = node.Parent;
        }
        chain.Reverse();
        return chain;
    }
}

/// <summary>
/// Auswertung eines Laufs: Mitgliedschafts-Index, Freigaben und zwischengespeicherte Zugriffstoken. Einmal pro
/// Aufruf bzw. Vergleich anlegen – nicht über Änderungen am <see cref="ScanResult"/> hinweg aufheben.
/// </summary>
internal sealed class AccessContext
{
    private readonly Dictionary<string, List<string>> _directGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShareInfo> _shares = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Sid, LogonKind Logon), Membership> _tokens = new(TokenKeyComparer.Instance);
    private readonly Dictionary<string, Membership> _groups = new(StringComparer.OrdinalIgnoreCase);
    // Je Knoten einmal sortiert und abgebildet – der Vergleich prüft jeden Knoten für jede Person
    private readonly Dictionary<FolderNode, PreparedAce[]> _prepared = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, FolderNode>? _stored;

    /// <param name="scan">Der Lauf.</param>
    /// <param name="useMembership">
    /// false: Gruppenmitgliedschaften aus <see cref="Principal.Members"/> ignorieren. Für den Vergleich mit einem Lauf
    /// ohne Gruppenauflösung – dort fehlen alle Mitgliedschaften, beide Seiten müssen gleich wenig wissen.
    /// </param>
    public AccessContext(ScanResult scan, bool useMembership = true)
    {
        Scan = scan;
        if (useMembership)
        {
            foreach (var group in scan.Principals.Values)
            {
                foreach (var member in group.Members)
                {
                    if (string.IsNullOrEmpty(member)) continue;
                    // Doppelte Einträge sind unschädlich – die Breitensuche besucht jede Gruppe nur einmal
                    if (!_directGroups.TryGetValue(member, out var list)) _directGroups[member] = list = [];
                    list.Add(group.Sid);
                }
            }
        }
        foreach (var share in scan.Shares) _shares.TryAdd(PathOrder.Normalize(share.UncPath), share);
    }

    public ScanResult Scan { get; }

    // ---------- Konten und Mitgliedschaft ----------

    public PrincipalKind KindOf(string sid)
    {
        if (Scan.Principals.TryGetValue(sid, out var p)) return p.Kind;
        if (SidCatalog.IsSpecialIdentity(sid)) return PrincipalKind.Bekannt;
        return sid.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase) ? PrincipalKind.Gruppe : PrincipalKind.Unbekannt;
    }

    public string Label(string sid)
    {
        if (Scan.Principals.TryGetValue(sid, out var p) && !string.IsNullOrEmpty(p.Name)) return p.Name;
        return SidCatalog.FallbackName(sid) ?? sid;
    }

    /// <summary>
    /// Konten, die sich anmelden (Benutzer, Computer) – und vorsichtshalber auch verwaiste oder nicht
    /// auflösbare SIDs, denn auch dahinter kann ein Konto stehen (z. B. über SID-Verlauf oder fremde Domäne).
    /// </summary>
    private bool IsAccountLike(string sid) =>
        !SidCatalog.IsCreatorPlaceholder(sid) && !SidCatalog.IsSpecialIdentity(sid)
        && KindOf(sid) is not (PrincipalKind.Gruppe or PrincipalKind.Bekannt);

    /// <summary>Mitgliedschaft laut Verzeichnis, bei Konten plus implizit Jeder und Authentifizierte Benutzer.</summary>
    public Membership GroupsOf(string sid)
    {
        if (_groups.TryGetValue(sid, out var cached)) return cached;
        string[] seeds = IsAccountLike(sid) ? [SidCatalog.Everyone, SidCatalog.AuthenticatedUsers] : [];
        return _groups[sid] = Build(sid, seeds);
    }

    /// <summary>
    /// Alle SIDs, deren Einträge für das Konto (bzw. jedes Mitglied der Gruppe) gelten: Mitgliedschaft, Jeder,
    /// Authentifizierte Benutzer und die Anmelde-SID (NETZWERK über die Freigabe, sonst INTERAKTIV).
    /// </summary>
    public Membership TokenOf(string sid, LogonKind logon)
    {
        if (_tokens.TryGetValue((sid, logon), out var cached)) return cached;
        var logonSid = logon == LogonKind.Network ? SidCatalog.Network : SidCatalog.Interactive;
        string[] seeds;
        if (SidCatalog.IsCreatorPlaceholder(sid))
            seeds = []; // steht nie in einem Token
        else if (SidCatalog.IsSpecialIdentity(sid))
        {
            // Sonder-Identitäten sind (bis auf Anonym/NULL) immer auch „Jeder“
            bool everyone = !(sid.Equals(SidCatalog.Everyone, StringComparison.OrdinalIgnoreCase)
                || sid.Equals(SidCatalog.AnonymousLogon, StringComparison.OrdinalIgnoreCase)
                || sid.Equals(SidCatalog.NullSid, StringComparison.OrdinalIgnoreCase));
            seeds = everyone ? [SidCatalog.Everyone, logonSid] : [logonSid];
        }
        else
        {
            // Konten und Gruppen: Mitglieder einer Gruppe sind angemeldete Konten
            seeds = [SidCatalog.Everyone, SidCatalog.AuthenticatedUsers, logonSid];
        }
        return _tokens[(sid, logon)] = Build(sid, seeds);
    }

    /// <summary>Breitensuche über „ist Mitglied von“ – iterativ und zyklensicher, kürzester Weg gewinnt.</summary>
    private Membership Build(string start, IReadOnlyList<string> seeds)
    {
        var nodes = new Dictionary<string, (string? Parent, int Depth)>(StringComparer.OrdinalIgnoreCase) { [start] = (null, 0) };
        var queue = new Queue<string>();

        // Echte Gruppen zuerst, dann die impliziten – bei gleicher Länge wird so der Weg über eine echte Gruppe gezeigt
        Visit(start, 1);
        foreach (var seed in seeds)
            if (nodes.TryAdd(seed, (start, 1))) queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            Visit(current, nodes[current].Depth + 1);
        }
        return new Membership(start, nodes);

        void Visit(string sid, int depth)
        {
            if (!_directGroups.TryGetValue(sid, out var groups)) return;
            foreach (var g in groups)
                if (nodes.TryAdd(g, (sid, depth))) queue.Enqueue(g);
        }
    }

    // ---------- Rechte auf einen Knoten ----------

    public static LogonKind LogonFor(FolderNode node) => node.ShareUnc is null ? LogonKind.Local : LogonKind.Network;

    /// <summary>Recht eines Kontos (oder jedes Mitglieds einer Gruppe) auf den Knoten selbst, samt Eintrag für „über welche Gruppen“.</summary>
    public AccessResult Evaluate(FolderNode node, Membership token) => Walk(node, token, onlyAllowSid: null, explain: true);

    /// <summary>Nur die Stufe – ohne Suche nach dem erklärenden Eintrag, für Massenvergleiche.</summary>
    public AccessLevel Level(FolderNode node, Membership token) => Walk(node, token, onlyAllowSid: null, explain: false).Level;

    /// <summary>
    /// Recht, das ein einzelner ACL-Eintrag (Konto oder Gruppe) seinen Mitgliedern auf diesem Knoten gibt:
    /// nur die Erlauben-Einträge genau dieser SID, aber alle Verweigerungen, die die Mitglieder treffen.
    /// </summary>
    public AccessResult EvaluateEntry(FolderNode node, string trustee, Membership trusteeToken) =>
        Walk(node, trusteeToken, onlyAllowSid: trustee, explain: false);

    private static bool OwnerInToken(FolderNode node, Membership token) =>
        !string.IsNullOrEmpty(node.OwnerSid) && token.Contains(node.OwnerSid);

    private static bool Applies(string aceSid, Membership token, bool ownerInToken) =>
        token.Contains(aceSid) || (ownerInToken && aceSid.Equals(SidCatalog.OwnerRights, StringComparison.OrdinalIgnoreCase));

    private static int ChainLength(string aceSid, Membership token, FolderNode node)
    {
        if (token.Contains(aceSid)) return token.DistanceTo(aceSid);
        // BESITZERRECHTE: Weg zum Besitzer plus ein Schritt
        int toOwner = token.DistanceTo(node.OwnerSid);
        return toOwner == int.MaxValue ? int.MaxValue : toOwner + 1;
    }

    /// <summary>
    /// Bildet die Zugriffsprüfung von Windows (MAXIMUM_ALLOWED) nach: Einträge in kanonischer Reihenfolge,
    /// ein Bit gilt, sobald es erlaubt wurde, bevor ein passender Verweigern-Eintrag es sperrt.
    /// </summary>
    /// <param name="onlyAllowSid">Nur Erlauben-Einträge dieser SID zählen (Verweigerungen weiter für das ganze Token).</param>
    /// <param name="explain">Eintrag mit dem höchsten Recht und kürzestem Weg merken (für „über welche Gruppen“).</param>
    private AccessResult Walk(FolderNode node, Membership token, string? onlyAllowSid, bool explain)
    {
        bool owner = OwnerInToken(node, token);
        FileRights granted = 0, denied = 0, allowed = 0;
        string? best = null;
        var bestLevel = AccessLevel.Keine;
        int bestChain = int.MaxValue;

        foreach (var ace in Prepared(node))
        {
            if (ace.IsDeny)
            {
                if (Applies(ace.Sid, token, owner)) denied |= ace.Mask & ~granted;
                continue;
            }
            bool applies = onlyAllowSid is null
                ? Applies(ace.Sid, token, owner)
                : ace.Sid.Equals(onlyAllowSid, StringComparison.OrdinalIgnoreCase);
            if (!applies) continue;

            allowed |= ace.Mask;
            var survives = ace.Mask & ~denied;
            granted |= survives;
            if (!explain) continue;

            var level = Rights.ToLevel(survives);
            if (level == AccessLevel.Keine || level < bestLevel) continue;
            int chain = ChainLength(ace.Sid, token, node);
            if (level > bestLevel || chain < bestChain)
            {
                best = ace.Sid;
                bestLevel = level;
                bestChain = chain;
            }
        }
        return new AccessResult(granted, allowed, best);
    }

    /// <summary>
    /// Schlüssel, der für jedes Token dasselbe Ergebnis garantiert: Zugriffsweg, Freigabe, wirksame Einträge in
    /// Auswertungsreihenfolge und – nur wenn BESITZERRECHTE eingetragen sind – der Besitzer. Mehr fragen
    /// <see cref="Walk"/>, <see cref="Via"/> und <see cref="ShareLevels"/> vom Knoten nicht ab; der Pfad zählt nicht.
    /// </summary>
    internal string EffectKey(FolderNode node)
    {
        var prepared = Prepared(node);
        var key = new System.Text.StringBuilder(32 + prepared.Length * 56);
        key.Append(LogonFor(node) == LogonKind.Network ? 'N' : 'L').Append('|');
        key.Append(ShareOf(node) is { } share ? PathOrder.Normalize(share.UncPath).ToUpperInvariant() : "-").Append('|');
        bool ownerRights = false;
        foreach (var ace in prepared)
        {
            key.Append(ace.Sid.ToUpperInvariant()).Append(ace.IsDeny ? "|D" : "|A").Append(((uint)ace.Mask).ToString("X", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            ownerRights |= ace.Sid.Equals(SidCatalog.OwnerRights, StringComparison.OrdinalIgnoreCase);
        }
        if (ownerRights) key.Append("|O").Append(node.OwnerSid.ToUpperInvariant());
        return key.ToString();
    }

    /// <summary>
    /// SIDs, von denen mindestens eine im Token stehen muss, damit der Knoten überhaupt etwas gewährt: die der
    /// Erlauben-Einträge und bei BESITZERRECHTE zusätzlich der Besitzer. Wer keine davon hat, bekommt hier nichts.
    /// </summary>
    internal IEnumerable<string> GrantingSids(FolderNode node)
    {
        foreach (var ace in Prepared(node))
        {
            if (ace.IsDeny) continue;
            yield return ace.Sid;
            if (ace.Sid.Equals(SidCatalog.OwnerRights, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(node.OwnerSid))
                yield return node.OwnerSid;
        }
    }

    /// <summary>Eintrag in Auswertungsreihenfolge; generische Rechte schon abgebildet.</summary>
    private readonly record struct PreparedAce(string Sid, FileRights Mask, bool IsDeny);

    /// <summary>Wirksame Einträge des Knotens selbst (ohne INHERIT_ONLY und ERSTELLER-BESITZER) in kanonischer Reihenfolge.</summary>
    private PreparedAce[] Prepared(FolderNode node)
    {
        if (_prepared.TryGetValue(node, out var prepared)) return prepared;
        var list = new List<PreparedAce>(node.Aces.Count);
        foreach (var ace in Canonical(node.Aces))
            if (!ace.InheritOnly && !SidCatalog.IsCreatorPlaceholder(ace.Sid))
                list.Add(new PreparedAce(ace.Sid, Rights.MapGeneric(ace.Rights), ace.IsDeny));
        return _prepared[node] = [.. list];
    }

    /// <summary>
    /// Kanonische Reihenfolge wie bei Windows: explizite Verweigerungen, explizite Erlaubnisse, danach die
    /// geerbten Einträge in gespeicherter Reihenfolge (näherer Elternordner zuerst). Explizite Einträge werden
    /// so unabhängig von ihrer Speicherreihenfolge ausgewertet – „Verweigern gewinnt“ auf derselben Ebene.
    /// </summary>
    internal static IEnumerable<AceEntry> Canonical(IReadOnlyList<AceEntry> aces)
    {
        foreach (var a in aces) if (!a.IsInherited && a.IsDeny) yield return a;
        foreach (var a in aces) if (!a.IsInherited && !a.IsDeny) yield return a;
        foreach (var a in aces) if (a.IsInherited) yield return a;
    }

    /// <summary>Weg vom Konto zum Eintrag, der das Recht bringt, als Namen (leer = direkt).</summary>
    public IReadOnlyList<string> Via(FolderNode node, Membership token, string? bestAceSid)
    {
        if (bestAceSid is null) return [];
        IReadOnlyList<string> chain;
        if (token.Contains(bestAceSid))
            chain = token.PathTo(bestAceSid);
        else if (bestAceSid.Equals(SidCatalog.OwnerRights, StringComparison.OrdinalIgnoreCase))
            chain = [.. token.PathTo(node.OwnerSid), SidCatalog.OwnerRights];
        else
            chain = [];
        return chain.Count == 0 ? [] : chain.Select(Label).ToArray();
    }

    // ---------- Freigaben ----------

    public ShareInfo? ShareOf(FolderNode node) =>
        node.ShareUnc is { } unc && _shares.TryGetValue(PathOrder.Normalize(unc), out var share) ? share : null;

    /// <summary>
    /// Freigabe-Recht für das Token, null wenn der Knoten unter keiner (bekannten, lesbaren) Freigabe liegt.
    /// Erlauben: die höchste gewährte Stufe aller passenden Einträge (die Stufen bauen aufeinander auf, mehrere
    /// Einträge ergeben also nie mehr als den höchsten). Verweigern: Ein Eintrag sperrt laut <see cref="ShareAce"/> seine
    /// Stufe und alle höheren – übrig bleibt höchstens die Stufe darunter. Verweigern „Ändern“ lässt Lesen also zu,
    /// Verweigern „Vollzugriff“ der Windows-Oberfläche (gespeichert als „ab Lesen“) sperrt alles.
    /// </summary>
    public ShareAccess? ShareAccessFor(FolderNode node, Membership token) => ShareLevels(node, token)?.Effective;

    /// <summary>
    /// Wie <see cref="ShareAccessFor"/>, zusätzlich mit der Stufe vor den Verweigerungen – daraus ergibt sich, ob ein
    /// Verweigern-Eintrag der Freigabe Rechte entzogen hat.
    /// </summary>
    public (ShareAccess Allowed, ShareAccess Effective)? ShareLevels(FolderNode node, Membership token)
    {
        var share = ShareOf(node);
        if (share is null || !share.PermissionsReadable) return null;
        var allowed = ShareAccess.Keine;
        // Höher als jede echte Stufe = nichts verweigert
        var deniedFrom = ShareAccess.Vollzugriff + 1;
        foreach (var ace in share.Permissions)
        {
            if (!token.Contains(ace.Sid)) continue;
            if (ace.IsDeny)
            {
                // „Keine“ legt der Scanner nie an; käme es doch vor, sperrt es nichts (lieber Zugriff zeigen)
                if (ace.Access > ShareAccess.Keine && ace.Access < deniedFrom) deniedFrom = ace.Access;
            }
            else if (ace.Access > allowed)
            {
                allowed = ace.Access;
            }
        }
        return (allowed, allowed < deniedFrom ? allowed : deniedFrom - 1);
    }

    // ---------- Ordner, die nicht gespeichert sind ----------

    /// <summary>
    /// Knoten für einen Pfad: der gespeicherte Knoten oder – wenn der Pfad in diesem Lauf nicht gespeichert
    /// wurde – ein nachgebildeter, der vom nächsthöheren gespeicherten Ordner erbt. null, wenn das nicht
    /// geht (Pfad außerhalb der Ziele) oder der Knoten bzw. sein Vorfahr nicht lesbar war.
    /// </summary>
    public FolderNode? NodeAt(string path, bool isFile)
    {
        _stored ??= BuildStoredIndex();
        if (_stored.TryGetValue(PathOrder.Normalize(path), out var node))
            return node.Error is null ? node : null;

        for (var p = PathOrder.Parent(path); p is not null; p = PathOrder.Parent(p))
        {
            if (!_stored.TryGetValue(PathOrder.Normalize(p), out var ancestor) || ancestor.IsFile) continue;
            if (ancestor.Error is not null) return null;
            var distance = PathOrder.Distance(ancestor.Path, path);
            return distance is > 0 ? Inherit(ancestor, path, distance.Value, isFile) : null;
        }
        return null;
    }

    /// <summary>Der in diesem Lauf gespeicherte Knoten genau für diesen Pfad (auch mit Fehler), sonst null.</summary>
    internal FolderNode? StoredAt(string path)
    {
        _stored ??= BuildStoredIndex();
        return _stored.GetValueOrDefault(PathOrder.Normalize(path));
    }

    private Dictionary<string, FolderNode> BuildStoredIndex()
    {
        var index = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Scan.Folders) index.TryAdd(PathOrder.Normalize(f.Path), f);
        return index;
    }

    /// <summary>
    /// Bildet nach, was ein Unterordner bzw. eine Datei <paramref name="distance"/> Ebenen tiefer vom Vorfahren
    /// erbt (Vererbung dazwischen nicht unterbrochen, sonst wäre der Ordner gespeichert).
    /// </summary>
    internal static FolderNode Inherit(FolderNode ancestor, string path, int distance, bool isFile)
    {
        var node = new FolderNode
        {
            Path = path,
            Depth = ancestor.Depth + distance,
            IsFile = isFile,
            ShareUnc = ancestor.ShareUnc,
            InheritanceBroken = false,
        };
        foreach (var ace in Canonical(ancestor.Aces))
        {
            // ERSTELLER-BESITZER wird beim Erben durch den (unbekannten) Ersteller ersetzt
            if (SidCatalog.IsCreatorPlaceholder(ace.Sid)) continue;
            bool noPropagate = (ace.Propagation & AcePropagation.NoPropagateInherit) != 0;
            // Dateien erben über ObjectInherit (auch über Zwischenordner hinweg), Ordner über ContainerInherit
            var needed = isFile ? AceInheritance.ObjectInherit : AceInheritance.ContainerInherit;
            if ((ace.Inheritance & needed) == 0 || (noPropagate && distance > 1)) continue;
            node.Aces.Add(ace with
            {
                Rights = Rights.MapGeneric(ace.Rights),
                IsInherited = true,
                Inheritance = noPropagate || isFile ? AceInheritance.None : ace.Inheritance,
                Propagation = AcePropagation.None,
            });
        }
        return node;
    }

    private sealed class TokenKeyComparer : IEqualityComparer<(string Sid, LogonKind Logon)>
    {
        public static TokenKeyComparer Instance { get; } = new();
        public bool Equals((string Sid, LogonKind Logon) x, (string Sid, LogonKind Logon) y) =>
            x.Logon == y.Logon && string.Equals(x.Sid, y.Sid, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Sid, LogonKind Logon) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sid), obj.Logon);
    }
}
