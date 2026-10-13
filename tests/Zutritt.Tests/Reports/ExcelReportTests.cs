using System.Diagnostics;
using ClosedXML.Excel;
using Xunit.Abstractions;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Reports.ReportSamples;
using static Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Reports;

public sealed class ExcelReportTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(ReportData data, string name = "bericht.xlsx")
    {
        var path = Path.Combine(_temp.Path, name);
        ExcelReport.Write(data, path);
        return path;
    }

    /// <summary>Zeilen eines Blatts als Wörterbuch Spaltenkopf → Text, ab Zeile 2.</summary>
    private static List<Dictionary<string, string>> Rows(IXLWorksheet ws)
    {
        var used = ws.RangeUsed()!;
        int lastCol = used.LastColumn().ColumnNumber();
        var headers = Enumerable.Range(1, lastCol).Select(c => ws.Cell(1, c).GetString()).ToList();
        return Enumerable.Range(2, used.LastRow().RowNumber() - 1)
            .Select(r => headers.Select((h, i) => (h, v: ws.Cell(r, i + 1).GetFormattedString())).ToDictionary(x => x.h, x => x.v))
            .ToList();
    }

    private static string AllText(IXLWorksheet ws) =>
        string.Join("\n", ws.CellsUsed().Select(c => c.GetFormattedString()));

    [Fact]
    public void Datei_EnthaeltAlleBlaetterInDerRichtigenReihenfolge()
    {
        var path = Write(Data());

        Assert.True(new FileInfo(path).Length > 5_000);
        using var wb = new XLWorkbook(path);
        Assert.Equal(["Übersicht", "Befunde", "Ordner & Rechte", "Personen", "Zugriffe je Person", "Gruppen", "Freigaben", "Prüfregeln", "Fehler"],
            wb.Worksheets.Select(w => w.Name));
        Assert.True(wb.Worksheet("Übersicht").TabActive);
        Assert.Equal(1, wb.Worksheets.Count(w => w.TabSelected)); // keine versehentlich gruppierten Blätter
        Assert.Equal("Berechtigungsbericht", wb.Properties.Title);
        Assert.Equal("Zutritt", wb.Properties.Author);
        // Keine Reste der temporären Datei
        Assert.Equal([path], Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void Datei_IstSchemaGueltig_ExcelMussNichtsReparieren()
    {
        var scan = Scan();
        scan.Folders.Add(new FolderNode { Path = Root + "\\Böse\u0007Datei\uD800", Depth = 1, ShareUnc = Root });
        var path = Write(ReportSamples.Data(scan, Comparison()));

        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc)
            .Select(e => $"{e.Part?.Uri}: {e.Description}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void MitVergleich_BlattAenderungen()
    {
        var data = DataWithComparison();
        var path = Write(data);

        using var wb = new XLWorkbook(path);
        Assert.Equal("Änderungen", wb.Worksheet(9).Name);
        var rows = Rows(wb.Worksheet("Änderungen"));
        Assert.Equal(data.Comparison!.Rows.Count, rows.Count);
        Assert.Equal(["Bereich", "Änderung", "Pfad bzw. Gruppe", "Konto", "Vorher", "Nachher"], rows[0].Keys);
        Assert.Contains(rows, r => r["Bereich"] == "Zugriff" && r["Konto"] == @"LAB\max.muster" && r["Vorher"] == "Ändern" && r["Nachher"] == "Vollzugriff");
        Assert.Contains(rows, r => r["Änderung"] == "behoben" && r["Konto"] == @"LAB\ehemalig");

        var overview = AllText(wb.Worksheet("Übersicht"));
        Assert.Contains("Vergleich mit dem Lauf vom", overview);
        Assert.Contains("Neue Befunde", overview);
    }

    [Fact]
    public void Uebersicht_KopfZaehlerSchwereUndHinweise()
    {
        var data = Data();
        var path = Write(data);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Übersicht");
        var text = AllText(ws);
        Assert.Contains("Berechtigungsbericht", text);
        Assert.Contains(Root, text);
        Assert.Contains("ADMIN-PC", text);
        Assert.Contains(@"LAB\admin.max", text);
        Assert.Contains(ReportText.Period(data.Header.StartedAt, data.Header.FinishedAt), text);
        Assert.Contains("1 Ordner nicht lesbar – dort konnte nichts geprüft werden.", text);
        Assert.Contains("Achtung", text);

        IXLCell ValueOf(string label) => ws.CellsUsed().Single(c => c.Address.ColumnNumber == 1 && c.GetString() == label).CellRight();
        Assert.Equal(1234, ValueOf("Ordner geprüft").GetValue<int>());
        Assert.Equal(6, ValueOf("Ordner mit eigenen Rechten").GetValue<int>());
        Assert.Equal(2, ValueOf("Hoch").GetValue<int>());
        Assert.Equal(5, ValueOf("Mittel").GetValue<int>());
        Assert.Equal(3, ValueOf("Niedrig").GetValue<int>());
        Assert.Equal(3, ValueOf("Info").GetValue<int>());
        Assert.Equal(13, ValueOf("Gesamt").GetValue<int>());
        Assert.Equal("#,##0", ValueOf("Ordner geprüft").Style.NumberFormat.Format);

        var hoch = ws.CellsUsed().Single(c => c.GetString() == "Hoch");
        Assert.Equal(XLColor.FromHtml("#C62828"), hoch.Style.Fill.BackgroundColor);
    }

    [Fact]
    public void Befunde_TabelleMitFilterFixierterKopfzeileUndFarbe()
    {
        var path = Write(Data());

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Befunde");
        var table = Assert.Single(ws.Tables);
        Assert.True(table.ShowAutoFilter);
        Assert.Equal(1, ws.SheetView.SplitRow);

        var rows = Rows(ws);
        Assert.Equal(13, rows.Count);
        Assert.Equal(["Schwere", "Regel", "Titel", "Pfad", "Konto", "Meldung", "Empfehlung", "SID"], rows[0].Keys);
        Assert.Equal(("Hoch", "ZT01", "Breite Gruppe darf schreiben", Austausch, "Jeder"),
            (rows[0]["Schwere"], rows[0]["Regel"], rows[0]["Titel"], rows[0]["Pfad"], rows[0]["Konto"]));
        Assert.Equal("Schreibrecht nur an Fachgruppen vergeben.", rows[0]["Empfehlung"]);
        Assert.Equal(["Hoch", "Hoch", "Mittel", "Mittel", "Mittel", "Mittel", "Mittel", "Niedrig", "Niedrig", "Niedrig", "Info", "Info", "Info"],
            rows.Select(r => r["Schwere"]));

        Assert.Equal(XLColor.FromHtml("#C62828"), ws.Cell(2, 1).Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#EF6C00"), ws.Cell(4, 1).Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#BDBDBD"), ws.Cell(14, 1).Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#5B8CFF"), ws.Cell(1, 1).Style.Fill.BackgroundColor);
        Assert.All(Enumerable.Range(1, 8), c => Assert.InRange(ws.Column(c).Width, 8, 90));
    }

    [Fact]
    public void OrdnerUndRechte_JeOrdnerUndEintragEineZeile()
    {
        var path = Write(Data());

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Ordner & Rechte");
        var rows = Rows(ws);
        Assert.Equal(4 + 5 + 4 + 1 + 4 + 4, rows.Count); // Wurzel, Archiv, Austausch, Personal (Fehler), Rohdaten, QC

        var everyone = rows.Single(r => r["Pfad"] == Austausch && r["Konto"] == "Jeder");
        Assert.Equal("1", everyone["Tiefe"]);
        Assert.Equal("Ordner", everyone["Typ"]);
        Assert.Equal("Nein", everyone["Vererbung unterbrochen"]);
        Assert.Equal(@"LAB\max.muster", everyone["Besitzer"]);
        Assert.Equal("Integriert", everyone["Art"]);
        Assert.Equal("Ändern", everyone["Recht"]);
        Assert.Equal("Erlauben", everyone["Erlauben/Verweigern"]);
        Assert.Equal("explizit", everyone["Geerbt/explizit"]);
        Assert.Equal("Diesen Ordner, Unterordner und Dateien", everyone["Gilt für"]);
        Assert.Equal(Root, everyone["Freigabe"]);
        Assert.Equal(Everyone, everyone["SID"]);
        Assert.Equal("0x001301BF", everyone["Rechte-Maske"]);

        var deny = rows.Single(r => r["Pfad"] == Archiv && r["Konto"] == @"LAB\max.muster");
        Assert.Equal("Verweigern", deny["Erlauben/Verweigern"]);
        Assert.Equal("geerbt", rows.First(r => r["Pfad"] == Archiv && r["Konto"] == @"LAB\Labor-Alle")["Geerbt/explizit"]);

        var personal = rows.Single(r => r["Pfad"] == Personal);
        Assert.Equal("Nicht lesbar: Zugriff verweigert", personal["Hinweis"]);
        Assert.Equal("", personal["Konto"]);

        Assert.Equal("Verwaist", rows.Single(r => r["SID"] == Verwaist)["Art"]);
        Assert.True(ws.Cell(2, 2).Value.IsNumber); // Tiefe als Zahl – filter- und sortierbar
    }

    [Fact]
    public void Personen_ZugriffeGruppenFreigabenRegeln()
    {
        var path = Write(Data());
        using var wb = new XLWorkbook(path);

        var people = Rows(wb.Worksheet("Personen"));
        Assert.Equal([@"LAB\erika.beispiel", @"LAB\klaus.alt", @"LAB\max.muster"], people.Select(p => p["Person"]));
        var max = people.Single(p => p["Person"] == @"LAB\max.muster");
        Assert.Equal(("Max Muster", "Nein", "3", "Vollzugriff", @"Jeder; LAB\Labor-Alle", "Ja", Max),
            (max["Anzeigename"], max["Deaktiviert"], max["Ordner mit Zugriff"], max["Höchstes Recht"], max["Über Gruppen"], max["Direkt berechtigt"], max["SID"]));
        Assert.Equal("Ja", people.Single(p => p["Person"] == @"LAB\klaus.alt")["Deaktiviert"]);

        var grants = Rows(wb.Worksheet("Zugriffe je Person"));
        Assert.Equal(10, grants.Count);
        var erikaRoot = grants.Single(g => g["Person"] == @"LAB\erika.beispiel" && g["Pfad"] == Root);
        Assert.Equal(("Ändern", "Ändern", "Ändern", @"LAB\QC-Team → LAB\Labor-Alle", "Nein"),
            (erikaRoot["Effektives Recht"], erikaRoot["NTFS-Recht"], erikaRoot["Über Freigabe"], erikaRoot["Über Gruppen"], erikaRoot["Durch Verweigern eingeschränkt"]));

        var groups = Rows(wb.Worksheet("Gruppen"));
        var labor = groups.Single(g => g["Gruppe"] == @"LAB\Labor-Alle");
        Assert.Equal(("2", "2", "Ja", "3"), (labor["Mitglieder direkt"], labor["Mitglieder gesamt (Personen)"], labor["Vollständig"], labor["In Ordner-ACLs"]));
        Assert.Equal(@"LAB\max.muster; LAB\QC-Team", labor["Direkte Mitglieder"]);

        var share = Assert.Single(Rows(wb.Worksheet("Freigaben")));
        Assert.Equal((Root, @"D:\Labor", "Labordaten", "Jeder: Vollzugriff", "Nein", "Ja"),
            (share["Freigabe"], share["Pfad"], share["Bemerkung"], share["Freigabe-Rechte"], share["Admin-Freigabe"], share["Rechte lesbar"]));

        var rules = Rows(wb.Worksheet("Prüfregeln"));
        Assert.Equal(13, rules.Count);
        Assert.Equal("2", rules.Single(r => r["Regel"] == "ZT03")["Befunde"]);

        var errors = Assert.Single(Rows(wb.Worksheet("Fehler")));
        Assert.Equal((Personal, "Zugriff verweigert"), (errors["Pfad"], errors["Meldung"]));
    }

    [Fact]
    public void GekuerzteEinzelzugriffe_PersonenZeigtWerFehlt()
    {
        // Budget 4: nur Erika passt – ohne Kennzeichnung hielte man Klaus und Max für unberechtigt
        using (var wb = ExcelReport.Create(Data(maxPersonGrants: 4)))
        {
            var people = Rows(wb.Worksheet("Personen"));
            Assert.Equal("Ja", people.Single(p => p["SID"] == Erika)["Einzelzugriffe aufgeführt"]);
            Assert.Equal("Nein – nur die Summe", people.Single(p => p["SID"] == Max)["Einzelzugriffe aufgeführt"]);
            Assert.Equal("3", people.Single(p => p["SID"] == Max)["Ordner mit Zugriff"]);
            Assert.All(Rows(wb.Worksheet("Zugriffe je Person")), r => Assert.Equal(@"LAB\erika.beispiel", r["Person"]));
        }
        // Ohne Kürzung keine zusätzliche Spalte
        using var full = ExcelReport.Create(Data());
        Assert.DoesNotContain("Einzelzugriffe aufgeführt", Rows(full.Worksheet("Personen"))[0].Keys);
    }

    [Fact]
    public void LeererLauf_TabellenMitHinweiszeile()
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"C:\Leer"] } };
        var path = Write(Data(scan));

        using var wb = new XLWorkbook(path);
        Assert.Equal("Keine Befunde.", wb.Worksheet("Befunde").Cell(2, 1).GetString());
        Assert.Equal("Keine Ordner gespeichert.", wb.Worksheet("Ordner & Rechte").Cell(2, 1).GetString());
        Assert.Equal("Keine Fehler beim Lesen.", wb.Worksheet("Fehler").Cell(2, 1).GetString());
        Assert.All(wb.Worksheets.Skip(1), ws => Assert.Single(ws.Tables));
        Assert.Contains(@"C:\Leer", AllText(wb.Worksheet("Übersicht")));
    }

    [Fact]
    public void ZuVieleZeilen_WerdenGekuerztUndGenannt()
    {
        using var wb = ExcelReport.Create(Data(), maxRows: 5);

        Assert.Equal(6, wb.Worksheet("Ordner & Rechte").LastRowUsed()!.RowNumber());
        Assert.Equal(6, wb.Worksheet("Befunde").LastRowUsed()!.RowNumber());
        var overview = AllText(wb.Worksheet("Übersicht"));
        Assert.Contains("Blatt „Ordner & Rechte“ gekürzt: 5 von 22 Zeilen", overview);
        Assert.Contains("Blatt „Befunde“ gekürzt: 5 von 13 Zeilen", overview);
    }

    [Fact]
    public void UngueltigeZeichenUndFormeln_WerdenAlsText_Gespeichert()
    {
        var scan = Scan();
        scan.Principals[Max].DisplayName = "Max\u0001Muster\uD800";
        scan.Folders.Add(new FolderNode { Path = Root + "\\Böse\u0007Datei", Depth = 1, ShareUnc = Root });
        scan.Findings.Add(F("ZT03", Severity.Mittel, "=cmd|' /C calc'!A0", Max, "=HYPERLINK(\"http://x\")"));
        var path = Write(Data(scan));

        using var wb = new XLWorkbook(path);
        var person = Rows(wb.Worksheet("Personen")).Single(p => p["SID"] == Max);
        Assert.Equal("Max\uFFFDMuster\uFFFD", person["Anzeigename"]);
        Assert.Contains(Rows(wb.Worksheet("Ordner & Rechte")), r => r["Pfad"] == Root + "\\Böse\uFFFDDatei");
        var befunde = wb.Worksheet("Befunde");
        var formulaLike = befunde.CellsUsed().Where(c => c.GetString().StartsWith('=')).ToList();
        Assert.Equal(2, formulaLike.Count);
        Assert.All(formulaLike, c => Assert.False(c.HasFormula));
    }

    [Fact]
    public void UeberschreibtVorhandeneDatei_UndLegtOrdnerAn()
    {
        var path = Path.Combine(_temp.Path, "neu", "tief", "bericht.xlsx");
        ExcelReport.Write(Data(), path);
        var first = File.GetLastWriteTimeUtc(path);
        ExcelReport.Write(DataWithComparison(), path);

        using var wb = new XLWorkbook(path);
        Assert.Contains(wb.Worksheets, w => w.Name == "Änderungen");
        Assert.True(File.GetLastWriteTimeUtc(path) >= first);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void GeoeffneteDatei_DeutscheFehlermeldung_AlteDateiBleibt_KeineReste()
    {
        var path = Path.Combine(_temp.Path, "offen.xlsx");
        File.WriteAllText(path, "alt");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var e = Assert.Throws<ReportWriteException>(() => ExcelReport.Write(Data(), path));
            Assert.Equal(path, e.Path);
            Assert.Contains("„" + path + "“", e.Message);
            Assert.True(e.Message.Contains("geöffnet") || e.Message.Contains("ersetzt"), e.Message);
            Assert.NotNull(e.InnerException);
            Assert.IsAssignableFrom<IOException>(e);
        }
        Assert.Equal("alt", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void UngueltigerPfad_ReportWriteException()
    {
        Assert.Throws<ReportWriteException>(() => ExcelReport.Write(Data(), Path.Combine(_temp.Path, "a|b.xlsx")));
        Assert.Throws<ReportWriteException>(() => ExcelReport.Write(Data(), _temp.Path)); // Ordner statt Datei
        Assert.Throws<ArgumentException>(() => ExcelReport.Write(Data(), " "));
        Assert.Throws<ArgumentNullException>(() => ExcelReport.Write(null!, null, Path.Combine(_temp.Path, "x.xlsx")));
        Assert.Empty(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void PfadOhneDateiname_ReportWriteException_LegtKeinenOrdnerAn()
    {
        var dir = Path.Combine(_temp.Path, "neu") + Path.DirectorySeparatorChar;
        var e = Assert.Throws<ReportWriteException>(() => ExcelReport.Write(Data(), dir));
        Assert.Contains("keinen Dateinamen", e.Message);
        Assert.Throws<ReportWriteException>(() => PdfReport.Write(Scan(), null, dir));
        Assert.Empty(Directory.GetFileSystemEntries(_temp.Path));
    }

    [Fact]
    public void GrosserLauf_ImZeitbudget()
    {
        // 20.000 Ordner × 5 Einträge = 100.000 Zeilen, 100 Personen (+3 aus dem SampleScan) mit je 400 Zugriffen.
        // Das Budget ist großzügig, weil parallel gebaut wird; es soll quadratisches Verhalten fangen.
        var scan = Large(folders: 20_000, acesPerFolder: 5, users: 100);
        var paths = scan.Folders.Select(f => f.Path).ToList();
        IReadOnlyList<AccessGrant> ForEveryone(string sid) =>
            paths.Take(400).Select(p => G(p, sid, AccessLevel.Lesen, @"LAB\Gruppe-01")).ToList();

        var sw = Stopwatch.StartNew();
        var data = ReportDataBuilder.Build(scan, null, Rules(), ForEveryone, CreatedAt);
        var built = sw.Elapsed;
        var path = Path.Combine(_temp.Path, "gross.xlsx");
        ExcelReport.Write(data, path);
        sw.Stop();
        output.WriteLine($"Aufbereiten {built}, gesamt {sw.Elapsed}, Datei {new FileInfo(path).Length / 1024} KB, "
            + $"Speicher max. {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), $"Excel-Bericht brauchte {sw.Elapsed}");
        using var wb = new XLWorkbook(path);
        Assert.Equal(100_001, wb.Worksheet("Ordner & Rechte").LastRowUsed()!.RowNumber());
        Assert.Equal(103 * 400 + 1, wb.Worksheet("Zugriffe je Person").LastRowUsed()!.RowNumber());
        Assert.Equal(104, wb.Worksheet("Personen").LastRowUsed()!.RowNumber());
    }
}
