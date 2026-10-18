using Zutritt.Platform.Windows.Scanning;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

public sealed class LongPathTests
{
    [Theory]
    [InlineData(@"C:\Daten", @"\\?\C:\Daten")]
    [InlineData(@"C:\", @"\\?\C:\")]
    [InlineData(@"\\FS01\Labor\QC", @"\\?\UNC\FS01\Labor\QC")]
    [InlineData(@"\\?\C:\Daten", @"\\?\C:\Daten")]
    [InlineData(@"C:\Ordner mit Leerzeichen am Ende ", @"\\?\C:\Ordner mit Leerzeichen am Ende ")]
    [InlineData(@"\\.\C:\Daten", @"\\?\C:\Daten")]                                // Gerätepfad, kein Server „.“
    [InlineData(@"\\?\Volume{0c1d}\Daten", @"\\?\Volume{0c1d}\Daten")]
    public void ToExtended(string path, string expected) => Assert.Equal(expected, LongPath.ToExtended(path));

    [Theory]
    [InlineData(@"\\?\Volume{0c1d}\Daten", true)]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1", true)]
    [InlineData(@"\\?\C:\Daten", false)]
    [InlineData(@"\\?\UNC\FS01\Labor", false)]
    [InlineData(@"C:\Daten", false)]
    [InlineData(@"Daten", false)]
    public void IsDevicePath(string path, bool expected) => Assert.Equal(expected, LongPath.IsDevicePath(path, LongPath.FromExtended(path)));

    [Theory]
    [InlineData(@"\\?\C:\Daten", @"C:\Daten")]
    [InlineData(@"\\?\UNC\FS01\Labor", @"\\FS01\Labor")]
    [InlineData(@"\\?\unc\FS01\Labor", @"\\FS01\Labor")]
    [InlineData(@"C:\Daten", @"C:\Daten")]
    public void FromExtended(string path, string expected) => Assert.Equal(expected, LongPath.FromExtended(path));

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
