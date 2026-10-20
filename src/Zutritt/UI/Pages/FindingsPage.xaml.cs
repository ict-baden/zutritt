using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.UI.Pages;

/// <summary>Befunde: nach Schwere gruppiert, filterbar, rechts Erklärung und Empfehlung der Regel.</summary>
public partial class FindingsPage : UserControl, IPage, ISearchPage
{
    /// <summary>Zeile der Befundliste.</summary>
    public sealed class FindingRow(Finding finding, string title, string account, string accountName)
    {
        public Finding Finding { get; } = finding;
        public Severity Severity => Finding.Severity;
        public string SeverityLabel => Labels.Of(Finding.Severity);
        public string RuleId => Finding.RuleId;
        public string Title { get; } = title;
        public string Path => Finding.Path;
        public string Account { get; } = account;
        /// <summary>Kurzform für die enge Spalte (Anzeigename oder Name ohne Domäne), voller Name im Tooltip.</summary>
        public string AccountName { get; } = accountName;
        public string Message => Finding.Message;
        internal string SearchText { get; } = string.Join('\n', finding.RuleId, title, finding.Path, account, accountName, finding.Message);
        public override string ToString() => $"{SeverityLabel}, {RuleId}, {Title}, {Path}";
    }

    public sealed record RuleChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppState _state;
    private readonly Debouncer _search = new(TimeSpan.FromMilliseconds(250));
    private ScanResult? _shown;
    private bool _loaded;
    private ListCollectionView? _view;
    private GridViewColumn? _sortColumn;
    private bool _descending;
    private string _query = "";
    private string? _rule;
    private readonly HashSet<Severity> _severities = [Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info];

    public FindingsPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        ShowDetail(null);
    }

    public void OnShown()
    {
        if (!_loaded || !ReferenceEquals(_shown, _state.Scan)) Load();
    }

    public async Task PrepareScreenshotAsync()
    {
        OnShown();
        foreach (var chip in new[] { ChipHigh, ChipMedium, ChipLow, ChipInfo }) chip.IsChecked = true;
        _severities.UnionWith([Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info]);
        SearchBox.Text = "";
        _query = "";
        RuleFilter.SelectedIndex = 0;
        Refresh();
        FindingsList.SelectedIndex = _view is null || _view.IsEmpty ? -1 : 0;
        await Ui.IdleAsync();
        if (FindingsList.SelectedItem is { } item) FindingsList.ScrollIntoView(item);
        await Ui.IdleAsync();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ================= Laden =================

    private void Load()
    {
        _loaded = true;
        _shown = _state.Scan;
        var scan = _shown;
        FindingsList.SelectedItem = null;

        if (scan is null)
        {
            _view = null;
            FindingsList.ItemsSource = null;
            RuleFilter.ItemsSource = new[] { new RuleChoice(null, Strings.Fi_AllRules) };
            RuleFilter.SelectedIndex = 0;
            SetChipCounts(null);
            ShowEmpty("\uE81C", Strings.Ui_NoRunLoaded, Strings.Fi_NoRunText, good: false);
            ShowDetail(null);
            return;
        }

        var rows = scan.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FindingRow(f, _state.Rule(f.RuleId)?.Title ?? f.Message, _state.NameOf(f.Sid), _state.ShortNameOf(f.Sid)))
            .ToList();

        _view = new ListCollectionView(rows) { Filter = Matches };
        _view.GroupDescriptions.Add(new SeverityGroups());
        _sortColumn = null;
        ListTable.ShowSortArrow(FindingsList, null, false);
        FindingsList.ItemsSource = _view;

        var rules = scan.Findings.Select(f => f.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Select(id => new RuleChoice(id, _state.Rule(id) is { } r ? $"{id} · {r.Title}" : id));
        RuleFilter.ItemsSource = new[] { new RuleChoice(null, Strings.Fi_AllRules) }.Concat(rules).ToList();
        _rule = null;
        RuleFilter.SelectedIndex = 0;

        SetChipCounts(scan);
        UpdateEmptyState();
        ShowDetail(null);
        // Den schwersten Befund gleich zeigen
        if (!_view.IsEmpty) FindingsList.SelectedIndex = 0;
    }

    private void SetChipCounts(ScanResult? scan)
    {
        string Text(Severity s) => scan is null ? Labels.Of(s) : $"{Labels.Of(s)}  {Labels.Number(scan.Findings.Count(f => f.Severity == s))}";
        ChipHighText.Text = Text(Severity.Hoch);
        ChipMediumText.Text = Text(Severity.Mittel);
        ChipLowText.Text = Text(Severity.Niedrig);
        ChipInfoText.Text = Text(Severity.Info);
    }

    // ================= Filter =================

    private bool Matches(object item)
    {
        var row = (FindingRow)item;
        if (!_severities.Contains(row.Severity)) return false;
        if (_rule is not null && !row.RuleId.Equals(_rule, StringComparison.OrdinalIgnoreCase)) return false;
        return _query.Length == 0 || row.SearchText.Contains(_query, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (chip, severity) in new[] { (ChipHigh, Severity.Hoch), (ChipMedium, Severity.Mittel), (ChipLow, Severity.Niedrig), (ChipInfo, Severity.Info) })
        {
            if (chip.IsChecked == true) _severities.Add(severity);
            else _severities.Remove(severity);
        }
        Refresh();
    }

    private void RuleFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var id = (RuleFilter.SelectedItem as RuleChoice)?.Id;
        if (id == _rule) return;
        _rule = id;
        Refresh();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _search.Run(() =>
    {
        _query = SearchBox.Text.Trim();
        Refresh();
    });

    private void Refresh()
    {
        if (_view is null) return;
        var selected = FindingsList.SelectedItem;
        _view.Refresh();
        if (selected is not null && _view.Contains(selected) && _view.PassesFilter(selected)) FindingsList.SelectedItem = selected;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (_view is null || _shown is null) return;
        if (_shown.Findings.Count == 0)
            ShowEmpty("\uE73E", Strings.Ui_NoFindings, Strings.Fi_NothingFound, good: true);
        else if (_view.IsEmpty)
            ShowEmpty("\uE721", Strings.Fi_NoMatches, Strings.Fi_NoMatchesText, good: false);
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            FindingsList.Visibility = Visibility.Visible;
        }
    }

    private void ShowEmpty(string glyph, string title, string text, bool good)
    {
        EmptyGlyph.Text = glyph;
        EmptyGlyph.SetResourceReference(TextBlock.ForegroundProperty, good ? "Good" : "TextFillColorTertiaryBrush");
        EmptyTitle.Text = title;
        EmptyText.Text = text;
        EmptyState.Visibility = Visibility.Visible;
        FindingsList.Visibility = Visibility.Collapsed;
    }

    // ================= Spalten =================

    private static Dictionary<string, Func<FindingRow, string>> SortKeys => new()
    {
        [Strings.Col_Rule] = r => r.RuleId,
        [Strings.Col_Title] = r => r.Title,
        [Strings.Col_Path] = r => r.Path,
        [Strings.Col_Account] = r => r.AccountName,
    };

    /// <summary>Klick auf einen Spaltenkopf sortiert innerhalb der Schwere-Gruppen (sonst gerieten die Gruppen durcheinander).</summary>
    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null || e.OriginalSource is not GridViewColumnHeader { Column: { } column }) return;
        if (!SortKeys.TryGetValue(ListTable.TitleOf(column), out var key)) return;
        _descending = ReferenceEquals(_sortColumn, column) && !_descending;
        _sortColumn = column;
        _view.CustomSort = new RowOrder(key, _descending);
        ListTable.ShowSortArrow(FindingsList, _sortColumn, _descending);
    }

    /// <summary>
    /// Gruppen nach Schwere, direkt gelesen: PropertyGroupDescription löst den Namen je Zeile über Reflexion auf –
    /// bei 10.000 Befunden kostete jedes Filtern so über eine halbe Sekunde.
    /// </summary>
    private sealed class SeverityGroups : System.ComponentModel.GroupDescription
    {
        public override object GroupNameFromItem(object item, int level, System.Globalization.CultureInfo culture) => ((FindingRow)item).Severity;
    }

    /// <summary>Schwere zuerst (wie die Gruppen), darin nach der gewählten Spalte – direkt statt über Reflexion wie SortDescriptions.</summary>
    private sealed class RowOrder(Func<FindingRow, string> key, bool descending) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (FindingRow)x!;
            var b = (FindingRow)y!;
            int c = b.Severity.CompareTo(a.Severity);
            if (c != 0) return c;
            c = StringComparer.CurrentCultureIgnoreCase.Compare(key(a), key(b));
            if (c != 0) return descending ? -c : c;
            c = string.CompareOrdinal(a.RuleId, b.RuleId);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
        }
    }

    // ================= Detail =================

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetail(FindingsList.SelectedItem as FindingRow);

    private void ShowDetail(FindingRow? row)
    {
        Detail.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        DetailEmpty.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        DetailEmpty.Text = _shown is null || _shown.Findings.Count == 0
            ? Strings.Fi_DetailPlaceholder
            : Strings.Fi_SelectFinding;
        if (row is null) return;

        var rule = _state.Rule(row.RuleId);
        DetailSeverity.Severity = row.Severity;
        DetailRule.Text = row.RuleId;
        DetailTitle.Text = rule?.Title ?? row.RuleId;
        DetailMessage.Text = row.Message;
        DetailPath.Text = row.Path;

        if (row.Finding.Sid is { } sid)
        {
            var p = _state.PrincipalOf(sid);
            DetailAccountPanel.Visibility = Visibility.Visible;
            DetailAccount.Text = p is not null && p.DisplayName.Length > 0 && !p.Label.EndsWith(p.DisplayName, StringComparison.OrdinalIgnoreCase)
                ? $"{p.Label} ({p.DisplayName})"
                : row.Account;
            DetailAccountKind.Text = p is null ? "" : Labels.Of(p.Kind) + (p.IsDisabled ? Strings.Fi_Disabled : "") + (p.Kind == PrincipalKind.Verwaist ? Strings.Fi_AccountGone : "");
        }
        else DetailAccountPanel.Visibility = Visibility.Collapsed;

        DetailExplanation.Text = rule?.Explanation ?? Strings.Fi_NoDescription;
        DetailRecommendation.Text = rule?.Recommendation ?? "–";
        Detail.ScrollToTop();
    }

    private void ShowFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsList.SelectedItem is FindingRow row) _state.ShowFolder(row.Path);
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(FindingsList, d) is ListViewItem { Content: FindingRow row })
            _state.ShowFolder(row.Path);
    }
}
