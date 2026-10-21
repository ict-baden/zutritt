// SPDX-License-Identifier: MIT
namespace Zutritt.Core.Storage;

/// <summary>Wo die Datenbank liegt und ob sie über das Netz erreicht wird.</summary>
internal static class DatabaseLocation
{
    public const string FileName = "zutritt.db";

    /// <summary>
    /// Datenordner im Profil: „Zutritt“ unter Windows (%LocalAppData%\Zutritt) und macOS
    /// (~/Library/Application Support/Zutritt), unter Linux klein geschrieben (~/.local/share/zutritt) – wie in den
    /// Richtlinien für Kennungsnamen (12) festgelegt.
    /// </summary>
    public static string FolderName => OperatingSystem.IsLinux() ? "zutritt" : "Zutritt";

    /// <summary>
    /// Immer im Profil des Benutzers (%LocalAppData%\Zutritt). Ein Lauf ist eine Landkarte der Berechtigungen
    /// eines Fileservers; in %ProgramData% könnte sie über die geerbten Standardrechte jeder lokale Benutzer lesen.
    /// </summary>
    public static string Default => Path.Combine(DataFolder, FileName);

    /// <summary>Datenordner des Benutzers (Datenbank, Protokolle).</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary>UNC-Pfad oder verbundenes Netzlaufwerk.</summary>
    public static bool IsNetworkPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
        if (fullPath.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)) fullPath = fullPath[4..];
        else if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        try
        {
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
