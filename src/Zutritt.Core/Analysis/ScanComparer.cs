using System.Security.AccessControl;
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
    /// dort gerade erst Zugriff bekommen.</item>
    /// <item>Mitgliedschaften nur für Gruppen, deren Mitglieder in beiden Läufen bekannt sind.</item>
    /// </list>
    /// </remarks>
    public static ComparisonResult Compare(ScanResult older, ScanResult newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        var result = new ComparisonResult { Older = older, Newer = newer };
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

    private readonly record struct AceKey(string Sid, bool IsDeny, bool IsInherited, InheritanceFlags Inheritance, PropagationFlags Propagation);

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
        var oldCtx = new AccessContext(older);
        var newCtx = new AccessContext(newer);

        var people = older.Principals.Values.Concat(newer.Principals.Values)
            .Where(p => p.Kind == PrincipalKind.Benutzer)
            .Select(p => p.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (people.Count == 0) return;

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

            foreach (var sid in people)
            {
                var b = EffectiveAccess.EffectiveLevel(oldCtx, before, sid);
                var a = EffectiveAccess.EffectiveLevel(newCtx, after, sid);
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
