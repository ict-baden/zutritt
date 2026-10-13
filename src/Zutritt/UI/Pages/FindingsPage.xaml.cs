using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Zutritt.Core.Model;

namespace Zutritt.UI.Pages;

/// <summary>Befunde: nach Schwere gruppiert, filterbar, rechts Erklärung und Empfehlung der Regel.</summary>
public partial class FindingsPage : UserControl, IPage, ISearchPage
{
    /// <summary>Zeile der Befundliste.</summary>
    public sealed class FindingRow(Finding finding, string title, string account)
    {
        public Finding Finding { get; } = finding;
        public Severity Severity => Finding.Severity;
        public string SeverityLabel => Labels.Of(Finding.Severity);
        public string RuleId => Finding.RuleId;
        public string Title { get; } = title;
        public string Path => Finding.Path;
        public string Account { get; } = account;
        public string Message => Finding.Message;
        internal string SearchText { get; } = string.Join('\n', finding.RuleId, title, finding.Path, account, finding.Message);
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
        FindingsGrid.SelectedIndex = _view is null || _view.IsEmpty ? -1 : 0;
        await Ui.IdleAsync();
        if (FindingsGrid.SelectedItem is { } item) FindingsGrid.ScrollIntoView(item);
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
        FindingsGrid.SelectedItem = null;

        if (scan is null)
        {
            _view = null;
            FindingsGrid.ItemsSource = null;
            RuleFilter.ItemsSource = new[] { new RuleChoice(null, "Alle Regeln") };
            RuleFilter.SelectedIndex = 0;
            SetChipCounts(null);
            ShowEmpty("\uE81C", "Kein Lauf geladen", "Starten Sie auf der Übersicht einen Scan oder öffnen Sie einen gespeicherten Lauf.", good: false);
            ShowDetail(null);
            return;
        }

        var rows = scan.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FindingRow(f, _state.Rule(f.RuleId)?.Title ?? f.Message, _state.NameOf(f.Sid)))
            .ToList();

        _view = new ListCollectionView(rows) { Filter = Matches };
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FindingRow.Severity)));
        foreach (var column in FindingsGrid.Columns) column.SortDirection = null;
        FindingsGrid.ItemsSource = _view;

        var rules = scan.Findings.Select(f => f.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Select(id => new RuleChoice(id, _state.Rule(id) is { } r ? $"{id} · {r.Title}" : id));
        RuleFilter.ItemsSource = new[] { new RuleChoice(null, "Alle Regeln") }.Concat(rules).ToList();
        _rule = null;
        RuleFilter.SelectedIndex = 0;

        SetChipCounts(scan);
        UpdateEmptyState();
        ShowDetail(null);
        // Den schwersten Befund gleich zeigen
        if (!_view.IsEmpty) FindingsGrid.SelectedIndex = 0;
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
        var selected = FindingsGrid.SelectedItem;
        _view.Refresh();
        if (selected is not null && _view.Contains(selected) && _view.PassesFilter(selected)) FindingsGrid.SelectedItem = selected;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (_view is null || _shown is null) return;
        if (_shown.Findings.Count == 0)
            ShowEmpty("\uE73E", "Keine Befunde", "In diesem Lauf ist nichts aufgefallen. Die Ordner-Seite zeigt trotzdem, wer wohin darf.", good: true);
        else if (_view.IsEmpty)
            ShowEmpty("\uE721", "Keine Treffer", "Kein Befund passt zu Filter und Suche.", good: false);
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            FindingsGrid.Visibility = Visibility.Visible;
        }
    }

    private void ShowEmpty(string glyph, string title, string text, bool good)
    {
        EmptyGlyph.Text = glyph;
        EmptyGlyph.SetResourceReference(TextBlock.ForegroundProperty, good ? "Good" : "TextFillColorTertiaryBrush");
        EmptyTitle.Text = title;
        EmptyText.Text = text;
        EmptyState.Visibility = Visibility.Visible;
        FindingsGrid.Visibility = Visibility.Collapsed;
    }

    /// <summary>Spaltensortierung innerhalb der Schwere-Gruppen – sonst würden die Gruppen durcheinandergeraten.</summary>
    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (_view is null) return;
        var path = e.Column.SortMemberPath;
        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in FindingsGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();
            if (path == nameof(FindingRow.Severity))
                _view.SortDescriptions.Add(new SortDescription(nameof(FindingRow.Severity), direction));
            else
            {
                _view.SortDescriptions.Add(new SortDescription(nameof(FindingRow.Severity), ListSortDirection.Descending));
                _view.SortDescriptions.Add(new SortDescription(path, direction));
            }
        }
    }

    // ================= Detail =================

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetail(FindingsGrid.SelectedItem as FindingRow);

    private void ShowDetail(FindingRow? row)
    {
        Detail.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        DetailEmpty.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        DetailEmpty.Text = _shown is null || _shown.Findings.Count == 0
            ? "Hier erscheinen Erklärung und Empfehlung zum ausgewählten Befund."
            : "Befund auswählen, um Erklärung und Empfehlung zu sehen.";
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
            DetailAccountKind.Text = p is null ? "" : Labels.Of(p.Kind) + (p.IsDisabled ? " · deaktiviert" : "") + (p.Kind == PrincipalKind.Verwaist ? " · Konto existiert nicht mehr" : "");
        }
        else DetailAccountPanel.Visibility = Visibility.Collapsed;

        DetailExplanation.Text = rule?.Explanation ?? "Für diese Regel liegt keine Beschreibung vor.";
        DetailRecommendation.Text = rule?.Recommendation ?? "–";
        Detail.ScrollToTop();
    }

    private void ShowFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsGrid.SelectedItem is FindingRow row) _state.ShowFolder(row.Path);
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(FindingsGrid, d) is DataGridRow { Item: FindingRow row })
            _state.ShowFolder(row.Path);
    }
}
