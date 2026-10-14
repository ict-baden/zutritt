using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Zutritt.Core.Reports;

namespace Zutritt.Tests.Reports;

/// <summary>
/// Prüft den Seitenumbruch eines fertig formatierten MigraDoc-Dokuments: keine Überschrift (oder ein anderer
/// Absatz, der zum Folgenden gehört) allein am Seitenende, keine Tabelle, die mit nur ihrem Kopf oder einer
/// einzelnen Zeile auf einer Seite beginnt, keine letzte Tabellenzeile allein auf der nächsten Seite – und keine Zeile,
/// die MigraDoc beim Umbruch verschluckt hat.
/// </summary>
internal static class PdfLayout
{
    /// <summary>Was auf einer Seite steht: Absätze und Tabellen-Ausschnitte (Zeile von–bis) in Reihenfolge.</summary>
    public sealed record Item(int Page, DocumentObject Object, int StartRow = -1, int EndRow = -1);

    public static List<List<Item>> Pages(Document document)
    {
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        var pages = new List<List<Item>>();
        for (int page = 1; page <= renderer.FormattedDocument!.PageCount; page++)
            pages.Add((renderer.GetRenderInfoFromPage(page) ?? [])
                .Select(info => info.FormatInfo is TableFormatInfo t
                    ? new Item(page, info.DocumentObject, t.StartRow, t.EndRow)
                    : new Item(page, info.DocumentObject))
                .ToList());
        return pages;
    }

    /// <summary>Alle Umbruchfehler als lesbare Sätze (leer = alles gut).</summary>
    public static List<string> Problems(Document document)
    {
        var pages = Pages(document);
        var problems = new List<string>();

        // MigraDoc lässt Zeilen stillschweigend weg, wenn ein KeepWith-Block nicht auf eine Seite passt
        var shownRows = new Dictionary<Table, HashSet<int>>(ReferenceEqualityComparer.Instance);
        foreach (var item in pages.SelectMany(p => p).Where(x => x.Object is Table && x.StartRow >= 0))
        {
            var table = (Table)item.Object;
            if (!shownRows.TryGetValue(table, out var rows)) shownRows[table] = rows = [];
            for (int r = item.StartRow; r <= item.EndRow; r++) rows.Add(r);
        }
        foreach (var (table, rows) in shownRows)
        {
            var missing = Enumerable.Range(0, table.Rows.Count).Where(r => !table.Rows[r].HeadingFormat && !rows.Contains(r)).ToList();
            if (missing.Count > 0)
                problems.Add($"Tabelle „{Shorten(PdfDom.Text(table))}“: {missing.Count} Zeile(n) fehlen im Satz (ab Zeile {missing[0]}).");
        }

        for (int i = 0; i < pages.Count; i++)
        {
            var items = pages[i];
            if (items.Count == 0) continue;
            int page = i + 1;
            var next = i + 1 < pages.Count ? pages[i + 1] : [];

            // Letzter Absatz der Seite gehört zum Folgenden, endet aber hier (geht nicht auf der nächsten Seite weiter)
            if (next.Count > 0 && items[^1].Object is Paragraph last && KeepsWithNext(last) && !ReferenceEquals(next[0].Object, last))
                problems.Add($"Seite {page} endet mit „{Shorten(PdfDom.Text(last))}“ – der Inhalt dazu steht erst auf Seite {page + 1}.");
            foreach (var item in items.Where(x => x.Object is Table))
            {
                var table = (Table)item.Object;
                int headers = table.Rows.Cast<Row>().TakeWhile(r => r.HeadingFormat).Count();
                int dataRows = table.Rows.Count - headers;
                bool starts = item.StartRow <= headers;
                bool ends = item.EndRow >= table.Rows.Count - 1;
                int shown = item.EndRow - Math.Max(item.StartRow, headers) + 1;
                if (dataRows == 0) continue;

                if (starts && !ends && shown < Math.Min(2, dataRows))
                    problems.Add($"Seite {page}: Tabelle „{Shorten(PdfDom.Text(table))}“ beginnt mit nur {shown} Zeile(n) am Seitenende.");
                // Letzte Zeile (z. B. der Nachsatz „Geerbt: …“) allein auf der neuen Seite
                if (!starts && ends && shown == 1 && dataRows >= 3)
                    problems.Add($"Seite {page}: letzte Zeile „{Shorten(PdfDom.Text(table.Rows[^1].Cells[0]))}“ steht allein.");
            }
        }
        return problems;
    }

    /// <summary>Überschriften und Einleitungstexte gehören zum Folgenden – unabhängig davon, ob KeepWithNext gesetzt ist.</summary>
    private static bool KeepsWithNext(Paragraph p) =>
        p.Format.KeepWithNext || p.Style is StyleNames.Heading1 or StyleNames.Heading2 or PdfReport.IntroStyle;

    private static string Shorten(string text)
    {
        text = text.Replace('\n', ' ').Replace('\t', ' ').Trim();
        return text.Length <= 60 ? text : text[..60] + "…";
    }
}
