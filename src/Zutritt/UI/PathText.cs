using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace Zutritt.UI;

/// <summary>
/// Einzeiliger Pfad, der bei Platzmangel in der Mitte gekürzt wird: Server und Freigabe vorn, der Ordner selbst
/// hinten bleiben lesbar („\\FS01\Labor\…\Rohdaten“). Passt nicht einmal das, wird am Anfang gekürzt („…daten“).
/// WPF kennt nur Kürzen am Ende – das zeigt bei Pfaden genau den unwichtigen Teil.
/// </summary>
/// <remarks>
/// Zeichnet selbst statt eine TextBlock-Eigenschaft im Layout zu setzen: so entsteht keine Layout-Schleife, und in
/// virtualisierten Tabellen kostet eine Zeile nur ein paar Breitenmessungen. Schrift und Farbe erbt es wie ein
/// TextBlock (Zellen, ausgewählte Zeilen, Dunkelmodus).
/// </remarks>
public sealed class PathText : FrameworkElement
{
    private const string Ellipsis = "…";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(PathText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextChanged));

    // Schrift und Farbe wie TextBlock: geerbt, Standardwerte vom Basiseigentümer (TextElement). Ein eigener Standard
    // (etwa SystemFonts.MessageFontSize) wiche vom Fluent-Standard ab – Werte, die niemand setzt, erbt WPF nicht.
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(PathText), FontMetadata());
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(PathText), FontMetadata());
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(PathText), FontMetadata());
    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(PathText), FontMetadata());
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(PathText),
        new FrameworkPropertyMetadata { Inherits = true, AffectsRender = true });

    private static FrameworkPropertyMetadata FontMetadata() =>
        new(OnFontChanged) { Inherits = true, AffectsMeasure = true, AffectsRender = true };

    // Zuletzt gezeichnete Darstellung – neu nur, wenn sich Breite, Text oder Schrift ändern
    private string? _fittedFor;
    private double _fittedWidth = -1;
    private string _fitted = "";
    private FormattedText? _shown;
    // Je Schrift einmal ermittelt: in Tabellen entstehen beim Blättern viele Zellen in kurzer Zeit
    private Typeface? _typeface;
    private GlyphTypeface? _glyphs;
    private double _pixelsPerDip;

    public PathText()
    {
        // Tooltip mit dem ganzen Pfad (setzt OnTextChanged), so verzögert wie bei den übrigen Tabellenzellen
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 700);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontStyle FontStyle { get => (FontStyle)GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Der angezeigte (eventuell gekürzte) Text – für Tests und Screenreader.</summary>
    public string DisplayedText => _fitted;

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (PathText)d;
        p._fittedFor = null;
        p.ToolTip = string.IsNullOrEmpty(e.NewValue as string) ? null : e.NewValue;
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (PathText)d;
        p._fittedFor = null;
        p._typeface = null;
        p._glyphs = null;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _pixelsPerDip = newDpi.PixelsPerDip;
        _fittedFor = null;
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Geschätzt aus den Zeichenbreiten der Schrift – genau gemessen wird erst beim Zeichnen
        double width = Math.Min(Math.Ceiling(MeasureWidth(Text ?? "")), availableSize.Width);
        return new Size(double.IsInfinity(width) ? 0 : width, Math.Ceiling(FontFamily.LineSpacing * FontSize));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var formatted = Fit(Text ?? "", ActualWidth);
        if (formatted is null) return;
        formatted.SetForegroundBrush(Foreground);
        double y = Math.Max(0, (ActualHeight - formatted.Height) / 2);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.DrawText(formatted, new Point(0, y));
        dc.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PathTextPeer(this);

    private Typeface CurrentTypeface => _typeface ??= new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);

    private FormattedText Format(string text)
    {
        if (_pixelsPerDip <= 0) _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, CurrentTypeface, FontSize, Foreground, _pixelsPerDip);
    }

    /// <summary>
    /// Breite eines Texts aus den Vorschubbreiten der Schrift – viel billiger als ein FormattedText je Pfadteil.
    /// Zeichen, die die Schrift nicht hat (Ersatzschrift), werden richtig gemessen.
    /// </summary>
    private double MeasureWidth(string text)
    {
        if (text.Length == 0) return 0;
        if (_glyphs is null && !CurrentTypeface.TryGetGlyphTypeface(out _glyphs)) return Format(text).WidthIncludingTrailingWhitespace;
        double em = 0;
        foreach (char c in text)
        {
            if (!_glyphs!.CharacterToGlyphMap.TryGetValue(c, out var glyph)) return Format(text).WidthIncludingTrailingWhitespace;
            em += _glyphs.AdvanceWidths[glyph];
        }
        return em * FontSize;
    }

    /// <summary>Was bei dieser Breite gezeichnet wird; geschätzt gekürzt, dann genau nachgemessen.</summary>
    private FormattedText? Fit(string text, double max)
    {
        if (string.Equals(text, _fittedFor, StringComparison.Ordinal) && Math.Abs(max - _fittedWidth) < 0.01) return _shown;
        _fittedFor = text;
        _fittedWidth = max;
        _shown = null;
        _fitted = "";
        if (text.Length == 0 || max <= 0) return null;

        var full = Format(text);
        if (full.WidthIncludingTrailingWhitespace <= max + 0.5)
        {
            _fitted = text;
            return _shown = full;
        }
        // Mit etwas Reserve schätzen (Kerning, variable Schrift) – die genaue Messung danach passt dann fast immer
        var shortened = Shorten(text, max * 0.98 - 1, MeasureWidth);
        var formatted = Format(shortened);
        if (formatted.WidthIncludingTrailingWhitespace > max + 0.5)
        {
            // Die Schätzung lag knapp daneben: genau messend vom Anfang her kürzen
            shortened = StartEllipsis(text, max, t => Format(t).WidthIncludingTrailingWhitespace);
            formatted = Format(shortened);
        }
        _fitted = shortened;
        return _shown = shortened.Length == 0 ? null : formatted;
    }

    /// <summary>
    /// Kürzt einen Pfad auf <paramref name="max"/> Breite. Teilt am Backslash und behält vorn möglichst Server und
    /// Freigabe, hinten möglichst viele Ebenen; dazwischen „…“. Gemessen wird jede Ebene nur einmal.
    /// </summary>
    internal static string Shorten(string text, double max, Func<string, double> measure)
    {
        if (string.IsNullOrEmpty(text) || max <= 0) return "";
        if (measure(text) <= max) return text;

        // „\\server“ bzw. „D:“ als erster Teil, der Rest nach Backslash getrennt
        string lead = text.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\" : "";
        var parts = text[lead.Length..].Split('\\');
        int n = parts.Length;
        if (n >= 3)
        {
            var widths = parts.Select(measure).ToArray();
            double sep = measure(@"\"), gap = measure(@"\" + Ellipsis + @"\"), leadWidth = measure(lead);
            int bestHead = -1, bestTail = 0;
            for (int head = 1; head <= n - 2; head++)
            {
                double headWidth = leadWidth + Sum(widths, 0, head) + sep * (head - 1);
                if (headWidth + gap > max) break;
                // Längster Schwanz, der neben diesen Kopf passt
                int tail = 0;
                double tailWidth = 0;
                for (int t = 1; t <= n - head - 1; t++)
                {
                    double w = tailWidth + widths[n - t] + (t > 1 ? sep : 0);
                    if (headWidth + gap + w > max) break;
                    tailWidth = w;
                    tail = t;
                }
                if (tail == 0) continue;
                // Mehr Ebenen gewinnen; bei Gleichstand Server und Freigabe (zwei Teile vorn) bevorzugen
                if (head + tail > bestHead + bestTail || (head + tail == bestHead + bestTail && Math.Abs(head - 2) < Math.Abs(bestHead - 2)))
                {
                    bestHead = head;
                    bestTail = tail;
                }
            }
            if (bestHead > 0)
            {
                var candidate = lead + string.Join('\\', parts, 0, bestHead) + @"\" + Ellipsis + @"\" + string.Join('\\', parts, n - bestTail, bestTail);
                if (measure(candidate) <= max) return candidate;
            }
        }

        // Kein Kopf passt: nur das Ende zeigen, möglichst ab einem ganzen Ordnernamen
        if (n >= 2)
        {
            for (int tail = n - 1; tail >= 1; tail--)
            {
                var candidate = Ellipsis + @"\" + string.Join('\\', parts, n - tail, tail);
                if (measure(candidate) <= max) return candidate;
            }
        }
        return StartEllipsis(text, max, measure);
    }

    /// <summary>„…“ plus so viel vom Ende, wie passt (Binärsuche über die Länge).</summary>
    private static string StartEllipsis(string text, double max, Func<string, double> measure)
    {
        int lo = 0, hi = text.Length; // Anzahl Zeichen vom Ende
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (measure(Ellipsis + text[^mid..]) <= max) lo = mid;
            else hi = mid - 1;
        }
        if (lo == 0) return measure(Ellipsis) <= max ? Ellipsis : "";
        // Kein halbes Ersatzzeichenpaar am Anfang stehen lassen
        int start = text.Length - lo;
        if (char.IsLowSurrogate(text[start]) && lo > 1) start++;
        return Ellipsis + text[start..];
    }

    private static double Sum(double[] values, int start, int count)
    {
        double sum = 0;
        for (int i = start; i < start + count; i++) sum += values[i];
        return sum;
    }

    /// <summary>Screenreader lesen den ganzen Pfad, nicht die gekürzte Fassung.</summary>
    private sealed class PathTextPeer(PathText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(PathText);
        protected override string GetNameCore() => ((PathText)Owner).Text;
        protected override bool IsControlElementCore() => true;
    }
}
