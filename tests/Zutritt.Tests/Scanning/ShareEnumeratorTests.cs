using System.Diagnostics;
using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Scanning;

/// <summary>Echte Freigaben auf diesem Rechner (Anlegen braucht Adminrechte, wird immer wieder entfernt).</summary>
public sealed class ShareEnumeratorTests
{
    private const string Sddl = "D:(A;;0x1200a9;;;WD)(A;;FA;;;BA)";

    private static void AssertSame(ShareInfo expected, ShareInfo? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Server, actual.Server);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.LocalPath, actual.LocalPath);
        Assert.Equal(expected.Remark, actual.Remark);
        Assert.Equal(expected.IsAdminShare, actual.IsAdminShare);
        Assert.Equal(expected.PermissionsReadable, actual.PermissionsReadable);
        Assert.Equal(expected.Permissions, actual.Permissions);
    }

    /// <summary>So sieht Windows selbst die Freigabe-Berechtigungen (unabhängiger Weg über WMI/CIM).</summary>
    private static string WindowsSddl(string share)
    {
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"(Get-SmbShare -Name '{share}').SecurityDescriptor\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }

    [AdminFact]
    public void Eigene_Freigabe_mit_Rechten()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, Sddl);

        var all = ShareEnumerator.GetShares(".");
        var found = Assert.Single(all, s => s.Name.Equals(share.Name, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(Environment.MachineName, found.Server);
        Assert.Equal(share.Unc, found.UncPath, ignoreCase: true);
        Assert.Equal(tmp.Path, found.LocalPath, ignoreCase: true);
        Assert.Equal("Zutritt-Test", found.Remark);
        Assert.False(found.IsAdminShare);
        Assert.True(found.PermissionsReadable);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Lesen, false), new ShareAce("S-1-5-32-544", ShareAccess.Vollzugriff, false)], found.Permissions);

        AssertSame(found, ShareEnumerator.GetShare(".", share.Name));
        AssertSame(found, ShareEnumerator.GetShare(Environment.MachineName, share.Name));
        // Gegenprobe mit der Sicht von Windows.
        Assert.Equal(ShareSecurity.FromDescriptor(new RawSecurityDescriptor(WindowsSddl(share.Name))).Aces, found.Permissions);
    }

    [AdminFact]
    public void Verweigern_und_Reihenfolge()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, "D:(D;;0x10116;;;BU)(A;;FA;;;WD)(A;;0x1301bf;;;AU)");

        var found = ShareEnumerator.GetShare(".", share.Name)!;

        Assert.Equal(
            [
                new ShareAce("S-1-5-32-545", ShareAccess.Aendern, true),
                new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false),
                new ShareAce("S-1-5-11", ShareAccess.Aendern, false),
            ], found.Permissions);
    }

    [AdminFact]
    public void Freigabe_ohne_Beschreibung_ist_Jeder_Vollzugriff_wie_bei_Windows()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, sddl: null);

        var found = ShareEnumerator.GetShare(".", share.Name)!;

        Assert.True(found.PermissionsReadable);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false)], found.Permissions);
        Assert.Equal(ShareSecurity.FromDescriptor(new RawSecurityDescriptor(WindowsSddl(share.Name))).Aces, found.Permissions);
    }

    [AdminFact]
    public void Versteckte_Freigabe_gilt_als_administrativ()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, Sddl, name: "zt-test-" + Guid.NewGuid().ToString("N")[..6] + "$");

        var found = Assert.Single(ShareEnumerator.GetShares("."), s => s.Name.Equals(share.Name, StringComparison.OrdinalIgnoreCase));
        Assert.True(found.IsAdminShare);
        Assert.True(found.PermissionsReadable);
    }

    [AdminFact]
    public void Administrative_Freigaben_markiert_und_IPC_fehlt()
    {
        var all = ShareEnumerator.GetShares(".");

        Assert.DoesNotContain(all, s => s.Name.Equals("IPC$", StringComparison.OrdinalIgnoreCase));
        Assert.All(all.Where(s => s.Name.EndsWith('$')), s => Assert.True(s.IsAdminShare));
        var admin = all.FirstOrDefault(s => s.Name.Equals("ADMIN$", StringComparison.OrdinalIgnoreCase));
        if (admin is not null)
        {
            // Ohne eigene Beschreibung gilt eine interne Vorgabe – nicht raten.
            Assert.False(admin.PermissionsReadable);
            Assert.Empty(admin.Permissions);
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.Windows), admin.LocalPath, ignoreCase: true);
        }
        Assert.Null(ShareEnumerator.GetShare(".", "IPC$")); // keine Datenträger-Freigabe
    }

    [AdminFact]
    public void Unbekannte_Freigabe_ist_null()
    {
        Assert.Null(ShareEnumerator.GetShare(".", "zt-test-gibt-es-nicht"));
        var lookup = ShareEnumerator.Lookup(".", "zt-test-gibt-es-nicht");
        Assert.True(lookup.NotFound);
        Assert.Equal($@"Freigabe „\\{Environment.MachineName}\zt-test-gibt-es-nicht“ nicht gefunden", lookup.Error);
    }

    [AdminFact]
    public void Servername_bleibt_wie_angegeben()
    {
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, Sddl);

        var viaLocalhost = ShareEnumerator.GetShare("localhost", share.Name)!;
        Assert.Equal("localhost", viaLocalhost.Server);
        Assert.Equal($@"\\localhost\{share.Name}", viaLocalhost.UncPath, ignoreCase: true);

        // Über das Netz (RPC an die eigene Adresse) – derselbe Inhalt, anderer Servername.
        var remote = Assert.Single(ShareEnumerator.GetShares(@"\\127.0.0.1"), s => s.Name.Equals(share.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("127.0.0.1", remote.Server);
        Assert.Equal(tmp.Path, remote.LocalPath, ignoreCase: true);
        Assert.Equal(viaLocalhost.Permissions, remote.Permissions);
    }

    [AdminFact]
    public void Ohne_Adminrechte_Liste_und_Rechte_stimmig()
    {
        // Ob Nicht-Admins Level 502 lesen dürfen, regelt der Server (SrvsvcShareAdminInfo). Windows 11 erlaubt es
        // „Jeder“ lokal; ältere Server und Domänen-Richtlinien verweigern es. Beides muss stimmig ankommen.
        using var tmp = new TempFolder();
        using var share = new TestShare(tmp.Path, Sddl, remark: "nur Liste");

        var (all, single, lookup) = NonAdminScope.Run(() =>
            (ShareEnumerator.GetShares("."), ShareEnumerator.GetShare(".", share.Name), ShareEnumerator.Lookup(".", share.Name)));

        var listed = Assert.Single(all, s => s.Name.Equals(share.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var s in new[] { listed, single! })
        {
            Assert.Equal("nur Liste", s.Remark);
            if (s.PermissionsReadable)
            {
                Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Lesen, false), new ShareAce("S-1-5-32-544", ShareAccess.Vollzugriff, false)], s.Permissions);
                Assert.Equal(tmp.Path, s.LocalPath, ignoreCase: true);
            }
            else
            {
                Assert.Empty(s.Permissions);
                Assert.Equal("", s.LocalPath);
                Assert.Equal("Freigabe-Berechtigungen nicht lesbar: Zugriff verweigert", lookup.Read!.Note);
            }
        }
    }

    [Fact]
    public void Rueckfall_ohne_Adminrechte_liefert_Freigabe_ohne_Rechte()
    {
        var read = ShareEnumerator.Basic("FS01", "Labor", 0, "Labordaten", "Freigabe-Berechtigungen nicht lesbar: Zugriff verweigert", out int status);

        Assert.Equal(0, status);
        Assert.NotNull(read);
        Assert.Equal(@"\\FS01\Labor", read.Share.UncPath);
        Assert.Equal("Labordaten", read.Share.Remark);
        Assert.Equal("", read.Share.LocalPath);
        Assert.False(read.Share.PermissionsReadable);
        Assert.Empty(read.Share.Permissions);
        Assert.False(read.Share.IsAdminShare);
        Assert.Equal("Freigabe-Berechtigungen nicht lesbar: Zugriff verweigert", read.Note);

        Assert.True(ShareEnumerator.Basic("FS01", "D$", 0x80000000, "", "x", out _)!.Share.IsAdminShare);
        Assert.Null(ShareEnumerator.Basic("FS01", "Drucker", 1, "", "x", out status));
        Assert.Equal(-1, status);
    }

    [Fact]
    public void Nicht_erreichbarer_Server_ergibt_leere_Liste()
    {
        Assert.Empty(ShareEnumerator.GetShares("zt-test-gibt-es-nicht.invalid"));
        Assert.Null(ShareEnumerator.GetShare(@"\\zt-test-gibt-es-nicht.invalid", "x"));
        var listing = ShareEnumerator.Enumerate("zt-test-gibt-es-nicht.invalid");
        Assert.StartsWith("Freigaben von „zt-test-gibt-es-nicht.invalid“ nicht lesbar: ", listing.Error);
        var lookup = ShareEnumerator.Lookup("zt-test-gibt-es-nicht.invalid", "x");
        Assert.False(lookup.NotFound);
        Assert.NotNull(lookup.Error);
    }

    [Theory]
    [InlineData(".", null)]
    [InlineData("", null)]
    [InlineData("localhost", null)]
    [InlineData(@"\\FS01", @"\\FS01")]
    [InlineData("FS01", @"\\FS01")]
    [InlineData(@"\\FS01\", @"\\FS01")]
    public void Servernamen_fuer_die_API(string server, string? api) => Assert.Equal(api, ShareEnumerator.ServerNames(server).Api);

    [Fact]
    public void Eigener_Rechnername_ist_lokal()
    {
        var (api, display) = ShareEnumerator.ServerNames(Environment.MachineName.ToLowerInvariant());
        Assert.Null(api);
        Assert.Equal(Environment.MachineName.ToLowerInvariant(), display);
        Assert.Equal(Environment.MachineName, ShareEnumerator.ServerNames(".").Display);
    }

    [Theory]
    [InlineData(0x00000000u, "Daten", false)]
    [InlineData(0x80000000u, "C$", true)]
    [InlineData(0x00000000u, "print$", true)]
    [InlineData(0x00000000u, "versteckt$", true)]
    [InlineData(0x40000000u, "temporaer", false)]
    public void Administrativ_nach_Typ_oder_Name(uint type, string name, bool admin) => Assert.Equal(admin, ShareEnumerator.IsAdmin(type, name));

    [Theory]
    [InlineData(0x00000000u, true)]   // STYPE_DISKTREE
    [InlineData(0x80000000u, true)]   // Datenträger, administrativ
    [InlineData(0x40000000u, true)]   // Datenträger, temporär
    [InlineData(0x00000001u, false)]  // Drucker
    [InlineData(0x80000003u, false)]  // IPC$
    public void Nur_Datentraeger(uint type, bool disk) => Assert.Equal(disk, ShareEnumerator.IsDiskShare(type));
}
