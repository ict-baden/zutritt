using Zutritt.Core.Model;

namespace Zutritt.Core.Storage;

/// <summary>
/// Ziele eines Laufs als Menge – Grundlage dafür, welcher frühere Lauf zum Vergleich passt.
/// </summary>
internal static class TargetSet
{
    /// <summary>Anzeige in der Liste der Läufe.</summary>
    public static string Display(IEnumerable<string>? targets) => string.Join("; ", targets ?? []);

    /// <summary>
    /// Schlüssel der Zielmenge: Reihenfolge, Groß/Klein, doppelte Einträge, „/“ statt „\“, ein
    /// abschließendes „\“ und „D:“ statt „D:\“ spielen keine Rolle. Zeilenumbruch als Trenner, weil er in Pfaden nicht vorkommen kann.
    /// </summary>
    public static string Key(IEnumerable<string>? targets) =>
        string.Join("\n", (targets ?? [])
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    public static string Normalize(string? target)
    {
        var t = WindowsPath.WithBackslashes((target ?? "").Trim());
        var trimmed = t.TrimEnd(WindowsPath.Separator);
        if (trimmed.Length == 0) trimmed = t;                                     // „\“ allein: nichts zu kürzen
        // „C:“ und „C:\“ meinen beide das ganze Laufwerk (so liest es auch der TargetParser) und bleiben Laufwerkswurzel
        else if (trimmed.Length == 2 && trimmed[1] == ':') trimmed += WindowsPath.Separator;
        return trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Ob die Ziele vom scannenden Rechner abhängen („.“, C:\…, \\localhost\…). Dann ist derselbe Text auf
    /// einem anderen Rechner ein anderes Ziel, und ein Vergleich darüber hinweg würde Scheinänderungen zeigen.
    /// UNC-Pfade auf einen benannten Server meinen dagegen überall dasselbe.
    /// </summary>
    public static bool DependsOnMachine(IEnumerable<string>? targets) => (targets ?? []).Any(DependsOnMachine);

    public static bool DependsOnMachine(string? target)
    {
        var t = Normalize(target);
        if (t.Length == 0) return false;
        if (t.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return IsLocalServer(ServerOf(t[8..]));
        if (t.StartsWith(@"\\?\", StringComparison.Ordinal) || t.StartsWith(@"\\.\", StringComparison.Ordinal)) return true;
        if (WindowsPath.IsUnc(t)) return IsLocalServer(ServerOf(t[2..]));
        return true;  // „.“, Laufwerkspfad oder relativer Pfad
    }

    private static string ServerOf(string rest)
    {
        var end = rest.IndexOf(WindowsPath.Separator);
        return end < 0 ? rest : rest[..end];
    }

    private static bool IsLocalServer(string server) =>
        server is "" or "." or "LOCALHOST" or "127.0.0.1" or "::1" or "[::1]" or "0:0:0:0:0:0:0:1";
}
