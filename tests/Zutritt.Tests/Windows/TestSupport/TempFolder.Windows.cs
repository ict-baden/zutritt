using System.Security.AccessControl;
using System.Security.Principal;

namespace Zutritt.Tests.TestSupport;

/// <summary>Rechte setzen auf Testordnern – nur unter Windows.</summary>
public sealed partial class TempFolder
{
    /// <summary>Vererbung abschalten (vorhandene geerbte Einträge als explizite übernehmen).</summary>
    public static void BreakInheritance(string dir, bool keepInherited = true)
    {
        var info = new DirectoryInfo(dir);
        var sec = info.GetAccessControl();
        sec.SetAccessRuleProtection(true, keepInherited);
        info.SetAccessControl(sec);
    }

    public static void Grant(string dir, IdentityReference who, FileSystemRights rights, AccessControlType type = AccessControlType.Allow)
    {
        var info = new DirectoryInfo(dir);
        var sec = info.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(who, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, type));
        info.SetAccessControl(sec);
    }

    partial void RestoreOwnAccess()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        foreach (var d in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories).Prepend(Path))
        {
            try
            {
                var info = new DirectoryInfo(d);
                var sec = info.GetAccessControl();
                sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
                info.SetAccessControl(sec);
            }
            catch { }
        }
    }
}
