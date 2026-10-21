// SPDX-License-Identifier: MIT
using Zutritt.Core.Resources;

namespace Zutritt.Core;

/// <summary>Eine Ausnahme mit Fehlercode für den Support (Richtlinie 13, Regel 9).</summary>
public interface IHasErrorCode
{
    /// <summary>Fehlercode wie „ZUT-0203“, unabhängig von der Sprache der Meldung.</summary>
    string ErrorCode { get; }
}

/// <summary>
/// Fehlercodes, die Zutritt zusätzlich zur übersetzten Meldung zeigt, damit der Support sie unabhängig von der Sprache
/// zuordnen kann (Richtlinie 13, Regel 9). Die Liste mit Bedeutung steht in docs/error-codes.md – Codes nie umnummerieren,
/// nur neue anhängen.
/// </summary>
public static class ErrorCodes
{
    public const string Unexpected = "ZUT-0001";
    public const string FileAccess = "ZUT-0002";
    public const string Usage = "ZUT-0101";
    public const string DatabaseOpen = "ZUT-0201";
    public const string DatabaseNotWritable = "ZUT-0202";
    public const string DatabaseSave = "ZUT-0203";
    public const string DatabaseRead = "ZUT-0204";
    public const string DatabaseDelete = "ZUT-0205";
    public const string DatabaseInvalid = "ZUT-0206";
    public const string DatabaseNewer = "ZUT-0207";
    public const string DatabaseForeign = "ZUT-0208";
    public const string DatabaseWalOverShare = "ZUT-0209";
    public const string ReportWrite = "ZUT-0301";
    public const string ScanFailed = "ZUT-0401";
    public const string ComparisonFailed = "ZUT-0402";
    public const string DiagnosticsPackage = "ZUT-0501";
    public const string SettingsSave = "ZUT-0502";
    public const string LinkOpen = "ZUT-0503";

    /// <summary>Alle Codes (für Tests und Doku).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Unexpected, FileAccess, Usage, DatabaseOpen, DatabaseNotWritable, DatabaseSave, DatabaseRead, DatabaseDelete,
        DatabaseInvalid, DatabaseNewer, DatabaseForeign, DatabaseWalOverShare, ReportWrite, ScanFailed, ComparisonFailed,
        DiagnosticsPackage, SettingsSave, LinkOpen,
    ];

    /// <summary>Code einer Ausnahme: ihr eigener, sonst <paramref name="fallback"/> bzw. der für Dateizugriffe.</summary>
    public static string Of(Exception ex, string fallback = Unexpected) => ex switch
    {
        IHasErrorCode coded => coded.ErrorCode,
        IOException or UnauthorizedAccessException when fallback == Unexpected => FileAccess,
        _ => fallback,
    };

    /// <summary>Meldung mit Code dahinter, z. B. „Die Datenbank ist schreibgeschützt. (Fehler ZUT-0202)“.</summary>
    public static string WithCode(string message, string code) => Texts.Format(Strings.Error_WithCode, message, code);

    /// <summary>Meldung der Ausnahme mit ihrem Code.</summary>
    public static string Describe(Exception ex, string fallback = Unexpected) => WithCode(ex.Message, Of(ex, fallback));
}
