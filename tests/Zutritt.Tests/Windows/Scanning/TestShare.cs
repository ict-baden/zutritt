using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

/// <summary>
/// Echte SMB-Freigabe für Tests (braucht Adminrechte), wird beim Dispose wieder entfernt. Rechte als SDDL –
/// sprachunabhängig, anders als „net share … /GRANT:Jeder,READ“.
/// </summary>
internal sealed class TestShare : IDisposable
{
    public string Name { get; }
    public string LocalPath { get; }
    public string Unc => $@"\\{Environment.MachineName}\{Name}";

    /// <param name="sddl">Sicherheitsbeschreibung der Freigabe, null = ohne (Windows-Standard).</param>
    public TestShare(string localPath, string? sddl, string? name = null, string remark = "Zutritt-Test")
    {
        Name = name ?? "zt-test-" + Guid.NewGuid().ToString("N")[..8];
        LocalPath = localPath;

        nint sd = 0;
        try
        {
            if (sddl is not null)
            {
                var raw = new RawSecurityDescriptor(sddl);
                var bytes = new byte[raw.BinaryLength];
                raw.GetBinaryForm(bytes, 0);
                sd = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, sd, bytes.Length);
            }
            var info = new TestNative.SHARE_INFO_502
            {
                shi502_netname = Name,
                shi502_type = 0, // STYPE_DISKTREE
                shi502_remark = remark,
                shi502_max_uses = uint.MaxValue,
                shi502_path = localPath,
                shi502_security_descriptor = sd,
            };
            int status = TestNative.NetShareAdd(null, 502, ref info, out int parmErr);
            if (status != 0) throw new Win32Exception(status, $"NetShareAdd {Name} fehlgeschlagen (Parameter {parmErr})");
        }
        finally
        {
            if (sd != 0) Marshal.FreeHGlobal(sd);
        }
    }

    /// <summary>Freigabe-Berechtigungen nachträglich setzen (NetShareSetInfo, wie die Oberfläche „Berechtigungen“).</summary>
    public void SetSddl(string sddl)
    {
        var raw = new RawSecurityDescriptor(sddl);
        var bytes = new byte[raw.BinaryLength];
        raw.GetBinaryForm(bytes, 0);
        nint sd = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, sd, bytes.Length);
            var info = new TestNative.SHARE_INFO_1501 { shi1501_security_descriptor = sd };
            int status = TestNative.NetShareSetInfo(null, Name, 1501, ref info, out int parmErr);
            if (status != 0) throw new Win32Exception(status, $"NetShareSetInfo {Name} fehlgeschlagen (Parameter {parmErr})");
        }
        finally
        {
            Marshal.FreeHGlobal(sd);
        }
    }

    public void Dispose() => TestNative.NetShareDel(null, Name, 0);
}
