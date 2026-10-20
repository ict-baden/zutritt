// SPDX-License-Identifier: MIT
using MigraDoc.DocumentObjectModel;
using Zutritt.Core.Reports;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Reports;

public sealed class PdfTextTests : IDisposable
{
    private readonly PdfText _text = new(PdfFonts.Family);

    public void Dispose() => _text.Dispose();

    [PdfFact]
    public void KurzerText_BleibtUnveraendert()
    {
        var parts = _text.Split(@"\\FS01\Labor\QC und noch ein paar Wörter", 300, 8.5, bold: false);
        Assert.Equal([@"\\FS01\Labor\QC und noch ein paar Wörter"], parts);
    }

    [PdfFact]
    public void LangerPfad_PasstInDieBreite_UndBleibtVollstaendig()
    {
        var path = @"\\FS01\Labor\" + string.Join(@"\", Enumerable.Range(0, 30).Select(i => $"Unterordner_{i:D2}"));
        const double width = 140;
        var parts = _text.Split(path, width, 8.5, bold: false);

        Assert.True(parts.Count > 3);
        Assert.Equal(path, string.Concat(parts));
        Assert.All(parts, p => Assert.True(_text.Width(p, 8.5) <= width, $"„{p}“ ist zu breit"));
        // Bevorzugt nach dem Trenner umgebrochen
        Assert.All(parts.SkipLast(1), p => Assert.Matches(@"[\\_]$", p));
    }

    [PdfFact]
    public void WortOhneTrenner_WirdNotfallsImWortGeteilt()
    {
        var word = new string('W', 200);
        var parts = _text.Split(word, 60, 9.5, bold: true);
        Assert.True(parts.Count > 1);
        Assert.Equal(word, string.Concat(parts));
        Assert.All(parts, p => Assert.True(_text.Width(p, 9.5, bold: true) <= 60));
    }

    [PdfFact]
    public void Surrogatpaare_WerdenNichtZerteilt()
    {
        var word = string.Concat(Enumerable.Repeat("😀", 80));
        var parts = _text.Split(word, 50, 8.5, bold: false);
        Assert.Equal(word, string.Concat(parts));
        Assert.All(parts, p => Assert.False(char.IsHighSurrogate(p[^1])));
    }

    [PdfFact]
    public void Zeilenumbruch_ImText_WirdZumAbschnitt()
    {
        var parts = _text.Split("Erste Zeile\nZweite Zeile", 300, 8.5, bold: false);
        Assert.Equal(["Erste Zeile", "Zweite Zeile"], parts);
    }

    [PdfFact]
    public void Append_SetztZeilenumbruecheNurBeiBedarf_UndKuerztRiesigeTexte()
    {
        var paragraph = new Paragraph();
        _text.Append(paragraph, "kurz", 200, 8.5);
        Assert.DoesNotContain(paragraph.Elements.OfType<Character>(), c => c.SymbolName == SymbolName.LineBreak);

        var longPath = new Paragraph();
        _text.Append(longPath, @"C:\" + new string('x', 400), 100, 8.5);
        Assert.Contains(longPath.Elements.OfType<Character>(), c => c.SymbolName == SymbolName.LineBreak);

        var huge = new Paragraph();
        _text.Append(huge, string.Join(" ", Enumerable.Repeat("Wort", 20_000)), 100, 8.5);
        var content = PdfDom.Text(huge);
        Assert.Contains("gekürzt – vollständig im Excel-Bericht", content);
        Assert.True(content.Length < 2_000);
    }

    [PdfFact]
    public void VieleKurzeZeilen_WerdenGekuerzt_DamitDieZelleAufEineSeitePasst()
    {
        // 150 kurze Zeilen sind wenige Zeichen, aber höher als eine Seite – MigraDoc schnitte sie still ab
        var lines = string.Join("\n", Enumerable.Range(0, 150).Select(i => $@"\\FS{i:D3}\Daten"));
        var paragraph = new Paragraph();
        _text.Append(paragraph, lines, 360, 9.5);

        var content = PdfDom.Text(paragraph);
        Assert.InRange(content.Count(c => c == '\n'), 25, 31);
        Assert.Contains(@"\\FS000\Daten", content);
        Assert.DoesNotContain(@"\\FS149\Daten", content);
        Assert.Contains("gekürzt – vollständig im Excel-Bericht", content);
    }

    [PdfFact]
    public void Begrenzung_LaesstPassendeTexteUnveraendert()
    {
        var thirty = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"Zeile {i}"));
        Assert.Same(thirty, PdfText.Limit(thirty, 300, 8.5));
        Assert.Equal("", PdfText.Limit("", 300, 8.5));
        Assert.EndsWith("(gekürzt – vollständig im Excel-Bericht)", PdfText.Limit(thirty + "\nZeile 30", 300, 8.5));
        // Kein halbes Zeichen am Schnitt („a“ vorne, damit der Schnitt mitten in ein Paar fiele)
        var emoji = "a" + string.Concat(Enumerable.Repeat("😀", 2_000));
        var cut = PdfText.Limit(emoji, 100, 8.5);
        Assert.False(char.IsHighSurrogate(cut[cut.IndexOf(' ') - 1]));
    }
}
