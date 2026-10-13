using System.Text;
using MigraDoc.DocumentObjectModel;
using PdfSharp.Pdf.IO;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Reports.ReportSamples;
using static Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Reports;

public sealed class PdfReportTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(ReportData data, string name = "bericht.pdf", PdfLimits? limits = null)
    {
        var path = Path.Combine(_temp.Path, name);
        PdfReport.Write(data, path, limits);
        return path;
    }

    private static int PageCount(string path)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return pdf.PageCount;
    }

    [Fact]
    public void Datei_IstPdfMitMehrerenSeitenUndMetadaten()
    {
        var path = Write(Data());

        var head = new byte[5];
        using (var stream = File.OpenRead(path)) stream.ReadExactly(head);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
        Assert.True(new FileInfo(path).Length > 10_000);
        Assert.True(PageCount(path) > 1);

        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.Equal("Berechtigungsbericht", pdf.Info.Title);
        Assert.Equal("Zutritt", pdf.Info.Author);
        Assert.Equal(Root, pdf.Info.Subject);
        Assert.Equal([path], Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void Dokument_AbschnitteInReihenfolge_OhneVergleichKeineAenderungen()
    {
        var doc = PdfReport.CreateDocument(Data());
        Assert.Equal(["Zusammenfassung", "Befunde", "Freigaben", "Ordner mit eigenen Rechten", "Personen-Übersicht",
            "Nicht geprüfte Bereiche", "Prüfung und Freigabe"], PdfDom.Headings(doc));
    }

    [Fact]
    public void Dokument_MitVergleich_AenderungenVorDemAbzeichnen()
    {
        var doc = PdfReport.CreateDocument(DataWithComparison());
        var headings = PdfDom.Headings(doc);
        Assert.Equal("Änderungen seit dem letzten Lauf", headings[^3]);
        var text = PdfDom.Text(doc);
        Assert.Contains("mehr Zugriff", text);
        Assert.Contains(@"LAB\ehemalig", text);
        Assert.Contains("Vergleich mit dem letzten Lauf", text);
    }

    [Fact]
    public void Deckblatt_Kopf_Fusszeile()
    {
        var data = Data();
        var doc = PdfReport.CreateDocument(data);
        var section = doc.Sections.Cast<Section>().Single();
        Assert.Equal(PageFormat.A4, section.PageSetup.PageFormat);
        Assert.True(section.PageSetup.DifferentFirstPageHeaderFooter);

        var footer = PdfDom.Text(section.Footers.Primary);
        Assert.Contains("Erstellt am " + ReportText.DateTime(CreatedAt), footer);
        Assert.Contains("Seite ", footer);
        Assert.Contains(" von ", footer);
        Assert.Contains(section.Footers.Primary.Elements.OfType<Paragraph>().Single().Elements.Cast<DocumentObject>(),
            e => e is MigraDoc.DocumentObjectModel.Fields.NumPagesField);
        Assert.Contains("Berechtigungsbericht", PdfDom.Text(section.Headers.Primary));
        Assert.Contains(Root, PdfDom.Text(section.Headers.Primary));

        var text = PdfDom.Text(doc);
        foreach (var expected in new[] { "Berechtigungsbericht", Root, "ADMIN-PC", @"LAB\admin.max", "1.0.0", ReportText.Period(data.Header.StartedAt, data.Header.FinishedAt),
                     "Gruppen bis zur Person aufgelöst" })
            Assert.Contains(expected, text);
    }

    [Fact]
    public void Inhalt_BefundeFreigabenOrdnerPersonen()
    {
        var text = PdfDom.Text(PdfReport.CreateDocument(Data()));

        // Zusammenfassung und Hinweise
        Assert.Contains("1 Ordner nicht lesbar – dort konnte nichts geprüft werden.", text);
        Assert.Contains("Achtung: ", text);
        // Befunde mit Regeltitel, Meldung, Konto
        Assert.Contains("Breite Gruppe darf schreiben", text);
        Assert.Contains("„Jeder“ darf in „Austausch“ ändern.", text);
        Assert.Contains("Schreibrecht nur an Fachgruppen vergeben.", text);
        // Freigaben
        Assert.Contains("Jeder: Vollzugriff", text);
        Assert.Contains("Labordaten", text);
        // Ordner: Meta, explizite Einträge, geerbte kompakt, Fehler
        Assert.Contains("Tiefe 4 · Vererbung unterbrochen · Besitzer: VORDEFINIERT\\Administratoren", text);
        Assert.Contains(@"Geerbt: NT-AUTORITÄT\SYSTEM (Vollzugriff), VORDEFINIERT\Administratoren (Vollzugriff), LAB\Labor-Alle (Ändern)", text);
        Assert.Contains("Nicht lesbar: Zugriff verweigert", text);
        Assert.Contains("Verweigern", text);
        // Personen
        Assert.Contains("3 von 3 Benutzerkonten", text);
        Assert.Contains("Max Muster", text);
        Assert.Contains("deaktiviert", text);
        Assert.Contains("direkt berechtigt", text);
    }

    [Fact]
    public void Abzeichnen_BlockMitAllenFeldern_ZusammenAufEinerSeite()
    {
        var doc = PdfReport.CreateDocument(Data());
        var section = doc.Sections.Cast<Section>().Single();
        var table = section.Elements.OfType<MigraDoc.DocumentObjectModel.Tables.Table>().Last();
        var labels = table.Rows.Cast<MigraDoc.DocumentObjectModel.Tables.Row>().Select(r => PdfDom.Text(r.Cells[0]).Trim()).ToList();

        Assert.Equal(["Geprüft von", "Datum", "Unterschrift", "Bemerkungen", "", "", ""], labels);
        Assert.Equal(table.Rows.Count - 1, table.Rows[0].KeepWith);
        Assert.All(table.Rows.Cast<MigraDoc.DocumentObjectModel.Tables.Row>(), r => Assert.True(r.Cells[1].Borders.Bottom.Visible));
    }

    [Fact]
    public void LangerPfad_WirdUmgebrochen_UndRendert()
    {
        var scan = Scan();
        var deep = Root + @"\" + string.Join(@"\", Enumerable.Range(0, 25).Select(i => $"Sehr_langer_Ordnername_ohne_Leerzeichen_{i:D2}"));
        scan.Folders.Add(new FolderNode { Path = deep, Depth = 26, InheritanceBroken = true, ShareUnc = Root, OwnerSid = Max });
        scan.Folders[^1].Aces.Add(Ace(Max, Full));
        scan.Findings.Add(F("ZT03", Severity.Mittel, deep, Max, "Recht direkt vergeben."));

        var data = Data(scan);
        var doc = PdfReport.CreateDocument(data);
        var text = PdfDom.Text(doc);
        // Umgebrochen, aber vollständig: ohne die Zeilenumbrüche steht der Pfad wieder da
        Assert.Contains(deep, text.Replace("\n", ""));
        Assert.DoesNotContain(deep, text);

        var path = Write(data);
        Assert.True(PageCount(path) > 1);
    }

    [Fact]
    public void Begrenzung_KuerztMitHinweis()
    {
        var limits = new PdfLimits(Findings: 2, Folders: 1, People: 1, Changes: 1, Errors: 0);
        var doc = PdfReport.CreateDocument(DataWithComparison(), limits);
        var text = PdfDom.Text(doc);

        Assert.Contains("Gekürzt: 2 von 13 Befunde gezeigt – vollständig im Excel-Bericht.", text);
        Assert.Contains("Gekürzt: 1 von 6 Ordner gezeigt", text);
        Assert.Contains("Gekürzt: 1 von 3 Personen gezeigt", text);
        Assert.Contains("Gekürzt: 1 von 11 Änderungen gezeigt", text);
        Assert.Contains("Dieses PDF zeigt bei langen Listen nur einen Teil – vollständig ist der Excel-Bericht: Befunde (2 von 13)", text);
        // Nur die Hoch-Befunde sind noch drin
        Assert.Contains("Vollzugriff für Nicht-Admins", text);
        Assert.DoesNotContain("„Authentifizierte Benutzer“ dürfen „Archiv“ lesen.", text);
        Assert.True(PageCount(Write(DataWithComparison(), limits: limits)) > 1);
    }

    [Fact]
    public void FreigabeMitVielenEintraegen_WirdMitHinweisGekuerzt_StattStillAbgeschnitten()
    {
        // Eine Tabellenzeile kann nicht über Seiten laufen: vorher erschienen nur 33 von 150 Einträgen, ohne Hinweis
        var scan = Scan();
        var perms = Enumerable.Range(0, 150).Select(i => new ShareAce($"{Domain}-{8000 + i}", ShareAccess.Lesen, i == 0)).ToList();
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Viele", Permissions = perms });
        var data = Data(scan);

        var doc = PdfReport.CreateDocument(data);
        var section = doc.Sections.Cast<Section>().Single();
        var shares = section.Elements.OfType<MigraDoc.DocumentObjectModel.Tables.Table>()
            .Single(t => PdfDom.Text(t).StartsWith("Freigabe\n\tPfad\n\t"));
        var row = shares.Rows.Cast<MigraDoc.DocumentObjectModel.Tables.Row>().Single(r => PdfDom.Text(r.Cells[0]).Contains("Viele"));
        Assert.Equal(21, row.Cells[3].Elements.Count);
        var cell = PdfDom.Text(row.Cells[3]).Replace("\n", ""); // lange SIDs werden umbrochen
        Assert.Contains($"{Domain}-8000: Verweigern Lesen", cell);
        Assert.Contains("… und 130 weitere Einträge (vollständig im Excel-Bericht)", cell);
        Assert.Contains("Freigabe-Berechtigungen (höchstens 20 Einträge je Freigabe, betrifft 1 Freigabe)", PdfDom.Text(doc));

        // Freigaben bis zur Grenze bleiben vollständig
        var exact = PdfReport.CreateDocument(data, PdfLimits.Default with { ShareEntries = 150 });
        Assert.DoesNotContain("weitere Einträge", PdfDom.Text(exact));
        Assert.True(PageCount(Write(data)) > 1);
    }

    [Fact]
    public void Deckblatt_VieleZiele_WerdenGekuerzt()
    {
        var scan = Scan();
        scan.Request = scan.Request with { Targets = Enumerable.Range(0, 150).Select(i => $@"\\FS{i:D3}\Daten").ToList() };
        var doc = PdfReport.CreateDocument(Data(scan));

        var section = doc.Sections.Cast<Section>().Single();
        var cover = section.Elements.OfType<MigraDoc.DocumentObjectModel.Tables.Table>().First(t => PdfDom.Text(t).StartsWith("Ziele\n\t"));
        var targets = PdfDom.Text(cover.Rows[0].Cells[1]);
        Assert.Contains(@"\\FS000\Daten", targets);
        Assert.DoesNotContain(@"\\FS149\Daten", targets);
        Assert.Contains("gekürzt – vollständig im Excel-Bericht", targets);
        Assert.Contains(@"\\FS000\Daten (+149 weitere)", PdfDom.Text(section.Headers.Primary));
    }

    [Fact]
    public void NichtLesbarerOrdner_GelesenEintraegeWerdenTrotzdemGezeigt()
    {
        // Der Scanner setzt Error auch, wenn die Rechte gelesen wurden, aber der Inhalt nicht auflistbar war
        var scan = Scan();
        var qc = scan.Folders.Single(f => f.Path == Qc);
        qc.Error = "Inhalt nicht auflistbar: Zugriff verweigert";

        var text = PdfDom.Text(PdfReport.CreateDocument(Data(scan)));
        var section = text.IndexOf("Ordner mit eigenen Rechten\nAufgeführt", StringComparison.Ordinal);
        var start = text.IndexOf(Qc + "\n", section, StringComparison.Ordinal); // QC ist der letzte Ordner im Baum
        var block = text[start..text.IndexOf("Personen-Übersicht", start, StringComparison.Ordinal)];
        Assert.Contains("Nicht lesbar: Inhalt nicht auflistbar: Zugriff verweigert", block);
        Assert.Contains(@"LAB\QC-Team", block);
        Assert.Contains(@"LAB\max.muster", block);
        Assert.DoesNotContain("Keine Einträge", block);
    }

    [Fact]
    public void Kopfzeile_KuerztLangeZieleOhneHalbesZeichen()
    {
        var scan = Scan();
        scan.Request = scan.Request with { Targets = [new string('a', 68) + "😀😀"] };
        var doc = PdfReport.CreateDocument(Data(scan));
        var header = PdfDom.Text(doc.Sections.Cast<Section>().Single().Headers.Primary).TrimEnd('\n');
        Assert.EndsWith(new string('a', 68) + "…", header);
    }

    [Fact]
    public void LeererLauf_RendertTrotzdem()
    {
        var data = Data(new ScanResult { Request = new ScanRequest { Targets = [@"C:\Leer"] } });
        var text = PdfDom.Text(PdfReport.CreateDocument(data));
        Assert.Contains("Keine Befunde – keine der Prüfregeln hat angeschlagen.", text);
        Assert.Contains("Keine Freigaben im Lauf", text);
        Assert.DoesNotContain("Nicht geprüfte Bereiche", text);
        Assert.True(PageCount(Write(data)) >= 2);
    }

    [Fact]
    public void SonderzeichenUndFremdeSchriften_RendernOhneFehler()
    {
        var scan = Scan();
        scan.Principals[Max].DisplayName = "Mäx 中文 😀 \uD800 \u0001";
        scan.Folders.Add(new FolderNode { Path = Root + @"\Daten_中文_😀\„Zitat“ – € \uD83D", Depth = 1, ShareUnc = Root, OwnerSid = Max });
        scan.Folders[^1].Aces.Add(Ace(Max, Modify));
        Assert.True(PageCount(Write(Data(scan))) > 1);
    }

    [Fact]
    public void Parallel_SchriftenWerdenThreadsicherEingerichtet()
    {
        var data = Data();
        Parallel.For(0, 4, i => PdfReport.Write(data, Path.Combine(_temp.Path, $"parallel-{i}.pdf")));
        Assert.Equal(4, Directory.GetFiles(_temp.Path, "parallel-*.pdf").Length);
        Assert.All(Directory.GetFiles(_temp.Path), p => Assert.True(PageCount(p) > 1));
    }

    [Fact]
    public void GeoeffneteDatei_ReportWriteException_AlteDateiBleibt()
    {
        var path = Path.Combine(_temp.Path, "offen.pdf");
        File.WriteAllText(path, "alt");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var e = Assert.Throws<ReportWriteException>(() => PdfReport.Write(Data(), path));
            Assert.Contains("„" + path + "“", e.Message);
        }
        Assert.Equal("alt", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void GrosserLauf_StandardgrenzenHaltenDasPdfHandlich()
    {
        var scan = Large(folders: 3_000, acesPerFolder: 4, users: 1_500);
        for (int i = 0; i < 3_000; i++)
            scan.Findings.Add(F("ZT03", Severity.Mittel, scan.Folders[i].Path, Max, "Recht direkt vergeben."));
        var data = ReportDataBuilder.Build(scan, null, Rules(), sid => [G(Root, sid, AccessLevel.Lesen, @"LAB\Gruppe-01")], CreatedAt);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var path = Write(data);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(3), $"PDF-Bericht brauchte {sw.Elapsed}");
        Assert.InRange(PageCount(path), 50, 400);
    }
}
