// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Eine Einstellung, in der sich zwei Läufe über dieselben Ziele unterscheiden und die bestimmt, was sie erfassen.</summary>
public enum ScopeDifference
{
    /// <summary>Nur einer der Läufe hat auch Dateien erfasst (<see cref="ScanRequest.IncludeFiles"/>).</summary>
    Files,
    /// <summary>Unterschiedliche maximale Tiefe (<see cref="ScanRequest.MaxDepth"/>).</summary>
    Depth,
    /// <summary>Nur einer der Läufe hat administrative Freigaben einbezogen (<see cref="ScanRequest.SkipAdminShares"/>).</summary>
    AdminShares,
    /// <summary>Nur einer der Läufe hat Gruppen bis zur Person aufgelöst (<see cref="ScanRequest.ResolveGroups"/>).</summary>
    Groups,
    /// <summary>Unterschiedliche Ebene, ab der eine Ausnahme als versteckt gilt (<see cref="ScanRequest.DeepInheritanceBreakDepth"/>, ZT06).</summary>
    DeepBreakLevel,
}

/// <summary>
/// Umfang eines Laufs: Zwei Läufe über dieselben Ziele, aber mit anderen Einstellungen (z. B. einmal mit Dateien,
/// einmal nur bis Tiefe 1), erfassen verschiedene Ordner und Befunde. Ein Vergleich über alles sähe dann überall
/// „entfallen“ oder „neu“, obwohl sich am Server nichts geändert hat. <see cref="ScanComparer"/> vergleicht deshalb
/// nur den gemeinsamen Umfang (<see cref="Restrict"/>) und nennt die Unterschiede; der Speicher sucht für den
/// automatischen Vergleich zuerst einen Lauf mit denselben Einstellungen.
/// </summary>
public static class ScanScope
{
    /// <summary>Worin sich die Einstellungen unterscheiden; leer = gleicher Umfang.</summary>
    public static IReadOnlyList<ScopeDifference> Differences(ScanRequest older, ScanRequest newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);
        var list = new List<ScopeDifference>();
        if (older.IncludeFiles != newer.IncludeFiles) list.Add(ScopeDifference.Files);
        if (older.MaxDepth != newer.MaxDepth) list.Add(ScopeDifference.Depth);
        if (older.SkipAdminShares != newer.SkipAdminShares) list.Add(ScopeDifference.AdminShares);
        if (older.ResolveGroups != newer.ResolveGroups) list.Add(ScopeDifference.Groups);
        if (DeepBreak(older) != DeepBreak(newer)) list.Add(ScopeDifference.DeepBreakLevel);
        return list;
    }

    /// <summary>
    /// Dieselben Ziele, so wie der automatische Vergleich sie zuordnet: Reihenfolge, Groß/Klein, „/“ statt „\“, ein
    /// abschließendes „\“ und „D:“ statt „D:\“ spielen keine Rolle.
    /// </summary>
    public static bool SameTargets(IEnumerable<string>? a, IEnumerable<string>? b) =>
        Storage.TargetSet.Key(a) == Storage.TargetSet.Key(b);

    /// <summary>Gleicher Umfang – die Läufe lassen sich ohne Einschränkung vergleichen.</summary>
    public static bool Same(ScanRequest a, ScanRequest b) => Differences(a, b).Count == 0;

    /// <summary>
    /// Deutscher Satz für Oberfläche, Befehlszeile und Berichte: worin der Umfang abweicht und dass nur der gemeinsame
    /// Teil verglichen ist. Leer, wenn der Umfang gleich ist.
    /// </summary>
    public static string Describe(ComparisonResult comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (comparison.ScopeDifferences.Count == 0) return "";
        var older = comparison.Older.Request;
        var newer = comparison.Newer.Request;
        var parts = comparison.ScopeDifferences.Select(d => d switch
        {
            ScopeDifference.Files => older.IncludeFiles ? "nur der ältere Lauf hat Dateien erfasst" : "nur der neuere Lauf hat Dateien erfasst",
            ScopeDifference.Depth => $"Tiefe vorher {DepthText(older.MaxDepth)}, jetzt {DepthText(newer.MaxDepth)}",
            ScopeDifference.AdminShares => older.SkipAdminShares
                ? "nur der neuere Lauf hat administrative Freigaben einbezogen"
                : "nur der ältere Lauf hat administrative Freigaben einbezogen",
            ScopeDifference.Groups => older.ResolveGroups
                ? "nur der ältere Lauf hat Gruppen bis zur Person aufgelöst"
                : "nur der neuere Lauf hat Gruppen bis zur Person aufgelöst",
            ScopeDifference.DeepBreakLevel => $"tief versteckte Ausnahme vorher ab Ebene {DeepBreak(older)}, jetzt ab Ebene {DeepBreak(newer)}",
            _ => d.ToString(),
        });
        return $"Umfang weicht ab: {string.Join("; ", parts)}. Verglichen ist nur, was beide Läufe erfasst haben.";
    }

    private static string DepthText(int? depth) => depth is { } d ? $"bis Ebene {d}" : "alle Ebenen";

    private static int DeepBreak(ScanRequest request) => Math.Max(1, request.DeepInheritanceBreakDepth);

    /// <summary>
    /// Beide Läufe auf den gemeinsamen Umfang zugeschnitten (flache Kopien, die Originale bleiben unverändert):
    /// Dateien nur, wenn beide sie erfasst haben; Ordner nur bis zur kleineren Tiefe; administrative Freigaben nur,
    /// wenn beide sie einbezogen haben (ausdrücklich als Ziel genannte bleiben); ZT06 erst ab der höheren Ebene.
    /// Befunde und Fehler außerhalb des gemeinsamen Umfangs entfallen.
    /// </summary>
    /// <remarks>
    /// Grenze: Ein Ordner genau auf der gemeinsamen Tiefe wurde nur vom tieferen Lauf aufgelistet – ein Fehler beim
    /// Auflisten seines Inhalts kann deshalb nur dort als ZT09 stehen. Die Abweichung wird ohnehin gemeldet.
    /// </remarks>
    internal static (ScanResult Older, ScanResult Newer) Restrict(ScanResult older, ScanResult newer)
    {
        var a = older.Request;
        var b = newer.Request;
        bool files = a.IncludeFiles && b.IncludeFiles;
        int? depth = a.MaxDepth is { } x ? (b.MaxDepth is { } y ? Math.Min(x, y) : x) : b.MaxDepth;
        bool adminShares = !a.SkipAdminShares && !b.SkipAdminShares;
        bool deepDiffers = DeepBreak(a) != DeepBreak(b);
        int deepBreak = Math.Max(DeepBreak(a), DeepBreak(b));

        // Ausdrücklich genannte Ziele gehören immer zum Umfang, auch wenn sie eine administrative Freigabe sind (\\srv\C$)
        var targets = new HashSet<string>(a.Targets.Concat(b.Targets).Select(t => Key(WindowsPath.WithBackslashes(t.Trim()))),
            StringComparer.OrdinalIgnoreCase);
        var adminUnc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adminRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!adminShares)
        {
            foreach (var share in older.Shares.Concat(newer.Shares).Where(s => s.IsAdminShare))
            {
                var unc = Key(share.UncPath);
                if (targets.Contains(unc)) continue;
                adminUnc.Add(unc);
                adminRoots.Add(unc);
                // „.“ prüft eine Freigabe über ihren lokalen Pfad (C$ als C:\)
                if (share.LocalPath.Length > 0 && !targets.Contains(Key(share.LocalPath))) adminRoots.Add(Key(share.LocalPath));
            }
        }

        return (Copy(older), Copy(newer));

        ScanResult Copy(ScanResult scan)
        {
            var copy = new ScanResult
            {
                Id = scan.Id,
                StartedAt = scan.StartedAt,
                FinishedAt = scan.FinishedAt,
                Machine = scan.Machine,
                ScannedBy = scan.ScannedBy,
                ToolVersion = scan.ToolVersion,
                Request = scan.Request,
                FoldersVisited = scan.FoldersVisited,
                FilesVisited = scan.FilesVisited,
            };
            foreach (var share in scan.Shares)
                if (!adminUnc.Contains(Key(share.UncPath))) copy.Shares.Add(share);
            foreach (var (sid, principal) in scan.Principals) copy.Principals[sid] = principal;

            var roots = scan.Folders.Where(f => f.IsRoot).Select(f => Key(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var outside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depthOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in scan.Folders)
            {
                var path = Key(folder.Path);
                depthOf.TryAdd(path, folder.Depth);
                bool inScope = (files || !folder.IsFile) && (depth is not int max || folder.Depth <= max)
                               && !(adminRoots.Count > 0 && RootOf(path, roots) is { } root && adminRoots.Contains(root));
                if (!inScope)
                {
                    outside.Add(path);
                    continue;
                }
                // Lokaler Ordner, den nur der Lauf mit administrativen Freigaben einer solchen zuordnet (C:\Daten → C$):
                // im gemeinsamen Umfang ohne Freigabe, sonst sähe es nach geändertem Zugriff übers Netz aus
                copy.Folders.Add(folder.ShareUnc is { } unc && adminUnc.Contains(Key(unc)) ? WithoutShare(folder) : folder);
            }

            foreach (var finding in scan.Findings)
            {
                var path = Key(finding.Path);
                if (outside.Contains(path) || adminUnc.Contains(path)) continue;
                if (deepDiffers && finding.RuleId == FindingRules.DeepBreak && depthOf.TryGetValue(path, out var d) && d < deepBreak) continue;
                copy.Findings.Add(finding);
            }
            foreach (var error in scan.Errors)
            {
                var path = Key(error.Path);
                if (!outside.Contains(path) && !adminUnc.Contains(path)) copy.Errors.Add(error);
            }
            return copy;
        }
    }

    private static string Key(string path) => PathOrder.Normalize(path);

    /// <summary>Die tiefste Wurzel, unter der der Pfad liegt (Unterordner, die selbst Ziel sind, haben eine eigene Wurzel).</summary>
    private static string? RootOf(string path, List<string> roots)
    {
        string? best = null;
        foreach (var root in roots)
            if (PathOrder.Distance(root, path) is not null && (best is null || root.Length > best.Length)) best = root;
        return best;
    }

    private static FolderNode WithoutShare(FolderNode folder)
    {
        var copy = new FolderNode
        {
            Id = folder.Id,
            Path = folder.Path,
            Depth = folder.Depth,
            IsFile = folder.IsFile,
            ShareUnc = null,
            OwnerSid = folder.OwnerSid,
            InheritanceBroken = folder.InheritanceBroken,
            InheritanceDiffers = folder.InheritanceDiffers,
            Error = folder.Error,
        };
        copy.Aces.AddRange(folder.Aces);
        return copy;
    }
}
