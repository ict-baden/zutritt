// SPDX-License-Identifier: MIT
using Zutritt.Core.Identity;
using Zutritt.Core.Model;

namespace Zutritt.Tests.Identity;

public class MachineNamesTests
{
    [Theory]
    [InlineData(@"\\FS01\Labor", "FS01")]
    [InlineData(@"\\fs01\Labor\Unter\Ordner", "FS01")]
    [InlineData(@"\\fs01.lab.local\Labor", "FS01.LAB.LOCAL")]
    [InlineData(@"\\FS01", "FS01")]
    [InlineData(@"\\FS01\", "FS01")]
    [InlineData(@"\\?\UNC\FS01\Labor\x", "FS01")]
    [InlineData(@"//FS01/Labor", "FS01")]
    [InlineData(@"\\10.0.0.5\Daten", "10.0.0.5")]
    public void Server_aus_UNC_Pfad(string path, string server) => Assert.Equal(server, MachineNames.ServerOfPath(path));

    [Theory]
    [InlineData(@"C:\Daten")]
    [InlineData(@"\\?\C:\Daten\sehr\lang")]
    [InlineData(@"\\.\C:\Daten")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData(@"\\localhost\Daten")]
    [InlineData(@"\\127.0.0.1\Daten")]
    [InlineData(@"\\.\")]
    public void Lokale_Pfade_ergeben_diesen_Rechner(string path) => Assert.Null(MachineNames.ServerOfPath(path));

    [Fact]
    public void Eigener_Rechnername_gilt_als_lokal()
    {
        Assert.Null(MachineNames.Normalize(Environment.MachineName.ToLowerInvariant()));
        Assert.Null(MachineNames.ServerOfPath($@"\\{Environment.MachineName}\Freigabe"));
        Assert.Null(MachineNames.Normalize(@"\\."));
        Assert.Null(MachineNames.Normalize("  "));
        Assert.Equal("FS01", MachineNames.Normalize(@"\\fs01"));
    }

    [Theory]
    [InlineData(@"\\FS01\", "FS01")]
    [InlineData(@"  //fs01/ ", "fs01")]
    [InlineData("localhost", "localhost")]
    [InlineData(@"\\", null)]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void Servername_ohne_Schraegstriche(string? input, string? expected) => Assert.Equal(expected, MachineNames.Strip(input));

    [Fact]
    public void Rechner_eines_Laufs_in_Reihenfolge_ohne_Doppelte()
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"\\FS02", ".", @"C:\Daten"] } };
        scan.Shares.Add(new ShareInfo { Server = "fs02", Name = "A" });
        scan.Shares.Add(new ShareInfo { Server = Environment.MachineName, Name = "B" });
        scan.Folders.Add(new FolderNode { Path = @"\\FS03\C" });
        scan.Folders.Add(new FolderNode { Path = @"D:\Lokal", ShareUnc = @"\\FS04\Lokal" });

        Assert.Equal(["FS02", null, "FS03", "FS04"], MachineNames.Of(scan));
    }

    [Fact]
    public void Leerer_Lauf_gilt_als_dieser_Rechner() => Assert.Equal([null], MachineNames.Of(new ScanResult()));
}
