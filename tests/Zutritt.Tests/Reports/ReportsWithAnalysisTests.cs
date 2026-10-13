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
/// Läuft erst nach dem Zusammenführen mit dem Baustein „analysis“ (siehe <see cref="AnalysisFactAttribute"/>).
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

    [AnalysisFact]
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

    [AnalysisFact]
    public void Excel_UndPdf_UeberDieOeffentlicheApi()
    {
        var older = EvaluatedScan(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(2)));
        var newer = EvaluatedScan();
        newer.Folders.Single(f => f.Path.EndsWith(@"\Archiv")).Aces.Add(new AceEntry { Sid = Erika, Rights = Full });
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
}
