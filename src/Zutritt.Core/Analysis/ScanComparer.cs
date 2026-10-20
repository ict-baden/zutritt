using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Vergleicht zwei Läufe über dieselben Ziele: Was hat sich an Rechten, Befunden und Gruppen geändert?</summary>
public static class ScanComparer
{
    /// <summary>
    /// Unterschiede von <paramref name="older"/> zu <paramref name="newer"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Ordner werden über den Pfad zugeordnet (Groß/Klein egal). Einträge über (SID, Verweigern, geerbt,
    /// Vererbung, Weitergabe); gleicher Schlüssel mit anderen Rechten = geändert. Nicht lesbare Ordner werden
    /// nicht verglichen (das zeigen die Befunde ZT09).</item>
    /// <item>Befunde über <see cref="Finding.Key"/> – verglichen werden die in den Läufen gespeicherten Befunde.</item>
    /// <item>Effektive Rechte: für alle Benutzer beider Läufe auf allen gespeicherten Ordnern. Ist ein Ordner nur in
    /// einem Lauf gespeichert (z. B. weil seine Vererbung neu unterbrochen wurde), gilt im anderen Lauf, was er vom
    /// nächsthöheren gespeicherten Ordner erbt – sonst sähe jede neue Ausnahme so aus, als hätten alle Personen
    /// dort gerade erst Zugriff bekommen. Hat einer der Läufe keine Gruppen aufgelöst, werden die effektiven Rechte
    /// auf beiden Seiten ohne Gruppenmitgliedschaften verglichen (direkte Einträge, Jeder, Authentifizierte Benutzer).</item>
    /// <item>Mitgliedschaften nur für Gruppen, deren Mitglieder in beiden Läufen bekannt sind.</item>
    /// <item>Haben die Läufe einen anderen Umfang (Dateien, Tiefe, administrative Freigaben, Ebene für ZT06), wird nur
    /// der gemeinsame Umfang verglichen (<see cref="ScanScope.Restrict"/>); <see cref="ComparisonResult.ScopeDifferences"/>
    /// nennt die Unterschiede, damit Oberfläche und Berichte darauf hinweisen.</item>
    /// </list>
    /// </remarks>
    public static ComparisonResult Compare(ScanResult older, ScanResult newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        var differences = ScanScope.Differences(older.Request, newer.Request);
        var result = new ComparisonResult { Older = older, Newer = newer, ScopeDifferences = differences };
        // Gruppen allein ändern den Umfang der Ordner nicht – das gleicht CompareAccess selbst aus
        if (differences.Any(d => d != ScopeDifference.Groups)) (older, newer) = ScanScope.Restrict(older, newer);
        var oldFolders = IndexFolders(older);
        var newFolders = IndexFolders(newer);

        CompareFolders(oldFolders, newFolders, result);
        CompareFindings(older, newer, result);
        CompareAccess(older, newer, oldFolders, newFolders, result);
        CompareMemberships(older, newer, result);
        return result;
    }

    private static Dictionary<string, FolderNode> IndexFolders(ScanResult scan)
    {
        var index = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in scan.Folders) index.TryAdd(PathOrder.Normalize(f.Path), f);
        return index;
    }

    // ---------- Ordner und Einträge ----------

    private static void CompareFolders(Dictionary<string, FolderNode> older, Dictionary<string, FolderNode> newer, ComparisonResult result)
    {
        foreach (var (path, node) in older.OrderBy(kv => kv.Key, PathOrder.Instance))
        {
            if (!newer.TryGetValue(path, out var other))
            {
                if (node.Error is null) result.FoldersRemoved.Add(node.Path);
                continue;
            }
            if (node.Error is not null || other.Error is not null) continue;
            CompareAces(node, other, result.AceChanges);
        }
        foreach (var (path, node) in newer.OrderBy(kv => kv.Key, PathOrder.Instance))
            if (!older.ContainsKey(path) && node.Error is null) result.FoldersAdded.Add(node.Path);
    }

    private readonly record struct AceKey(string Sid, bool IsDeny, bool IsInherited, AceInheritance Inheritance, AcePropagation Propagation);

    private sealed class AceKeyComparer : IEqualityComparer<AceKey>
    {
        public static AceKeyComparer Instance { get; } = new();
        public bool Equals(AceKey x, AceKey y) =>
            x.IsDeny == y.IsDeny && x.IsInherited == y.IsInherited && x.Inheritance == y.Inheritance
            && x.Propagation == y.Propagation && string.Equals(x.Sid, y.Sid, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(AceKey k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Sid), k.IsDeny, k.IsInherited, k.Inheritance, k.Propagation);
    }

    /// <summary>Einträge je Schlüssel; mehrere Einträge mit gleichem Schlüssel wirken wie einer mit allen Rechten.</summary>
    private static Dictionary<AceKey, AceEntry> GroupAces(FolderNode node)
    {
        var map = new Dictionary<AceKey, AceEntry>(AceKeyComparer.Instance);
        foreach (var ace in node.Aces)
        {
            var key = new AceKey(ace.Sid, ace.IsDeny, ace.IsInherited, ace.Inheritance, ace.Propagation);
            map[key] = map.TryGetValue(key, out var existing) ? existing with { Rights = existing.Rights | ace.Rights } : ace;
        }
        return map;
    }

    private static void CompareAces(FolderNode older, FolderNode newer, List<AceChange> changes)
    {
        var before = GroupAces(older);
        var after = GroupAces(newer);
        foreach (var (key, ace) in before)
        {
            if (!after.TryGetValue(key, out var now))
                changes.Add(new AceChange(newer.Path, ChangeKind.Entfernt, ace.Sid, ace, null));
            else if (Rights.MapGeneric(now.Rights) != Rights.MapGeneric(ace.Rights))
                changes.Add(new AceChange(newer.Path, ChangeKind.Geaendert, ace.Sid, ace, now));
        }
        foreach (var (key, ace) in after)
            if (!before.ContainsKey(key))
                changes.Add(new AceChange(newer.Path, ChangeKind.Hinzugefuegt, ace.Sid, null, ace));
    }

    // ---------- Befunde ----------

    private static void CompareFindings(ScanResult older, ScanResult newer, ComparisonResult result)
    {
        var oldKeys = older.Findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var newKeys = newer.Findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in newer.Findings)
            if (!oldKeys.Contains(f.Key) && seen.Add(f.Key)) result.NewFindings.Add(f);
        seen.Clear();
        foreach (var f in older.Findings)
            if (!newKeys.Contains(f.Key) && seen.Add(f.Key)) result.ResolvedFindings.Add(f);
    }

    // ---------- Effektive Rechte ----------

    private static void CompareAccess(ScanResult older, ScanResult newer,
        Dictionary<string, FolderNode> oldFolders, Dictionary<string, FolderNode> newFolders, ComparisonResult result)
    {
        // Hat ein Lauf keine Gruppen aufgelöst, fehlen dort alle Mitgliedschaften. Dann zählen auf beiden Seiten nur
        // direkte Einträge, Jeder und Authentifizierte Benutzer – sonst sähe jedes Gruppenrecht neu vergeben aus.
        bool membership = older.Request.ResolveGroups && newer.Request.ResolveGroups;
        var oldCtx = new AccessContext(older, membership);
        var newCtx = new AccessContext(newer, membership);

        var people = older.Principals.Values.Concat(newer.Principals.Values)
            .Where(p => p.Kind == PrincipalKind.Benutzer)
            .Select(p => p.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (people.Count == 0) return;

        // Wer – soweit Einträge, Besitzer und Freigaben es betreffen – dieselben SIDs im Token hat, hat auf jedem Knoten
        // dasselbe Recht. Gerechnet wird deshalb je solcher Klasse nur einmal, und auf unveränderten Ordnern nur für
        // Personen, deren Klasse sich zwischen den Läufen geändert hat. Das spart bei großen Läufen fast alles.
        var classes = new TokenClasses(RelevantSids(older, newer));
        LogonKind[] logons = [LogonKind.Local, LogonKind.Network];
        var oldClass = logons.ToDictionary(l => l, l => people.Select(sid => classes.Of(oldCtx.TokenOf(sid, l))).ToArray());
        var newClass = logons.ToDictionary(l => l, l => people.Select(sid => classes.Of(newCtx.TokenOf(sid, l))).ToArray());
        var everyone = Enumerable.Range(0, people.Count).ToArray();
        var classChanged = logons.ToDictionary(l => l, l => everyone.Where(i => oldClass[l][i] != newClass[l][i]).ToArray());
        var levelsBefore = new LevelCache(classes.Count);
        var levelsAfter = new LevelCache(classes.Count);

        // Alle in mindestens einem Lauf gespeicherten, lesbaren Pfade
        var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, node) in oldFolders) if (node.Error is null) paths.TryAdd(path, node.IsFile);
        foreach (var (path, node) in newFolders) if (node.Error is null) paths.TryAdd(path, node.IsFile);

        var changes = new List<AccessChange>();
        foreach (var (path, isFile) in paths)
        {
            var before = oldCtx.NodeAt(path, isFile);
            var after = newCtx.NodeAt(path, isFile);
            // Ohne verlässlichen Stand auf einer Seite (nicht lesbar, außerhalb der Ziele) lässt sich nichts sagen
            if (before is null || after is null) continue;
            var display = (newFolders.GetValueOrDefault(path) ?? oldFolders[path]).Path;

            var oldLogon = AccessContext.LogonFor(before);
            var newLogon = AccessContext.LogonFor(after);
            var candidates = SameEffect(oldCtx, before, newCtx, after) ? classChanged[newLogon] : everyone;
            if (candidates.Length == 0) continue;
            levelsBefore.Reset();
            levelsAfter.Reset();
            foreach (var i in candidates)
            {
                var sid = people[i];
                int cb = oldClass[oldLogon][i], ca = newClass[newLogon][i];
                if (!levelsBefore.TryGet(cb, out var b)) levelsBefore.Set(cb, b = EffectiveAccess.EffectiveLevel(oldCtx, before, sid));
                if (!levelsAfter.TryGet(ca, out var a)) levelsAfter.Set(ca, a = EffectiveAccess.EffectiveLevel(newCtx, after, sid));
                if (a != b) changes.Add(new AccessChange(display, sid, b, a));
            }
        }

        changes.Sort((x, y) =>
        {
            int c = PathOrder.Instance.Compare(x.Path, y.Path);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(Label(newCtx, oldCtx, x.Sid), Label(newCtx, oldCtx, y.Sid));
        });
        result.AccessChanges.AddRange(changes);
    }

    private static string Label(AccessContext newer, AccessContext older, string sid)
    {
        var label = newer.Label(sid);
        return label == sid ? older.Label(sid) : label;
    }

    /// <summary>
    /// SIDs, von denen ein effektives Recht abhängen kann: Einträge, Besitzer (wegen BESITZERRECHTE) und
    /// Freigabe-Einträge beider Läufe. Nachgebildete Knoten tragen nur Einträge ihrer gespeicherten Vorfahren.
    /// </summary>
    private static HashSet<string> RelevantSids(ScanResult older, ScanResult newer)
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in new[] { older, newer })
        {
            foreach (var folder in scan.Folders)
            {
                if (!string.IsNullOrEmpty(folder.OwnerSid)) sids.Add(folder.OwnerSid);
                foreach (var ace in folder.Aces) sids.Add(ace.Sid);
            }
            foreach (var share in scan.Shares)
                foreach (var ace in share.Permissions) sids.Add(ace.Sid);
        }
        return sids;
    }

    /// <summary>
    /// Nummeriert Token nach den relevanten SIDs darin. Gleiche Nummer = gleiches Recht auf jedem Knoten, denn die
    /// Auswertung fragt das Token nur nach SIDs aus Einträgen, Besitzern und Freigaben. Gilt für beide Läufe.
    /// </summary>
    private sealed class TokenClasses(HashSet<string> relevant)
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

        public int Count => _ids.Count;

        public int Of(Membership token)
        {
            var sids = token.Sids.Where(relevant.Contains).Select(s => s.ToUpperInvariant()).ToList();
            sids.Sort(StringComparer.Ordinal);
            var key = string.Join('|', sids);
            if (!_ids.TryGetValue(key, out var id)) _ids[key] = id = _ids.Count;
            return id;
        }
    }

    /// <summary>Ergebnis je Token-Klasse für den gerade betrachteten Knoten; <see cref="Reset"/> vor jedem Knoten.</summary>
    private sealed class LevelCache(int size)
    {
        private readonly int[] _stamp = new int[size];
        private readonly AccessLevel[] _level = new AccessLevel[size];
        private int _generation;

        public void Reset() => _generation++;

        public bool TryGet(int id, out AccessLevel level)
        {
            level = _level[id];
            return _stamp[id] == _generation;
        }

        public void Set(int id, AccessLevel level)
        {
            _stamp[id] = _generation;
            _level[id] = level;
        }
    }

    /// <summary>
    /// Ergeben beide Knoten für jedes Token dasselbe Recht? Gleicher Zugriffsweg, gleicher Besitzer, gleiche Einträge
    /// in gleicher Reihenfolge und gleiche Freigabe-Rechte.
    /// </summary>
    private static bool SameEffect(AccessContext oldCtx, FolderNode before, AccessContext newCtx, FolderNode after)
    {
        if (AccessContext.LogonFor(before) != AccessContext.LogonFor(after)) return false;
        if (!string.Equals(before.OwnerSid, after.OwnerSid, StringComparison.OrdinalIgnoreCase)) return false;
        if (before.Aces.Count != after.Aces.Count) return false;
        for (int i = 0; i < before.Aces.Count; i++)
        {
            AceEntry x = before.Aces[i], y = after.Aces[i];
            if (x.IsDeny != y.IsDeny || x.IsInherited != y.IsInherited || x.Inheritance != y.Inheritance
                || x.Propagation != y.Propagation || Rights.MapGeneric(x.Rights) != Rights.MapGeneric(y.Rights)
                || !string.Equals(x.Sid, y.Sid, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var shareBefore = oldCtx.ShareOf(before);
        var shareAfter = newCtx.ShareOf(after);
        if (shareBefore is null || shareAfter is null) return shareBefore is null && shareAfter is null;
        if (shareBefore.PermissionsReadable != shareAfter.PermissionsReadable) return false;
        if (!shareBefore.PermissionsReadable) return true;
        return shareBefore.Permissions.Count == shareAfter.Permissions.Count
            && shareBefore.Permissions.Zip(shareAfter.Permissions).All(p =>
                p.First.Access == p.Second.Access && p.First.IsDeny == p.Second.IsDeny
                && string.Equals(p.First.Sid, p.Second.Sid, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Mitgliedschaften ----------

    private static void CompareMemberships(ScanResult older, ScanResult newer, ComparisonResult result)
    {
        // Ohne Gruppenauflösung sind alle Mitgliederlisten leer – das wäre kein echter Austritt
        if (!older.Request.ResolveGroups || !newer.Request.ResolveGroups) return;

        foreach (var oldGroup in older.Principals.Values.OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase))
        {
            if (!newer.Principals.TryGetValue(oldGroup.Sid, out var newGroup)) continue;
            if (oldGroup.MembersUnknown || newGroup.MembersUnknown) continue;
            if (!IsGroup(oldGroup) || !IsGroup(newGroup)) continue;

            var before = new HashSet<string>(oldGroup.Members, StringComparer.OrdinalIgnoreCase);
            var after = new HashSet<string>(newGroup.Members, StringComparer.OrdinalIgnoreCase);
            foreach (var m in newGroup.Members.Where(m => !before.Contains(m)).Distinct(StringComparer.OrdinalIgnoreCase))
                result.MembershipChanges.Add(new MembershipChange(newGroup.Sid, m, ChangeKind.Hinzugefuegt));
            foreach (var m in oldGroup.Members.Where(m => !after.Contains(m)).Distinct(StringComparer.OrdinalIgnoreCase))
                result.MembershipChanges.Add(new MembershipChange(newGroup.Sid, m, ChangeKind.Entfernt));
        }
    }

    /// <summary>Gruppen und bekannte Prinzipale mit Mitgliedern (z. B. VORDEFINIERT\Benutzer, falls so eingeordnet).</summary>
    private static bool IsGroup(Principal p) => p.Kind is PrincipalKind.Gruppe or PrincipalKind.Bekannt;
}
