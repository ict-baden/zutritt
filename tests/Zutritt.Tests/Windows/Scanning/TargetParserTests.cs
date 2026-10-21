// SPDX-License-Identifier: MIT
using Zutritt.Core.Scanning;
using Zutritt.Platform.Windows.Scanning;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

public sealed class TargetParserTests
{
    [Fact]
    public void Punkt_bedeutet_lokale_Freigaben()
    {
        var t = TargetParser.Parse(" . ", out var error);
        Assert.Null(error);
        Assert.Equal(TargetKind.LocalShares, t!.Kind);
    }

    [Theory]
    [InlineData(@"\\FS01", "FS01")]
    [InlineData(@"\\FS01\", "FS01")]
    [InlineData(@"//fs01.lab.local", "fs01.lab.local")]
    public void Server(string raw, string server)
    {
        var t = TargetParser.Parse(raw, out _);
        Assert.Equal(TargetKind.Server, t!.Kind);
        Assert.Equal(server, t.Server);
        Assert.Equal(@"\\" + server, t.Path);
    }

    [Theory]
    [InlineData(@"\\FS01\Labor", @"\\FS01\Labor", "FS01", "Labor")]
    [InlineData(@"\\FS01\Labor\", @"\\FS01\Labor", "FS01", "Labor")]
    [InlineData(@"\\FS01\Labor\QC\Rohdaten\", @"\\FS01\Labor\QC\Rohdaten", "FS01", "Labor")]
    [InlineData(@"\\FS01\Labor\QC\..\Archiv", @"\\FS01\Labor\Archiv", "FS01", "Labor")]
    [InlineData(@"\\?\UNC\FS01\Labor\QC", @"\\FS01\Labor\QC", "FS01", "Labor")]
    [InlineData(@"//FS01/Labor/QC", @"\\FS01\Labor\QC", "FS01", "Labor")]
    public void Unc(string raw, string path, string server, string share)
    {
        var t = TargetParser.Parse(raw, out var error);
        Assert.Null(error);
        Assert.Equal(TargetKind.UncPath, t!.Kind);
        Assert.Equal(path, t.Path);
        Assert.Equal(server, t.Server);
        Assert.Equal(share, t.Share);
        Assert.Equal($@"\\{server}\{share}", t.ShareUnc);
    }

    [Theory]
    [InlineData(@"D:\Daten", @"D:\Daten")]
    [InlineData(@"D:\Daten\", @"D:\Daten")]
    [InlineData(@"D:\Daten\.\Labor\..", @"D:\Daten")]
    [InlineData(@"d:", @"d:\")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData(@"\\?\D:\Daten", @"D:\Daten")]
    [InlineData(@"D:/Daten/Labor", @"D:\Daten\Labor")]
    public void Lokal(string raw, string path)
    {
        var t = TargetParser.Parse(raw, out var error);
        Assert.Null(error);
        Assert.Equal(TargetKind.LocalPath, t!.Kind);
        Assert.Equal(path, t.Path);
    }

    [Fact]
    public void Relativer_Pfad_wird_zum_aktuellen_Verzeichnis()
    {
        var t = TargetParser.Parse("Unterordner", out _);
        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "Unterordner"), t!.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(@"\\")]
    [InlineData(@"\\\Labor")]
    [InlineData(@"\\FS01\\Labor")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1")]
    [InlineData(@"\\?\Volume{7754b835-8f11-4608-8817-76a29c12a80a}\Daten")] // war: relativer Pfad zum aktuellen Verzeichnis
    [InlineData(@"\\FS*\Labor")]
    [InlineData("C:\\Da\0ten")]
    public void Ungueltig(string? raw)
    {
        var t = TargetParser.Parse(raw, out var error);
        Assert.Null(t);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
