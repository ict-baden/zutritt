// SPDX-License-Identifier: MIT
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace Zutritt.Tests.Reports;

/// <summary>
/// Liest den Text aus einem MigraDoc-Dokument (vor dem Rendern). Das fertige PDF enthält Schrift-Teilmengen
/// mit Glyphen-IDs – dort lässt sich Text nicht einfach suchen.
/// </summary>
internal static class PdfDom
{
    public static string Text(DocumentObject obj)
    {
        var sb = new StringBuilder();
        Walk(obj, sb);
        return sb.ToString();
    }

    /// <summary>Text der Überschriften erster Ebene in Reihenfolge.</summary>
    public static List<string> Headings(Document document) =>
        document.Sections.Cast<Section>()
            .SelectMany(s => s.Elements.OfType<Paragraph>())
            .Where(p => p.Style == StyleNames.Heading1)
            .Select(p => Text(p).Trim())
            .ToList();

    private static void Walk(DocumentObject? obj, StringBuilder sb)
    {
        switch (obj)
        {
            case null:
                return;
            case Document d:
                foreach (var s in d.Sections.Cast<Section>()) Walk(s, sb);
                break;
            case Section s:
                Walk(s.Headers.Primary, sb);
                foreach (var e in s.Elements.Cast<DocumentObject>()) Walk(e, sb);
                Walk(s.Footers.Primary, sb);
                break;
            case HeaderFooter hf:
                foreach (var e in hf.Elements.Cast<DocumentObject>()) Walk(e, sb);
                break;
            case Paragraph p:
                foreach (var e in p.Elements.Cast<DocumentObject>()) Walk(e, sb);
                sb.Append('\n');
                break;
            case FormattedText f:
                foreach (var e in f.Elements.Cast<DocumentObject>()) Walk(e, sb);
                break;
            case Hyperlink h:
                foreach (var e in h.Elements.Cast<DocumentObject>()) Walk(e, sb);
                break;
            case Text t:
                sb.Append(t.Content);
                break;
            case Character c:
                sb.Append(c.SymbolName switch
                {
                    SymbolName.LineBreak => "\n",
                    SymbolName.Tab => "\t",
                    SymbolName.Blank => new string(' ', Math.Max(1, c.Count)),
                    _ => c.Char == '\0' ? "" : c.Char.ToString(),
                });
                break;
            case Cell cell:
                foreach (var e in cell.Elements.Cast<DocumentObject>()) Walk(e, sb);
                break;
            case Table t:
                foreach (var row in t.Rows.Cast<Row>())
                {
                    foreach (var cell in row.Cells.Cast<Cell>())
                    {
                        foreach (var e in cell.Elements.Cast<DocumentObject>()) Walk(e, sb);
                        sb.Append('\t');
                    }
                    sb.Append('\n');
                }
                break;
        }
    }
}
