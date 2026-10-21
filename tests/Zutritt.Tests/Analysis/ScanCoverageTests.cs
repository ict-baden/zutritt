// SPDX-License-Identifier: MIT
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Analysis;

/// <summary>
/// „N von M Zielen bzw. Freigaben nicht geprüft“ und Exitcode 3 der Befehlszeile (Befund der Prüfung: gezählt wurden nur
/// Wurzeln – ein Server ohne Antwort blieb stumm, und eine Wurzel mit unlesbaren Rechten galt als ungeprüft, auch wenn
/// ihre Unterordner gelesen wurden).
/// </summary>
public sealed class ScanCoverageTests
{
    private static readonly AceEntry Ace = new() { Sid = "S-1-5-32-544", Rights = FileRights.FullControl };

    private static FolderNode Node(string path, int depth, string? error = null, bool aces = true)
    {
        var node = new FolderNode { Path = path, Depth = depth, Error = error };
        if (aces) node.Aces.Add(Ace);
        return node;
    }

    [Fact]
    public void Lesbares_Ziel_ist_geprueft() => Assert.Equal((0, 1), ScanCoverage.Count(SampleScan.Create()));

    [Fact]
    public void Server_ohne_Freigabeliste_zaehlt_als_nicht_geprueft()
    {
        // scan \\FS01 \\FS02: FS01 hat zwei Freigaben, FS02 antwortet nicht
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"\\FS01", @"\\FS02"] } };
        scan.Folders.Add(Node(@"\\FS01\Labor", 0));
        scan.Folders.Add(Node(@"\\FS01\QM", 0));
        scan.Errors.Add(new ScanError(@"\\FS02", "Netzwerkpfad nicht gefunden"));
        scan.TargetsWithoutRoot.Add(@"\\FS02");

        Assert.Equal((1, 3), ScanCoverage.Count(scan));
    }

    [Fact]
    public void Ohne_jede_Wurzel_ist_nichts_geprueft()
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"\\AUS"] } };
        scan.TargetsWithoutRoot.Add(@"\\AUS");
        Assert.Equal((1, 1), ScanCoverage.Count(scan));
        Assert.Equal((0, 0), ScanCoverage.Count(new ScanResult()));
    }

    [Fact]
    public void Wurzel_mit_unlesbaren_Rechten_ist_geprueft_wenn_darunter_etwas_lesbar_war()
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"D:\Daten"] } };
        scan.Folders.Add(Node(@"D:\Daten", 0, "Zugriff verweigert", aces: false));
        scan.Folders.Add(Node(@"D:\Daten\Labor", 1));
        Assert.Equal((0, 1), ScanCoverage.Count(scan));

        // Darunter ebenfalls nichts lesbar: nicht geprüft
        scan.Folders.RemoveAt(1);
        scan.Folders.Add(Node(@"D:\Daten\Labor", 1, "Zugriff verweigert", aces: false));
        Assert.Equal((1, 1), ScanCoverage.Count(scan));
    }

    [Fact]
    public void Ordner_eines_inneren_Ziels_zaehlen_nicht_fuer_das_aeussere()
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"D:\Daten", @"D:\Daten\Labor"] } };
        scan.Folders.Add(Node(@"D:\Daten", 0, "Ziel nicht erreichbar", aces: false));
        scan.Folders.Add(Node(@"D:\Daten\Labor", 0));
        scan.Folders.Add(Node(@"D:\Daten\Labor\QC", 1));
        Assert.Equal((1, 2), ScanCoverage.Count(scan));
    }
}
