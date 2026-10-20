// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zutritt.Platform.Windows.Scanning;

/// <summary>Win32-Aufrufe des Scanners: Freigaben (netapi32), Sicherheitsbeschreibungen (advapi32), Dateien (kernel32).</summary>
internal static unsafe partial class Native
{
    // ---------- Fehlercodes ----------
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_NO_MORE_FILES = 18;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_MORE_DATA = 234;
    public const int NERR_ServerNotStarted = 2114;
    public const int NERR_NetNameNotFound = 2310;

    // ---------- netapi32: Freigaben ----------
    public const uint STYPE_MASK = 0x000000FF;
    public const uint STYPE_DISKTREE = 0;
    public const uint STYPE_SPECIAL = 0x80000000;
    public const int MAX_PREFERRED_LENGTH = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHARE_INFO_1
    {
        public nint shi1_netname;
        public uint shi1_type;
        public nint shi1_remark;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHARE_INFO_502
    {
        public nint shi502_netname;
        public uint shi502_type;
        public nint shi502_remark;
        public uint shi502_permissions;
        public uint shi502_max_uses;
        public uint shi502_current_uses;
        public nint shi502_path;
        public nint shi502_passwd;
        public uint shi502_reserved;
        public nint shi502_security_descriptor;
    }

    // Die Net*-Funktionen liefern ihren Fehler als Rückgabewert, nicht über GetLastError.
    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetShareEnum(string? servername, int level, out nint bufptr, int prefmaxlen,
        out int entriesread, out int totalentries, ref int resumeHandle);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetShareGetInfo(string? servername, string netname, int level, out nint bufptr);

    [LibraryImport("netapi32.dll")]
    public static partial int NetApiBufferFree(nint buffer);

    // ---------- advapi32: Sicherheitsbeschreibungen ----------
    public const uint OWNER_SECURITY_INFORMATION = 0x00000001;
    public const uint GROUP_SECURITY_INFORMATION = 0x00000002;
    public const uint DACL_SECURITY_INFORMATION = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    public struct GENERIC_MAPPING
    {
        public uint GenericRead;
        public uint GenericWrite;
        public uint GenericExecute;
        public uint GenericAll;
    }

    /// <summary>Stellt fest, welche Einträge einer Zugriffsliste im NT4-Stil (ohne Vererbungskennzeichen) geerbt sind.</summary>
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertToAutoInheritPrivateObjectSecurity(byte* parentDescriptor, byte* currentSecurityDescriptor,
        out nint newSecurityDescriptor, nint objectType, byte isDirectoryObject, ref GENERIC_MAPPING genericMapping);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyPrivateObjectSecurity(ref nint objectDescriptor);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsValidSecurityDescriptor(nint securityDescriptor);

    [LibraryImport("advapi32.dll")]
    public static partial uint GetSecurityDescriptorLength(nint securityDescriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetKernelObjectSecurity(SafeFileHandle handle, uint requestedInformation,
        byte* securityDescriptor, uint length, out uint lengthNeeded);

    // ---------- ntdll: Öffnen mit genau den angeforderten Rechten ----------
    // CreateFileW verlangt immer zusätzlich SYNCHRONIZE und FILE_READ_ATTRIBUTES. Wer nur „Berechtigungen lesen“ darf
    // (z. B. als Besitzer), bekäme damit „Zugriff verweigert“, obwohl der Explorer die Rechte anzeigt.
    public const uint OBJ_CASE_INSENSITIVE = 0x00000040;
    public const uint FILE_OPEN_FOR_BACKUP_INTENT = 0x00004000;
    public const uint FILE_OPEN_REPARSE_POINT = 0x00200000;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_ATTRIBUTES
    {
        public uint Length;
        public nint RootDirectory;
        public UNICODE_STRING* ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        public nint Status;
        public nint Information;
    }

    [LibraryImport("ntdll.dll")]
    public static partial int NtOpenFile(out SafeFileHandle fileHandle, uint desiredAccess, OBJECT_ATTRIBUTES* objectAttributes,
        IO_STATUS_BLOCK* ioStatusBlock, uint shareAccess, uint openOptions);

    [LibraryImport("ntdll.dll")]
    public static partial uint RtlNtStatusToDosError(int status);

    // ---------- kernel32: Dateien und Ordner ----------
    public const uint READ_CONTROL = 0x00020000;
    public const uint FILE_SHARE_READ_WRITE_DELETE = 0x00000007;
    public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

    public const int FindExInfoBasic = 1;
    public const int FindExSearchNameMatch = 0;
    public const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;

    /// <summary>Bit 29 im Reparse-Tag: Der Eintrag ist nur ein Verweis auf etwas anderes (Junction, symbolische Verknüpfung, Bereitstellungspunkt).</summary>
    public const uint IO_REPARSE_TAG_NAME_SURROGATE = 0x20000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    // FILETIME besteht aus zwei DWORDs (Ausrichtung 4) – mit long stünden die Felder versetzt.
    [StructLayout(LayoutKind.Sequential)]
    public struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        /// <summary>Reparse-Tag, wenn FILE_ATTRIBUTE_REPARSE_POINT gesetzt ist.</summary>
        public uint dwReserved0;
        public uint dwReserved1;
        public fixed char cFileName[260];
        public fixed char cAlternateFileName[14];
    }

    public const uint FILE_NAME_NORMALIZED = 0x0;
    public const uint VOLUME_NAME_DOS = 0x0;

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* filePath, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetFileAttributes(string fileName);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFindHandle FindFirstFileEx(string fileName, int infoLevelId, WIN32_FIND_DATAW* findFileData,
        int searchOp, nint searchFilter, uint additionalFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindNextFile(SafeFindHandle findFile, WIN32_FIND_DATAW* findFileData);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindClose(nint findFile);
}

/// <summary>Handle aus FindFirstFileEx, wird mit FindClose geschlossen.</summary>
internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeFindHandle() : base(true) { }

    protected override bool ReleaseHandle() => Native.FindClose(handle);
}
