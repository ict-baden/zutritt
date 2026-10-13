using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Tests.Scanning;

public sealed class LongPathTests
{
    [Theory]
    [InlineData(@"C:\Daten", @"\\?\C:\Daten")]
    [InlineData(@"C:\", @"\\?\C:\")]
    [InlineData(@"\\FS01\Labor\QC", @"\\?\UNC\FS01\Labor\QC")]
    [InlineData(@"\\?\C:\Daten", @"\\?\C:\Daten")]
    [InlineData(@"C:\Ordner mit Leerzeichen am Ende ", @"\\?\C:\Ordner mit Leerzeichen am Ende ")]
    public void ToExtended(string path, string expected) => Assert.Equal(expected, LongPath.ToExtended(path));

    [Theory]
    [InlineData(@"\\?\C:\Daten", @"C:\Daten")]
    [InlineData(@"\\?\UNC\FS01\Labor", @"\\FS01\Labor")]
    [InlineData(@"\\?\unc\FS01\Labor", @"\\FS01\Labor")]
    [InlineData(@"C:\Daten", @"C:\Daten")]
    public void FromExtended(string path, string expected) => Assert.Equal(expected, LongPath.FromExtended(path));

    [Theory]
    [InlineData(@"C:\", "a", @"C:\a")]
    [InlineData(@"C:\Daten", "a ", @"C:\Daten\a ")]
    [InlineData(@"\\FS01\Labor", "QC", @"\\FS01\Labor\QC")]
    public void Combine_laesst_Namen_unveraendert(string dir, string name, string expected)
        => Assert.Equal(expected, LongPath.Combine(dir, name));

    [Theory]
    [InlineData(@"C:\Daten\", @"C:\Daten")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\\", @"C:\")]
    [InlineData(@"\\FS01\Labor\", @"\\FS01\Labor")]
    public void TrimEndSeparator(string path, string expected) => Assert.Equal(expected, LongPath.TrimEndSeparator(path));

    [Theory]
    [InlineData(@"C:\Daten\Labor", @"C:\Daten", true)]
    [InlineData(@"C:\Daten", @"C:\Daten", true)]
    [InlineData(@"c:\daten\labor", @"C:\Daten", true)]
    [InlineData(@"C:\Daten2", @"C:\Daten", false)]
    [InlineData(@"C:\Daten\Labor", @"C:\", true)]
    [InlineData(@"D:\Daten", @"C:\", false)]
    public void IsSameOrBelow(string path, string dir, bool expected) => Assert.Equal(expected, LongPath.IsSameOrBelow(path, dir));

    [Theory]
    [InlineData(@"C:\Daten\..\x", true)]
    [InlineData(@"C:\Daten\.\x", true)]
    [InlineData(@"C:/Daten", true)]
    [InlineData(@"C:\Daten\\x", true)]
    [InlineData(@"\\FS01\Labor\x", false)]
    [InlineData(@"C:\Daten\x.", false)]
    [InlineData(@"C:\Daten\x ", false)]
    public void NeedsNormalization(string path, bool expected) => Assert.Equal(expected, LongPath.NeedsNormalization(path));
}

public sealed class Win32ErrorTests
{
    [Theory]
    [InlineData(5, "Zugriff verweigert")]
    [InlineData(2, "Pfad nicht gefunden")]
    [InlineData(3, "Pfad nicht gefunden")]
    [InlineData(53, "Netzwerkpfad nicht gefunden")]
    [InlineData(206, "Pfad oder Name zu lang")]
    [InlineData(2310, "Freigabe nicht gefunden")]
    public void Bekannte_Fehler_auf_Deutsch(int code, string text) => Assert.Equal(text, Win32Error.Describe(code));

    [Fact]
    public void Unbekannte_Fehler_mit_Nummer()
    {
        var text = Win32Error.Describe(1117);
        Assert.StartsWith("Windows-Fehler 1117: ", text);
        Assert.False(text.EndsWith('.'));
    }
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
    private const InheritanceFlags CIOI = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    private static FolderNode Node(params AceEntry[] aces)
    {
        var n = new FolderNode { Path = @"C:\x" };
        n.Aces.AddRange(aces);
        return n;
    }

    private static AceEntry Ace(string sid, FileSystemRights rights, bool inherited = true, bool deny = false,
        InheritanceFlags inh = CIOI, PropagationFlags prop = PropagationFlags.None)
        => new() { Sid = sid, Rights = rights, IsInherited = inherited, IsDeny = deny, Inheritance = inh, Propagation = prop };

    [Fact]
    public void Geerbt_oder_explizit_ist_egal()
    {
        var parent = Node(Ace("S-1-5-18", FileSystemRights.FullControl, inherited: false), Ace("S-1-5-20", FileSystemRights.Modify, inherited: false));
        var child = Node(Ace("S-1-5-18", FileSystemRights.FullControl), Ace("S-1-5-20", FileSystemRights.Modify));
        Assert.Equal(NodeSignature.Of(parent), NodeSignature.Of(child));
    }

    [Fact]
    public void Nur_vererben_zaehlt_nicht()
    {
        var a = Node(Ace("S-1-5-18", FileSystemRights.FullControl));
        var b = Node(Ace("S-1-5-18", FileSystemRights.FullControl), Ace("S-1-3-0", (FileSystemRights)0x10000000, prop: PropagationFlags.InheritOnly));
        Assert.Equal(NodeSignature.Of(a), NodeSignature.Of(b));
    }

    [Fact]
    public void Generisch_und_aufgeloest_sind_gleich()
    {
        // Beim Vererben löst Windows GENERIC_ALL in 0x1F01FF auf.
        var a = Node(Ace("S-1-5-20", (FileSystemRights)0x10000000));
        var b = Node(Ace("S-1-5-20", FileSystemRights.FullControl));
        Assert.Equal(NodeSignature.Of(a), NodeSignature.Of(b));
    }

    [Fact]
    public void Doppelte_Eintraege_werden_zusammengefasst()
    {
        var a = Node(Ace("S-1-5-32-544", FileSystemRights.FullControl));
        var b = Node(Ace("S-1-5-32-544", FileSystemRights.FullControl), Ace("S-1-5-32-544", FileSystemRights.FullControl, inh: InheritanceFlags.None));
        Assert.Equal(NodeSignature.Of(a), NodeSignature.Of(b));
    }

    [Fact]
    public void Zusaetzliches_Konto_oder_anderes_Recht_unterscheidet()
    {
        var a = Node(Ace("S-1-5-18", FileSystemRights.FullControl));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node(Ace("S-1-5-18", FileSystemRights.FullControl), Ace("S-1-5-20", FileSystemRights.Read))));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node(Ace("S-1-5-18", FileSystemRights.Modify))));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node(Ace("S-1-5-18", FileSystemRights.FullControl, deny: true))));
        Assert.NotEqual(NodeSignature.Of(a), NodeSignature.Of(Node()));
    }

    [Fact]
    public void Fehler_ergibt_keine_Signatur()
    {
        var n = Node(Ace("S-1-5-18", FileSystemRights.FullControl));
        n.Error = "Zugriff verweigert";
        Assert.Null(NodeSignature.Of(n));
    }
}

public sealed class LocalShareMapTests
{
    private static ShareRead Share(string name, string path, bool admin = false, bool readable = true, params ShareAce[] aces)
        => new(new ShareInfo { Server = "PC", Name = name, LocalPath = path, IsAdminShare = admin, PermissionsReadable = readable, Permissions = [.. aces] }, null);

    [Fact]
    public void Laengstes_Praefix_gewinnt()
    {
        var map = new LocalShareMap([Share("Daten", @"D:\Daten"), Share("Labor", @"D:\Daten\Labor\"), Share("c", @"C:\")], includeAdminShares: false);
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
        Assert.Null(new LocalShareMap(shares, includeAdminShares: false).Longest(@"C:\Daten"));
        Assert.Equal("C$", new LocalShareMap(shares, includeAdminShares: true).Longest(@"C:\Daten")!.Share.Name);
    }

    [Fact]
    public void Ohne_lokalen_Pfad_keine_Zuordnung()
    {
        var map = new LocalShareMap([Share("x", "")], includeAdminShares: true);
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void Gleicher_Pfad_weitester_Zugriff_gewinnt()
    {
        var lesen = Share("a-lesen", @"D:\Daten", aces: new ShareAce("S-1-1-0", ShareAccess.Lesen, false));
        var voll = Share("b-voll", @"D:\Daten", aces: new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false));
        var unlesbar = Share("c-unlesbar", @"D:\Daten", readable: false);
        var admin = Share("d$", @"D:\Daten", admin: true, readable: false);

        Assert.Equal("b-voll", new LocalShareMap([lesen, voll], true).At(@"D:\Daten")!.Share.Name);
        Assert.Equal("b-voll", new LocalShareMap([voll, lesen], true).At(@"D:\Daten")!.Share.Name);
        Assert.Equal("c-unlesbar", new LocalShareMap([lesen, voll, unlesbar], true).At(@"D:\Daten")!.Share.Name);
        Assert.Equal("b-voll", new LocalShareMap([admin, lesen, voll], true).At(@"D:\Daten")!.Share.Name);
    }
}
