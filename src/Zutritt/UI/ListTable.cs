using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Zutritt.UI;

/// <summary>
/// Schlanke Tabelle aus ListView und GridView für Listen, die oft neu gefüllt werden (Befunde, „Wer darf hier rein“,
/// Zugriff einer Person). Eine DataGrid-Zeile kostet ein Vielfaches – bei großen Läufen spürbar beim Filtern, Blättern
/// und bei jedem Klick auf einen Ordner. Das Aussehen (Stil „ListTable“) legt ThemeManager an.
/// </summary>
/// <remarks>
/// GridView kennt keine Sternspalten: <see cref="ColumnsProperty"/> („76, 1.35*:110, 1*“ – fest, Stern mit
/// Mindestbreite) verteilt die Breite bei jeder Größenänderung neu. Spalten mit <see cref="SortByProperty"/> lassen sich
/// per Klick auf den Kopf sortieren (Pfeil am Kopf, zweiter Klick dreht um).
/// </remarks>
public static class ListTable
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.RegisterAttached("Columns", typeof(string), typeof(ListTable),
        new PropertyMetadata(null, OnColumnsChanged));

    /// <summary>Eigenschaft der Zeile, nach der ein Klick auf den Spaltenkopf sortiert.</summary>
    public static readonly DependencyProperty SortByProperty = DependencyProperty.RegisterAttached("SortBy", typeof(string), typeof(ListTable),
        new PropertyMetadata(null));

    // Gemerkter Spaltentitel (der Kopf wird beim Sortieren durch Titel + Pfeil ersetzt)
    private static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached("Title", typeof(string), typeof(ListTable),
        new PropertyMetadata(null));

    public static string? GetColumns(DependencyObject o) => (string?)o.GetValue(ColumnsProperty);
    public static void SetColumns(DependencyObject o, string? value) => o.SetValue(ColumnsProperty, value);
    public static string? GetSortBy(DependencyObject o) => (string?)o.GetValue(SortByProperty);
    public static void SetSortBy(DependencyObject o, string? value) => o.SetValue(SortByProperty, value);

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView list) return;
        list.SizeChanged -= OnSizeChanged;
        list.SizeChanged += OnSizeChanged;
        list.Loaded -= OnLoaded;
        list.Loaded += OnLoaded;
        list.RemoveHandler(GridViewColumnHeader.ClickEvent, (RoutedEventHandler)OnHeaderClick);
        list.AddHandler(GridViewColumnHeader.ClickEvent, (RoutedEventHandler)OnHeaderClick);
        Update(list);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Update((ListView)sender);
    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((ListView)sender);

    /// <summary>Spaltenbreiten neu verteilen und den Kopfstil setzen (beides auch nach einem Wechsel Hell/Dunkel).</summary>
    public static void Update(ListView list)
    {
        if (list.View is not GridView view) return;
        if (list.TryFindResource("ListTableHeader") is Style header && !ReferenceEquals(view.ColumnHeaderContainerStyle, header))
            view.ColumnHeaderContainerStyle = header;
        if (list.TryFindResource("ListTableItem") is Style item && !ReferenceEquals(list.ItemContainerStyle, item))
            list.ItemContainerStyle = item;
        if (list.ActualWidth <= 0 || GetColumns(list) is not { } spec) return;

        var specs = spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToList();
        int n = Math.Min(specs.Count, view.Columns.Count);
        // Ohne senkrechte Bildlaufleiste, mit etwas Luft – sonst erschiene eine waagrechte
        double available = list.ActualWidth - list.BorderThickness.Left - list.BorderThickness.Right - SystemParameters.VerticalScrollBarWidth - 8;
        double fixedWidth = specs.Take(n).Where(s => !s.IsStar).Sum(s => s.Value);
        double stars = specs.Take(n).Where(s => s.IsStar).Sum(s => s.Value);
        double rest = Math.Max(0, available - fixedWidth);
        for (int i = 0; i < n; i++)
        {
            var s = specs[i];
            double width = s.IsStar ? Math.Max(s.Min, stars > 0 ? rest * s.Value / stars : 0) : s.Value;
            view.Columns[i].Width = Math.Floor(width);
        }
    }

    private readonly record struct ColumnSpec(double Value, bool IsStar, double Min);

    private static ColumnSpec Parse(string text)
    {
        var parts = text.Split(':');
        double min = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var main = parts[0];
        if (!main.EndsWith('*')) return new ColumnSpec(double.Parse(main, CultureInfo.InvariantCulture), false, 0);
        var factor = main[..^1];
        return new ColumnSpec(factor.Length == 0 ? 1 : double.Parse(factor, CultureInfo.InvariantCulture), true, min);
    }

    // ================= Sortieren =================

    // Gewählte Sortierung je Liste – gilt weiter, wenn die Liste neue Zeilen bekommt (anderer Ordner, andere Person)
    private static readonly DependencyProperty SortProperty = DependencyProperty.RegisterAttached("Sort", typeof(SortState), typeof(ListTable),
        new PropertyMetadata(null));

    private sealed record SortState(GridViewColumn Column, string Property, bool Descending);

    private static readonly DependencyPropertyDescriptor ItemsSourceDescriptor =
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListView));

    private static void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || e.OriginalSource is not GridViewColumnHeader { Column: { } column }) return;
        if (GetSortBy(column) is not { } property) return;
        bool descending = list.GetValue(SortProperty) is SortState old && ReferenceEquals(old.Column, column) && !old.Descending;
        if (list.GetValue(SortProperty) is null) ItemsSourceDescriptor.AddValueChanged(list, OnItemsSourceChanged);
        list.SetValue(SortProperty, new SortState(column, property, descending));
        ApplySort(list);
        e.Handled = true;
    }

    private static void OnItemsSourceChanged(object? sender, EventArgs e)
    {
        if (sender is ListView list) ApplySort(list);
    }

    private static void ApplySort(ListView list)
    {
        if (list.GetValue(SortProperty) is not SortState sort) return;
        ShowSortArrow(list, sort.Column, sort.Descending);
        if (list.ItemsSource is null || CollectionViewSource.GetDefaultView(list.ItemsSource) is not ListCollectionView view) return;
        view.CustomSort = new PropertyOrder(sort.Property, sort.Descending);
    }

    /// <summary>Vergleich über eine Eigenschaft der Zeile, einmal nachgeschlagen – SortDescriptions lösen den Pfad bei jedem Vergleich neu auf.</summary>
    private sealed class PropertyOrder(string property, bool descending) : System.Collections.IComparer
    {
        private System.Reflection.PropertyInfo? _info;

        public int Compare(object? x, object? y)
        {
            if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
            _info ??= x.GetType().GetProperty(property);
            var a = _info?.GetValue(x);
            var b = _info?.GetValue(y);
            int c = a is string sa && b is string sb
                ? StringComparer.CurrentCultureIgnoreCase.Compare(sa, sb)
                : System.Collections.Comparer.Default.Compare(a, b);
            return descending ? -c : c;
        }
    }

    /// <summary>Pfeil am Kopf der sortierten Spalte, alle anderen Köpfe wieder schlicht. <paramref name="column"/> null = keiner.</summary>
    public static void ShowSortArrow(ListView list, GridViewColumn? column, bool descending)
    {
        if (list.View is not GridView view) return;
        foreach (var c in view.Columns)
        {
            var title = c.GetValue(TitleProperty) as string ?? c.Header as string;
            if (title is null) continue;
            c.SetValue(TitleProperty, title);
            if (!ReferenceEquals(c, column))
            {
                c.Header = title;
                continue;
            }
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = title });
            var arrow = new TextBlock { Text = descending ? "\uE70D" : "\uE70E", FontSize = 9, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            arrow.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            header.Children.Add(arrow);
            AutomationProperties.SetName(header, $"{title}, {(descending ? "absteigend" : "aufsteigend")} sortiert");
            c.Header = header;
        }
    }

    /// <summary>Titel einer Spalte, auch während ihr Kopf den Sortierpfeil trägt.</summary>
    public static string TitleOf(GridViewColumn column) => column.GetValue(TitleProperty) as string ?? column.Header as string ?? "";
}
