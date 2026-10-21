// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

/// <summary>
/// Führt Code mit einer Kopie des eigenen Tokens aus, in der das Sicherungs- und Wiederherstellungsrecht
/// (SeBackupPrivilege/SeRestorePrivilege) entfernt oder eingeschaltet ist. Je nachdem, wie der Testhost gestartet
/// wurde, sind diese Rechte aktiv – dann umgeht das Öffnen mit Sicherungssemantik jede Zugriffsliste, und
/// „gesperrte“ Ordner wären lesbar. Die Identitätswechsel fließen über async/Task.Run mit.
/// </summary>
internal static class PrivilegeScope
{
    private const uint TOKEN_DUPLICATE = 0x0002, TOKEN_IMPERSONATE = 0x0004, TOKEN_QUERY = 0x0008, TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002, SE_PRIVILEGE_REMOVED = 0x00000004;
    private const int SecurityImpersonation = 2, TokenImpersonation = 2;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES_2
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES First;
        public LUID_AND_ATTRIBUTES Second;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, nint attributes, int level, int type, out SafeAccessTokenHandle duplicate);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES_2 state, uint length, nint previous, nint returnLength);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    /// <summary>Ohne Sicherungsrecht – so wie eine normal gestartete Anwendung, auch mit Adminrechten.</summary>
    public static T WithoutBackupPrivilege<T>(Func<T> action)
    {
        using var token = CreateToken(SE_PRIVILEGE_REMOVED);
        return WindowsIdentity.RunImpersonated(token, action);
    }

    public static async Task<T> WithoutBackupPrivilegeAsync<T>(Func<Task<T>> action)
    {
        using var token = CreateToken(SE_PRIVILEGE_REMOVED);
        return await WindowsIdentity.RunImpersonatedAsync(token, action);
    }

    /// <summary>Mit eingeschaltetem Sicherungsrecht (braucht Adminrechte).</summary>
    public static async Task<T> WithBackupPrivilegeAsync<T>(Func<Task<T>> action)
    {
        using var token = CreateToken(SE_PRIVILEGE_ENABLED);
        return await WindowsIdentity.RunImpersonatedAsync(token, action);
    }

    private static SafeAccessTokenHandle CreateToken(uint attributes)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, out var process))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "OpenProcessToken");
        using (process)
        {
            if (!DuplicateTokenEx(process, TOKEN_IMPERSONATE | TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES | TOKEN_DUPLICATE, 0,
                    SecurityImpersonation, TokenImpersonation, out var duplicate))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "DuplicateTokenEx");

            if (!LookupPrivilegeValue(null, "SeBackupPrivilege", out var backup) || !LookupPrivilegeValue(null, "SeRestorePrivilege", out var restore))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "LookupPrivilegeValue");
            var state = new TOKEN_PRIVILEGES_2
            {
                PrivilegeCount = 2,
                First = new LUID_AND_ATTRIBUTES { Luid = backup, Attributes = attributes },
                Second = new LUID_AND_ATTRIBUTES { Luid = restore, Attributes = attributes },
            };
            bool ok = AdjustTokenPrivileges(duplicate, false, ref state, 0, 0, 0);
            int error = Marshal.GetLastPInvokeError();
            // Ohne die Rechte im Token (kein Admin) ist „entfernen“ ohnehin erfüllt, „einschalten“ aber nicht.
            if (!ok || (error == ERROR_NOT_ALL_ASSIGNED && attributes == SE_PRIVILEGE_ENABLED))
            {
                duplicate.Dispose();
                throw new Win32Exception(error, "AdjustTokenPrivileges");
            }
            return duplicate;
        }
    }
}

/// <summary>
/// Führt Code als „normaler Benutzer“ aus: eigenes Token mit Administratoren nur noch zum Verweigern und ohne
/// Sonderrechte (wie ein Konto ohne Adminrechte auf dem Server).
/// </summary>
internal static class NonAdminScope
{
    private const uint DISABLE_MAX_PRIVILEGE = 0x1;
    private const uint TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, TOKEN_ASSIGN_PRIMARY = 0x0001, TOKEN_IMPERSONATE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES { public nint Sid; public uint Attributes; }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(SafeAccessTokenHandle existing, uint flags, uint disableSidCount,
        SID_AND_ATTRIBUTES[] sidsToDisable, uint deletePrivilegeCount, nint privilegesToDelete, uint restrictedSidCount,
        nint sidsToRestrict, out SafeAccessTokenHandle newToken);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        using var token = Create();
        return await WindowsIdentity.RunImpersonatedAsync(token, action);
    }

    public static T Run<T>(Func<T> action)
    {
        using var token = Create();
        return WindowsIdentity.RunImpersonated(token, action);
    }

    private static SafeAccessTokenHandle Create()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_IMPERSONATE, out var process))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "OpenProcessToken");
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var bytes = new byte[admins.BinaryLength];
        admins.GetBinaryForm(bytes, 0);
        nint sid = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, sid, bytes.Length);
            using (process)
            {
                if (!CreateRestrictedToken(process, DISABLE_MAX_PRIVILEGE, 1, [new SID_AND_ATTRIBUTES { Sid = sid }], 0, 0, 0, 0, out var restricted))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateRestrictedToken");
                return restricted;
            }
        }
        finally { Marshal.FreeHGlobal(sid); }
    }
}
