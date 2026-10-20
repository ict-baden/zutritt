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
            throw new ReportWriteException(path, $"Der Dateiname {ReportText.Quote(path)} ist ungültig.", e);
        }
        if (Directory.Exists(full))
            throw new ReportWriteException(full, $"{ReportText.Quote(full)} ist ein Ordner – bitte einen Dateinamen angeben.");
        // „D:\Berichte\neu\“: sonst würde der Ordner angelegt und das Ersetzen schlüge danach unverständlich fehl
        if (Path.GetFileName(full).Length == 0)
            throw new ReportWriteException(full, $"{ReportText.Quote(full)} enthält keinen Dateinamen – bitte einen Dateinamen angeben.");
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
        var dir = Path.GetDirectoryName(full) ?? throw new ReportWriteException(full, $"{ReportText.Quote(full)} hat keinen Ordner.");
        var probe = Path.Combine(dir, $"~{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(dir);
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (UnauthorizedAccessException e)
        {
            TryDelete(probe);
            throw new ReportWriteException(full, $"Keine Berechtigung, {ReportText.Quote(full)} zu schreiben.", e);
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
        var dir = Path.GetDirectoryName(full) ?? throw new ReportWriteException(full, $"{ReportText.Quote(full)} hat keinen Ordner.");
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
            PathTooLongException => $"Der Pfad {file} ist zu lang.",
            DirectoryNotFoundException => $"Der Ordner für {file} existiert nicht und konnte nicht angelegt werden.",
            UnauthorizedAccessException when exists =>
                $"Die Datei {file} kann nicht ersetzt werden – ist sie noch geöffnet (z. B. in Excel) oder fehlt die Schreibberechtigung?",
            UnauthorizedAccessException => $"Keine Berechtigung, {file} zu schreiben.",
            IOException when code is ErrorSharingViolation or ErrorLockViolation =>
                $"Die Datei {file} ist in einem anderen Programm geöffnet (z. B. Excel oder einem PDF-Betrachter). Bitte schließen und erneut versuchen.",
            IOException when code is ErrorDiskFull or ErrorHandleDiskFull => $"Nicht genug Speicherplatz für {file}.",
            _ => $"Der Bericht {file} konnte nicht gespeichert werden: {e.Message}",
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
