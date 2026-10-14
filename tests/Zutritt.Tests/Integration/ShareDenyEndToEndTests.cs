using System.Diagnostics;
using System.Security.Principal;
using Zutritt.Core.Analysis;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Scanning.FsTestHelper;

namespace Zutritt.Tests.Integration;

/// <summary>
/// Verweigern-Einträge der Freigabe über Scanner und Auswertung hinweg: echte Freigabe, Verweigern so gesetzt wie ein
/// Administrator es tut, Scan über \\localhost, Ergebnis aus <see cref="EffectiveAccess"/> – und als Gegenprobe der
/// tatsächliche Zugriff über die Freigabe. Bedeutung laut <see cref="ShareAce"/>: „ab dieser Stufe verweigert“.
/// </summary>
public sealed class ShareDenyEndToEndTests
{
    private static readonly string Me = WindowsIdentity.GetCurrent().User!.Value;

    private static async Task<ScanResult> ScanAsync(string unc)
    {
        using var resolver = IdentityResolver.CreateDefault();
        return await new Scanner(resolver).RunAsync(new ScanRequest { Targets = [unc], MaxDepth = 0 });
    }

    [AdminFact]
    public async Task Verweigern_Aendern_per_NetShareSetInfo_laesst_Lesen_zu()
    {
        using var tmp = new TempFolder();
        WriteFile(Path.Combine(tmp.Path, "daten.txt"), "inhalt");
        using var share = new TestShare(tmp.Path, "D:(A;;FA;;;WD)");
        // Wie das Häkchen „Verweigern: Ändern“ in den Freigabe-Berechtigungen (0x10116)
        share.SetSddl($"D:(D;;0x10116;;;{Me})(A;;FA;;;WD)");
        var unc = $@"\\localhost\{share.Name}";

        var result = await ScanAsync(unc);

        var info = Assert.Single(result.Shares);
        Assert.Equal([new ShareAce(Me, ShareAccess.Aendern, true), new ShareAce(SidCatalog.Everyone, ShareAccess.Vollzugriff, false)], info.Permissions);
        var root = Assert.Single(result.Folders);
        var grant = Assert.Single(EffectiveAccess.ForPerson(result, Me), g => g.Path == root.Path);
        Assert.Equal(AccessLevel.Vollzugriff, grant.Ntfs);
        Assert.Equal(AccessLevel.Lesen, grant.ViaShare);
        Assert.Equal(AccessLevel.Lesen, grant.Effective);
        Assert.True(grant.ReducedByDeny);
        Assert.Contains(EffectiveAccess.PeopleForFolder(result, root), g => g.Sid == Me && g.Effective == AccessLevel.Lesen);

        // Gegenprobe mit Windows: über die Freigabe lesen geht, schreiben nicht
        Assert.Equal("inhalt", File.ReadAllText(Path.Combine(unc, "daten.txt")));
        Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(unc, "neu.txt"), "x"));
    }

    [AdminFact]
    public async Task Verweigern_Vollzugriff_per_Block_SmbShareAccess_sperrt_alles()
    {
        using var tmp = new TempFolder();
        WriteFile(Path.Combine(tmp.Path, "daten.txt"), "inhalt");
        using var share = new TestShare(tmp.Path, "D:(A;;FA;;;WD)");
        BlockSmbShareAccess(share.Name, WindowsIdentity.GetCurrent().Name);
        var unc = $@"\\localhost\{share.Name}";

        var result = await ScanAsync(unc);

        // Verweigern „Vollzugriff“ (0x1F01FF) sperrt schon das Lesen – gespeichert als „ab Lesen“
        var info = Assert.Single(result.Shares);
        Assert.Contains(new ShareAce(Me, ShareAccess.Lesen, true), info.Permissions);
        var root = Assert.Single(result.Folders);
        var ctx = new AccessContext(result);
        Assert.Equal(ShareAccess.Keine, ctx.ShareAccessFor(root, ctx.TokenOf(Me, LogonKind.Network)));
        Assert.DoesNotContain(EffectiveAccess.ForPerson(result, Me), g => g.Path == root.Path);
        Assert.DoesNotContain(EffectiveAccess.PeopleForFolder(result, root), g => g.Sid == Me);
        // Die anderen (über „Jeder“) behalten ihren Zugriff
        Assert.Equal(ShareAccess.Vollzugriff, ctx.ShareAccessFor(root, ctx.TokenOf(SidCatalog.AuthenticatedUsers, LogonKind.Network)));

        // Gegenprobe mit Windows: nicht einmal lesen
        Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(Path.Combine(unc, "daten.txt")));
    }

    /// <summary>Wie ein Administrator es tut – „Block-SmbShareAccess“ trägt „Verweigern: Vollzugriff“ ein.</summary>
    private static void BlockSmbShareAccess(string shareName, string account)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["ZT_SHARE"] = shareName;
        psi.Environment["ZT_ACCOUNT"] = account;
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command",
                     "Block-SmbShareAccess -Name $env:ZT_SHARE -AccountName $env:ZT_ACCOUNT -Force -ErrorAction Stop | Out-Null" })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000)) { p.Kill(); throw new TimeoutException("Block-SmbShareAccess hängt"); }
        Assert.True(p.ExitCode == 0, $"Block-SmbShareAccess fehlgeschlagen: {output.Result}{error.Result}");
    }
}
