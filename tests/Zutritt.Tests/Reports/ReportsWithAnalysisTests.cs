// SPDX-License-Identifier: MIT
using ClosedXML.Excel;
using PdfSharp.Pdf.IO;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Reports;

/// <summary>
/// Ende-zu-Ende über die öffentliche API mit der echten Auswertung: SampleScan → FindingRules → Bericht.
/// </summary>
public sealed class ReportsWithAnalysisTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static ScanResult EvaluatedScan(DateTimeOffset? started = null)
    {
        var scan = SampleScan.Create(started);
        scan.Findings.AddRange(FindingRules.Evaluate(scan));
        return scan;
    }

    [Fact]
    public void Builder_NutztRegeltexteUndEffektiveRechte()
    {
        var scan = EvaluatedScan();
        var data = ReportDataBuilder.Build(scan, null);

        Assert.Equal(scan.Findings.Count, data.Findings.Count);
        Assert.Equal(FindingRules.All.Count, data.Rules.Count);
        Assert.All(data.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Title)));
        Assert.All(data.Findings.Where(f => FindingRules.All.Any(r => r.Id == f.RuleId)), f => Assert.NotEqual(f.RuleId, f.Title));

        // Max: Wurzel (über Labor-Alle), QC (direkt, Vollzugriff), Austausch; Archiv ist verweigert
        var max = data.People.Single(p => p.Sid == Max);
        Assert.Equal(3, max.FolderCount);
        Assert.Equal(AccessLevel.Vollzugriff, max.Highest);
        Assert.True(max.HasDirect);
        Assert.DoesNotContain(max.Grants, g => g.Path.EndsWith(@"\Archiv", StringComparison.OrdinalIgnoreCase));
        // Klaus darf Rohdaten lesen (direkt), Personal ist nicht lesbar und taucht bei niemandem auf
        Assert.Contains(data.People.Single(p => p.Sid == Klaus).Grants, g => g.Path.EndsWith(@"\Rohdaten") && g.Via == "direkt");
        Assert.DoesNotContain(data.People.SelectMany(p => p.Grants), g => g.Path.EndsWith(@"\Personal"));
    }

    [Fact]
    public void Builder_Personen_gleich_der_Rechnung_je_Person()
    {
        // Der Builder rechnet alle Personen auf einmal (PeopleAccess); Ergebnis wie EffectiveAccess.ForPerson je Person –
        // auch die Einzelzugriffe, ihre Reihenfolge und welche Personen bei knappem Budget nur Summen bekommen
        var older = ReportSamples.Realistic(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.FromHours(2)), changed: false);
        var newer = ReportSamples.Realistic(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.FromHours(2)), changed: true);
        var comparison = ScanComparer.Compare(older, newer);
        foreach (var (scan, budget) in new[] { (EvaluatedScan(), 200_000), (newer, 200_000), (newer, 500) })
        {
            var ctx = new AccessContext(scan);
            var expected = ReportDataBuilder.Build(scan, comparison, FindingRules.All, sid => EffectiveAccess.ForPerson(ctx, sid),
                ReportSamples.CreatedAt, budget);
            var actual = ReportDataBuilder.Build(scan, comparison, budget);

            Assert.Equal(expected.People.Select(Describe), actual.People.Select(Describe));
            Assert.Equal(expected.PersonGrantsTruncated, actual.PersonGrantsTruncated);
        }
        Assert.True(ReportDataBuilder.Build(newer, null, 500).PersonGrantsTruncated, "Das kleine Budget soll Personen kürzen");

        static string Describe(PersonRow p) =>
            $"{p.Sid}|{p.Name}|{p.FolderCount}|{p.Highest}|{p.HasDirect}|{p.GrantsComplete}|{string.Join(";", p.ViaGroups)}|"
            + string.Join(";", p.Grants.Select(g => $"{g.Path}>{g.Effective}>{g.Ntfs}>{g.ViaShare}>{g.Via}>{g.ReducedByDeny}"));
    }

    [PdfFact]
    public void Excel_UndPdf_UeberDieOeffentlicheApi()
    {
        var older = EvaluatedScan(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(2)));
        var newer = EvaluatedScan();
        // Neuer expliziter Eintrag vor den geerbten (kanonisch, wie Windows ihn einträgt)
        var archiv = newer.Folders.Single(f => f.Path.EndsWith(@"\Archiv"));
        archiv.Aces.Insert(archiv.Aces.FindIndex(a => a.IsInherited), new AceEntry { Sid = Erika, Rights = Full });
        newer.Findings.Clear();
        newer.Findings.AddRange(FindingRules.Evaluate(newer));
        var comparison = ScanComparer.Compare(older, newer);

        var xlsx = Path.Combine(_temp.Path, "bericht.xlsx");
        var pdf = Path.Combine(_temp.Path, "bericht.pdf");
        ExcelReport.Write(newer, comparison, xlsx);
        PdfReport.Write(newer, comparison, pdf);

        using (var wb = new XLWorkbook(xlsx))
        {
            Assert.Contains(wb.Worksheets, w => w.Name == "Änderungen");
            Assert.Equal(newer.Findings.Count + 1, wb.Worksheet("Befunde").LastRowUsed()!.RowNumber());
            var people = wb.Worksheet("Personen");
            var maxRow = people.RowsUsed().Single(r => r.Cell(1).GetString() == @"LAB\max.muster");
            Assert.Equal(3, maxRow.Cell(4).GetValue<int>());
            Assert.Equal("Vollzugriff", maxRow.Cell(5).GetString());
        }
        using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount > 1);
    }

    [PdfFact]
    public void Pdf_WirklichkeitsnaherLaufMitVergleich_SauberUmbrochen()
    {
        // 300 Ordner, 80 Personen, rund 470 Befunde, ~1.000 Änderungen: hier standen Überschriften wie „Ordner mit
        // eigenen Rechten“ und „Personen-Übersicht“ allein am Seitenende
        static ScanResult Evaluated(DateTimeOffset started, bool changed)
        {
            var scan = ReportSamples.Realistic(started, changed);
            scan.Findings.AddRange(FindingRules.Evaluate(scan));
            return scan;
        }
        var older = Evaluated(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.FromHours(2)), changed: false);
        var newer = Evaluated(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.FromHours(2)), changed: true);
        var data = ReportDataBuilder.Build(newer, ScanComparer.Compare(older, newer));
        Assert.True(data.Findings.Count > 300 && data.Comparison!.Rows.Count > 500,
            $"Zu wenig Stoff für den Test: {data.Findings.Count} Befunde, {data.Comparison!.Rows.Count} Änderungen");

        var doc = PdfReport.CreateDocument(data);
        var problems = PdfLayout.Problems(doc);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // Abzeichnen-Block mit Überschrift auf einer Seite, und zwar der letzten
        var pages = PdfLayout.Pages(doc);
        var signOff = pages.FindIndex(p => p.Any(i => i.Object is MigraDoc.DocumentObjectModel.Paragraph h
            && h.Style == MigraDoc.DocumentObjectModel.StyleNames.Heading1 && PdfDom.Text(h).Trim() == "Prüfung und Freigabe"));
        Assert.Equal(pages.Count - 1, signOff);
        Assert.Contains(pages[signOff], i => i.Object is MigraDoc.DocumentObjectModel.Tables.Table t && t.Rows.Count == 7
            && i.StartRow == 0 && i.EndRow == 6);
    }
}
