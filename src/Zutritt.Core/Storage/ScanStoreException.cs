// SPDX-License-Identifier: MIT
using Microsoft.Data.Sqlite;
using Zutritt.Core.Resources;

namespace Zutritt.Core.Storage;

/// <summary>
/// Die Datenbank konnte nicht geöffnet, gelesen oder beschrieben werden. Die Meldung ist in der Sprache der Oberfläche
/// und kann direkt angezeigt werden; die technische Ursache steht in <see cref="Exception.InnerException"/>, der Code für
/// den Support in <see cref="ErrorCode"/>.
/// </summary>
public sealed class ScanStoreException : Exception, IHasErrorCode
{
    public ScanStoreException(string message, string errorCode = ErrorCodes.DatabaseRead) : base(message) => ErrorCode = errorCode;

    public ScanStoreException(string message, Exception inner, string errorCode = ErrorCodes.DatabaseRead) : base(message, inner) =>
        ErrorCode = errorCode;

    /// <summary>Fehlercode (ZUT-02xx), siehe <see cref="ErrorCodes"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Die Datenbank war nur gerade von einem anderen Programm gesperrt (z. B. speichert die Oberfläche gleichzeitig
    /// einen großen Lauf) – ein späterer Versuch kann gelingen.
    /// </summary>
    public bool IsLocked => InnerException is SqliteException { SqliteErrorCode: var code } && (code & 0xFF) is 5 or 6;

    /// <summary>Deutscher Grund zu einem SQLite-Fehler, ohne den Pfad.</summary>
    internal static string Describe(SqliteException ex) => (ex.SqliteErrorCode & 0xFF) switch
    {
        // Primärcode (untere 8 Bit), erweiterte Codes tragen Zusatzinformation in den oberen Bits
        5 or 6 => Strings.Store_Locked,
        8 => Strings.Store_ReadOnly,
        10 => Strings.Store_IoError,
        11 => Strings.Store_Corrupt,
        13 => Strings.Store_DiskFull,
        // Folgt beim Öffnen auf „… kann nicht geöffnet werden.“ – deshalb hier der Grund, nicht derselbe Satz noch einmal
        14 => Strings.Store_CantOpen,
        19 => Texts.Format(Strings.Store_Constraint, ex.Message),
        23 => Strings.Store_AccessDenied,
        26 => Strings.Store_NotSqlite,
        _ => Texts.Format(Strings.Store_SqliteError, ex.SqliteErrorCode, ex.Message),
    };
}
