// SPDX-License-Identifier: MIT
using System.Reflection;

namespace Zutritt;

/// <summary>
/// Lizenztexte für die Info-Seite und <c>--info</c>: die eigene MIT-Lizenz (LICENSE) und die der mitgelieferten
/// Bibliotheken (THIRD-PARTY-NOTICES.txt). Beide sind in Zutritt.exe eingebettet – die einzelne Exe zum Herunterladen
/// hat keine Dateien daneben; in ZIP und MSI liegen sie zusätzlich neben der Exe.
/// </summary>
public static class LicenseTexts
{
    public const string LicenseFile = "LICENSE";
    public const string NoticesFile = "THIRD-PARTY-NOTICES.txt";

    /// <summary>Lizenztext von Zutritt, null wenn er in dieser Ausgabe fehlt.</summary>
    public static string? License() => Read(LicenseFile);

    /// <summary>Lizenzen der mitgelieferten Bibliotheken, null wenn sie in dieser Ausgabe fehlen.</summary>
    public static string? ThirdPartyNotices() => Read(NoticesFile);

    /// <summary>Eingebettete Fassung, sonst die Datei neben der Exe.</summary>
    internal static string? Read(string name)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
