using Zutritt.Core.Model;
using Zutritt.Core.Reports;

namespace Zutritt.Tests.Reports;

public sealed class ReportTextTests
{
    private const AceInheritance CI = AceInheritance.ContainerInherit;
    private const AceInheritance OI = AceInheritance.ObjectInherit;
    private const AcePropagation IO = AcePropagation.InheritOnly;
    private const AcePropagation NP = AcePropagation.NoPropagateInherit;

    [Theory]
    [InlineData(AceInheritance.None, AcePropagation.None, "Nur diesen Ordner")]
    [InlineData(CI | OI, AcePropagation.None, "Diesen Ordner, Unterordner und Dateien")]
    [InlineData(CI, AcePropagation.None, "Diesen Ordner und Unterordner")]
    [InlineData(OI, AcePropagation.None, "Diesen Ordner und Dateien")]
    [InlineData(CI | OI, IO, "Nur Unterordner und Dateien")]
    [InlineData(CI, IO, "Nur Unterordner")]
    [InlineData(OI, IO, "Nur Dateien")]
    [InlineData(CI | OI, NP, "Diesen Ordner, Unterordner und Dateien (nur eine Ebene)")]
    [InlineData(AceInheritance.None, NP, "Nur diesen Ordner")]
    public void GiltFuer_WieImWindowsDialog(AceInheritance inheritance, AcePropagation propagation, string expected) =>
        Assert.Equal(expected, ReportText.AppliesTo(inheritance, propagation, isFile: false));

    [Fact]
    public void GiltFuer_Datei() => Assert.Equal("Nur diese Datei", ReportText.AppliesTo(CI | OI, AcePropagation.None, isFile: true));

    [Fact]
    public void Clean_ErsetztSteuerzeichenUndHalbeSurrogate_BehaeltGueltiges()
    {
        Assert.Equal("", ReportText.Clean(null));
        const string valid = "Ä ö ü ß „x“ – € 中文 😀\tZeile\nZwei";
        Assert.Same(valid, ReportText.Clean(valid));
        Assert.Equal("a\uFFFDb\uFFFDc\uFFFD", ReportText.Clean("a\u0001b\uD800c\uFFFF"));
        Assert.Equal("\uFFFDx", ReportText.Clean("\uDC00x"));
        Assert.Equal("ende\uFFFD", ReportText.Clean("ende\uD83D"));
    }

    [Fact]
    public void Zahlen_UndDatum_Deutsch()
    {
        Assert.Equal("1.234.567", ReportText.Number(1_234_567));
        var start = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(2));
        var local = start.ToLocalTime();
        Assert.Equal(local.ToString("dd.MM.yyyy HH:mm"), ReportText.DateTime(start));
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$", ReportText.DateTime(start));
        Assert.Equal("–", ReportText.DateTime(default));
    }

    [Fact]
    public void Zeitraum_GleicherTagOhneZweitesDatum()
    {
        var start = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero).ToLocalTime();
        var sameDay = ReportText.Period(start, start.AddMinutes(3));
        Assert.Equal($"{start:dd.MM.yyyy HH:mm} – {start.AddMinutes(3):HH:mm} Uhr", sameDay);

        var nextDay = ReportText.Period(start, start.AddDays(1));
        Assert.Equal($"{start:dd.MM.yyyy HH:mm} – {start.AddDays(1):dd.MM.yyyy HH:mm} Uhr", nextDay);

        Assert.Equal($"{start:dd.MM.yyyy HH:mm} Uhr", ReportText.Period(start, default));
        Assert.Equal("–", ReportText.Period(default, default));
    }

    [Fact]
    public void Umfang_BeschreibtAnfrage()
    {
        Assert.Equal("nur Ordner (Dateien nicht einzeln) · alle Ebenen · Gruppen bis zur Person aufgelöst · administrative Freigaben übersprungen",
            ReportText.Scope(new ScanRequest { Targets = ["."] }));
        Assert.Equal("Ordner und Dateien · bis Tiefe 3 · Gruppen nicht aufgelöst · administrative Freigaben einbezogen",
            ReportText.Scope(new ScanRequest { Targets = ["."], IncludeFiles = true, MaxDepth = 3, ResolveGroups = false, SkipAdminShares = false }));
    }

    [Fact]
    public void Eintrag_Kurzbeschreibung_MitMaske()
    {
        var ace = new AceEntry { Sid = "S-1-1-0", Rights = FileRights.ReadAndExecute | FileRights.Synchronize, IsDeny = true, IsInherited = true };
        Assert.Equal("Lesen · Verweigern · geerbt · Nur diesen Ordner", ReportText.Ace(ace, isFile: false));
        Assert.Equal("Lesen · Verweigern · geerbt · Nur diese Datei (0x001200A9)", ReportText.Ace(ace, isFile: true, withMask: true));
    }

    [Fact]
    public void Bezeichnungen_Deutsch()
    {
        Assert.Equal(["Info", "Niedrig", "Mittel", "Hoch"], Enum.GetValues<Severity>().Select(ReportText.Severity));
        Assert.Equal("Integriert", ReportText.Kind(PrincipalKind.Bekannt));
        Assert.Equal("Verwaist", ReportText.Kind(PrincipalKind.Verwaist));
        Assert.Equal("Hinzugefügt", ReportText.Change(ChangeKind.Hinzugefuegt));
        Assert.Equal("Geändert", ReportText.Change(ChangeKind.Geaendert));
    }

    [Fact]
    public void Pfade_AlsBaumSortiert()
    {
        string[] paths =
        [
            @"\\FS01\Labor-Alt",
            @"\\FS01\Labor\Zentrale",
            @"\\FS01\labor\Ärzte",
            @"\\FS01\Labor",
            @"\\FS01\Labor\Bau",
            @"\\FS01\Labor\Archiv\Unter",
        ];
        var sorted = paths.Order(PathOrder.Instance).ToList();
        Assert.Equal(
        [
            @"\\FS01\Labor",
            @"\\FS01\Labor\Archiv\Unter",
            @"\\FS01\labor\Ärzte",
            @"\\FS01\Labor\Bau",
            @"\\FS01\Labor\Zentrale",
            @"\\FS01\Labor-Alt",
        ], sorted);
        Assert.Equal(0, PathOrder.Instance.Compare(null, null));
        Assert.True(PathOrder.Instance.Compare(null, "a") < 0);
        Assert.NotEqual(0, PathOrder.Instance.Compare(@"C:\A", @"C:\a")); // stabil, nicht gleich
    }

    [Theory]
    [InlineData("1.0.0+f733926704a0b920f824e12bacf806e5c132891d", "1.0.0+f733926")]
    [InlineData("1.0.0+f733926", "1.0.0+f733926")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-ci.12+lokal-geaendert", "1.2.3-ci.12+lokal-geaendert")] // kein Hash: unverändert
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Version_KuerztCommitHash(string? version, string expected) =>
        Assert.Equal(expected, ReportText.Version(version));
}
