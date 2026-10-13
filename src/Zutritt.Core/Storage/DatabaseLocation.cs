using System.Security;

namespace Zutritt.Core.Storage;

/// <summary>Wo die Datenbank liegt und ob sie über das Netz erreicht wird.</summary>
internal static class DatabaseLocation
{
    public const string FileName = "zutritt.db";
    private const string FolderName = "Zutritt";

    public static string Default => Choose(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Bevorzugt der gemeinsame Ordner (alle Admins des Rechners sehen dieselbe Historie); ist er nicht
    /// beschreibbar – typisch für normale Benutzer, wenn ein Admin die Datei angelegt hat –, das eigene Profil.
    /// </summary>
    internal static string Choose(string programData, string localAppData)
    {
        if (!string.IsNullOrEmpty(programData))
        {
            var dir = Path.Combine(programData, FolderName);
            if (IsWritable(dir)) return Path.Combine(dir, FileName);
        }
        return Path.Combine(localAppData, FolderName, FileName);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, FileName);
            if (File.Exists(file))
            {
                // Ordner beschreibbar heißt nicht Datei beschreibbar: in %ProgramData% gehört sie dem, der sie anlegte
                try
                {
                    using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    // Gerade von einem anderen Programm gesperrt (Sicherung, Virenscanner, Kopie): trotzdem diese
                    // Datei. Ein stiller Wechsel ins Profil würde die Historie spalten – der Lauf landete in einer
                    // zweiten Datenbank, der Vergleich fände keinen Vorgänger. ScanStore meldet die Sperre.
                }
                return true;
            }
            var probe = Path.Combine(dir, $".schreibprobe-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            return false;
        }
    }

    // HRESULT zu ERROR_SHARING_VIOLATION (32) bzw. ERROR_LOCK_VIOLATION (33)
    private static bool IsSharingViolation(IOException ex) => ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

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
