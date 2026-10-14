namespace Zutritt.Core.Scanning;

/// <summary>
/// Pfad-Hilfen für Win32-Aufrufe. Alle Aufrufe laufen über die „\\?\“-Form: Sie hebt die Grenze von 260 Zeichen auf
/// und schaltet die Win32-Normalisierung ab. Ohne sie würde aus „Ordner “ (Leerzeichen am Ende) still „Ordner“ –
/// der Scanner läse dann die Rechte eines anderen Ordners.
/// </summary>
internal static class LongPath
{
    private const string ExtendedPrefix = @"\\?\";
    private const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>Vollständigen lokalen oder UNC-Pfad in die „\\?\“-Form bringen.</summary>
    public static string ToExtended(string path)
    {
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)) return path;
        // „\\.\“ ist ebenfalls ein Gerätepfad, kein UNC-Server namens „.“.
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal)) return ExtendedPrefix + path[4..];
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return ExtendedUncPrefix + path[2..];
        return ExtendedPrefix + path;
    }

    /// <summary>„\\?\“-Form zurück in die lesbare Form (\\?\UNC\s\f → \\s\f, \\?\C:\x → C:\x).</summary>
    public static string FromExtended(string path)
    {
        if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase)) return @"\\" + path[ExtendedUncPrefix.Length..];
        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)) return path[ExtendedPrefix.Length..];
        return path;
    }

    /// <summary>
    /// „\\?\“-Pfad ohne Laufwerk und ohne UNC (z. B. „\\?\Volume{…}\x“)? <paramref name="readable"/> ist
    /// <see cref="FromExtended"/> von <paramref name="path"/>.
    /// </summary>
    public static bool IsDevicePath(string path, string readable)
        => path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
           && !readable.StartsWith(@"\\", StringComparison.Ordinal)
           && !Path.IsPathFullyQualified(readable);

    /// <summary>Kind-Pfad bilden, ohne den Namen anzufassen (Path.Combine würde nichts kürzen, prüft aber Zeichen).</summary>
    public static string Combine(string directory, string name)
        => directory.EndsWith('\\') ? directory + name : directory + "\\" + name;

    /// <summary>Abschließenden Backslash entfernen, außer bei Laufwerkswurzeln („C:\“).</summary>
    public static string TrimEndSeparator(string path)
    {
        var trimmed = path.TrimEnd('\\');
        if (trimmed.Length == 2 && trimmed[1] == ':') return trimmed + "\\";
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>Enthält der Pfad „.“/„..“-Abschnitte, „/“ oder doppelte Trenner, die erst aufgelöst werden müssen?</summary>
    public static bool NeedsNormalization(string path)
    {
        if (path.Contains('/')) return true;
        int start = path.StartsWith(@"\\", StringComparison.Ordinal) ? 2 : 0;
        if (path.IndexOf(@"\\", start, StringComparison.Ordinal) >= 0) return true;
        foreach (var part in path.Split('\\'))
            if (part is "." or "..") return true;
        return false;
    }

    /// <summary>Liegt <paramref name="path"/> in <paramref name="directory"/> oder ist es selbst (Groß/Klein egal, nur ganze Namen)?</summary>
    public static bool IsSameOrBelow(string path, string directory)
    {
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Length == directory.Length) return true;
        return directory.EndsWith('\\') || path[directory.Length] == '\\';
    }
}
