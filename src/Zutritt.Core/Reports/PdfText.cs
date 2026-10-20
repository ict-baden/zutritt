using System.Text;
using MigraDoc.DocumentObjectModel;
using PdfSharp.Drawing;
using Zutritt.Core.Resources;

namespace Zutritt.Core.Reports;

/// <summary>
/// Setzt Text in MigraDoc-Absätze und bricht überlange Wörter – vor allem Pfade ohne Leerzeichen – selbst
/// um. MigraDoc bricht nur an Leerzeichen um und ließe solche Pfade über den Zellenrand hinauslaufen.
/// Getrennt wird bevorzugt nach „\“, „/“, „_“, „-“ und „.“, notfalls mitten im Wort, und nur dort, wo es
/// nötig ist: kurze Pfade bleiben beim Kopieren aus dem PDF unverändert.
/// </summary>
internal sealed class PdfText : IDisposable
{
    /// <summary>Etwas Luft gegenüber der gemessenen Breite (Rundung, Glyphen-Überhang).</summary>
    private const double Safety = 0.96;

    /// <summary>
    /// Mehr Zeilen bekommt eine Zelle nicht. MigraDoc kann Tabellenzeilen nicht über Seiten teilen;
    /// eine Zeile höher als die Seite würde abgeschnitten.
    /// </summary>
    private const int MaxLines = 30;

    private static string Shortened => Strings.Pdf_Truncated;

    private readonly string _family;
    private readonly string? _fallback;
    private readonly XGraphics _gfx;
    private readonly Dictionary<(double Size, bool Bold), XFont> _fonts = [];
    private readonly Dictionary<(double Size, bool Bold, string Glyph), double> _glyphs = [];

    /// <param name="family">Schrift des Berichts.</param>
    /// <param name="fallback">Schrift für chinesische, japanische und koreanische Zeichen, die <paramref name="family"/> nicht hat; null = keine.</param>
    public PdfText(string family, string? fallback = null)
    {
        _family = family;
        _fallback = fallback is not null && !fallback.Equals(family, StringComparison.OrdinalIgnoreCase) ? fallback : null;
        _gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
    }

    /// <summary>
    /// Hängt <paramref name="text"/> an <paramref name="paragraph"/> an; keine Zeile wird breiter als
    /// <paramref name="width"/> Punkt. <paramref name="size"/> und <paramref name="bold"/> müssen der
    /// Schrift des Absatzes entsprechen, sonst stimmt die Messung nicht.
    /// </summary>
    public void Append(Paragraph paragraph, string? text, double width, double size, bool bold = false)
    {
        var segments = Split(Limit(ReportText.Clean(text), width, size), width, size, bold);
        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0) paragraph.AddLineBreak();
            if (segments[i].Length == 0) continue;
            foreach (var (run, cjk) in Runs(segments[i]))
            {
                if (cjk && _fallback is not null)
                {
                    var formatted = paragraph.AddFormattedText(run);
                    formatted.Font.Name = _fallback;
                    if (bold) formatted.Bold = true;
                }
                else if (bold) paragraph.AddFormattedText(run, TextFormat.Bold);
                else paragraph.AddText(run);
            }
        }
    }

    /// <summary>
    /// Kürzt Texte, die umbrochen höher als <see cref="MaxLines"/> Zeilen würden. Zählt auch die
    /// Zeilenumbrüche im Text: viele kurze Zeilen (z. B. 150 Ziele) wären sonst kurz genug an Zeichen,
    /// aber höher als eine Seite – MigraDoc schnitte den Rest der Tabellenzeile stillschweigend ab.
    /// </summary>
    internal static string Limit(string text, double width, double size)
    {
        // Durchschnittliche Zeichenbreite grob ½ Geviert – reicht als Schätzung
        int perLine = Math.Max(10, (int)(width / (0.5 * size)));
        int lines = 0;
        int start = 0;
        while (true)
        {
            int end = text.IndexOf('\n', start);
            int stop = end < 0 ? text.Length : end;
            int needed = Math.Max(1, (stop - start + perLine - 1) / perLine);
            if (lines + needed > MaxLines)
            {
                int cut = Math.Min(stop, start + (MaxLines - lines) * perLine);
                if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
                return text[..cut].TrimEnd('\r', '\n', ' ') + Shortened;
            }
            lines += needed;
            if (end < 0) return text;
            start = end + 1;
        }
    }

    /// <summary>
    /// Zerlegt <paramref name="text"/> in Abschnitte, zwischen denen ein erzwungener Zeilenumbruch nötig
    /// ist. Normale Wörter bleiben zusammen (die bricht MigraDoc selbst an Leerzeichen um).
    /// </summary>
    internal IReadOnlyList<string> Split(string text, double width, double size, bool bold)
    {
        var font = Font(size, bold);
        double available = Math.Max(1, width * Safety);
        var segments = new List<string>();
        var current = new StringBuilder();

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int l = 0; l < lines.Length; l++)
        {
            if (l > 0)
            {
                segments.Add(current.ToString());
                current.Clear();
            }
            foreach (var word in Words(lines[l]))
            {
                var core = word.TrimEnd(' ');
                if (core.Length == 0 || Measure(core, font) <= available)
                {
                    current.Append(word);
                    continue;
                }
                var pieces = Pieces(core, font, available);
                for (int p = 0; p < pieces.Count; p++)
                {
                    current.Append(pieces[p]);
                    if (p < pieces.Count - 1)
                    {
                        segments.Add(current.ToString());
                        current.Clear();
                    }
                }
                current.Append(word, core.Length, word.Length - core.Length);
            }
        }
        segments.Add(current.ToString());
        return segments;
    }

    /// <summary>Wörter samt folgender Leerzeichen.</summary>
    private static IEnumerable<string> Words(string line)
    {
        int start = 0;
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && line[i] != ' ') i++;
            while (i < line.Length && line[i] == ' ') i++;
            yield return line[start..i];
            start = i;
        }
    }

    /// <summary>Ein zu langes Wort in Stücke teilen, die jeweils in die Breite passen.</summary>
    private List<string> Pieces(string word, XFont font, double available)
    {
        var pieces = new List<string>();
        int start = 0;
        int lastBreak = -1;
        double width = 0;
        int i = 0;
        while (i < word.Length)
        {
            int length = char.IsHighSurrogate(word[i]) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]) ? 2 : 1;
            double glyph = Glyph(word.Substring(i, length), font);
            if (width + glyph > available && i > start)
            {
                int cut = lastBreak > start ? lastBreak : i;
                pieces.Add(word[start..cut]);
                start = cut;
                lastBreak = -1;
                width = start < i ? Measure(word[start..i], font) : 0;
                continue; // dasselbe Zeichen gegen die neue Zeile prüfen
            }
            width += glyph;
            i += length;
            if (IsBreakAfter(word[i - 1])) lastBreak = i;
        }
        if (start < word.Length) pieces.Add(word[start..]);
        return pieces;
    }

    private static bool IsBreakAfter(char c) => c is '\\' or '/' or '_' or '-' or '.' or ',' or ';' or ':';

    private XFont Font(double size, bool bold)
    {
        if (!_fonts.TryGetValue((size, bold), out var font))
            _fonts[(size, bold)] = font = new XFont(_family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        return font;
    }

    /// <summary>
    /// Zeilen, die MigraDoc für <paramref name="text"/> in <paramref name="width"/> Punkt braucht – zum Abschätzen
    /// von Tabellenhöhen. Bricht wie MigraDoc an Leerzeichen um; überlange Wörter hat <see cref="Append"/> schon
    /// mit Zeilenumbrüchen versehen.
    /// </summary>
    internal int Lines(string text, double width, double size, bool bold = false)
    {
        var font = Font(size, bold);
        double available = Math.Max(1, width * Safety);
        int lines = 0;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            lines++;
            double x = 0;
            foreach (var word in Words(line))
            {
                if (x > 0 && x + Measure(word.TrimEnd(' '), font) > available)
                {
                    lines++;
                    x = 0;
                }
                x += Measure(word, font);
            }
        }
        return lines;
    }

    /// <summary>Breite in Punkt, so wie MigraDoc sie setzt.</summary>
    internal double Width(string text, double size, bool bold = false) => Measure(text, Font(size, bold));

    private double Measure(string text, XFont font)
    {
        if (_fallback is null || !text.Any(IsCjk)) return _gfx.MeasureString(text, font).Width;
        double width = 0;
        foreach (var (run, cjk) in Runs(text)) width += _gfx.MeasureString(run, cjk ? Fallback(font) : font).Width;
        return width;
    }

    private double Glyph(string glyph, XFont font)
    {
        var key = (font.Size, font.Bold, glyph);
        if (!_glyphs.TryGetValue(key, out var width))
            _glyphs[key] = width = Measure(glyph, font);
        return width;
    }

    private XFont Fallback(XFont font)
    {
        var key = (-font.Size, font.Bold);
        if (!_fonts.TryGetValue(key, out var fallback))
            _fonts[key] = fallback = new XFont(_fallback!, font.Size, font.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        return fallback;
    }

    /// <summary>
    /// Zerlegt Text in Abschnitte mit und ohne chinesische, japanische oder koreanische Zeichen – die bekommen die
    /// Ersatzschrift. Leerzeichen und Satzzeichen bleiben beim umgebenden Abschnitt.
    /// </summary>
    internal static IEnumerable<(string Text, bool Cjk)> Runs(string text)
    {
        int start = 0;
        bool? current = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLowSurrogate(c)) continue;
            bool cjk = IsCjk(c);
            if (!cjk && !char.IsLetterOrDigit(c) && !char.IsHighSurrogate(c)) continue; // neutral
            if (current is null) current = cjk;
            else if (cjk != current)
            {
                yield return (text[start..i], current.Value);
                start = i;
                current = cjk;
            }
        }
        if (start < text.Length) yield return (text[start..], current ?? false);
    }

    /// <summary>Zeichen aus den CJK-Blöcken (Hangul, Kana, Ideogramme, Vollbreite) und die Ergänzungsebenen ab U+20000.</summary>
    internal static bool IsCjk(char c) =>
        c is >= '\u1100' and <= '\u11FF' or >= '\u2E80' and <= '\u9FFF' or >= '\uA960' and <= '\uA97F'
            or >= '\uAC00' and <= '\uD7AF' or >= '\uF900' and <= '\uFAFF' or >= '\uFE30' and <= '\uFE4F' or >= '\uFF00' and <= '\uFFEF'
            or >= '\uD840' and <= '\uD8BF';

    public void Dispose() => _gfx.Dispose();
}
