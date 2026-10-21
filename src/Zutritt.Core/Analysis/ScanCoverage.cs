// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Wie viel ein frischer Lauf wirklich prüfen konnte – für „N von M nicht geprüft“ und Exitcode 3 der Befehlszeile.</summary>
public static class ScanCoverage
{
    /// <summary>
    /// Gezählt wird je Wurzel (ein Ziel, bei einem ganzen Server oder „.“ jede seiner Freigaben) und je Ziel oder
    /// Freigabe, die gar keine Wurzel ergab (<see cref="ScanResult.TargetsWithoutRoot"/>: Server aus, Freigabeliste nicht
    /// lesbar …). Nicht geprüft ist außerdem eine Wurzel, deren Rechte nicht lesbar waren und unter der der Scan auch
    /// nichts lesen konnte – war darunter etwas lesbar, gilt sie als geprüft.
    /// </summary>
    public static (int Unchecked, int Total) Count(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var roots = scan.Folders.Where(f => f.IsRoot).ToList();
        // Meist keine oder wenige: nur für sie die gespeicherten Ordner durchsehen
        var open = roots.Where(r => !Readable(r)).ToList();
        // Ein Ziel in einem anderen hat seinen eigenen Durchlauf – was darunter liegt, zählt nur für dieses
        var nested = open.ToDictionary(u => u, u => roots.Where(r => PathOrder.Distance(u.Path, r.Path) is > 0).ToList());
        foreach (var folder in scan.Folders)
        {
            if (open.Count == 0) break;
            if (folder.IsRoot || !Readable(folder)) continue;
            open.RemoveAll(u => PathOrder.Distance(u.Path, folder.Path) is > 0
                                && !nested[u].Any(r => PathOrder.Distance(r.Path, folder.Path) is not null));
        }
        int uncheckedRoots = open.Count;
        int without = scan.TargetsWithoutRoot.Count;
        return (uncheckedRoots + without, roots.Count + without);
    }

    private static bool Readable(FolderNode node) => node.Error is null || node.Aces.Count > 0;
}
