// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Tests.Scanning;

/// <summary>Gescannte Windows-Pfade als Daten – auf jedem System gleich.</summary>
public sealed class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\", "a", @"C:\a")]
    [InlineData(@"C:\Daten", "a ", @"C:\Daten\a ")]
    [InlineData(@"\\FS01\Labor", "QC", @"\\FS01\Labor\QC")]
    public void Combine_laesst_Namen_unveraendert(string dir, string name, string expected)
        => Assert.Equal(expected, WindowsPath.Join(dir, name));

    [Theory]
    [InlineData(@"C:\Daten\", @"C:\Daten")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\\", @"C:\")]
    [InlineData(@"\\FS01\Labor\", @"\\FS01\Labor")]
    public void TrimEnd(string path, string expected) => Assert.Equal(expected, WindowsPath.TrimEnd(path));

    [Theory]
    [InlineData(@"C:\Daten\Labor", @"C:\Daten", true)]
    [InlineData(@"C:\Daten", @"C:\Daten", true)]
    [InlineData(@"c:\daten\labor", @"C:\Daten", true)]
    [InlineData(@"C:\Daten2", @"C:\Daten", false)]
    [InlineData(@"C:\Daten\Labor", @"C:\", true)]
    [InlineData(@"D:\Daten", @"C:\", false)]
    public void IsSameOrBelow(string path, string dir, bool expected) => Assert.Equal(expected, WindowsPath.IsSameOrBelow(path, dir));
}

public sealed class FileAccessMaskTests
{
    [Fact]
    public void Generische_Rechte_wie_Windows()
    {
        Assert.Equal(0x001F01FFu, FileAccessMask.MapGeneric(0x10000000));
        Assert.Equal(0x00120089u, FileAccessMask.MapGeneric(0x80000000));
        Assert.Equal(0x00120116u, FileAccessMask.MapGeneric(0x40000000));
        Assert.Equal(0x001200A0u, FileAccessMask.MapGeneric(0x20000000));
        Assert.Equal(0x001201BFu, FileAccessMask.MapGeneric(0x80000000 | 0x40000000 | 0x20000000));
        Assert.Equal(0x00010000u, FileAccessMask.MapGeneric(0x00010000));
    }
}

public sealed class NodeSignatureTests
{
    private const AceInheritance CIOI = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit;

    private static FolderNode Node(params AceEntry[] aces)
    {
        var n = new FolderNode { Path = @"C:\x" };
        n.Aces.AddRange(aces);
        return n;
    }

    private static AceEntry Ace(string sid, FileRights rights, bool inherited = true, bool deny = false,
        AceInheritance inh = CIOI, AcePropagation prop = AcePropagation.None)
        => new() { Sid = sid, Rights = rights, IsInherited = inherited, IsDeny = deny, Inheritance = inh, Propagation = prop };

    [Fact]
    public void Geerbt_oder_explizit_ist_egal()
    {
        var parent = Node(Ace("S-1-5-18", FileRights.FullControl, inherited: false), Ace("S-1-5-20", FileRights.Modify, inherited: false));
        var child = Node(Ace("S-1-5-18", FileRights.FullControl), Ace("S-1-5-20", FileRights.Modify));
        Assert.Equal(NodeSignature.Of(parent), NodeSignature.Of(child));
    }

    [Fact]
    public void Nur_vererben_zaehlt_nicht_fuer_den_Ordner_selbst_aber_fuer_die_Weitergabe()
    {
        var a = Node(Ace("S-1-5-18", FileRights.FullControl));
        var b = Node(Ace("S-1-5-18", FileRights.FullControl), Ace("S-1-3-0", (FileRights)0x10000000, prop: AcePropagation.InheritOnly));
        Assert.Equal(NodeSignature.Effective(a), NodeSignature.Effective(b));
        // b gibt ERSTELLER-BESITZER an Unterordner weiter, a nicht – die Unterordner sähen verschieden aus.
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(b));
    }

    [Fact]
    public void Mit_Verweigerung_zaehlt_die_Reihenfolge()
    {
        // Ordnet ein Skript nur den Elternordner neu, behält das Kind die alte Reihenfolge – es gewährt anderes (ZT15)
        var allowFirst = Node(Ace("S-1-5-20", FileRights.Modify), Ace("S-1-5-19", FileRights.Write, deny: true));
        var denyFirst = Node(Ace("S-1-5-19", FileRights.Write, deny: true), Ace("S-1-5-20", FileRights.Modify));
        Assert.NotEqual(NodeSignature.Of(allowFirst), NodeSignature.Of(denyFirst));
        Assert.NotEqual(NodeSignature.Effective(allowFirst), NodeSignature.Effective(denyFirst));

        // Ohne Verweigerung ist die Reihenfolge gleichgültig
        var ab = Node(Ace("S-1-5-20", FileRights.Modify), Ace("S-1-5-19", FileRights.Read));
        var ba = Node(Ace("S-1-5-19", FileRights.Read), Ace("S-1-5-20", FileRights.Modify));
        Assert.Equal(NodeSignature.Of(ab), NodeSignature.Of(ba));
    }

    [Fact]
    public void Gleiche_Rechte_aber_andere_Weitergabe_unterscheidet()
    {
        // „Nur eine Ebene“ (NP) gegenüber normal vererbbar: dieselben Rechte am Ordner, aber nicht an den Enkeln.
        var once = Node(Ace("S-1-5-20", FileRights.Modify, inh: AceInheritance.ContainerInherit, prop: AcePropagation.NoPropagateInherit));
        var always = Node(Ace("S-1-5-20", FileRights.Modify, inh: AceInheritance.ContainerInherit));
        Assert.Equal(NodeSignature.Effective(once), NodeSignature.Effective(always));
        Assert.NotEqual(NodeSignature.Of(once), NodeSignature.Of(always));

        // Nur Dateien (OI) gehen nicht an Unterordner weiter.
        var filesOnly = Node(Ace("S-1-5-20", FileRights.Modify, inh: AceInheritance.ObjectInherit));
        Assert.Equal(NodeSignature.Of(Node(Ace("S-1-5-20", FileRights.Modify, inh: AceInheritance.None))), NodeSignature.Of(filesOnly));
    }

    [Fact]
    public void Normale_Vererbung_ergibt_beim_Kind_dieselbe_Signatur()
    {
        // So schreibt Windows die Einträge beim Erben: generische Rechte aufgeteilt in einen wirksamen und einen
        // Nur-vererben-Eintrag, NP-Einträge ohne Vererbungskennzeichen, Container-Einträge unverändert.
        var parent = Node(
            Ace("S-1-5-19", (FileRights)0x10000000, inherited: false),
            Ace("S-1-5-32-551", FileRights.Modify, inherited: false, inh: AceInheritance.ContainerInherit, prop: AcePropagation.NoPropagateInherit),
            Ace("S-1-5-32-545", FileRights.ReadAndExecute));
        var child = Node(
            Ace("S-1-5-19", FileRights.FullControl, inh: AceInheritance.None),
            Ace("S-1-5-19", (FileRights)0x10000000, prop: AcePropagation.InheritOnly),
            Ace("S-1-5-32-551", FileRights.Modify, inh: AceInheritance.None),
            Ace("S-1-5-32-545", FileRights.ReadAndExecute));
        Assert.Equal(NodeSignature.Of(parent), NodeSignature.Of(child));
    }

    [Fact]
    public void Generisch_und_aufgeloest_sind_gleich()
    {
        // Beim Vererben löst Windows GENERIC_ALL in 0x1F01FF auf.
        var a = Node(Ace("S-1-5-20", (FileRights)0x10000000));
        var b = Node(Ace("S-1-5-20", FileRights.FullControl));
        Assert.Equal(NodeSignature.Of(a), NodeSignature.Of(b));
    }

    [Fact]
    public void Doppelte_Eintraege_werden_zusammengefasst()
    {
        var a = Node(Ace("S-1-5-32-544", FileRights.FullControl));
        var b = Node(Ace("S-1-5-32-544", FileRights.FullControl), Ace("S-1-5-32-544", FileRights.FullControl, inh: AceInheritance.None));
        Assert.Equal(NodeSignature.Of(a), NodeSignature.Of(b));
    }

    [Fact]
    public void Zusaetzliches_Konto_oder_anderes_Recht_unterscheidet()
    {
        var a = Node(Ace("S-1-5-18", FileRights.FullControl));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node(Ace("S-1-5-18", FileRights.FullControl), Ace("S-1-5-20", FileRights.Read))));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node(Ace("S-1-5-18", FileRights.Modify))));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node(Ace("S-1-5-18", FileRights.FullControl, deny: true))));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node()));
    }

    [Fact]
    public void Fehler_ergibt_keine_Signatur()
    {
        var n = Node(Ace("S-1-5-18", FileRights.FullControl));
        n.Error = "Zugriff verweigert";
        Assert.Null(NodeSignature.Of(n));
    }
}

public sealed class LocalShareMapTests
{
    // Die Pfade hier sind schon vollständig – unter Windows macht die Plattform das mit Path.GetFullPath
    private static string? AsIs(string path) => path;

    private static ShareRead Share(string name, string path, bool admin = false, bool readable = true, params ShareAce[] aces)
        => new(new ShareInfo { Server = "PC", Name = name, LocalPath = path, IsAdminShare = admin, PermissionsReadable = readable, Permissions = [.. aces] }, null);

    [Fact]
    public void Laengstes_Praefix_gewinnt()
    {
        var map = new LocalShareMap([Share("Daten", @"D:\Daten"), Share("Labor", @"D:\Daten\Labor\"), Share("c", @"C:\")], includeAdminShares: false, AsIs);
        Assert.Equal("Labor", map.Longest(@"D:\Daten\Labor\QC")!.Share.Name);
        Assert.Equal("Labor", map.Longest(@"d:\daten\labor")!.Share.Name);
        Assert.Equal("Daten", map.Longest(@"D:\Daten\Labor2")!.Share.Name);
        Assert.Equal("c", map.Longest(@"C:\Windows")!.Share.Name);
        Assert.Null(map.Longest(@"E:\x"));
        Assert.Equal("Labor", map.At(@"D:\Daten\Labor")!.Share.Name);
        Assert.Null(map.At(@"D:\Daten\Labor\QC"));
    }

    [Fact]
    public void Admin_Freigaben_nur_auf_Wunsch()
    {
        var shares = new[] { Share("C$", @"C:\", admin: true) };
        Assert.Null(new LocalShareMap(shares, includeAdminShares: false, AsIs).Longest(@"C:\Daten"));
        Assert.Equal("C$", new LocalShareMap(shares, includeAdminShares: true, AsIs).Longest(@"C:\Daten")!.Share.Name);
    }

    [Fact]
    public void Ohne_lokalen_Pfad_keine_Zuordnung()
    {
        var map = new LocalShareMap([Share("x", "")], includeAdminShares: true, AsIs);
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void Gleicher_Pfad_weitester_Zugriff_gewinnt()
    {
        var lesen = Share("a-lesen", @"D:\Daten", aces: new ShareAce("S-1-1-0", ShareAccess.Lesen, false));
        var voll = Share("b-voll", @"D:\Daten", aces: new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false));
        var unlesbar = Share("c-unlesbar", @"D:\Daten", readable: false);
        var admin = Share("d$", @"D:\Daten", admin: true, readable: false);

        Assert.Equal("b-voll", new LocalShareMap([lesen, voll], true, AsIs).At(@"D:\Daten")!.Share.Name);
        Assert.Equal("b-voll", new LocalShareMap([voll, lesen], true, AsIs).At(@"D:\Daten")!.Share.Name);
        Assert.Equal("c-unlesbar", new LocalShareMap([lesen, voll, unlesbar], true, AsIs).At(@"D:\Daten")!.Share.Name);
        Assert.Equal("b-voll", new LocalShareMap([admin, lesen, voll], true, AsIs).At(@"D:\Daten")!.Share.Name);
    }
}

public sealed class WindowsPathPartsTests
{
    [Theory]
    [InlineData(@"\\FS01\Labor\QC", @"\\FS01\Labor")]
    [InlineData(@"\\FS01\Labor", null)]
    [InlineData(@"C:\Daten", @"C:\")]
    [InlineData(@"C:\Daten\", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData(@"Daten", null)]
    public void Parent(string path, string? expected) => Assert.Equal(expected, WindowsPath.Parent(path));

    [Theory]
    [InlineData(@"\\FS01\Labor\QC", "QC")]
    [InlineData(@"\\FS01\Labor\QC\", "QC")]
    [InlineData("QC", "QC")]
    public void LastSegment(string path, string expected) => Assert.Equal(expected, WindowsPath.LastSegment(path));

    [Fact]
    public void Unc_und_Server()
    {
        Assert.Equal(@"\\FS01\Labor", WindowsPath.Unc("FS01", "Labor"));
        Assert.Equal(@"\\FS01", WindowsPath.Server("FS01"));
        Assert.True(WindowsPath.IsUnc(@"\\FS01"));
        Assert.False(WindowsPath.IsUnc(@"C:\FS01"));
    }

    [Theory]
    [InlineData(@"MUSTER\QC-Team", "QC-Team")]
    [InlineData("Jeder", "Jeder")]
    [InlineData(@"MUSTER\", @"MUSTER\")]
    public void Kurzname(string name, string expected) => Assert.Equal(expected, AccountName.ShortName(name));

    [Theory]
    [InlineData("LAB", "max", @"LAB\max")]
    [InlineData("", "Jeder", "Jeder")]
    [InlineData("LAB", "", "LAB")]
    public void Kontoname_zusammensetzen(string domain, string name, string expected) => Assert.Equal(expected, AccountName.Join(domain, name));
}
