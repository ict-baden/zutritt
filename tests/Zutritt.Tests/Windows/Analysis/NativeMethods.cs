// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Identity;
using Zutritt.Tests.Windows.Scanning;

namespace Zutritt.Tests.Windows.Analysis;

/// <summary>
/// P/Invoke für den Abgleich mit der Zugriffsprüfung von Windows (AuthZ) und das unveränderte Lesen von
/// Sicherheitsbeschreibungen. DllImport statt LibraryImport, weil das Testprojekt keinen unsicheren Code erlaubt.
/// </summary>
internal static class NativeMethods
{
    public const uint AuthzRmFlagNoAudit = 0x1;
    public const uint MaximumAllowed = 0x02000000;
    public const int AuthzContextInfoGroupsSids = 2;
    public const uint OwnerSecurityInformation = 0x1;
    public const uint GroupSecurityInformation = 0x2;
    public const uint DaclSecurityInformation = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AuthzAccessRequest
    {
        public uint DesiredAccess;
        public IntPtr PrincipalSelfSid;
        public IntPtr ObjectTypeList;
        public uint ObjectTypeListLength;
        public IntPtr OptionalArguments;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AuthzAccessReply
    {
        public uint ResultListLength;
        public IntPtr GrantedAccessMask;
        public IntPtr SaclEvaluationResults;
        public IntPtr Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [DllImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AuthzInitializeResourceManager(uint flags, IntPtr dynamicAccessCheck, IntPtr computeDynamicGroups,
        IntPtr freeDynamicGroups, [MarshalAs(UnmanagedType.LPWStr)] string? name, out IntPtr resourceManager);

    [DllImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AuthzInitializeContextFromSid(uint flags, byte[] userSid, IntPtr resourceManager, IntPtr expirationTime,
        Luid identifier, IntPtr dynamicGroupArgs, out IntPtr clientContext);

    [DllImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AuthzAccessCheck(uint flags, IntPtr clientContext, ref AuthzAccessRequest request, IntPtr auditEvent,
        byte[] securityDescriptor, IntPtr optionalSecurityDescriptors, uint optionalSecurityDescriptorCount,
        ref AuthzAccessReply reply, IntPtr accessCheckResults);

    [DllImport("authz.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AuthzGetInformationFromContext(IntPtr clientContext, int infoClass, uint bufferSize, out uint sizeRequired, IntPtr buffer);

    [DllImport("authz.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AuthzFreeContext(IntPtr clientContext);

    [DllImport("authz.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AuthzFreeResourceManager(IntPtr resourceManager);

    [DllImport("advapi32.dll", EntryPoint = "GetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileSecurity(string fileName, uint requestedInformation, byte[]? securityDescriptor, uint length, out uint lengthNeeded);
}
