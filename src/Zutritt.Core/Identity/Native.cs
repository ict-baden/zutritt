using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zutritt.Core.Identity;

/// <summary>
/// Win32-Aufrufe für die Kontenauflösung: LSA (SID → Name, Domänenzugehörigkeit) und die lokale
/// Kontendatenbank (SAM) über NetApi – auch auf entfernten Servern.
/// </summary>
internal static unsafe partial class Native
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_BAD_NETPATH = 53;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_MORE_DATA = 234;
    public const int ERROR_NO_LOGON_SERVERS = 1311;
    public const int ERROR_NONE_MAPPED = 1332;
    public const int ERROR_INVALID_SID = 1337;
    public const int ERROR_NO_SUCH_DOMAIN = 1355;
    public const int ERROR_NO_SUCH_ALIAS = 1376;
    public const int RPC_S_SERVER_UNAVAILABLE = 1722;
    public const int RPC_S_CALL_FAILED = 1726;
    public const int RPC_S_CALL_FAILED_DNE = 1727;
    public const int ERROR_TRUSTED_DOMAIN_FAILURE = 1788;
    public const int ERROR_TRUSTED_RELATIONSHIP_FAILURE = 1789;
    public const int NERR_GroupNotFound = 2220;
    public const int NERR_UserNotFound = 2221;

    public const uint MAX_PREFERRED_LENGTH = uint.MaxValue;
    public const uint UF_ACCOUNTDISABLE = 0x0002;

    // ---------- LSA: SID → Name ----------

    /// <summary>SID_NAME_USE aus winnt.h.</summary>
    public enum SidNameUse
    {
        User = 1,
        Group = 2,
        Domain = 3,
        Alias = 4,
        WellKnownGroup = 5,
        DeletedAccount = 6,
        Invalid = 7,
        Unknown = 8,
        Computer = 9,
        Label = 10,
        LogonSession = 11,
    }

    [LibraryImport("advapi32.dll", EntryPoint = "LookupAccountSidW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupAccountSid(string? systemName, byte* sid, char* name, ref uint nameLength,
        char* referencedDomainName, ref uint domainLength, out SidNameUse use);

    // ---------- LSA-Richtlinie: ist der Rechner in einer Domäne? ----------

    public const uint POLICY_VIEW_LOCAL_INFORMATION = 0x00000001;
    public const int PolicyDnsDomainInformation = 12;

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;

        public readonly string Value => Buffer == 0 ? "" : new string((char*)Buffer, 0, Length / sizeof(char));
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_OBJECT_ATTRIBUTES
    {
        public uint Length;
        public nint RootDirectory;
        public nint ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POLICY_DNS_DOMAIN_INFO
    {
        public LSA_UNICODE_STRING Name;
        public LSA_UNICODE_STRING DnsDomainName;
        public LSA_UNICODE_STRING DnsForestName;
        public Guid DomainGuid;
        public nint Sid;
    }

    [LibraryImport("advapi32.dll")]
    public static partial int LsaOpenPolicy(LSA_UNICODE_STRING* systemName, ref LSA_OBJECT_ATTRIBUTES objectAttributes, uint desiredAccess, out nint policyHandle);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaQueryInformationPolicy(nint policyHandle, int informationClass, out nint buffer);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaFreeMemory(nint buffer);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaClose(nint policyHandle);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaNtStatusToWinError(int status);

    // ---------- NetApi: lokale Konten und Gruppen (SAM) ----------

    [StructLayout(LayoutKind.Sequential)]
    public struct LOCALGROUP_MEMBERS_INFO_0
    {
        public nint Sid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GROUP_USERS_INFO_0
    {
        public nint Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_INFO_23
    {
        public nint Name;
        public nint FullName;
        public nint Comment;
        public uint Flags;
        public nint UserSid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_MODALS_INFO_2
    {
        public nint DomainName;
        public nint DomainId;
    }

    public const uint SV_TYPE_DOMAIN_CTRL = 0x00000008;
    public const uint SV_TYPE_DOMAIN_BAKCTRL = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVER_INFO_101
    {
        public uint PlatformId;
        public nint Name;
        public uint VersionMajor;
        public uint VersionMinor;
        public uint Type;
        public nint Comment;
    }

    // NetApi-Funktionen liefern den Fehlercode direkt zurück (NET_API_STATUS), nicht über GetLastError.

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetServerGetInfo(string? serverName, uint level, out nint buffer);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetLocalGroupGetMembers(string? serverName, string localGroupName, uint level, out nint buffer,
        uint prefMaxLength, out uint entriesRead, out uint totalEntries, ref nuint resumeHandle);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetGroupGetUsers(string? serverName, string groupName, uint level, out nint buffer,
        uint prefMaxLength, out uint entriesRead, out uint totalEntries, ref nuint resumeHandle);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserGetInfo(string? serverName, string userName, uint level, out nint buffer);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetUserModalsGet(string? serverName, uint level, out nint buffer);

    [LibraryImport("netapi32.dll")]
    public static partial int NetApiBufferFree(nint buffer);

    /// <summary>Puffer einer NetApi-Funktion freigeben (auch nach Fehlern, falls einer zugeteilt wurde).</summary>
    public static void Free(nint buffer)
    {
        if (buffer != 0) NetApiBufferFree(buffer);
    }

    /// <summary>
    /// Fehlercode als deutscher Text für Fehlermeldungen im Bericht. Häufige Fälle fest übersetzt, damit der
    /// Bericht nicht von der Sprache der Windows-Installation abhängt.
    /// </summary>
    public static string Describe(int code)
    {
        var text = code switch
        {
            ERROR_ACCESS_DENIED => "Zugriff verweigert",
            ERROR_BAD_NETPATH => "Netzwerkpfad nicht gefunden",
            ERROR_NO_LOGON_SERVERS => "Kein Anmeldeserver (Domänencontroller) erreichbar",
            ERROR_NONE_MAPPED => "Konto nicht vorhanden",
            ERROR_INVALID_SID => "Ungültige SID",
            ERROR_NO_SUCH_DOMAIN => "Domäne nicht gefunden",
            ERROR_NO_SUCH_ALIAS => "Gruppe nicht gefunden",
            RPC_S_SERVER_UNAVAILABLE => "RPC-Server nicht verfügbar",
            RPC_S_CALL_FAILED or RPC_S_CALL_FAILED_DNE => "RPC-Aufruf fehlgeschlagen",
            ERROR_TRUSTED_DOMAIN_FAILURE => "Vertrauensstellung zur Domäne fehlgeschlagen",
            ERROR_TRUSTED_RELATIONSHIP_FAILURE => "Vertrauensstellung zwischen Rechner und Domäne fehlgeschlagen",
            NERR_GroupNotFound => "Gruppe nicht gefunden",
            NERR_UserNotFound => "Benutzer nicht gefunden",
            _ => new Win32Exception(code).Message.TrimEnd('.', ' ', '\r', '\n'),
        };
        return $"{text} (Fehler {code})";
    }
}
