using Zutritt.Core.Resources;

namespace Zutritt.Core.Reports;

/// <summary>
/// Schreibt Berichtsdateien sicher: erst in eine temporäre Datei im Zielordner, dann ersetzen. So bleibt
/// bei einem Fehler weder eine halbe Datei zurück noch wird ein älterer Bericht zerstört, der z. B.
/// gerade in Excel offen ist.
/// </summary>
internal static class ReportFile
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;

    /// <summary>
    /// Prüft das Ziel, bevor teure Arbeit (Auswertung, Rendern) beginnt, und liefert den vollen Pfad.
    /// </summary>
    /// <exception cref="ReportWriteException">Ungültiger Dateiname oder ein Ordner statt einer Datei.</exception>
    public static string FullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ReportWriteException(path, Texts.Format(Strings.ReportFile_InvalidName, ReportText.Quote(path)), e);
        }
        if (Directory.Exists(full))
            throw new ReportWriteException(full, Texts.Format(Strings.ReportFile_IsFolder, ReportText.Quote(full)));
        // „D:\Berichte\neu\“: sonst würde der Ordner angelegt und das Ersetzen schlüge danach unverständlich fehl
        if (Path.GetFileName(full).Length == 0)
            throw new ReportWriteException(full, Texts.Format(Strings.ReportFile_NoFileName, ReportText.Quote(full)));
        return full;
    }

    /// <summary>
    /// Prüft vor einem langen Scan, ob der Bericht später geschrieben werden kann: gültiger Dateiname, Ordner anlegbar
    /// und beschreibbar (probeweise eine temporäre Datei, die sofort wieder verschwindet). Eine vorhandene Datei, die
    /// gerade in Excel offen ist, fällt erst beim Schreiben auf.
    /// </summary>
    /// <exception cref="ReportWriteException">Das Ziel ist ungültig oder der Ordner nicht beschreibbar.</exception>
    public static string CheckWritable(string path)
    {
        var full = FullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new ReportWriteException(full, Texts.Format(Strings.ReportFile_NoFolder, ReportText.Quote(full)));
        var probe = Path.Combine(dir, $"~{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(dir);
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (UnauthorizedAccessException e)
        {
            TryDelete(probe);
            throw new ReportWriteException(full, Texts.Format(Strings.ReportFile_NoPermission, ReportText.Quote(full)), e);
        }
        catch (IOException e)
        {
            TryDelete(probe);
            throw new ReportWriteException(full, Describe(e, full), e);
        }
        return full;
    }

    public static void Write(string path, Action<Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var full = FullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new ReportWriteException(full, Texts.Format(Strings.ReportFile_NoFolder, ReportText.Quote(full)));
        var temp = Path.Combine(dir, $"~{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(dir);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                // Manche Bibliotheken schließen den Stream selbst
                if (stream.CanWrite) stream.Flush(flushToDisk: true);
            }
            File.Move(temp, full, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new ReportWriteException(full, Describe(e, full), e);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static string Describe(Exception e, string path)
    {
        var file = ReportText.Quote(path);
        int code = e.HResult & 0xFFFF;
        bool exists = File.Exists(path);
        return e switch
        {
            PathTooLongException => Texts.Format(Strings.ReportFile_PathTooLong, file),
            DirectoryNotFoundException => Texts.Format(Strings.ReportFile_FolderMissing, file),
            UnauthorizedAccessException when exists =>
                Texts.Format(Strings.ReportFile_CannotReplace, file),
            UnauthorizedAccessException => Texts.Format(Strings.ReportFile_NoPermission, file),
            IOException when code is ErrorSharingViolation or ErrorLockViolation =>
                Texts.Format(Strings.ReportFile_InUse, file),
            IOException when code is ErrorDiskFull or ErrorHandleDiskFull => Texts.Format(Strings.ReportFile_DiskFull, file),
            _ => Texts.Format(Strings.ReportFile_SaveFailed, file, e.Message),
        };
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Aufräumen ist best effort */ }
    }
}

/// <summary>Berichtsziele prüfen, bevor ein langer Scan beginnt (Befehlszeile).</summary>
public static class ReportTarget
{
    /// <summary>Voller Pfad, wenn der Bericht dort geschrieben werden kann; siehe <see cref="ReportFile.CheckWritable"/>.</summary>
    /// <exception cref="ReportWriteException">Das Ziel ist ungültig oder der Ordner nicht beschreibbar.</exception>
    public static string Check(string path) => ReportFile.CheckWritable(path);
}
