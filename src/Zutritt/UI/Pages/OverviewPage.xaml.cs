using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Zutritt.Core.Identity;
using Zutritt.Platform.Windows;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Core;
using Zutritt.Core.Resources;

namespace Zutritt.UI.Pages;

/// <summary>Übersicht: neuen Scan starten, Ergebnis des geladenen Laufs, letzte Läufe öffnen oder löschen.</summary>
public partial class OverviewPage : UserControl, IPage
{
    private readonly AppState _state;
    private readonly DispatcherTimer _clock;
    private CancellationTokenSource? _cts;
    private DateTime _started;
    private Task _runsTask = Task.CompletedTask;

    public OverviewPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => ProgressElapsed.Text = Labels.Duration(DateTime.UtcNow - _started);
        DatabaseHint.Text = state.Store is null
            ? Strings.Ov_NoDatabase
            : state.IsDemo
                ? Texts.Format(Strings.Ov_DemoDatabase, state.DatabasePath)
                : Texts.Format(Strings.Ov_SavedIn, state.DatabasePath);
    }

    /// <summary>Ein Scan läuft gerade (das Hauptfenster fragt dann beim Schließen nach).</summary>
    internal bool IsScanning => _cts is not null;

    public void OnShown()
    {
        ShowResult();
        ReloadRuns();
    }

    public async Task PrepareScreenshotAsync()
    {
        ShowResult();
        await _runsTask;
        RunsGrid.SelectedItem = null;
        Scroller.ScrollToTop();
        await Ui.IdleAsync();
    }

    // ================= Ergebnis =================

    private void ShowResult()
    {
        var scan = _state.Scan;
        bool show = scan is not null && !IsScanning;
        WelcomeHero.Visibility = scan is null && !IsScanning ? Visibility.Visible : Visibility.Collapsed;
        ResultHero.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ResultTiles.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (scan is null) return;

        ResultTitle.Text = Labels.Targets(scan.Request.Targets);
        ResultTitle.ToolTip = string.Join(Environment.NewLine, scan.Request.Targets.Select(Labels.TargetName));
        var duration = scan.FinishedAt > scan.StartedAt ? Texts.Format(Strings.Ov_Duration, Labels.Duration(scan.FinishedAt - scan.StartedAt)) : "";
        ResultMeta.Text = Texts.Format(Strings.Ov_ResultMeta, Labels.Date(scan.StartedAt), duration, scan.ScannedBy, scan.Machine) + (_state.IsDemo ? Strings.Ov_DemoSuffix : "");

        if (_state.Comparison is { } c)
        {
            ResultChanges.Text = Texts.Format(Strings.Ov_ChangesSince, Labels.Date(c.Older.StartedAt),
                Plural(c.NewFindings.Count, Strings.Ov_NewFindingsOne, Strings.Ov_NewFindingsMany),
                Plural(c.ResolvedFindings.Count, Strings.Ov_ResolvedOne, Strings.Ov_ResolvedMany),
                Plural(c.AccessChanges.Count, Strings.Ov_AccessChangesOne, Strings.Ov_AccessChangesMany));
            ResultChanges.Visibility = Visibility.Visible;
            ShowCompareButton.Visibility = Visibility.Visible;
        }
        else
        {
            ResultChanges.Visibility = Visibility.Collapsed;
            ShowCompareButton.Visibility = Visibility.Collapsed;
        }

        int Count(Severity s) => scan.Findings.Count(f => f.Severity == s);
        int unreadable = Math.Max(scan.Folders.Count(f => f.Error is not null), scan.Errors.Count);
        TileVisited.Value = Labels.Number(scan.FoldersVisited);
        TileVisited.Label = scan.FilesVisited > 0 ? Texts.Format(Strings.Ov_FoldersCheckedFiles, Labels.Number(scan.FilesVisited)) : Strings.Pdf_FoldersChecked;
        TileStored.Value = Labels.Number(scan.Folders.Count(f => f.Error is null));
        TilePeople.Value = Labels.Number(scan.Principals.Values.Count(p => p.Kind == PrincipalKind.Benutzer));
        TileErrors.Value = Labels.Number(unreadable);
        TileErrors.Tone = unreadable > 0 ? "Info" : "Gut";
        TileHigh.Value = Labels.Number(Count(Severity.Hoch));
        TileMedium.Value = Labels.Number(Count(Severity.Mittel));
        TileLow.Value = Labels.Number(Count(Severity.Niedrig));
        TileInfo.Value = Labels.Number(Count(Severity.Info));
    }

    private static string Plural(int n, string one, string many) => Texts.Format(Texts.Plural(n, one, many), Labels.Number(n));

    private void ShowFindings_Click(object sender, RoutedEventArgs e) => _state.ShowPage("befunde");
    private void ShowCompare_Click(object sender, RoutedEventArgs e) => _state.ShowPage("vergleich");

    // ================= Neuer Scan =================

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.Ov_ChooseFolderTitle, Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var folder in dialog.FolderNames) AppendTarget(folder);
    }

    private void LocalShares_Click(object sender, RoutedEventArgs e) => AppendTarget(".");

    private void AppendTarget(string target)
    {
        var lines = TargetsBox.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Contains(target, StringComparer.OrdinalIgnoreCase)) return;
        lines.Add(target);
        TargetsBox.Text = string.Join(Environment.NewLine, lines);
        TargetsBox.CaretIndex = TargetsBox.Text.Length;
    }

    private void Targets_TextChanged(object sender, TextChangedEventArgs e) => ShowHint(null);

    private void ShowHint(string? problem)
    {
        if (problem is null)
        {
            ScanHintIcon.Text = "\uE72E";
            ScanHintIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            ScanHint.Text = Strings.Ov_ReadOnly;
            ScanHint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return;
        }
        ScanHintIcon.Text = "\uE7BA";
        ScanHintIcon.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        ScanHint.Text = problem;
        ScanHint.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
    }

    private bool TryBuildRequest(out ScanRequest request, out string problem)
    {
        request = new ScanRequest { Targets = [] };
        problem = "";
        var targets = TargetsBox.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0)
        {
            problem = Strings.Ov_NeedTarget;
            return false;
        }

        int? maxDepth = null;
        if (MaxDepthBox.Text.Trim() is { Length: > 0 } depthText)
        {
            if (!int.TryParse(depthText, out var d) || d < 0)
            {
                problem = Strings.Ov_BadDepth;
                return false;
            }
            maxDepth = d;
        }
        if (!int.TryParse(DeepBox.Text.Trim(), out var deep) || deep < 1)
        {
            problem = Strings.Ov_BadDeepFrom;
            return false;
        }

        request = new ScanRequest
        {
            Targets = targets,
            IncludeFiles = OptFiles.IsChecked == true,
            ResolveGroups = OptGroups.IsChecked == true,
            SkipAdminShares = OptSkipAdmin.IsChecked == true,
            MaxDepth = maxDepth,
            DeepInheritanceBreakDepth = deep,
        };
        return true;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (IsScanning) return;
        if (!TryBuildRequest(out var request, out var problem))
        {
            ShowHint(problem);
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetScanning(true);
        var progress = new Progress<ScanProgress>(OnProgress);
        try
        {
            var scan = await Task.Run(async () =>
            {
                var scanner = WindowsPlatform.CreateScanner();
                return await scanner.RunAsync(request, progress, cts.Token);
            });
            ProgressPhase.Text = Strings.Ov_SavingComparing;
            ProgressPath.Text = "";
            var notSaved = await _state.AdoptAsync(scan);
            var findings = scan.Findings.Count;
            if (notSaved is not null)
                _state.Report(NoticeKind.Warning, Texts.Format(Strings.Ov_NotSaved, notSaved));
            else
                _state.Report(NoticeKind.Success, Texts.Format(Texts.Plural(findings, Strings.Ov_ScanDoneOne, Strings.Ov_ScanDoneMany),
                    Labels.Number(scan.FoldersVisited), Labels.Number(findings)));
        }
        catch (OperationCanceledException)
        {
            _state.Report(NoticeKind.Info, Strings.Ov_ScanCancelled);
        }
        catch (Exception ex) when (!_state.Strict)
        {
            _state.Report(NoticeKind.Error, Texts.Format(Strings.Ov_ScanFailed, ErrorCodes.Describe(ex, ErrorCodes.ScanFailed)));
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            SetScanning(false);
            ReloadRuns();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null) return;
        _cts.Cancel();
        ProgressPhase.Text = Strings.Ov_Cancelling;
        CancelButton.IsEnabled = false;
    }

    private void SetScanning(bool on)
    {
        ProgressCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        NewScanCard.IsEnabled = !on;
        StartButton.IsEnabled = !on;
        CancelButton.IsEnabled = on;
        if (on)
        {
            _started = DateTime.UtcNow;
            ProgressPhase.Text = Strings.Ov_Preparing;
            ProgressPath.Text = "";
            ProgressVisited.Text = ProgressStored.Text = ProgressErrors.Text = "0";
            ProgressElapsed.Text = Labels.Duration(TimeSpan.Zero);
            _clock.Start();
            Scroller.ScrollToTop();
        }
        else
        {
            _clock.Stop();
        }
        ShowResult();
    }

    private void OnProgress(ScanProgress p)
    {
        if (!IsScanning) return;
        if (p.Phase.Length > 0) ProgressPhase.Text = p.Phase;
        ProgressPath.Text = p.CurrentPath;
        ProgressVisited.Text = Labels.Number(p.FoldersVisited);
        ProgressStored.Text = Labels.Number(p.FoldersStored);
        ProgressErrors.Text = Labels.Number(p.Errors);
    }

    // ================= Letzte Läufe =================

    /// <summary>Zeile der Lauf-Liste (öffentlich, damit die Bindungen sie sicher lesen können).</summary>
    public sealed class RunRow(ScanSummary summary, bool isCurrent)
    {
        public override string ToString() => Texts.Format(Strings.Ov_RunRowText, Date, Targets, FindingsText);
        public ScanSummary Summary { get; } = summary;
        public long Id => Summary.Id;
        public DateTimeOffset StartedAt => Summary.StartedAt;
        public string Date => Labels.Date(Summary.StartedAt);
        public string Targets => Summary.Targets;
        // Geprüfte Ordner wie in der Zusammenfassung – gespeichert sind nur die mit eigenen Rechten
        public long Folders => Summary.FoldersVisited;
        public string FoldersText => Labels.Number(Summary.FoldersVisited);
        public int Findings => Summary.Findings;
        public string FindingsText => Labels.Number(Summary.Findings);
        public bool HasHigh => Summary.HighFindings > 0;
        public string HighText => Texts.Format(Strings.Ov_HighCount, Labels.Number(Summary.HighFindings));
        public string ScannedBy => Summary.ScannedBy;
        public bool IsCurrent { get; } = isCurrent;
    }

    private void ReloadRuns()
    {
        var task = LoadRunsAsync();
        _runsTask = task;
        Ui.Run(_state, () => task, Strings.Ov_RunsUnreadable);
    }

    private async Task LoadRunsAsync()
    {
        var runs = await _state.ListRunsAsync();
        var current = _state.Scan;
        var selected = (RunsGrid.SelectedItem as RunRow)?.Id;
        var rows = runs.Select(r => new RunRow(r, current is not null && current.Id != 0 && r.Id == current.Id)).ToList();
        RunsGrid.ItemsSource = rows;
        RunsGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
        RunsGrid.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunsEmpty.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        RunsEmpty.Text = _state.Store is null
            ? Strings.Ov_NoDatabaseNoRuns
            : Strings.Ov_NoRuns;
        UpdateRunButtons();
    }

    private void UpdateRunButtons()
    {
        bool any = RunsGrid.SelectedItem is RunRow;
        OpenRunButton.IsEnabled = any && !IsScanning;
        DeleteRunButton.IsEnabled = any && !IsScanning;
    }

    private void RunsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRunButtons();

    private void RunsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(RunsGrid, d) is DataGridRow { Item: RunRow row })
            OpenRun(row);
    }

    private void RunsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (RunsGrid.SelectedItem is not RunRow row) return;
        if (e.Key == Key.Enter)
        {
            OpenRun(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteRun(row);
            e.Handled = true;
        }
    }

    private void OpenRun_Click(object sender, RoutedEventArgs e)
    {
        if (RunsGrid.SelectedItem is RunRow row) OpenRun(row);
    }

    private void DeleteRun_Click(object sender, RoutedEventArgs e)
    {
        if (RunsGrid.SelectedItem is RunRow row) DeleteRun(row);
    }

    private void RefreshRuns_Click(object sender, RoutedEventArgs e) => ReloadRuns();

    private void OpenRun(RunRow row)
    {
        if (IsScanning) return;
        Ui.Run(_state, async () =>
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await _state.OpenRunAsync(row.Id);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
            _state.Report(NoticeKind.Success, Texts.Format(Strings.Ov_RunLoaded, row.Date, row.Targets));
        }, Strings.Ov_RunNotOpened);
    }

    private void DeleteRun(RunRow row)
    {
        if (IsScanning) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            Texts.Format(Strings.Ov_DeleteConfirm, row.Date, row.Targets),
            Strings.Ov_DeleteRun, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        Ui.Run(_state, async () =>
        {
            await _state.DeleteRunAsync(row.Id);
            ReloadRuns();
            _state.Report(NoticeKind.Info, Texts.Format(Strings.Ov_RunDeleted, row.Date));
        }, Strings.Ov_RunNotDeleted);
    }
}
