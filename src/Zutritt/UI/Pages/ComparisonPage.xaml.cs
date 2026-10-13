using System.Windows;
using System.Windows.Controls;
using Zutritt.Core.Model;

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
        public override string ToString() => $"{Person}, {Path}, vorher {Before}, nachher {After}";
    }
    public sealed record AceRow(string Change, string Path, string Account, string Before, string After)
    {
        public override string ToString() => $"{Change}, {Path}, {Account}";
    }
    public sealed record MemberRow(string Change, string Group, string Member)
    {
        public override string ToString() => $"{Change}: {Member} in {Group}";
    }

    private readonly AppState _state;
    private ScanResult? _shownScan;
    private bool _updating;
    private int _choicesVersion;
    private List<OlderChoice> _choices = [];
    private List<FindingRow> _newFindings = [];
    private List<FindingRow> _resolvedFindings = [];
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
            _choicesTask = Task.CompletedTask;
            ShowComparison();
            return;
        }
        var task = LoadChoicesAsync(scan, version);
        _choicesTask = task;
        Ui.Run(_state, () => task, "Die gespeicherten Läufe ließen sich nicht lesen");
    }

    private async Task LoadChoicesAsync(ScanResult scan, int version)
    {
        var runs = await _state.ListRunsAsync();
        if (version != _choicesVersion || !ReferenceEquals(scan, _shownScan)) return;
        var targets = new HashSet<string>(scan.Request.Targets.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        _choices = runs
            .Where(r => r.Id != scan.Id && r.StartedAt < scan.StartedAt)
            .OrderByDescending(r => r.StartedAt)
            .Select(r =>
            {
                var same = targets.SetEquals(r.Targets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                var findings = r.Findings == 1 ? "1 Befund" : $"{Labels.Number(r.Findings)} Befunde";
                return new OlderChoice(r.Id, $"{Labels.Date(r.StartedAt)} · {r.Targets} · {findings}", same);
            })
            .ToList();
        ShowComparison();
    }

    private void OlderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || OlderBox.SelectedItem is not OlderChoice choice) return;
        if (_state.Comparison is { } c && c.Older.Id == choice.Id) return;
        Ui.Run(_state, () => _state.CompareWithAsync(choice.Id), "Der Vergleich ist fehlgeschlagen");
    }

    // ================= Anzeige =================

    private void ShowComparison()
    {
        var scan = _state.Scan;
        var c = _state.Comparison;

        if (scan is null)
        {
            ShowNothing("Kein Lauf geladen", "Laden Sie auf der Übersicht einen Lauf – dann zeigt diese Seite, was sich seit dem Lauf davor geändert hat.");
            return;
        }
        if (c is null && _choices.Count == 0)
        {
            ShowNothing("Noch kein früherer Lauf",
                _state.Store is null
                    ? "Ohne Datenbank gibt es keine früheren Läufe zum Vergleichen."
                    : "Für einen Vergleich braucht es mindestens zwei Läufe. Nach dem nächsten Scan derselben Ziele sehen Sie hier, " +
                      "was sich geändert hat: neue und behobene Befunde, wer mehr oder weniger darf, geänderte Einträge und Gruppenmitgliedschaften.");
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
            FillGrids(null);
            ShowList(null, "Es gibt keinen früheren Lauf mit denselben Zielen. Wählen Sie oben einen Lauf, mit dem verglichen werden soll.");
            return;
        }

        var days = (c.Newer.StartedAt - c.Older.StartedAt).TotalDays;
        CompareInfo.Text = days >= 1.5 ? $"{Math.Round(days):0} Tage vorher" : days >= 0.5 ? "1 Tag vorher" : "am selben Tag";
        var sameTargets = new HashSet<string>(c.Older.Request.Targets, StringComparer.OrdinalIgnoreCase).SetEquals(c.Newer.Request.Targets);
        TargetsWarning.Visibility = sameTargets ? Visibility.Collapsed : Visibility.Visible;
        TargetsWarningText.Text = $"Der ältere Lauf hatte andere Ziele ({Labels.Targets(c.Older.Request.Targets)}) – Unterschiede können auch daher kommen.";

        FillGrids(c);
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

    private void FillGrids(ComparisonResult? c)
    {
        if (c is null)
        {
            FindingsGrid.ItemsSource = AccessGrid.ItemsSource = AcesGrid.ItemsSource = MembersGrid.ItemsSource = null;
            return;
        }
        string Name(string? sid) => sid is null ? "" : AppState.NameIn(c, sid);

        _newFindings = c.NewFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FindingRow(f.Severity, f.RuleId, f.Path, Name(f.Sid), f.Message)).ToList();
        _resolvedFindings = c.ResolvedFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FindingRow(f.Severity, f.RuleId, f.Path, Name(f.Sid), f.Message)).ToList();
        var access = c.AccessChanges.OrderBy(a => Name(a.Sid), StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccessRow(PersonName(c, a.Sid), Name(a.Sid), a.Path, Labels.Of(a.Before), a.Before, Labels.Of(a.After), a.After, a.After > a.Before)).ToList();
        var aces = c.AceChanges.OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AceRow(Labels.Of(a.Kind), a.Path, Name(a.Sid), AceText(a.Before), AceText(a.After)))
            .Concat(c.FoldersAdded.Select(p => new AceRow("Eigene Rechte neu", p, "", "", "")))
            .Concat(c.FoldersRemoved.Select(p => new AceRow("Eigene Rechte entfallen", p, "", "", "")))
            .ToList();
        var members = c.MembershipChanges.OrderBy(m => Name(m.GroupSid), StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new MemberRow(m.Kind == ChangeKind.Hinzugefuegt ? "Hinzugekommen" : m.Kind == ChangeKind.Entfernt ? "Entfernt" : "Geändert",
                Name(m.GroupSid), Name(m.MemberSid))).ToList();

        AccessGrid.ItemsSource = access;
        AcesGrid.ItemsSource = aces;
        MembersGrid.ItemsSource = members;
        CountNew.Text = Labels.Number(_newFindings.Count);
        CountResolved.Text = Labels.Number(_resolvedFindings.Count);
        CountAccess.Text = Labels.Number(access.Count);
        CountAces.Text = Labels.Number(aces.Count);
        CountMembers.Text = Labels.Number(members.Count);
    }

    private static string PersonName(ComparisonResult c, string sid)
    {
        var p = c.Newer.Principals.GetValueOrDefault(sid) ?? c.Older.Principals.GetValueOrDefault(sid);
        return p is null ? sid : p.DisplayName.Length > 0 ? p.DisplayName : p.Label;
    }

    private static string AceText(AceEntry? ace) => ace is null ? ""
        : $"{(ace.IsDeny ? "Verweigern" : "Erlauben")}: {Labels.RightsText(ace)}{(ace.IsInherited ? " (geerbt)" : "")}";

    private void Tile_Checked(object sender, RoutedEventArgs e)
    {
        if (_state.Comparison is null)
        {
            ShowList(null, "Es gibt keinen früheren Lauf mit denselben Zielen. Wählen Sie oben einen Lauf, mit dem verglichen werden soll.");
            return;
        }
        if (sender == TileNew)
        {
            FindingsGrid.ItemsSource = _newFindings;
            ShowList(FindingsGrid, _newFindings.Count == 0 ? "Keine neuen Befunde seit dem älteren Lauf." : null);
        }
        else if (sender == TileResolved)
        {
            FindingsGrid.ItemsSource = _resolvedFindings;
            ShowList(FindingsGrid, _resolvedFindings.Count == 0 ? "Seit dem älteren Lauf wurde kein Befund behoben." : null);
        }
        else if (sender == TileAccess)
            ShowList(AccessGrid, AccessGrid.Items.Count == 0 ? "Niemand darf irgendwo mehr oder weniger als vorher." : null);
        else if (sender == TileAces)
            ShowList(AcesGrid, AcesGrid.Items.Count == 0 ? "Keine Einträge in den Zugriffssteuerungslisten geändert." : null);
        else if (sender == TileMembers)
            ShowList(MembersGrid, MembersGrid.Items.Count == 0 ? "Keine Gruppenmitgliedschaft hat sich geändert." : null);
    }

    private void ShowList(DataGrid? grid, string? emptyText)
    {
        foreach (var g in new[] { FindingsGrid, AccessGrid, AcesGrid, MembersGrid })
            g.Visibility = g == grid && emptyText is null ? Visibility.Visible : Visibility.Collapsed;
        ListEmpty.Visibility = emptyText is null ? Visibility.Collapsed : Visibility.Visible;
        ListEmpty.Text = emptyText ?? "";
    }
}
