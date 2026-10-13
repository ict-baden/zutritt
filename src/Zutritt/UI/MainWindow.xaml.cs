using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Zutritt.Core.Model;
using Zutritt.UI.Pages;
using Zutritt.UI.Theme;

namespace Zutritt.UI;

/// <summary>Hauptfenster: Navigation links (wie die Einstellungen von Vitrine), Seiten rechts, Meldungen unten.</summary>
public partial class MainWindow : Window, IThemedWindow
{
    private sealed record PageEntry(RadioButton Nav, string Slug, string Title, string Subtitle, UserControl Page);

    /// <summary>Seiten in der Reihenfolge, in der <c>--screenshots</c> sie aufnimmt (Dateiname = Kürzel).</summary>
    internal static readonly string[] PageSlugs = ["uebersicht", "befunde", "ordner", "personen", "vergleich", "bericht", "info"];

    private readonly AppState _state;
    private readonly bool _screenshot;
    private readonly PageEntry[] _pages;
    private readonly FoldersPage _folders;
    private readonly OverviewPage _overview;
    private readonly DispatcherTimer _infoTimer;
    private PageEntry? _current;

    public MainWindow(AppState state, bool screenshotMode = false)
    {
        InitializeComponent();
        _state = state;
        _screenshot = screenshotMode;
        _folders = new FoldersPage(state);
        _overview = new OverviewPage(state);
        _pages =
        [
            new(NavOverview, "uebersicht", "Übersicht", "Neuen Scan starten oder einen gespeicherten Lauf öffnen", _overview),
            new(NavFindings, "befunde", "Befunde", "Was auffällt – nach Schwere geordnet, mit Erklärung und Empfehlung", new FindingsPage(state)),
            new(NavFolders, "ordner", "Ordner", "Wer darf hier rein?", _folders),
            new(NavPeople, "personen", "Personen", "Wo darf jemand überall hin?", new PeoplePage(state)),
            new(NavCompare, "vergleich", "Vergleich", "Was hat sich seit dem letzten Lauf geändert?", new ComparisonPage(state)),
            new(NavReport, "bericht", "Bericht", "Excel zum Weiterarbeiten, PDF zum Abzeichnen", new ReportPage(state)),
            new(NavInfo, "info", "Info", "Über Zutritt", new InfoPage(state)),
        ];

        _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _infoTimer.Tick += (_, _) =>
        {
            _infoTimer.Stop();
            InfoBar.Visibility = Visibility.Collapsed;
        };

        state.ScanChanged += OnScanChanged;
        state.ComparisonChanged += OnComparisonChanged;
        state.FolderRequested += OnFolderRequested;
        state.PageRequested += ShowPage;
        state.Notice += ShowNotice;
        Closed += (_, _) =>
        {
            _infoTimer.Stop();
            state.ScanChanged -= OnScanChanged;
            state.ComparisonChanged -= OnComparisonChanged;
            state.FolderRequested -= OnFolderRequested;
            state.PageRequested -= ShowPage;
            state.Notice -= ShowNotice;
        };

        // Screenshots: kein Mica (gibt es in Bildern nicht), normaler Hintergrund aus dem Theme
        SourceInitialized += (_, _) => { if (!_screenshot) ThemeManager.ApplyWindowChrome(this); };
        Loaded += (_, _) => { if (state.StartupProblem is { } problem) ShowNotice(NoticeKind.Warning, problem); };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        if (state.IsDemo) Title = "Zutritt – Demo";

        UpdateStatus();
        NavOverview.IsChecked = true;
    }

    public void ThemeChanged()
    {
        if (!_screenshot) ThemeManager.ApplyWindowChrome(this);
    }

    // ================= Navigation =================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Während InitializeComponent gibt es die Seiten noch nicht
        if (_pages is null || _pages.FirstOrDefault(p => p.Nav == sender) is not { } entry) return;
        _current = entry;
        PageHost.Content = entry.Page;
        PageTitle.Text = entry.Title;
        PageSubtitle.Text = entry.Subtitle;
        ((IPage)entry.Page).OnShown();
    }

    public void ShowPage(string slug)
    {
        var entry = _pages.FirstOrDefault(p => p.Slug == slug) ?? _pages[0];
        if (entry.Nav.IsChecked == true) ((IPage)entry.Page).OnShown();
        else entry.Nav.IsChecked = true;
    }

    private void OnFolderRequested(string path)
    {
        ShowPage("ordner");
        _folders.SelectPath(path);
    }

    private void OnScanChanged()
    {
        UpdateStatus();
        if (_current is not null) ((IPage)_current.Page).OnShown();
    }

    private void OnComparisonChanged()
    {
        if (_current is not null) ((IPage)_current.Page).OnShown();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.F && _current?.Page is ISearchPage search)
        {
            search.FocusSearch();
            e.Handled = true;
            return;
        }
        // Strg+1 … Strg+7 springt zu den Seiten in der Reihenfolge der Navigation
        int index = e.Key is >= Key.D1 and <= Key.D7 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad7 ? e.Key - Key.NumPad1 : -1;
        if (index >= 0)
        {
            _pages[index].Nav.IsChecked = true;
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_screenshot || !_overview.IsScanning) return;
        var answer = MessageBox.Show(this, "Ein Scan läuft noch. Zutritt trotzdem beenden? Der Lauf wird dann nicht gespeichert.",
            "Zutritt beenden", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }

    // ================= Status links =================

    private void UpdateStatus()
    {
        var scan = _state.Scan;
        if (scan is null)
        {
            StatusTitle.Text = "Kein Lauf geladen";
            StatusDetail.Text = _state.IsDemo ? "Demo" : "Scan starten oder Lauf öffnen";
            StatusGlyph.Text = "\uE81C";
            Nav.SetBadge(NavFindings, null);
            return;
        }

        StatusTitle.Text = Labels.Targets(scan.Request.Targets);
        StatusTitle.ToolTip = string.Join(Environment.NewLine, scan.Request.Targets.Select(Labels.TargetName));
        StatusDetail.Text = (_state.IsDemo ? "Demo · " : "") + Labels.Date(scan.StartedAt);
        StatusGlyph.Text = "\uE8B7";

        int count = scan.Findings.Count;
        Nav.SetBadge(NavFindings, count == 0 ? null : count > 999 ? "999+" : count.ToString(Labels.German));
        var worst = count == 0 ? Severity.Info : scan.Findings.Max(f => f.Severity);
        Nav.SetBadgeTone(NavFindings, worst switch { Severity.Hoch => "Hoch", Severity.Mittel => "Mittel", _ => "" });
        NavFindings.ToolTip = count == 0 ? "Keine Befunde" : $"{count} Befunde";
    }

    // ================= Meldungen =================

    private void ShowNotice(NoticeKind kind, string text)
    {
        var (glyph, iconBrush, background) = kind switch
        {
            NoticeKind.Error => ("\uE783", "SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
            NoticeKind.Warning => ("\uE7BA", "SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
            NoticeKind.Success => ("\uE73E", "SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush"),
            _ => ("\uE946", "AccentTextFillColorPrimaryBrush", (string?)null),
        };
        InfoIcon.Text = glyph;
        InfoText.Text = text;
        InfoIcon.SetResourceReference(TextBlock.ForegroundProperty, iconBrush);
        if (background is null) InfoBar.Background = System.Windows.Media.Brushes.Transparent;
        else InfoBar.SetResourceReference(Border.BackgroundProperty, background);
        InfoBar.Visibility = Visibility.Visible;

        // Erfolg und Hinweise verschwinden von selbst, Fehler und Warnungen bleiben bis zum Schließen
        _infoTimer.Stop();
        if (kind is NoticeKind.Info or NoticeKind.Success) _infoTimer.Start();
    }

    private void CloseInfo_Click(object sender, RoutedEventArgs e)
    {
        _infoTimer.Stop();
        InfoBar.Visibility = Visibility.Collapsed;
    }

    // ================= Screenshots =================

    internal async Task PrepareScreenshotAsync(string slug)
    {
        _infoTimer.Stop();
        InfoBar.Visibility = Visibility.Collapsed;
        ShowPage(slug);
        await ((IPage)_current!.Page).PrepareScreenshotAsync();
    }
}
