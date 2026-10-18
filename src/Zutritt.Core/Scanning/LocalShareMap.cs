using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Ordnet lokale Pfade der Freigabe zu, über die sie erreichbar sind: Das längste passende Präfix gewinnt
/// (D:\Daten\Labor gehört zu „Labor“ auf D:\Daten\Labor, nicht zu „Daten“ auf D:\Daten).
/// </summary>
internal sealed class LocalShareMap
{
    private readonly Dictionary<string, ShareRead> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="shares">Freigaben dieses Rechners; ohne lokalen Pfad werden sie übergangen.</param>
    /// <param name="includeAdminShares">Administrative Freigaben (C$ …) mitzählen – sonst gehörte jeder Pfad zu C$.</param>
    /// <param name="fullPath">Lokalen Pfad vollständig machen (<see cref="Platform.IScanPlatform.FullLocalPath"/>), null = ungültig.</param>
    public LocalShareMap(IEnumerable<ShareRead> shares, bool includeAdminShares, Func<string, string?> fullPath)
    {
        foreach (var read in shares)
        {
            var share = read.Share;
            if (share.LocalPath.Length == 0 || (share.IsAdminShare && !includeAdminShares)) continue;
            if (fullPath(share.LocalPath) is not { } full) continue;
            var path = WindowsPath.TrimEnd(full);

            if (!_byPath.TryGetValue(path, out var existing) || Prefer(read, existing)) _byPath[path] = read;
        }
    }

    public bool IsEmpty => _byPath.Count == 0;

    /// <summary>Freigabe, deren Pfad genau <paramref name="path"/> ist.</summary>
    public ShareRead? At(string path) => _byPath.TryGetValue(path, out var read) ? read : null;

    /// <summary>Freigabe mit dem längsten Pfad, der <paramref name="path"/> enthält.</summary>
    public ShareRead? Longest(string path)
    {
        ShareRead? best = null;
        int bestLength = -1;
        foreach (var (sharePath, read) in _byPath)
        {
            if (sharePath.Length > bestLength && WindowsPath.IsSameOrBelow(path, sharePath))
            {
                best = read;
                bestLength = sharePath.Length;
            }
        }
        return best;
    }

    /// <summary>
    /// Mehrere Freigaben auf demselben Ordner: normale vor administrativen, dann die mit dem weitesten Zugriff
    /// (nicht lesbare Rechte zählen als weitester – die Auswertung rechnet dann nur mit NTFS), dann nach Name.
    /// So zeigt der Bericht im Zweifel eher zu viel Zugriff als zu wenig.
    /// </summary>
    private static bool Prefer(ShareRead candidate, ShareRead current)
    {
        if (candidate.Share.IsAdminShare != current.Share.IsAdminShare) return !candidate.Share.IsAdminShare;
        int a = Breadth(candidate.Share), b = Breadth(current.Share);
        if (a != b) return a > b;
        return string.Compare(candidate.Share.Name, current.Share.Name, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int Breadth(ShareInfo share)
        => !share.PermissionsReadable ? int.MaxValue
           : share.Permissions.Where(p => !p.IsDeny).Select(p => (int)p.Access).DefaultIfEmpty(0).Max();
}
