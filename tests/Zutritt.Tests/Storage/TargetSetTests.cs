using Zutritt.Core.Storage;

namespace Zutritt.Tests.Storage;

public sealed class TargetSetTests
{
    [Theory]
    [InlineData(@"\\FS01\Labor", @"\\FS01\LABOR")]
    [InlineData(@"\\fs01\labor\", @"\\FS01\LABOR")]
    [InlineData(@"  \\fs01\labor\\  ", @"\\FS01\LABOR")]
    [InlineData(@"//fs01/labor/", @"\\FS01\LABOR")]
    [InlineData(@"c:\daten\", @"C:\DATEN")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"c:/", @"C:\")]
    [InlineData(@"C:", @"C:\")]       // wie TargetParser: das ganze Laufwerk, sonst fände „scan D:“ nie „scan D:\“
    [InlineData(@"d:", @"D:\")]
    [InlineData(".", ".")]
    [InlineData(@"\", @"\")]
    [InlineData(@"\\server", @"\\SERVER")]
    [InlineData(@"\\server\", @"\\SERVER")]
    [InlineData(@"D:\Prüfung\Ärzte", @"D:\PRÜFUNG\ÄRZTE")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalisierung(string? input, string expected) => Assert.Equal(expected, TargetSet.Normalize(input));

    [Fact]
    public void Schluessel_ist_eine_Menge()
    {
        var a = TargetSet.Key([@"\\FS01\Labor", @"\\FS01\Archiv"]);

        Assert.Equal(a, TargetSet.Key([@"\\fs01\archiv\", @"\\FS01\Labor", @"\\FS01\LABOR"]));
        Assert.NotEqual(a, TargetSet.Key([@"\\FS01\Labor"]));
        Assert.NotEqual(a, TargetSet.Key([@"\\FS01\Labor", @"\\FS01\Archiv", "."]));
        Assert.Equal("", TargetSet.Key([]));
        Assert.Equal("", TargetSet.Key(null));
        Assert.Equal(TargetSet.Key(["."]), TargetSet.Key(["", " ", "."]));
    }

    [Fact]
    public void Trenner_im_Schluessel_kann_nicht_aus_Pfaden_entstehen()
    {
        // „;“ ist in Pfaden erlaubt – die Anzeige verbindet mit „; “, der Schlüssel darf das nicht
        Assert.NotEqual(TargetSet.Key([@"C:\a; C:\b"]), TargetSet.Key([@"C:\a", @"C:\b"]));
        Assert.Equal(@"C:\a; C:\b", TargetSet.Display([@"C:\a", @"C:\b"]));
        Assert.Equal("", TargetSet.Display(null));
    }

    [Theory]
    [InlineData(".", true)]
    [InlineData(@"C:\Daten", true)]
    [InlineData(@"Daten\Relativ", true)]
    [InlineData(@"\\localhost\Daten", true)]
    [InlineData(@"\\LOCALHOST\Daten", true)]
    [InlineData(@"\\127.0.0.1\Daten", true)]
    [InlineData(@"\\.\C:\Daten", true)]
    [InlineData(@"\\?\C:\Daten", true)]
    [InlineData(@"\\?\UNC\localhost\Daten", true)]
    [InlineData(@"\\FS01\Labor", false)]
    [InlineData(@"\\FS01", false)]
    [InlineData(@"\\fs01.lab.local\Labor", false)]
    [InlineData(@"\\?\UNC\FS01\Labor", false)]
    [InlineData("", false)]
    public void Rechnerabhaengige_Ziele(string target, bool expected) => Assert.Equal(expected, TargetSet.DependsOnMachine(target));

    [Fact]
    public void Ein_rechnerabhaengiges_Ziel_macht_die_ganze_Menge_rechnerabhaengig()
    {
        Assert.True(TargetSet.DependsOnMachine([@"\\FS01\Labor", @"C:\Daten"]));
        Assert.False(TargetSet.DependsOnMachine([@"\\FS01\Labor", @"\\FS02\Archiv"]));
        Assert.False(TargetSet.DependsOnMachine((IEnumerable<string>?)null));
    }
}
