using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
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

    [PdfFact]
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

    [PdfFact]
    public void Dokument_AbschnitteInReihenfolge_OhneVergleichKeineAenderungen()
    {
        var doc = PdfReport.CreateDocument(Data());
        Assert.Equal(["Zusammenfassung", "Befunde", "Freigaben", "Ordner mit eigenen Rechten", "Personen-Übersicht",
            "Nicht geprüfte Bereiche", "Prüfung und Freigabe"], PdfDom.Headings(doc));
    }

    [PdfFact]
    public void Dokument_MitVergleich_AenderungenVorDemAbzeichnen()
    {
        var doc = PdfReport.CreateDocument(DataWithComparison());
        var headings = PdfDom.Headings(doc);
        Assert.Equal("Änderungen seit dem letzten Lauf", headings[^3]);
        var text = PdfDom.Text(doc);
        Assert.Contains("Mehr Zugriff", text);
        Assert.Contains(@"LAB\ehemalig", text);
        Assert.Contains("Vergleich mit dem letzten Lauf", text);
    }

    [PdfFact]
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

    [PdfFact]
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

    [PdfFact]
    public void Abzeichnen_BlockMitAllenFeldern_ZusammenAufEinerSeite()
    {
        var doc = PdfReport.CreateDocument(Data());
        var section = doc.Sections.Cast<Section>().Single();
        var table = section.Elements.OfType<Table>().Last();
        var labels = table.Rows.Cast<Row>().Select(r => PdfDom.Text(r.Cells[0]).Trim()).ToList();

        Assert.Equal(["Geprüft von", "Datum", "Unterschrift", "Bemerkungen", "", "", ""], labels);
        Assert.Equal(table.Rows.Count - 1, table.Rows[0].KeepWith);
        Assert.All(table.Rows.Cast<Row>(), r => Assert.True(r.Cells[1].Borders.Bottom.Visible));
    }

    [PdfFact]
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

    [PdfFact]
    public void Begrenzung_KuerztMitHinweis()
    {
        var limits = new PdfLimits(Findings: 2, Folders: 1, People: 1, Changes: 1, Errors: 0);
        var doc = PdfReport.CreateDocument(DataWithComparison(), limits);
        var text = PdfDom.Text(doc);

        Assert.Contains("Gekürzt: 2 von 13 Befunden gezeigt – vollständig im Excel-Bericht.", text);
        Assert.Contains("Gekürzt: 1 von 6 Ordnern gezeigt", text);
        Assert.Contains("Gekürzt: 1 von 3 Personen gezeigt", text);
        // Änderungen: Grenze je Bereich, damit kein Bereich ganz verdrängt wird; Zugriffe je Ordner zusammengefasst
        Assert.Contains("Gekürzt: 1 von 3 Änderungen gezeigt", text); // Berechtigungseinträge
        Assert.Contains("Gekürzt: 1 von 2 Ordnern gezeigt", text);    // Zugriffe von Personen (QC, Archiv)
        Assert.Contains("Änderungen (5 von 11)", text);
        Assert.Contains("Dieses PDF zeigt bei langen Listen nur einen Teil – vollständig ist der Excel-Bericht: Befunde (2 von 13)", text);
        // Nur die Hoch-Befunde sind noch drin
        Assert.Contains("Vollzugriff für Nicht-Admins", text);
        Assert.DoesNotContain("„Authentifizierte Benutzer“ dürfen „Archiv“ lesen.", text);
        Assert.True(PageCount(Write(DataWithComparison(), limits: limits)) > 1);
    }

    [PdfFact]
    public void FreigabeMitVielenEintraegen_WirdMitHinweisGekuerzt_StattStillAbgeschnitten()
    {
        // Eine Tabellenzeile kann nicht über Seiten laufen: vorher erschienen nur 33 von 150 Einträgen, ohne Hinweis
        var scan = Scan();
        var perms = Enumerable.Range(0, 150).Select(i => new ShareAce($"{Domain}-{8000 + i}", ShareAccess.Lesen, i == 0)).ToList();
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Viele", Permissions = perms });
        var data = Data(scan);

        var doc = PdfReport.CreateDocument(data);
        var section = doc.Sections.Cast<Section>().Single();
        var shares = section.Elements.OfType<Table>()
            .Single(t => PdfDom.Text(t).StartsWith("Freigabe\n\tPfad\n\t"));
        var row = shares.Rows.Cast<Row>().Single(r => PdfDom.Text(r.Cells[0]).Contains("Viele"));
        Assert.Equal(21, row.Cells[3].Elements.Count);
        var cell = PdfDom.Text(row.Cells[3]).Replace("\n", ""); // lange SIDs werden umbrochen
        // Verweigern „ab Lesen“ sperrt alles – so steht es da, nicht als „Verweigern Lesen“
        Assert.Contains($"{Domain}-8000: Verweigert: alles (ab Lesen)", cell);
        Assert.DoesNotContain("Verweigern Lesen", cell);
        Assert.Contains("… und 130 weitere Einträge (vollständig im Excel-Bericht)", cell);
        Assert.Contains("Freigabe-Berechtigungen (höchstens 20 Einträge je Freigabe, betrifft 1 Freigabe)", PdfDom.Text(doc));

        // Freigaben bis zur Grenze bleiben vollständig
        var exact = PdfReport.CreateDocument(data, PdfLimits.Default with { ShareEntries = 150 });
        Assert.DoesNotContain("weitere Einträge", PdfDom.Text(exact));
        Assert.True(PageCount(Write(data)) > 1);
    }

    [PdfFact]
    public void Deckblatt_VieleZiele_WerdenGekuerzt()
    {
        var scan = Scan();
        scan.Request = scan.Request with { Targets = Enumerable.Range(0, 150).Select(i => $@"\\FS{i:D3}\Daten").ToList() };
        var doc = PdfReport.CreateDocument(Data(scan));

        var section = doc.Sections.Cast<Section>().Single();
        var cover = section.Elements.OfType<Table>().First(t => PdfDom.Text(t).StartsWith("Ziele (150)\n\t"));
        var targets = PdfDom.Text(cover.Rows[0].Cells[1]);
        Assert.Contains(@"\\FS004\Daten", targets);
        Assert.DoesNotContain(@"\\FS005\Daten", targets);
        Assert.Contains("… und 145 weitere (gekürzt – vollständig im Excel-Bericht)", targets);
        Assert.Contains(@"\\FS000\Daten (+149 weitere)", PdfDom.Text(section.Headers.Primary));

        // Das Deckblatt bleibt eine Seite – auch mit Vergleich und Inhaltsverzeichnis
        var pages = PdfLayout.Pages(PdfReport.CreateDocument(Data(scan, Comparison())));
        Assert.DoesNotContain(pages[1], i => i.Object is Paragraph p && PdfDom.Text(p).Contains("Vertraulich – enthält"));
        Assert.Contains(pages[1], i => i.Object is Paragraph p && p.Style == StyleNames.Heading1 && PdfDom.Text(p).Trim() == "Zusammenfassung");
    }

    [PdfFact]
    public void Deckblatt_InhaltsverzeichnisVerweistAufJedenAbschnitt()
    {
        var doc = PdfReport.CreateDocument(DataWithComparison());
        var section = doc.Sections.Cast<Section>().Single();
        var paragraphs = section.Elements.OfType<Paragraph>().ToList();

        // Einträge mit Seitenverweis; jeder Verweis zeigt auf genau eine Überschrift mit diesem Namen
        var entries = paragraphs
            .Select(p => (Text: PdfDom.Text(p).Split('\t')[0], Ref: p.Elements.OfType<MigraDoc.DocumentObjectModel.Fields.PageRefField>().SingleOrDefault()))
            .Where(e => e.Ref is not null)
            .ToList();
        Assert.Equal(["Zusammenfassung", "Befunde", "Prüfregeln und Empfehlungen", "Freigaben", "Ordner mit eigenen Rechten",
            "Personen-Übersicht", "Änderungen seit dem letzten Lauf", "Nicht geprüfte Bereiche", "Prüfung und Freigabe"], entries.Select(e => e.Text));
        foreach (var (title, pageRef) in entries)
        {
            var target = Assert.Single(paragraphs, p => p.Elements.OfType<MigraDoc.DocumentObjectModel.Fields.BookmarkField>().Any(b => b.Name == pageRef!.Name));
            Assert.Equal(title, PdfDom.Text(target).Trim());
        }

        // Ohne Vergleich und ohne Fehler fehlen die Einträge dazu
        var plain = PdfDom.Text(PdfReport.CreateDocument(Data(new ScanResult { Request = new ScanRequest { Targets = [@"C:\Leer"] } })));
        Assert.DoesNotContain("Änderungen seit dem letzten Lauf", plain);
        Assert.DoesNotContain("Nicht geprüfte Bereiche", plain);
    }

    [PdfFact]
    public void Seitenumbruch_KeineUeberschriftAllein_KeineVerschlucktenZeilen()
    {
        // Verschieden lange Befundlisten schieben die folgenden Abschnitte über alle Stellen der Seite – so landet
        // jede Überschrift irgendwann am Seitenende. Früher stand sie dort allein, ihr Inhalt erst auf der nächsten Seite.
        var problems = new List<string>();
        for (int k = 0; k < 40; k++)
        {
            var scan = Scan();
            for (int i = 0; i < k; i++)
                scan.Findings.Add(F("ZT03", Severity.Mittel, Root + $@"\Projekt{i:D2}", Max, "Recht direkt vergeben."));
            problems.AddRange(PdfLayout.Problems(PdfReport.CreateDocument(Data(scan))).Select(p => $"{k} Befunde mehr: {p}"));
            problems.AddRange(PdfLayout.Problems(PdfReport.CreateDocument(Data(scan, Comparison()))).Select(p => $"{k} Befunde mehr, mit Vergleich: {p}"));
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [PdfFact]
    public void Seitenumbruch_GrosserLauf_OrdnerkopfBleibtBeiSeinerTabelle()
    {
        var scan = Large(folders: 120, acesPerFolder: 6, users: 40);
        for (int i = 0; i < 120; i += 3)
            scan.Findings.Add(F(i % 2 == 0 ? "ZT01" : "ZT05", i % 2 == 0 ? Severity.Hoch : Severity.Mittel, scan.Folders[i].Path, Everyone, "Breite Gruppe."));
        var data = ReportDataBuilder.Build(scan, null, Rules(), sid => [G(Root, sid, AccessLevel.Lesen, @"LAB\Gruppe-01")], CreatedAt);
        var doc = PdfReport.CreateDocument(data);

        var problems = PdfLayout.Problems(doc);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // Tabellenköpfe wiederholen sich auf Folgeseiten
        var tables = doc.Sections.Cast<Section>().Single().Elements.OfType<Table>()
            .Where(t => t.Rows.Count > 1 && t.Columns.Count > 2 && !t.Rows[0].Shading.Color.IsEmpty).ToList();
        Assert.NotEmpty(tables);
        Assert.All(tables, t => Assert.True(t.Rows[0].HeadingFormat, PdfDom.Text(t.Rows[0].Cells[0])));
    }

    [PdfFact]
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

    [PdfFact]
    public void Kopfzeile_KuerztLangeZieleOhneHalbesZeichen()
    {
        var scan = Scan();
        scan.Request = scan.Request with { Targets = [new string('a', 68) + "😀😀"] };
        var doc = PdfReport.CreateDocument(Data(scan));
        var header = PdfDom.Text(doc.Sections.Cast<Section>().Single().Headers.Primary).TrimEnd('\n');
        Assert.EndsWith(new string('a', 68) + "…", header);
    }

    [PdfFact]
    public void LeererLauf_RendertTrotzdem()
    {
        var data = Data(new ScanResult { Request = new ScanRequest { Targets = [@"C:\Leer"] } });
        var text = PdfDom.Text(PdfReport.CreateDocument(data));
        Assert.Contains("Keine Befunde – keine der Prüfregeln hat angeschlagen.", text);
        Assert.Contains("Keine Freigaben im Lauf", text);
        Assert.DoesNotContain("Nicht geprüfte Bereiche", text);
        Assert.True(PageCount(Write(data)) >= 2);
    }

    [PdfFact]
    public void SonderzeichenUndFremdeSchriften_RendernOhneFehler()
    {
        var scan = Scan();
        scan.Principals[Max].DisplayName = "Mäx 中文 😀 \uD800 \u0001";
        scan.Folders.Add(new FolderNode { Path = Root + @"\Daten_中文_😀\„Zitat“ – € \uD83D", Depth = 1, ShareUnc = Root, OwnerSid = Max });
        scan.Folders[^1].Aces.Add(Ace(Max, Modify));
        Assert.True(PageCount(Write(Data(scan))) > 1);
    }

    [PdfFact]
    public void Parallel_SchriftenWerdenThreadsicherEingerichtet()
    {
        var data = Data();
        Parallel.For(0, 4, i => PdfReport.Write(data, Path.Combine(_temp.Path, $"parallel-{i}.pdf")));
        Assert.Equal(4, Directory.GetFiles(_temp.Path, "parallel-*.pdf").Length);
        Assert.All(Directory.GetFiles(_temp.Path), p => Assert.True(PageCount(p) > 1));
    }

    [PdfFact]
    public void Parallel_GleicherSatzWieNacheinander()
    {
        // MigraDoc merkt sich die zuletzt benutzte Schrift statisch und ohne Sperre: ohne RenderGate setzt ein
        // paralleler Bericht mit fremder Schrift – andere Umbrüche, im Extremfall falsche Schriftgröße
        var scan = Large(folders: 120, acesPerFolder: 6, users: 40);
        for (int i = 0; i < 120; i += 3)
            scan.Findings.Add(F(i % 2 == 0 ? "ZT01" : "ZT05", i % 2 == 0 ? Severity.Hoch : Severity.Mittel, scan.Folders[i].Path, Everyone, "Breite Gruppe."));
        var data = ReportDataBuilder.Build(scan, null, Rules(), sid => [G(Root, sid, AccessLevel.Lesen, @"LAB\Gruppe-01")], CreatedAt);

        var reference = PageContents(Write(data, "nacheinander.pdf"));
        Parallel.For(0, 6, i => Write(data, $"parallel-{i}.pdf"));
        for (int i = 0; i < 6; i++)
            Assert.Equal(reference, PageContents(Path.Combine(_temp.Path, $"parallel-{i}.pdf")));
    }

    /// <summary>Inhalt jeder Seite (Zeichenbefehle mit Positionen) als Prüfsumme – gleich gesetzt heißt gleich.</summary>
    private static List<string> PageContents(string path)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        return pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .Select(p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Contents.CreateSingleContent().Stream!.UnfilteredValue)))
            .ToList();
    }

    [WindowsFact("Dateisperren gelten nur unter Windows verbindlich")]
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

    [PdfFact]
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

    [PdfFact]
    public void Ordner_MitAbweichenderVererbung_SoGekennzeichnet()
    {
        var scan = Scan();
        scan.Folders.Single(f => f.Path == Archiv).InheritanceDiffers = true;
        var text = PdfDom.Text(PdfReport.CreateDocument(Data(scan)));
        Assert.Contains("Tiefe 1 · Vererbung weicht ab (vermutlich verschoben) · Besitzer:", text);
        Assert.Contains("unterbrochene oder abweichende Vererbung", text);
    }

    [PdfLayoutFact]
    public void Ordner_VerweigernNenntGesperrteRechte_OhneUmbruchImWort()
    {
        // „Nur Löschen verweigert“ hieß als Stufe „Kein Zugriff“ – jetzt steht da, was gesperrt ist, und passt in die Spalte
        var scan = Scan();
        scan.Folders.Single(f => f.Path == Archiv).Aces.Insert(0, new AceEntry
        {
            Sid = Erika, Rights = FileRights.Delete | FileRights.DeleteSubdirectoriesAndFiles,
            IsDeny = true, Inheritance = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit,
        });
        var doc = PdfReport.CreateDocument(Data(scan));
        var rows = doc.Sections.Cast<Section>().Single().Elements.OfType<Table>()
            .Where(t => t.Columns.Count == 5 && t.Rows.Count > 0 && PdfDom.Text(t.Rows[0].Cells[2]).Trim() == "Recht")
            .SelectMany(t => t.Rows.Cast<Row>().Skip(1))
            .Where(r => PdfDom.Text(r.Cells[3]).Trim() == "Verweigern")
            .Select(r => PdfDom.Text(r.Cells[2]).TrimEnd('\n'))
            .ToList();
        Assert.Contains("Löschen, Unterordner und Dateien löschen", rows);
        Assert.DoesNotContain("Kein Zugriff", rows);
        Assert.All(rows, r => Assert.DoesNotContain('\n', r));
    }

    [PdfLayoutFact]
    public void Aenderungen_SpalteAenderung_OhneUmbruchImWort()
    {
        // „Aufgenommen“ (jetzt groß geschrieben) passte nicht mehr in die Spalte und wurde mitten im Wort umbrochen
        var doc = PdfReport.CreateDocument(DataWithComparison());
        var section = doc.Sections.Cast<Section>().Single();
        var tables = section.Elements.OfType<Table>()
            .Where(t => PdfDom.Text(t.Rows[0].Cells[0]).Trim() == "Änderung").ToList();
        Assert.NotEmpty(tables);
        var labels = tables.SelectMany(t => t.Rows.Cast<Row>().Skip(1))
            .Select(r => PdfDom.Text(r.Cells[0]).TrimEnd('\n')).ToList();
        Assert.Contains("Aufgenommen", labels);
        Assert.All(labels, l => Assert.DoesNotContain('\n', l));
        Assert.All(labels, l => Assert.True(char.IsUpper(l[0]), l));
    }
}
