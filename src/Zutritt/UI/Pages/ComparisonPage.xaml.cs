// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.UI.Pages;

/// <summary>Vergleich mit einem älteren Lauf: neue und behobene Befunde, Zugriffs-, ACL- und Mitgliedschaftsänderungen.</summary>
public partial class ComparisonPage : UserControl, IPage
{
    public sealed record OlderChoice(long Id, string Label, bool SameTargets)
    {
        public override string ToString() => Label;
    }
    public sealed record FindingRow(Severity Severity, string RuleId, string Path, string Account, string Message)
    {
        public override string ToString() => $"{Labels.Of(Severity)}, {RuleId}, {Path}, {Message}";
    }
    public sealed record AccessRow(string Person, string Account, string Path, string Before, AccessLevel BeforeLevel, string After, AccessLevel AfterLevel, bool IsMore)
    {
        public int Direction => IsMore ? 1 : 0;
        public override string ToString() => Texts.Format(Strings.Cmp_AccessRowText, Person, Path, Before, After);
    }
    public sealed record AceRow(string Change, string Path, string Account, string Before, string After)
    {
        public override string ToString() => $"{Change}, {Path}, {Account}";
    }
    public sealed record MemberRow(string Change, string Group, string Member)
    {
        public override string ToString() => Texts.Format(Strings.Cmp_MemberRowText, Change, Member, Group);
    }

    private readonly AppState _state;
    private ScanResult? _shownScan;
    private bool _updating;
    private int _choicesVersion;
    private List<OlderChoice> _choices = [];
    private List<FindingRow> _newFindings = [];
    private List<FindingRow> _resolvedFindings = [];
    // Aufbereitete Zeilen je Vergleich: bei großen Läufen Hunderttausende Zugriffsänderungen – einmal im
    // Hintergrund sortiert und benannt, nicht bei jedem Seitenwechsel im UI-Thread
    private ComparisonResult? _rowsFor;
    private ComparisonRows? _rows;
    private ComparisonResult? _buildingFor;
    private Task<ComparisonRows>? _building;

    private sealed record ComparisonRows(List<FindingRow> New, List<FindingRow> Resolved, List<AccessRow> Access, List<AceRow> Aces, List<MemberRow> Members);
    private Task _choicesTask = Task.CompletedTask;

    public ComparisonPage(AppState state)
    {
        InitializeComponent();
        _state = state;
    }

    /// <summary>Liest die gespeicherten Läufe jedes Mal neu – inzwischen kann einer gelöscht oder dazugekommen sein.</summary>
    public void OnShown() => LoadChoices();

    public async Task PrepareScreenshotAsync()
    {
        OnShown();
        await _choicesTask;
        TileNew.IsChecked = true;
        await Ui.IdleAsync();
    }

    // ================= Auswahl des älteren Laufs =================

    private void LoadChoices()
    {
        int version = ++_choicesVersion;
        if (!ReferenceEquals(_shownScan, _state.Scan)) _choices = [];
        _shownScan = _state.Scan;
        if (_shownScan is not { } scan)
        {
            _choices = [];
            _choicesTask = ShowComparisonAsync(version);
            return;
        }
        var task = LoadChoicesAsync(scan, version);
        _choicesTask = task;
        Ui.Run(_state, () => task, Strings.Ov_RunsUnreadable);
    }

    private async Task LoadChoicesAsync(ScanResult scan, int version)
    {
        var runs = await _state.ListRunsAsync();
        if (version != _choicesVersion || !ReferenceEquals(scan, _shownScan)) return;
        _choices = runs
            .Where(r => r.Id != scan.Id && r.StartedAt < scan.StartedAt)
            .OrderByDescending(r => r.StartedAt)
            .Select(r =>
            {
                var same = ScanScope.SameTargets(scan.Request.Targets, r.Targets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                var findings = Texts.Format(Texts.Plural(r.Findings, Strings.Pdf_FindingCountOne, Strings.Pdf_FindingCountMany), Labels.Number(r.Findings));
                return new OlderChoice(r.Id, $"{Labels.Date(r.StartedAt)} · {r.Targets} · {findings}", same);
            })
            .ToList();
        await ShowComparisonAsync(version);
    }

    private void OlderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || OlderBox.SelectedItem is not OlderChoice choice) return;
        if (_state.Comparison is { } c && c.Older.Id == choice.Id) return;
        Ui.Run(_state, () => _state.CompareWithAsync(choice.Id), Strings.Cmp_Failed);
    }

    // ================= Anzeige =================

    private async Task ShowComparisonAsync(int version)
    {
        var scan = _state.Scan;
        var c = _state.Comparison;

        if (scan is null)
        {
            ShowNothing(Strings.Ui_NoRunLoaded, Strings.Cmp_NoRunText);
            return;
        }
        if (c is null && _choices.Count == 0)
        {
            ShowNothing(Strings.Cmp_NoEarlierRun,
                _state.Store is null
                    ? Strings.Cmp_NoDatabase
                    : Strings.Cmp_NeedTwoRuns);
            return;
        }

        NoComparison.Visibility = Visibility.Collapsed;
        Main.Visibility = Visibility.Visible;

        _updating = true;
        try
        {
            OlderBox.ItemsSource = _choices;
            OlderBox.SelectedItem = c is null ? null : _choices.FirstOrDefault(x => x.Id == c.Older.Id);
        }
        finally
        {
            _updating = false;
        }

        if (c is null)
        {
            CompareInfo.Text = "";
            TargetsWarning.Visibility = Visibility.Collapsed;
            foreach (var count in new[] { CountNew, CountResolved, CountAccess, CountAces, CountMembers }) count.Text = "–";
            ApplyRows(null);
            ShowList(null, Strings.Cmp_NoSameTargets);
            return;
        }

        var days = (c.Newer.StartedAt - c.Older.StartedAt).TotalDays;
        CompareInfo.Text = days >= 1.5 ? Texts.Format(Strings.Cmp_DaysBefore, Math.Round(days)) : days >= 0.5 ? Strings.Cmp_OneDayBefore : Strings.Cmp_SameDay;
        // Ziele wie beim automatischen Vergleich normalisiert („D:“ = „D:\“, „/“ = „\“, Groß/Klein egal)
        var warnings = new List<string>();
        if (!ScanScope.SameTargets(c.Older.Request.Targets, c.Newer.Request.Targets))
            warnings.Add(Texts.Format(Strings.Cmp_OtherTargets, Labels.Targets(c.Older.Request.Targets)));
        if (c.ScopeDiffers) warnings.Add(ScanScope.Describe(c));
        TargetsWarning.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TargetsWarningText.Text = string.Join(Environment.NewLine, warnings);

        if (!ReferenceEquals(_rowsFor, c))
        {
            foreach (var count in new[] { CountNew, CountResolved, CountAccess, CountAces, CountMembers }) count.Text = "…";
            ApplyRows(null);
            ShowList(null, Strings.Cmp_Preparing);
            // Läuft schon eine Aufbereitung für diesen Vergleich (Seite erneut gezeigt), auf sie warten
            if (!ReferenceEquals(_buildingFor, c) || _building is null)
            {
                _buildingFor = c;
                _building = Task.Run(() => BuildRows(c));
            }
            var rows = await _building;
            if (version != _choicesVersion || !ReferenceEquals(c, _state.Comparison)) return;
            _rows = rows;
            _rowsFor = c;
        }
        ApplyRows(_rows);
        // Erste Kachel mit Inhalt vorwählen (oder die bisherige Auswahl behalten)
        var tiles = new[] { TileNew, TileResolved, TileAccess, TileAces, TileMembers };
        var selected = tiles.FirstOrDefault(t => t.IsChecked == true);
        if (selected is null)
        {
            var counts = new[] { CountNew, CountResolved, CountAccess, CountAces, CountMembers };
            int first = Array.FindIndex(counts, t => t.Text != "0");
            tiles[first < 0 ? 0 : first].IsChecked = true;
        }
        else Tile_Checked(selected, new RoutedEventArgs());
    }

    private void ShowNothing(string title, string text)
    {
        Main.Visibility = Visibility.Collapsed;
        NoComparison.Visibility = Visibility.Visible;
        NoComparisonTitle.Text = title;
        NoComparisonText.Text = text;
    }

    /// <summary>Zeilen für alle Listen eines Vergleichs – rein rechnend, läuft im Hintergrund.</summary>
    private static ComparisonRows BuildRows(ComparisonResult c)
    {
        string Name(string? sid) => sid is null ? "" : AppState.NameIn(c, sid);

        var newFindings = c.NewFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FindingRow(f.Severity, f.RuleId, f.Path, Name(f.Sid), f.Message)).ToList();
        var resolvedFindings = c.ResolvedFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FindingRow(f.Severity, f.RuleId, f.Path, Name(f.Sid), f.Message)).ToList();
        // Namen je SID nur einmal nachschlagen und sortieren – es können Hunderttausende Zeilen sein
        var people = c.AccessChanges.Select(a => a.Sid).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(sid => sid, sid => (Name: Name(sid), Person: PersonName(c, sid)), StringComparer.OrdinalIgnoreCase);
        var access = c.AccessChanges.OrderBy(a => people[a.Sid].Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccessRow(people[a.Sid].Person, people[a.Sid].Name, a.Path, Labels.Of(a.Before), a.Before, Labels.Of(a.After), a.After, a.After > a.Before)).ToList();
        var aces = c.AceChanges.OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AceRow(Labels.Of(a.Kind), a.Path, Name(a.Sid), AceText(a.Before), AceText(a.After)))
            .Concat(c.FoldersAdded.Select(p => new AceRow(Strings.Cmp_OwnRightsNew, p, "", "", "")))
            .Concat(c.FoldersRemoved.Select(p => new AceRow(Strings.Cmp_OwnRightsGone, p, "", "", "")))
            .ToList();
        var members = c.MembershipChanges.OrderBy(m => Name(m.GroupSid), StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new MemberRow(m.Kind == ChangeKind.Hinzugefuegt ? Strings.Cmp_Joined : m.Kind == ChangeKind.Entfernt ? Strings.Change_Removed : Strings.Change_Changed,
                Name(m.GroupSid), Name(m.MemberSid))).ToList();
        return new ComparisonRows(newFindings, resolvedFindings, access, aces, members);
    }

    private void ApplyRows(ComparisonRows? rows)
    {
        // Gleiche Listen nicht neu zuweisen – sonst legt die Tabelle bei jedem Seitenwechsel alle Zeilen neu an
        _newFindings = rows?.New ?? [];
        _resolvedFindings = rows?.Resolved ?? [];
        if (rows is null) FindingsGrid.ItemsSource = null;
        AccessGrid.ItemsSource = rows?.Access;
        AcesGrid.ItemsSource = rows?.Aces;
        MembersGrid.ItemsSource = rows?.Members;
        if (rows is null) return;
        CountNew.Text = Labels.Number(rows.New.Count);
        CountResolved.Text = Labels.Number(rows.Resolved.Count);
        CountAccess.Text = Labels.Number(rows.Access.Count);
        CountAces.Text = Labels.Number(rows.Aces.Count);
        CountMembers.Text = Labels.Number(rows.Members.Count);
    }

    private static string PersonName(ComparisonResult c, string sid)
    {
        var p = c.Newer.Principals.GetValueOrDefault(sid) ?? c.Older.Principals.GetValueOrDefault(sid);
        return p is null ? sid : p.DisplayName.Length > 0 ? p.DisplayName : p.Label;
    }

    private static string AceText(AceEntry? ace) => ace is null ? ""
        : $"{(ace.IsDeny ? Strings.Ace_Deny : Strings.Ace_Allow)}: {Labels.RightsText(ace)}{(ace.IsInherited ? Strings.Cmp_InheritedSuffix : "")}";

    private void Tile_Checked(object sender, RoutedEventArgs e)
    {
        if (_state.Comparison is null)
        {
            ShowList(null, Strings.Cmp_NoSameTargets);
            return;
        }
        if (sender == TileNew)
        {
            FindingsGrid.ItemsSource = _newFindings;
            ShowList(FindingsGrid, _newFindings.Count == 0 ? Strings.Cmp_NoNewFindings : null);
        }
        else if (sender == TileResolved)
        {
            FindingsGrid.ItemsSource = _resolvedFindings;
            ShowList(FindingsGrid, _resolvedFindings.Count == 0 ? Strings.Cmp_NoResolved : null);
        }
        else if (sender == TileAccess)
            ShowList(AccessGrid, AccessGrid.Items.Count == 0 ? Strings.Cmp_NoAccessChanges : null);
        else if (sender == TileAces)
            ShowList(AcesGrid, AcesGrid.Items.Count == 0 ? Strings.Cmp_NoAceChanges : null);
        else if (sender == TileMembers)
            ShowList(MembersGrid, MembersGrid.Items.Count == 0 ? Strings.Cmp_NoMembershipChanges : null);
    }

    /// <summary>
    /// Sortieren per Spaltenkopf im Hintergrund: Die Tabelle selbst sortiert über Reflexion im UI-Thread – bei
    /// 200.000 Zugriffsänderungen stand die Oberfläche dafür zwei Sekunden still.
    /// </summary>
    private async void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (sender is not DataGrid grid || grid.ItemsSource is not System.Collections.IList { Count: > 0 } list) return;
        var column = e.Column;
        var property = list[0]!.GetType().GetProperty(column.SortMemberPath);
        if (property is null) return;
        var descending = column.SortDirection == ListSortDirection.Ascending;
        var items = list.Cast<object>().ToList();
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var sorted = await Task.Run(() =>
            {
                // Schlüssel einmal lesen; OrderBy ist stabil – gleiche Werte behalten ihre bisherige Reihenfolge
                var keyed = items.Select(item => (Key: property.GetValue(item), Item: item));
                IComparer<object?> order = property.PropertyType == typeof(string) ? new TextOrder() : Comparer<object?>.Default;
                return (descending ? keyed.OrderByDescending(k => k.Key, order) : keyed.OrderBy(k => k.Key, order)).Select(k => k.Item).ToList();
            });
            if (!ReferenceEquals(grid.ItemsSource, list)) return; // inzwischen andere Liste
            grid.ItemsSource = sorted;
            foreach (var c in grid.Columns) c.SortDirection = null;
            column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private sealed class TextOrder : IComparer<object?>
    {
        public int Compare(object? x, object? y) => StringComparer.CurrentCultureIgnoreCase.Compare(x as string, y as string);
    }

    private void ShowList(DataGrid? grid, string? emptyText)
    {
        foreach (var g in new[] { FindingsGrid, AccessGrid, AcesGrid, MembersGrid })
            g.Visibility = g == grid && emptyText is null ? Visibility.Visible : Visibility.Collapsed;
        ListEmpty.Visibility = emptyText is null ? Visibility.Collapsed : Visibility.Visible;
        ListEmpty.Text = emptyText ?? "";
    }
}
