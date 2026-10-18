using System.Globalization;
using System.Text;

namespace Zutritt.Core.Diagnostics;

/// <summary>
/// Protokoll für den Support: eine Datei je Tag unter <c>%LocalAppData%\Zutritt\logs</c> (Linux: ~/.local/share/zutritt/logs),
/// höchstens 7 Tage und 1 MB je Tag. Steht drin: Start mit Version und System, Beginn und Ende von Scans mit Zahlen und
/// Dauer, Fehlermeldungen und Ausnahmetypen. Nie drin: Zugriffslisten, Mitglieder, Kontennamen – die gehören in die
/// Datenbank, nicht in eine Datei, die man weitergibt. Fehlerarten eines Scans schreibt der Scanner deshalb erst nach dem
/// <see cref="Anonymizer"/>.
/// </summary>
/// <remarks>
/// Schreibt erst nach <see cref="Start"/> (die App ruft es beim Start auf; Tests nicht). Fehler beim Schreiben werden
/// verschluckt – das Protokoll darf nie selbst einen Absturz auslösen.
/// </remarks>
public static class Log
{
    /// <summary>So viele Tage bleiben Protokolle liegen.</summary>
    public const int KeepDays = 7;

    /// <summary>Höchstgröße je Tagesdatei; darüber wird für den Tag nichts mehr geschrieben.</summary>
    public const long MaxBytesPerDay = 1024 * 1024;

    private const string Prefix = "zutritt-";
    private static readonly Lock Gate = new();
    private static string? _folder;
    private static Func<DateTime> _now = () => DateTime.Now;

    /// <summary>Ordner der Protokolldateien, null solange nicht gestartet.</summary>
    public static string? Folder
    {
        get { lock (Gate) return _folder; }
    }

    /// <summary>Standardordner: logs im Datenordner des Benutzers.</summary>
    public static string DefaultFolder => Path.Combine(Storage.DatabaseLocation.DataFolder, "logs");

    /// <summary>Protokoll in <paramref name="folder"/> einschalten und alte Dateien aufräumen.</summary>
    public static void Start(string folder)
    {
        lock (Gate)
        {
            _folder = folder;
            try
            {
                Directory.CreateDirectory(folder);
                Cleanup(folder, _now());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Ohne Protokoll weiterarbeiten
            }
        }
    }

    /// <summary>Protokoll ausschalten (Tests).</summary>
    internal static void Stop()
    {
        lock (Gate) _folder = null;
    }

    /// <summary>Uhr für Tests.</summary>
    internal static void UseClock(Func<DateTime> now)
    {
        lock (Gate) _now = now;
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    /// <summary>Fehler mit Ausnahmetyp und Meldung; der Stapel nur bei unerwarteten Fehlern (<paramref name="withStack"/>).</summary>
    public static void Error(string message, Exception? exception = null, bool withStack = false)
    {
        if (exception is null)
        {
            Write("FEHLER", message);
            return;
        }
        var text = new StringBuilder(message).Append(": ").Append(exception.GetType().FullName).Append(": ").Append(OneLine(exception.Message));
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            text.Append(" | ").Append(inner.GetType().FullName).Append(": ").Append(OneLine(inner.Message));
        if (withStack && exception.StackTrace is { } stack) text.Append('\n').Append(stack);
        Write("FEHLER", text.ToString());
    }

    /// <summary>Protokolldateien der letzten <paramref name="days"/> Tage, älteste zuerst.</summary>
    public static IReadOnlyList<string> Files(int days = KeepDays)
    {
        string? folder;
        DateTime now;
        lock (Gate)
        {
            folder = _folder;
            now = _now();
        }
        if (folder is null || !Directory.Exists(folder)) return [];
        var from = now.Date.AddDays(-(days - 1));
        return Directory.EnumerateFiles(folder, Prefix + "*.log")
            .Where(f => DateOf(f) is { } d && d >= from)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (_folder is null) return;
            try
            {
                var now = _now();
                var path = Path.Combine(_folder, FileName(now));
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= MaxBytesPerDay) return;
                var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} {level,-6} {message.Replace("\r", "", StringComparison.Ordinal)}{Environment.NewLine}";
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Protokoll ist nur eine Hilfe
            }
        }
    }

    internal static string FileName(DateTime day) => Prefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";

    private static DateTime? DateOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length == Prefix.Length + 10
               && DateTime.TryParseExact(name[Prefix.Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static void Cleanup(string folder, DateTime now)
    {
        var keepFrom = now.Date.AddDays(-(KeepDays - 1));
        foreach (var file in Directory.EnumerateFiles(folder, Prefix + "*.log"))
        {
            if (DateOf(file) is { } d && d < keepFrom)
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static string OneLine(string text) => text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
