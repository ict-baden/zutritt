// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

/// <summary>Hilfen für echte Ordnerbäume: Rechte exakt setzen, Junctions anlegen, Testkonten-SIDs.</summary>
internal static class FsTestHelper
{
    public const string Everyone = "S-1-1-0";
    public const string NetworkService = "S-1-5-20";
    public const string PowerUsers = "S-1-5-32-547";
    public const string BackupOperators = "S-1-5-32-551";
    public const string Administrators = "S-1-5-32-544";
    public const string SystemSid = "S-1-5-18";
    public const string CreatorOwner = "S-1-3-0";
    public const string OwnerRights = "S-1-3-4";

    public static string CurrentUserSid => WindowsIdentity.GetCurrent().User!.Value;

    /// <summary>Zugriffsliste genau so setzen, wie angegeben (ohne Zusammenführen mit geerbten Einträgen).</summary>
    public static void SetRawDacl(string path, string sddl, bool protect = true)
    {
        var raw = new RawSecurityDescriptor(sddl);
        var bytes = new byte[raw.BinaryLength];
        raw.GetBinaryForm(bytes, 0);
        uint info = TestNative.DACL_SECURITY_INFORMATION | (protect ? TestNative.PROTECTED_DACL_SECURITY_INFORMATION : 0);
        if (!TestNative.SetFileSecurity(Extended(path), info, bytes))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SetFileSecurity {path}");
    }

    /// <summary>Zugriffsliste einer Verknüpfung selbst setzen (nicht die ihres Ziels).</summary>
    public static void SetRawDaclNoFollow(string path, string sddl, bool protect = true)
    {
        var raw = new RawSecurityDescriptor(sddl);
        var bytes = new byte[raw.BinaryLength];
        raw.GetBinaryForm(bytes, 0);
        using var handle = TestNative.CreateFile(Extended(path), TestNative.WRITE_DAC | TestNative.READ_CONTROL, 7, 0, TestNative.OPEN_EXISTING,
            TestNative.FILE_FLAG_BACKUP_SEMANTICS | TestNative.FILE_FLAG_OPEN_REPARSE_POINT, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateFile {path}");
        uint info = TestNative.DACL_SECURITY_INFORMATION | (protect ? TestNative.PROTECTED_DACL_SECURITY_INFORMATION : 0);
        if (!TestNative.SetKernelObjectSecurity(handle, info, bytes))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SetKernelObjectSecurity {path}");
    }

    /// <summary>
    /// Explizite Einträge setzen wie der Explorer: geerbte Einträge des Elternordners bleiben, alles ist „automatisch
    /// geerbt“ markiert. Anders als <see cref="Grant"/> auch mit generischen Rechten (GA) möglich.
    /// </summary>
    public static void SetExplicitAces(string path, string sddl)
    {
        var dacl = new RawSecurityDescriptor(sddl).DiscretionaryAcl!;
        var bytes = new byte[dacl.BinaryLength];
        dacl.GetBinaryForm(bytes, 0);
        int status = TestNative.SetNamedDacl(path, bytes);
        if (status != 0) throw new Win32Exception(status, $"SetNamedSecurityInfo {path}");
    }

    /// <summary>Eigene Vollzugriffsrechte zurückholen (ohne vorher zu lesen – das kann gesperrt sein).</summary>
    public static void RestoreAccess(string path)
        => SetRawDacl(path, $"D:(A;OICI;FA;;;{CurrentUserSid})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", protect: false);

    /// <summary>Expliziten, vererbbaren Eintrag über die .NET-API hinzufügen (Windows führt geerbte Einträge zusammen).</summary>
    public static void Grant(string path, string sid, FileSystemRights rights,
        InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags propagation = PropagationFlags.None, AccessControlType type = AccessControlType.Allow)
    {
        var info = new DirectoryInfo(path);
        var sec = info.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), rights, inheritance, propagation, type));
        info.SetAccessControl(sec);
    }

    public static void GrantFile(string path, string sid, FileSystemRights rights)
    {
        var info = new FileInfo(path);
        var sec = info.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), rights, AccessControlType.Allow));
        info.SetAccessControl(sec);
    }

    /// <summary>
    /// Junction (mklink /J) anlegen – braucht keine Adminrechte. Beim Dispose wird nur der Verweis entfernt
    /// (nie das Ziel); ohne das bliebe beim Aufräumen von <see cref="TestSupport.TempFolder"/> der leere Ordner zurück.
    /// </summary>
    public static IDisposable Junction(string link, string target)
    {
        CreateJunction(link, target);
        return new RemoveLink(link);
    }

    private sealed class RemoveLink(string link) : IDisposable
    {
        public void Dispose()
        {
            try { Directory.Delete(link); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void CreateJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
            throw new InvalidOperationException($"mklink /J fehlgeschlagen: {output}");
    }

    public static string Extended(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) ? path : @"\\?\" + path;

    public static void WriteFile(string path, string content = "x") => File.WriteAllText(path, content);
}
