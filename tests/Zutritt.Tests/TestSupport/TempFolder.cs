using System.Security.AccessControl;
using System.Security.Principal;

namespace Zutritt.Tests.TestSupport;

/// <summary>Temporärer Ordnerbaum für Tests, wird beim Dispose samt Inhalt gelöscht.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zutritt-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

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

    public void Dispose()
    {
        try
        {
            // Eigene Rechte zurückholen, falls ein Test sich ausgesperrt hat
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
            Directory.Delete(Path, true);
        }
        catch { /* Aufräumen ist best effort */ }
    }
}
