// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Zutritt.Core.Model;

namespace Zutritt.UI;

/// <summary>Eine Einstellungszeile im Windows-11-Stil: Symbol, Titel, Beschreibung, Steuerelement rechts (wie Vitrine).</summary>
public class SettingCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(null));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata(null));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
}

/// <summary>Zahl auf der Navigation (z. B. Anzahl Befunde). Leer = kein Abzeichen.</summary>
public static class Nav
{
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(string), typeof(Nav), new PropertyMetadata(null));
    /// <summary>Farbe des Abzeichens: "Hoch", "Mittel" oder leer (neutral).</summary>
    public static readonly DependencyProperty BadgeToneProperty =
        DependencyProperty.RegisterAttached("BadgeTone", typeof(string), typeof(Nav), new PropertyMetadata(""));

    public static string? GetBadge(DependencyObject o) => (string?)o.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject o, string? value) => o.SetValue(BadgeProperty, value);
    public static string GetBadgeTone(DependencyObject o) => (string)o.GetValue(BadgeToneProperty);
    public static void SetBadgeTone(DependencyObject o, string value) => o.SetValue(BadgeToneProperty, value);
}

/// <summary>
/// Farbiges Schwere-Abzeichen („● Hoch“). Die Farben setzen Trigger im Stil per DynamicResource,
/// damit sie beim Wechsel zwischen Hell und Dunkel mitwechseln (ein Konverter könnte das nicht).
/// </summary>
public sealed class SeverityPill : Control
{
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(Severity), typeof(SeverityPill),
            new PropertyMetadata(Severity.Info, (d, e) => d.SetValue(LabelPropertyKey, Labels.Of((Severity)e.NewValue))));
    private static readonly DependencyPropertyKey LabelPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(SeverityPill), new PropertyMetadata(Labels.Of(Severity.Info)));
    public static readonly DependencyProperty LabelProperty = LabelPropertyKey.DependencyProperty;
    /// <summary>Nur der Punkt, ohne Text (für enge Spalten).</summary>
    public static readonly DependencyProperty CompactProperty =
        DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(SeverityPill), new PropertyMetadata(false));

    static SeverityPill() => FocusableProperty.OverrideMetadata(typeof(SeverityPill), new FrameworkPropertyMetadata(false));

    public Severity Severity { get => (Severity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public string Label => (string)GetValue(LabelProperty);
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
}

/// <summary>Kennzahl-Kachel: große Zahl, Beschriftung, optional Symbol und Farbton.</summary>
public sealed class StatTile : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata("–"));
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatTile), new PropertyMetadata(null));
    /// <summary>"Hoch", "Mittel", "Niedrig", "Info", "Gut" oder leer (Akzent).</summary>
    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    static StatTile() => FocusableProperty.OverrideMetadata(typeof(StatTile), new FrameworkPropertyMetadata(false));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}

/// <summary>
/// Leerer Text → sichtbar (für Platzhalter über Eingabefeldern), sonst eingeklappt; mit ConverterParameter
/// "invert" umgekehrt (Hinweis-Symbol nur, wenn es einen Hinweis gibt).
/// </summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true → sichtbar, false → eingeklappt; mit ConverterParameter "invert" umgekehrt.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
