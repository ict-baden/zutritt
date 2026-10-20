// SPDX-License-Identifier: MIT
using System.Security.AccessControl;
using Zutritt.Platform.Windows;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Tests.TestSupport;
using Zutritt.Platform.Windows.Scanning;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;
using static Zutritt.Tests.Windows.Scanning.FsTestHelper;

namespace Zutritt.Tests.Windows.Scanning;

/// <summary>
/// Scanner über echte Freigaben. Wichtig: „.“ und „\\localhost“ nur mit MaxDepth = 0 – auf diesem Rechner gibt es
/// eine Freigabe „c“ auf C:\, die sonst komplett durchlaufen würde.
/// </summary>
public sealed class ScannerShareTests
{
    private const string ShareSddl = "D:(A;;0x1200a9;;;WD)(A;;FA;;;BA)";
    private static readonly ShareAce[] SharePermissions =
        [new ShareAce("S-1-1-0", ShareAccess.Lesen, false), new ShareAce("S-1-5-32-544", ShareAccess.Vollzugriff, false)];

    private static Scanner Plain() => new(WindowsPlatform.Instance, (_, _) => { }, _ => []);

    private static void BuildTree(TempFolder tmp)
    {
        tmp.Sub("a", "a1");
        tmp.Sub("plain");
        Grant(Path.Combine(tmp.Path, "a"), NetworkService, FileSystemRights.Modify);
    }

    [AdminFact]
    public async Task Scan_ueber_UNC()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);
        using var share = new TestShare(tmp.Path, ShareSddl);
        var unc = $@"\\localhost\{share.Name}";

        var result = await Plain().RunAsync(new ScanRequest { Targets = [unc] });

        Assert.Empty(result.Errors);
        Assert.Equal([unc, unc + @"\a"], result.Folders.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        Assert.All(result.Folders, f => Assert.Equal(unc, f.ShareUnc, ignoreCase: true));
        Assert.Contains(result.Folders[1].Aces, a => a.Sid == NetworkService && !a.IsInherited);
        Assert.False(result.Folders[1].InheritanceBroken);
        Assert.Equal(4, result.FoldersVisited);
        var info = Assert.Single(result.Shares);
        Assert.Equal("localhost", info.Server);
        Assert.Equal(tmp.Path, info.LocalPath, ignoreCase: true);
        Assert.Equal(SharePermissions, info.Permissions);
    }

    [AdminFact]
    public async Task Scan_ueber_UNC_liefert_dieselben_Rechte_wie_lokal()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);
        using var share = new TestShare(tmp.Path, ShareSddl);

        var local = await Plain().RunAsync(new ScanRequest { Targets = [Path.Combine(tmp.Path, "a")] });
        var remote = await Plain().RunAsync(new ScanRequest { Targets = [$@"\\localhost\{share.Name}\a"] });

        Assert.Equal(local.Folders[0].Aces, remote.Folders[0].Aces);
        Assert.Equal(local.Folders[0].OwnerSid, remote.Folders[0].OwnerSid);
        Assert.Equal($@"\\localhost\{share.Name}", remote.Folders[0].ShareUnc, ignoreCase: true);
        Assert.Equal(0, remote.Folders[0].Depth);
    }

    [AdminFact]
    public async Task Server_als_Ziel_ohne_administrative_Freigaben()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, ShareSddl);
        using var hidden = new TestShare(tmp.Sub("versteckt"), ShareSddl, name: "zt-test-" + Guid.NewGuid().ToString("N")[..6] + "$");

        var result = await Plain().RunAsync(new ScanRequest { Targets = [@"\\localhost"], MaxDepth = 0 });

        var root = Assert.Single(result.Folders, f => f.Path.Equals($@"\\localhost\{share.Name}", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, root.Depth);
        Assert.Equal(root.Path, root.ShareUnc);
        Assert.All(result.Folders, f => Assert.Equal(0, f.Depth));
        Assert.DoesNotContain(result.Folders, f => f.Path.EndsWith('$'));
        Assert.DoesNotContain(result.Shares, s => s.IsAdminShare);
        Assert.Contains(result.Shares, s => s.Name.Equals(share.Name, StringComparison.OrdinalIgnoreCase) && s.Permissions.SequenceEqual(SharePermissions));
    }

    [AdminFact]
    public async Task Server_als_Ziel_mit_administrativen_Freigaben()
    {
        using var tmp = new TempFolder();
        using var hidden = new TestShare(tmp.Path, ShareSddl, name: "zt-test-" + Guid.NewGuid().ToString("N")[..6] + "$");

        var result = await Plain().RunAsync(new ScanRequest { Targets = [@"\\localhost"], MaxDepth = 0, SkipAdminShares = false });

        Assert.Contains(result.Folders, f => f.Path.Equals($@"\\localhost\{hidden.Name}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Shares, s => s.IsAdminShare && s.Name.Equals(hidden.Name, StringComparison.OrdinalIgnoreCase));
    }

    [AdminFact]
    public async Task Punkt_scannt_lokale_Freigaben_ueber_ihren_Pfad()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, ShareSddl);

        var result = await Plain().RunAsync(new ScanRequest { Targets = ["."], MaxDepth = 0 });

        var root = Assert.Single(result.Folders, f => f.Path.Equals(tmp.Path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(share.Unc, root.ShareUnc, ignoreCase: true);
        Assert.All(result.Folders, f => Assert.Equal(0, f.Depth));
        var info = Assert.Single(result.Shares, s => s.Name.Equals(share.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Environment.MachineName, info.Server);
        Assert.Equal(SharePermissions, info.Permissions);
        Assert.DoesNotContain(result.Shares, s => s.IsAdminShare);
        var paths = result.Folders.Select(f => f.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [AdminFact]
    public async Task Punkt_nennt_auch_Freigaben_auf_demselben_Ordner()
    {
        // Regression: Zwei Freigaben auf einem Ordner – geprüft wird er einmal, aber beide Freigaben (mit ihren
        // Rechten) gehören ins Ergebnis. Bisher fehlte die zweite, ihr „Jeder: Lesen“ bzw. „Vollzugriff“ ging verloren.
        using var tmp = new TempFolder();
        using var narrow = new TestShare(tmp.Path, "D:(A;;0x1200a9;;;WD)");
        using var wide = new TestShare(tmp.Path, "D:(A;;FA;;;WD)");

        var result = await Plain().RunAsync(new ScanRequest { Targets = ["."], MaxDepth = 0 });

        var root = Assert.Single(result.Folders, f => f.Path.Equals(tmp.Path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(wide.Unc, root.ShareUnc, ignoreCase: true); // der weitere Zugriff zählt
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Lesen, false)],
            Assert.Single(result.Shares, s => s.UncPath.Equals(narrow.Unc, StringComparison.OrdinalIgnoreCase)).Permissions);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false)],
            Assert.Single(result.Shares, s => s.UncPath.Equals(wide.Unc, StringComparison.OrdinalIgnoreCase)).Permissions);
    }

    [AdminFact]
    public async Task UNC_Ziel_in_anderer_Schreibweise_passt_genau_zur_Freigabe()
    {
        using var tmp = new TempFolder();
        tmp.Sub("a");
        using var share = new TestShare(tmp.Path, ShareSddl);
        var typed = $@"\\localhost\{share.Name.ToUpperInvariant()}";

        var result = await Plain().RunAsync(new ScanRequest { Targets = [typed], MaxDepth = 1 });

        var info = Assert.Single(result.Shares);
        Assert.Equal(share.Name, info.Name); // Schreibweise des Servers
        Assert.All(result.Folders, f => Assert.Equal(info.UncPath, f.ShareUnc));
        Assert.Equal(typed, result.Folders[0].Path); // Pfade bleiben, wie angegeben
    }

    [AdminFact]
    public async Task Lokaler_Pfad_bekommt_die_naechste_Freigabe()
    {
        using var tmp = new TempFolder();
        tmp.Sub("innen", "tief");
        tmp.Sub("aussen");
        Grant(Path.Combine(tmp.Path, "innen", "tief"), NetworkService, FileSystemRights.Modify);
        Grant(Path.Combine(tmp.Path, "aussen"), NetworkService, FileSystemRights.Modify);
        using var outer = new TestShare(tmp.Path, ShareSddl);
        using var inner = new TestShare(Path.Combine(tmp.Path, "innen"), "D:(A;;FA;;;WD)");

        var result = await Plain().RunAsync(new ScanRequest { Targets = [tmp.Path] });

        string? UncOf(params string[] parts) => result.Folders.Single(f => f.Path == Path.Combine([tmp.Path, .. parts])).ShareUnc;
        Assert.Equal(outer.Unc, UncOf(), ignoreCase: true);
        Assert.Equal(outer.Unc, UncOf("aussen"), ignoreCase: true);
        Assert.Equal(inner.Unc, UncOf("innen", "tief"), ignoreCase: true);
        Assert.Contains(result.Shares, s => s.UncPath.Equals(outer.Unc, StringComparison.OrdinalIgnoreCase));
        var innerInfo = Assert.Single(result.Shares, s => s.UncPath.Equals(inner.Unc, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false)], innerInfo.Permissions);
    }

    [AdminFact]
    public async Task Nicht_lesbare_Freigabe_Rechte_werden_gemeldet_Ordner_trotzdem_geprueft()
    {
        // C$ hat keine eigene Sicherheitsbeschreibung (interne Windows-Vorgabe) – ihre Rechte gelten als nicht lesbar.
        if (ShareEnumerator.GetShare(".", "C$") is null) return; // administrative Freigaben abgeschaltet
        using var tmp = new TempFolder();
        tmp.Sub("a");
        Grant(Path.Combine(tmp.Path, "a"), NetworkService, FileSystemRights.Modify);
        var unc = @"\\localhost\C$\" + tmp.Path[3..];

        var result = await Plain().RunAsync(new ScanRequest { Targets = [unc] });

        var info = Assert.Single(result.Shares);
        Assert.Equal(@"\\localhost\C$", info.UncPath, ignoreCase: true);
        Assert.True(info.IsAdminShare);
        Assert.False(info.PermissionsReadable);
        Assert.Empty(info.Permissions);
        Assert.Contains(result.Errors, e => e.Path.Equals(info.UncPath, StringComparison.OrdinalIgnoreCase)
                                            && e.Message.StartsWith("Freigabe-Berechtigungen nicht lesbar: "));
        Assert.Equal([unc, unc + @"\a"], result.Folders.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        Assert.All(result.Folders, f => Assert.Null(f.Error));
        Assert.All(result.Folders, f => Assert.Equal(info.UncPath, f.ShareUnc, ignoreCase: true));
    }

    [AdminFact]
    public async Task Nicht_vorhandene_Freigabe_auf_erreichbarem_Server()
    {
        var unc = $@"\\localhost\zt-test-gibt-es-nicht";

        var result = await Plain().RunAsync(new ScanRequest { Targets = [unc] });

        var root = Assert.Single(result.Folders);
        Assert.StartsWith("Ziel nicht erreichbar: ", root.Error);
        Assert.Null(root.ShareUnc);
        Assert.Empty(result.Shares);
    }
}
