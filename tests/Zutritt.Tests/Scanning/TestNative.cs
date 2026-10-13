using System.Runtime.InteropServices;

namespace Zutritt.Tests.Scanning;

/// <summary>Win32-Aufrufe, die nur die Tests brauchen (Freigaben anlegen/löschen, Rechte roh setzen).</summary>
internal static class TestNative
{
    public const uint OWNER_SECURITY_INFORMATION = 0x00000001;
    public const uint DACL_SECURITY_INFORMATION = 0x00000004;
    public const uint PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHARE_INFO_502
    {
        public string shi502_netname;
        public uint shi502_type;
        public string? shi502_remark;
        public uint shi502_permissions;
        public uint shi502_max_uses;
        public uint shi502_current_uses;
        public string shi502_path;
        public string? shi502_passwd;
        public uint shi502_reserved;
        public nint shi502_security_descriptor;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareAdd(string? servername, int level, ref SHARE_INFO_502 buf, out int parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareDel(string? servername, string netname, int reserved);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFileSecurity(string fileName, uint securityInformation, byte[] securityDescriptor);

    public const uint WRITE_DAC = 0x00040000;
    public const uint READ_CONTROL = 0x00020000;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetKernelObjectSecurity(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint securityInformation, byte[] securityDescriptor);
}
