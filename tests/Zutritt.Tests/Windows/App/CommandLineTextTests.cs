// SPDX-License-Identifier: MIT
using Zutritt.Cli;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Windows.App;

/// <summary>Englische Schreibweisen, Fortschrittszeile in schmalen Konsolen, „nichts geprüft“ und Texte der App.</summary>
public sealed class CommandLineTextTests
{
    [Theory]
    [InlineData("list", "liste")]
    [InlineData("REPORT", "bericht")]
    [InlineData("--help", "--hilfe")]
    [InlineData("--files", "--dateien")]
    [InlineData("--depth", "--tiefe")]
    [InlineData("--deep-from", "--ausnahme-ab")]
    [InlineData("--no-groups", "--ohne-gruppen")]
    [InlineData("--include-admin-shares", "--mit-admin-freigaben")]
    [InlineData("--report", "--bericht")]
    [InlineData("--compare", "--vergleich")]
    [InlineData("--licenses", "--lizenzen")]
    [InlineData("scan", "scan")]
    [InlineData("--db", "--db")]
    public void Englische_Schreibweisen_sind_gleichwertig(string english, string german) =>
        Assert.Equal(german, CommandLine.Canonical(english));

    [Theory]
    [InlineData(40)]
    [InlineData(57)]
    [InlineData(60)]
    [InlineData(70)]
    [InlineData(75)]
    [InlineData(80)]
    [InlineData(200)]
    public void Fortschrittszeile_passt_in_jede_Konsolenbreite(int width)
    {
        // Befund: bei 57–75 Spalten wurde das Budget für den Pfad 1–3 Zeichen, ShortenPath warf und brach den Scan ab
        var progress = new ScanProgress("Ordner", @"\\FS01\Labor\" + new string('x', 300), 1_234_567, 98_765, 12);

        var line = CommandLine.ProgressLine(progress, width);

        Assert.True(line.Length <= Math.Max(40, width - 1), line);
        Assert.StartsWith("Ordner: 1.234.567 Ordner", line);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "a")]
    [InlineData(4, "abcd")]
    [InlineData(5, "…ghij")]
    [InlineData(8, "ab…fghij")]
    [InlineData(-3, "")]
    public void Pfade_kuerzen_auch_bei_sehr_wenig_Platz(int max, string expected) =>
        Assert.Equal(expected, Labels.ShortenPath("abcdefghij", max));

    [Fact]
    public void Kein_Ziel_geprueft_ergibt_Exitcode_3()
    {
        using var db = new Zutritt.Tests.Storage.TestDb();
        using var store = db.Open();
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"\\AUS\Labor"] } };
        scan.Folders.Add(new FolderNode { Path = @"\\AUS\Labor", Depth = 0, Error = "Ziel nicht erreichbar: Netzwerkpfad nicht gefunden" });
        scan.Findings.AddRange(Core.Analysis.FindingRules.Evaluate(scan));

        var (failed, total) = CommandLine.UncheckedTargets(scan);
        Assert.Equal((1, 1), (failed, total));
        var output = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.Equal(CommandLine.ExitError, CommandLine.FinishScan(store, scan, compare: false, []));
        }
        finally
        {
            Console.SetOut(previous);
        }
        Assert.Contains("kein Ziel konnte geprüft werden", output.ToString());
    }

    [Fact]
    public void Ein_lesbares_Ziel_reicht_fuer_den_Exitcode_der_Befunde()
    {
        var scan = SampleScan.Create();
        Assert.Equal((0, 1), CommandLine.UncheckedTargets(scan));
    }

    [Fact]
    public void Fehlerzeile_nennt_den_Code() =>
        Assert.Equal("Fehler ZUT-0101: Unbekannte Option.", CommandLine.ErrorLine("Unbekannte Option.", Core.ErrorCodes.Usage));

    [Fact]
    public void Daten_und_Zahlen_folgen_dem_Gebietsschema()
    {
        var t = new DateTimeOffset(2026, 10, 14, 9, 5, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 14, 9, 5, 0)));
        Assert.Equal("14.10.2026, 09:05", Labels.Date(t));
        Assert.Equal("1.234", Labels.Number(1234));
        using (TestCulture.Use("en-US"))
        {
            Assert.Equal("1,234", Labels.Number(1234));
            Assert.Equal("Shares of this computer", Labels.TargetName("."));
        }
    }
}
