// SPDX-License-Identifier: MIT
using Zutritt.Core.Storage;

namespace Zutritt.Tests.Storage;

/// <summary>Standardpfad der Datenbank und Erkennung von Netzwerkpfaden.</summary>
public sealed class DatabaseLocationTests
{
    [Fact]
    public void Standardpfad_liegt_im_Benutzerprofil()
    {
        // Windows: %LocalAppData%\Zutritt, macOS: ~/Library/Application Support/Zutritt, Linux: ~/.local/share/zutritt
        var folder = OperatingSystem.IsLinux() ? "zutritt" : "Zutritt";
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder, "zutritt.db");

        Assert.Equal(expected, ScanStore.DefaultPath);
        Assert.Equal(expected, DatabaseLocation.Default);
    }

    [Fact]
    public void Standardpfad_liegt_nicht_unter_ProgramData()
    {
        // Ein Lauf ist eine Landkarte der Berechtigungen – in %ProgramData% wäre sie für alle lokalen Benutzer lesbar
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.False(ScanStore.DefaultPath.StartsWith(programData, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"\\server\freigabe\zutritt.db", true)]
    [InlineData(@"\\?\UNC\server\freigabe\zutritt.db", true)]
    [InlineData(@"\\?\C:\Daten\zutritt.db", false)]
    [InlineData(@"\\.\pipe\irgendwas", false)]
    [InlineData(@"C:\Daten\zutritt.db", false)]
    public void Netzwerkpfade_werden_erkannt(string path, bool expected) =>
        Assert.Equal(expected, DatabaseLocation.IsNetworkPath(path));
}
