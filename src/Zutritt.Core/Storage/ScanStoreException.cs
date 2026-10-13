using Microsoft.Data.Sqlite;

namespace Zutritt.Core.Storage;

/// <summary>
/// Die Datenbank konnte nicht geöffnet, gelesen oder beschrieben werden. Die Meldung ist auf Deutsch
/// und kann der Oberfläche direkt angezeigt werden; die technische Ursache steht in <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class ScanStoreException : Exception
{
    public ScanStoreException(string message) : base(message) { }

    public ScanStoreException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Deutscher Grund zu einem SQLite-Fehler, ohne den Pfad.</summary>
    internal static string Describe(SqliteException ex) => (ex.SqliteErrorCode & 0xFF) switch
    {
        // Primärcode (untere 8 Bit), erweiterte Codes tragen Zusatzinformation in den oberen Bits
        5 or 6 => "Die Datenbank ist von einem anderen Programm gesperrt. Bitte später erneut versuchen.",
        8 => "Die Datenbank ist schreibgeschützt (Dateirechte oder schreibgeschützter Datenträger).",
        10 => "Ein-/Ausgabefehler beim Zugriff auf die Datenbankdatei.",
        11 => "Die Datenbankdatei ist beschädigt.",
        13 => "Der Datenträger ist voll.",
        14 => "Die Datenbankdatei kann nicht geöffnet werden (Pfad und Zugriffsrechte prüfen; evtl. hält ein anderes Programm sie gesperrt).",
        19 => $"Die Daten verletzen eine Regel der Datenbank ({ex.Message}).",
        23 => "Zugriff auf die Datenbank verweigert.",
        26 => "Die Datei ist keine SQLite-Datenbank.",
        _ => $"SQLite-Fehler {ex.SqliteErrorCode}: {ex.Message}",
    };
}
