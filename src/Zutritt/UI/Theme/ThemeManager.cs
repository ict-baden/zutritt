using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Zutritt.Interop;

namespace Zutritt.UI.Theme;

/// <summary>
/// Hell/Dunkel nach Windows, Fluent-Steuerelemente und Fensterrahmen – übernommen von Vitrine,
/// damit beide Programme gleich aussehen.
/// </summary>
public static class ThemeManager
{
    // Fluent-Theme aus WPF (.NET 9+). Anders als Vitrine (eigenes Fluent-Wörterbuch) über Application.ThemeMode:
    // WPF erkennt ein Fluent-Wörterbuch selbst, leitet daraus ThemeMode ab und fügt beim Wechsel zwischen Hell und
    // Dunkel sein altes Wörterbuch wieder ein – der Wechsel bliebe dann wirkungslos. Mica setzt weiterhin
    // ApplyWindowChrome selbst (in Screenshots gibt es keins).
    private const string FluentUri = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{0}.xaml";
    private const string ZutrittUri = "pack://application:,,,/Zutritt;component/UI/Theme/{0}.xaml";

    private static readonly string[] SelectedBackgroundKeys =
        ["DataGridRowSelectedBackgroundThemeBrush", "ListBoxItemSelectedBackgroundThemeBrush", "ListBoxItemBackgroundSelectedPressedThemeBrush"];
    private static readonly string[] SelectedHoverKeys =
        ["ListBoxItemSelectedBackgroundPointerOverThemeBrush", "ListBoxItemSelectedBackgroundPressedThemeBrush"];
    private static readonly string[] SelectedForegroundKeys =
        ["DataGridRowSelectedForegroundThemeBrush", "ListBoxItemSelectedForegroundThemeBrush"];

    private static ResourceDictionary? _fluent;
    private static ResourceDictionary? _own;
    private static bool _following;

    public static bool IsDark { get; private set; } = true;

    /// <summary>Hell/Dunkel festlegen. <paramref name="forceDark"/> = null folgt Windows (auch bei späteren Wechseln).</summary>
    public static void Initialize(bool? forceDark = null)
    {
        IsDark = forceDark ?? !SystemUsesLightTheme();
        ApplyTheme(allowHighContrast: forceDark is null);
        if (forceDark is not null || _following) return;
        _following = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            // General: Hell/Dunkel; Color: Windows-Akzentfarbe geändert
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
            IsDark = !SystemUsesLightTheme();
            Application.Current?.Dispatcher.BeginInvoke(() => ApplyTheme(allowHighContrast: true));
        };
    }

    private static void ApplyTheme(bool allowHighContrast)
    {
        var app = Application.Current;
        var merged = app.Resources.MergedDictionaries;
        if (_own is not null) merged.Remove(_own);

        bool highContrast = allowHighContrast && SystemParameters.HighContrast;
#pragma warning disable WPF0001 // ThemeMode ist in WPF als Vorschau gekennzeichnet
        app.ThemeMode = highContrast ? ThemeMode.System : IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        _fluent = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) == true);
        if (_fluent is null)
        {
            // Rückfall, falls WPF das Wörterbuch nicht selbst einfügt
            _fluent = new ResourceDictionary { Source = new Uri(string.Format(FluentUri, highContrast ? "HC" : IsDark ? "Dark" : "Light")) };
            merged.Insert(0, _fluent);
        }
        _own = new ResourceDictionary { Source = new Uri(string.Format(ZutrittUri, IsDark ? "Dark" : "Light")) };
        merged.Insert(merged.IndexOf(_fluent) + 1, _own);

        BuildDerivedStyles();
        var accent = (_fluent["AccentFillColorDefaultBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x67, 0xC0);
        app.Resources["AccentSubtle"] = Freeze(new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)));

        // Auswahl in Listen und Tabellen dezent getönt statt voll mit Akzent gefüllt – sonst stünde im dunklen
        // Theme heller Text auf hellem Akzent
        var selected = Freeze(new SolidColorBrush(Color.FromArgb(0x3D, accent.R, accent.G, accent.B)));
        var selectedHover = Freeze(new SolidColorBrush(Color.FromArgb(0x52, accent.R, accent.G, accent.B)));
        var text = _fluent["TextFillColorPrimaryBrush"] as Brush ?? Brushes.Black;
        foreach (var key in SelectedBackgroundKeys) app.Resources[key] = selected;
        foreach (var key in SelectedHoverKeys) app.Resources[key] = selectedHover;
        foreach (var key in SelectedForegroundKeys) app.Resources[key] = text;

        foreach (Window w in app.Windows)
            if (w is IThemedWindow t) t.ThemeChanged();
    }

    /// <summary>
    /// Stile, die auf Fluent aufbauen. Im Code statt in XAML, weil BasedOn per StaticResource
    /// über Geschwister-Wörterbücher hinweg nicht zuverlässig auflöst.
    /// </summary>
    private static void BuildDerivedStyles()
    {
        var r = Application.Current.Resources;
        var button = _fluent![typeof(Button)] as Style;
        var accent = _fluent["AccentButtonStyle"] as Style ?? button;
        var textBox = _fluent[typeof(TextBox)] as Style;
        var dataGrid = _fluent[typeof(DataGrid)] as Style;

        r["AccentButton"] = new Style(typeof(Button), accent);

        var ghost = new Style(typeof(Button), button);
        ghost.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        ghost.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        ghost.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        r["GhostButton"] = ghost;

        var multi = new Style(typeof(TextBox), textBox);
        multi.Setters.Add(new Setter(TextBoxBase.AcceptsReturnProperty, true));
        multi.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.NoWrap));
        multi.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 96.0));
        multi.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
        multi.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas")));
        multi.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        multi.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto));
        r["MultiLineBox"] = multi;

        // Schreibgeschützte Tabellen: virtualisiert (auch gruppiert), ohne Gitterlinien und Zeilenköpfe
        var table = new Style(typeof(DataGrid), dataGrid);
        table.Setters.Add(new Setter(DataGrid.AutoGenerateColumnsProperty, false));
        table.Setters.Add(new Setter(DataGrid.IsReadOnlyProperty, true));
        table.Setters.Add(new Setter(DataGrid.CanUserAddRowsProperty, false));
        table.Setters.Add(new Setter(DataGrid.CanUserDeleteRowsProperty, false));
        table.Setters.Add(new Setter(DataGrid.CanUserResizeRowsProperty, false));
        table.Setters.Add(new Setter(DataGrid.CanUserReorderColumnsProperty, false));
        table.Setters.Add(new Setter(DataGrid.SelectionModeProperty, DataGridSelectionMode.Single));
        table.Setters.Add(new Setter(DataGrid.SelectionUnitProperty, DataGridSelectionUnit.FullRow));
        table.Setters.Add(new Setter(DataGrid.HeadersVisibilityProperty, DataGridHeadersVisibility.Column));
        table.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.None));
        table.Setters.Add(new Setter(DataGrid.EnableRowVirtualizationProperty, true));
        table.Setters.Add(new Setter(DataGrid.EnableColumnVirtualizationProperty, true));
        table.Setters.Add(new Setter(VirtualizingPanel.IsVirtualizingProperty, true));
        table.Setters.Add(new Setter(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling));
        table.Setters.Add(new Setter(VirtualizingPanel.IsVirtualizingWhenGroupingProperty, true));
        table.Setters.Add(new Setter(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel));
        // Vorrat außerhalb der Sicht: eine halbe Seite statt einer ganzen davor und danach – jede Zeile einer
        // Tabelle kostet beim Filtern und Blättern ein paar Millisekunden
        table.Setters.Add(new Setter(VirtualizingPanel.CacheLengthProperty, new VirtualizationCacheLength(0.5)));
        table.Setters.Add(new Setter(VirtualizingPanel.CacheLengthUnitProperty, VirtualizationCacheLengthUnit.Page));
        table.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        table.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        table.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        table.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        // Fluent schaltet beim Gruppieren das logische Scrollen ab – und damit die Virtualisierung. Bei Tausenden
        // Befunden wären dann alle Zeilen auf einmal angelegt. Pixelweises Scrollen (oben) geht auch mit Gruppen.
        var grouping = new Trigger { Property = ItemsControl.IsGroupingProperty, Value = true };
        grouping.Setters.Add(new Setter(ScrollViewer.CanContentScrollProperty, true));
        table.Triggers.Add(grouping);

        // Zellen und Spaltenköpfe mit gleichem Abstand links, Zellen auch rechts – Text klebt nie an der Nachbarspalte
        var cell = new Style(typeof(DataGridCell), SetterValue<Style>(dataGrid, DataGrid.CellStyleProperty));
        cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(CellPaddingLeft, 0, CellPaddingRight, 0)));
        if (Application.Current.TryFindResource("TableCellTemplate") is ControlTemplate cellTemplate)
            cell.Setters.Add(new Setter(Control.TemplateProperty, cellTemplate));
        table.Setters.Add(new Setter(DataGrid.CellStyleProperty, cell));
        var row = new Style(typeof(DataGridRow), SetterValue<Style>(dataGrid, DataGrid.RowStyleProperty));
        if (Application.Current.TryFindResource("TableRowTemplate") is ControlTemplate rowTemplate)
            row.Setters.Add(new Setter(Control.TemplateProperty, rowTemplate));
        table.Setters.Add(new Setter(DataGrid.RowStyleProperty, row));
        // Den Kopfstil setzt WPF nicht aus DataGrid.ColumnHeaderStyle, wenn Fluent seinen eigenen mitbringt –
        // dessen Abstand kommt aber aus einer Ressource, die sich hier überschreiben lässt
        r["DataGridColumnHeaderPadding"] = new Thickness(CellPaddingLeft, 0, 4, 0);
        r["Table"] = table;

        // Schlanke Tabelle (ListView + GridView, siehe ListTable): so dicht und im selben Stil wie „Table“
        var listItem = new Style(typeof(ListViewItem), _fluent[GridView.GridViewItemContainerStyleKey] as Style);
        listItem.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32.0));
        listItem.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        var list = new Style(typeof(ListView), _fluent[typeof(ListView)] as Style);
        list.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        list.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        list.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        list.Setters.Add(new Setter(ListBox.SelectionModeProperty, SelectionMode.Single));
        list.Setters.Add(new Setter(Selector.IsSynchronizedWithCurrentItemProperty, false));
        list.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        list.Setters.Add(new Setter(ScrollViewer.CanContentScrollProperty, true));
        list.Setters.Add(new Setter(VirtualizingPanel.IsVirtualizingProperty, true));
        list.Setters.Add(new Setter(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling));
        list.Setters.Add(new Setter(VirtualizingPanel.IsVirtualizingWhenGroupingProperty, true));
        list.Setters.Add(new Setter(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel));
        list.Setters.Add(new Setter(VirtualizingPanel.CacheLengthProperty, new VirtualizationCacheLength(0.5)));
        list.Setters.Add(new Setter(VirtualizingPanel.CacheLengthUnitProperty, VirtualizationCacheLengthUnit.Page));
        // Den Zeilenstil setzt ListTable direkt an der Liste: Fluent wählt ihn für GridView per Trigger, und
        // Trigger schlagen Setter
        r["ListTableItem"] = listItem;
        r["ListTable"] = list;
        var listHeader = new Style(typeof(GridViewColumnHeader), _fluent["DefaultGridViewColumnHeaderStyle"] as Style);
        listHeader.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        listHeader.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        listHeader.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("DividerStrokeColorDefaultBrush")));
        // Der Füllkopf rechts neben der letzten Spalte bekommt bei Fluent per Trigger oben und unten einen Rand –
        // der stünde als Strich über der Bildlaufleiste. Wie die übrigen Köpfe nur unten.
        var padding = new Trigger { Property = GridViewColumnHeader.RoleProperty, Value = GridViewColumnHeaderRole.Padding };
        padding.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        listHeader.Triggers.Add(padding);
        r["ListTableHeader"] = listHeader;
    }

    private const double CellPaddingLeft = 8;
    private const double CellPaddingRight = 10;

    /// <summary>Wert eines Setters im Stil oder seinen Basisstilen (Fluent legt Zell- und Kopfstil so ab).</summary>
    private static T? SetterValue<T>(Style? style, DependencyProperty property) where T : class
    {
        for (var s = style; s is not null; s = s.BasedOn)
            foreach (var setter in s.Setters.OfType<Setter>())
                if (setter.Property == property) return setter.Value as T;
        return null;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int i && i == 1;
        }
        catch { return false; }
    }

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Dunkle Titelleiste und – unter Windows 11 – Mica-Hintergrund.</summary>
    public static void ApplyWindowChrome(Window w, bool mica = true)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == 0) return;

        int dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (mica && IsWindows11)
        {
            int backdrop = 2; // DWMSBT_MAINWINDOW (Mica)
            if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0)
            {
                w.Background = Brushes.Transparent;
                if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } ct })
                    ct.BackgroundColor = Colors.Transparent;
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
                return;
            }
        }
        w.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
    }
}

/// <summary>Fenster, die bei einem Wechsel zwischen Hell und Dunkel ihren Rahmen neu setzen.</summary>
public interface IThemedWindow
{
    void ThemeChanged();
}
