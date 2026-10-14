using System.Text.RegularExpressions;

namespace Zutritt.Core.Scanning;

internal enum TargetKind
{
    /// <summary>Lokaler Ordner, z. B. D:\Daten.</summary>
    LocalPath,
    /// <summary>\\server\freigabe[\unterordner].</summary>
    UncPath,
    /// <summary>\\server – alle Datenträger-Freigaben.</summary>
    Server,
    /// <summary>„.“ – alle Datenträger-Freigaben dieses Rechners, über ihre lokalen Pfade.</summary>
    LocalShares,
}

/// <summary>Ein aufgelöstes Scan-Ziel.</summary>
/// <param name="Path">Normalisierter Pfad (lokal oder UNC), bei <see cref="TargetKind.Server"/> „\\server“, bei „.“ leer.</param>
internal sealed record ScanTarget(TargetKind Kind, string Path, string? Server = null, string? Share = null)
{
    public string ShareUnc => $@"\\{Server}\{Share}";
}

/// <summary>Zerlegt die Ziele aus <see cref="Model.ScanRequest.Targets"/>.</summary>
internal static partial class TargetParser
{
    [GeneratedRegex(@"^[A-Za-z]:$")]
    private static partial Regex DriveOnly();

    /// <summary>Ziel erkennen und normalisieren; bei ungültigen Angaben null und ein deutscher Fehlertext.</summary>
    public static ScanTarget? Parse(string? raw, out string? error)
    {
        error = null;
        var t = (raw ?? "").Trim();
        if (t.Length == 0) { error = "Leeres Ziel"; return null; }
        if (t == ".") return new ScanTarget(TargetKind.LocalShares, "");

        t = t.Replace('/', '\\');
        if (t.StartsWith(@"\\.\", StringComparison.Ordinal) || t.StartsWith(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Gerätepfade werden nicht unterstützt: „{raw}“";
            return null;
        }
        var readable = LongPath.FromExtended(t);
        // „\\?\Volume{…}\“ und andere Gerätenamen: ohne Laufwerk wären sie sonst ein relativer Pfad zum aktuellen
        // Verzeichnis – der Scanner prüfte still einen ganz anderen Ordner.
        if (LongPath.IsDevicePath(t, readable))
        {
            error = $"Gerätepfade werden nicht unterstützt: „{raw}“";
            return null;
        }
        t = readable;

        try
        {
            return t.StartsWith(@"\\", StringComparison.Ordinal) ? ParseUnc(t, raw!, out error) : ParseLocal(t);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Ungültiger Pfad: „{raw}“";
            return null;
        }
    }

    private static ScanTarget? ParseUnc(string t, string raw, out string? error)
    {
        error = null;
        var parts = t[2..].TrimEnd('\\').Split('\\');
        if (parts.Length == 0 || parts[0].Length == 0 || parts[0].IndexOfAny(['?', '*', ':', '"', '<', '>', '|']) >= 0)
        {
            error = $"Ungültiger UNC-Pfad: „{raw}“";
            return null;
        }
        if (parts.Length == 1) return new ScanTarget(TargetKind.Server, @"\\" + parts[0], parts[0]);
        if (parts[1].Length == 0)
        {
            error = $"Ungültiger UNC-Pfad: „{raw}“";
            return null;
        }

        // GetFullPath löst „.“, „..“ und doppelte Trenner auf, bleibt aber innerhalb der Freigabe.
        var full = LongPath.TrimEndSeparator(Path.GetFullPath(t));
        var fullParts = full[2..].Split('\\');
        return new ScanTarget(TargetKind.UncPath, full, fullParts[0], fullParts[1]);
    }

    private static ScanTarget ParseLocal(string t)
    {
        // „D:“ ist als ganzes Laufwerk gemeint, nicht als aktuelles Verzeichnis auf D:.
        if (DriveOnly().IsMatch(t)) t += "\\";
        return new ScanTarget(TargetKind.LocalPath, LongPath.TrimEndSeparator(Path.GetFullPath(t)));
    }
}
