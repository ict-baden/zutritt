// SPDX-License-Identifier: MIT
using System.ComponentModel;
using Zutritt.Core.Resources;

namespace Zutritt.Platform.Windows.Scanning;

/// <summary>Kurztexte (in der Sprache der Oberfläche) für Win32- und Netzwerkfehler, wie sie in Fehlerliste und Bericht erscheinen.</summary>
internal static class Win32Error
{
    public static string Describe(int code) => code switch
    {
        2 or 3 => Strings.Win_PathNotFound,
        5 => Strings.Win_AccessDenied,
        15 => Strings.Win_DriveNotFound,
        21 => Strings.Win_NotReady,
        32 => Strings.Win_InUse,
        50 => Strings.Win_NotSupported,
        51 => Strings.Win_ServerUnreachable,
        53 => Strings.Win_NetPathNotFound,
        59 => Strings.Win_UnexpectedNetError,
        64 => Strings.Win_NetNameDeleted,
        65 => Strings.Win_NetAccessDenied,
        67 => Strings.Win_NetNameNotFound,
        123 => Strings.Win_InvalidName,
        161 => Strings.Win_InvalidPath,
        206 => Strings.Win_PathTooLong,
        267 => Strings.Win_NotAFolder,
        1219 => Strings.Win_CredentialConflict,
        1222 => Strings.Win_NoNetwork,
        1231 => Strings.Win_NetUnreachable,
        1311 => Strings.Win_NoLogonServers,
        1326 => Strings.Win_LogonFailure,
        1722 => Strings.Win_RpcUnavailable,
        1920 => Strings.Win_CantAccessFile,
        1921 => Strings.Win_CantResolveLink,
        2114 => Strings.Win_ServerServiceStopped,
        2310 => Strings.Win_ShareNotFound,
        _ => Fallback(code),
    };

    // Unbekannte Codes: Windows-Text (Sprache des Systems) plus Nummer, damit man nachschlagen kann.
    private static string Fallback(int code)
    {
        var text = new Win32Exception(code).Message.Trim().TrimEnd('.');
        return Texts.Format(Strings.Win_ErrorCode, code, text);
    }
}
