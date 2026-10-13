using System.Security.AccessControl;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using static Zutritt.Tests.Analysis.ScanBuilder;

namespace Zutritt.Tests.Analysis;

public sealed class PathOrderTests
{
    [Fact]
    public void Sortiert_wie_ein_Baum()
    {
        string[] paths = [@"\\FS01\Labor-Alt", @"\\FS01\Labor\QC", @"\\FS01\Labor", @"\\fs01\labor\Archiv", @"\\FS01\Labor\Archiv\2019", @"C:\Daten"];
        var sorted = paths.Order(PathOrder.Instance).ToList();
        Assert.Equal([@"\\FS01\Labor", @"\\fs01\labor\Archiv", @"\\FS01\Labor\Archiv\2019", @"\\FS01\Labor\QC", @"\\FS01\Labor-Alt", @"C:\Daten"], sorted);
        Assert.Equal(0, PathOrder.Instance.Compare(@"\\FS01\Labor", @"\\fs01\LABOR"));
        Assert.True(PathOrder.Instance.Compare(null, "x") < 0);
    }

    [Theory]
    [InlineData(@"\\FS01\Labor\QC\Proben", @"\\FS01\Labor\QC")]
    [InlineData(@"\\FS01\Labor\QC", @"\\FS01\Labor")]
    [InlineData(@"\\FS01\Labor", null)]
    [InlineData(@"\\FS01", null)]
    [InlineData(@"C:\Daten\A\", @"C:\Daten")]
    [InlineData(@"C:\Daten", @"C:\")]
    [InlineData(@"C:\", null)]
    public void Elternpfad(string path, string? parent) => Assert.Equal(parent, PathOrder.Parent(path));

    [Theory]
    [InlineData(@"\\FS01\Labor", @"\\FS01\Labor", 0)]
    [InlineData(@"\\FS01\Labor", @"\\fs01\labor\QC\Proben", 2)]
    [InlineData(@"C:\", @"C:\Daten\X", 2)]
    [InlineData(@"\\FS01\Labor", @"\\FS01\Labor-Alt\X", null)]
    [InlineData(@"\\FS01\Labor\QC", @"\\FS01\Labor", null)]
    public void Abstand(string ancestor, string path, int? distance) => Assert.Equal(distance, PathOrder.Distance(ancestor, path));
}

public sealed class SidCatalogTests
{
    [Theory]
    [InlineData("S-1-1-0", true)]
    [InlineData("S-1-5-11", true)]
    [InlineData("S-1-5-32-545", true)]
    [InlineData("S-1-5-32-546", true)]
    [InlineData("S-1-5-4", true)]
    [InlineData("S-1-5-2", true)]
    [InlineData("S-1-5-21-1-2-3-513", true)]
    [InlineData("S-1-5-21-1-2-3-514", true)]
    [InlineData("S-1-5-21-1-2-3-512", false)]
    [InlineData("S-1-5-21-1-2-3-1513", false)]
    [InlineData("S-1-5-32-544", false)]
    public void Breite_Gruppen(string sid, bool broad) => Assert.Equal(broad, SidCatalog.IsBroad(sid));

    [Theory]
    [InlineData("S-1-5-18", true)]
    [InlineData("S-1-5-32-544", true)]
    [InlineData("S-1-5-32-551", true)]
    [InlineData("S-1-3-0", true)]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", true)]
    [InlineData("S-1-5-21-1-2-3-512", true)]
    [InlineData("S-1-5-21-1-2-3-519", true)]
    [InlineData("S-1-5-21-1-2-3-500", false)]
    [InlineData("S-1-5-32-547", false)]
    [InlineData("S-1-5-19", false)]
    public void Admin_artig(string sid, bool adminLike) => Assert.Equal(adminLike, SidCatalog.IsAdminLike(sid));

    [Theory]
    [InlineData("S-1-5-21-1-2-3-500", 500)]
    [InlineData("s-1-5-21-1111111111-2222222222-3333333333-1105", 1105)]
    [InlineData("S-1-5-21-1-2-500", null)]
    [InlineData("S-1-5-32-544", null)]
    [InlineData("S-1-5-21-1-2-3-x", null)]
    [InlineData("kaputt", null)]
    public void Domaenen_RID(string sid, int? rid) => Assert.Equal(rid, SidCatalog.DomainRid(sid));

    [Theory]
    [InlineData("S-1-1-0", true)]
    [InlineData("S-1-5-18", true)]
    [InlineData("S-1-5-11", true)]
    [InlineData("S-1-3-4", true)]
    [InlineData("S-1-5-80-123-456", true)]
    [InlineData("S-1-15-2-1", true)]
    [InlineData("S-1-5-32-545", false)]
    [InlineData("S-1-5-21-1-2-3-1001", false)]
    [InlineData("kaputt", false)]
    public void Sonder_Identitaeten(string sid, bool special) => Assert.Equal(special, SidCatalog.IsSpecialIdentity(sid));

    [Fact]
    public void Platzhalter_und_Namen()
    {
        Assert.True(SidCatalog.IsCreatorPlaceholder("S-1-3-0"));
        Assert.True(SidCatalog.IsCreatorPlaceholder("S-1-3-1"));
        Assert.False(SidCatalog.IsCreatorPlaceholder("S-1-3-4"));
        Assert.Equal("Jeder", SidCatalog.GermanName("S-1-1-0"));
        Assert.Null(SidCatalog.GermanName("S-1-5-21-1-2-3-1001"));
    }
}

public sealed class InheritanceReconstructionTests
{
    private static readonly string U = Sid(2001);

    private static FolderNode Parent(params AceEntry[] aces)
    {
        var f = new FolderNode { Path = @"D:\A", Depth = 1, ShareUnc = @"\\SRV\A", OwnerSid = U };
        f.Aces.AddRange(aces);
        return f;
    }

    [Fact]
    public void Ordner_erben_ContainerInherit_auch_nur_vererbende_Eintraege()
    {
        var parent = Parent(
            Allow(U, FileSystemRights.Modify, inheritance: InheritanceFlags.ContainerInherit, propagation: PropagationFlags.InheritOnly),
            Allow(Sid(1001), FileSystemRights.Read, inheritance: InheritanceFlags.ObjectInherit),
            Allow(Sid(1002), FileSystemRights.Read, inheritance: InheritanceFlags.None));
        var child = AccessContext.Inherit(parent, @"D:\A\B", 1, isFile: false);

        var ace = Assert.Single(child.Aces);
        Assert.Equal(U, ace.Sid);
        Assert.True(ace.IsInherited);
        Assert.False(ace.InheritOnly);
        Assert.Equal(2, child.Depth);
        Assert.Equal(@"\\SRV\A", child.ShareUnc);
        Assert.Equal("", child.OwnerSid); // Besitzer eines Unterordners ist unbekannt
    }

    [Fact]
    public void Nur_eine_Ebene_bei_NoPropagateInherit()
    {
        var parent = Parent(Allow(U, FileSystemRights.Modify, propagation: PropagationFlags.NoPropagateInherit));
        Assert.Single(AccessContext.Inherit(parent, @"D:\A\B", 1, false).Aces);
        Assert.Empty(AccessContext.Inherit(parent, @"D:\A\B\C", 2, false).Aces);
        Assert.Single(AccessContext.Inherit(parent, @"D:\A\x.txt", 1, true).Aces);
        Assert.Empty(AccessContext.Inherit(parent, @"D:\A\B\x.txt", 2, true).Aces);
    }

    [Fact]
    public void Dateien_erben_ObjectInherit_ueber_Zwischenordner()
    {
        var parent = Parent(Allow(U, FileSystemRights.Modify, inheritance: InheritanceFlags.ObjectInherit));
        Assert.Empty(AccessContext.Inherit(parent, @"D:\A\B", 1, false).Aces);
        Assert.Single(AccessContext.Inherit(parent, @"D:\A\B\C\x.txt", 3, true).Aces);
    }

    [Fact]
    public void Ersteller_Besitzer_entfaellt_generische_Rechte_werden_abgebildet()
    {
        var parent = Parent(
            Allow("S-1-3-0", (FileSystemRights)0x10000000, propagation: PropagationFlags.InheritOnly),
            Allow(U, unchecked((FileSystemRights)0x80000000)));
        var ace = Assert.Single(AccessContext.Inherit(parent, @"D:\A\B", 1, false).Aces);
        Assert.Equal(FileSystemRights.Read, ace.Rights);
        Assert.Equal(AccessLevel.Lesen, ace.Level);
    }

    [Fact]
    public void Kanonische_Reihenfolge_des_Vorfahren_bleibt_erhalten()
    {
        // Explizites Erlauben am Vorfahren steht im Kind vor dessen geerbter Verweigerung – wie bei Windows
        var g = Sid(1001);
        var parent = Parent(
            Deny(g, FileSystemRights.Write, inherited: true),
            Allow(U, FileSystemRights.Modify));
        var child = AccessContext.Inherit(parent, @"D:\A\B", 1, false);
        Assert.Equal([U, g], child.Aces.Select(a => a.Sid));

        var b = new ScanBuilder().User(U, @"T\u").Group(g, @"T\G", U);
        var ctx = new AccessContext(b.Scan);
        Assert.Equal(AccessLevel.Aendern, ctx.Evaluate(child, ctx.TokenOf(U, LogonKind.Network)).Level);
    }
}
