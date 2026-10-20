using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Core;
using Zutritt.Core.Resources;

namespace Zutritt.UI.Pages;

/// <summary>Bericht: Excel zum Weiterarbeiten, PDF zum Abzeichnen – optional mit den Änderungen seit dem letzten Lauf.</summary>
public partial class ReportPage : UserControl, IPage
{
    private readonly AppState _state;
    private string? _lastPath;
    private string? _lastFolder;
    private bool _busy;
    private bool _wantChanges = true;

    public ReportPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        IncludeChanges.Click += (_, _) => _wantChanges = IncludeChanges.IsChecked == true;
    }

    public void OnShown() => UpdateState();

    public async Task PrepareScreenshotAsync()
    {
        UpdateState();
        ResultCard.Visibility = Visibility.Collapsed;
        await Ui.IdleAsync();
    }

    private void UpdateState()
    {
        var scan = _state.Scan;
        if (scan is null)
        {
            HeroTitle.Text = Strings.Ui_NoRunLoaded;
            HeroText.Text = Strings.Rp_NoRunText;
            HeroFile.Visibility = Visibility.Collapsed;
            ExcelButton.IsEnabled = PdfButton.IsEnabled = false;
            ChangesCard.IsEnabled = false;
            return;
        }

        int high = scan.Findings.Count(f => f.Severity == Severity.Hoch);
        HeroTitle.Text = Texts.Format(Strings.Rp_ReportFor, Labels.Targets(scan.Request.Targets));
        var summary = high > 0
            ? Texts.Plural(scan.Findings.Count, Strings.Rp_RunSummaryHighOne, Strings.Rp_RunSummaryHighMany)
            : Texts.Plural(scan.Findings.Count, Strings.Rp_RunSummaryOne, Strings.Rp_RunSummaryMany);
        HeroText.Text = Texts.Format(summary, Labels.Date(scan.StartedAt), Labels.Number(scan.FoldersVisited),
            Labels.Number(scan.Findings.Count), Labels.Number(high));
        HeroFile.Text = Texts.Format(Strings.Rp_SuggestedName, Labels.ReportFileName(scan));
        HeroFile.Visibility = Visibility.Visible;
        ExcelButton.IsEnabled = PdfButton.IsEnabled = !_busy;

        if (_state.Comparison is { } c)
        {
            ChangesCard.IsEnabled = true;
            IncludeChanges.IsChecked = _wantChanges;
            ChangesCard.Description = Texts.Format(Strings.Rp_ComparisonWith, Labels.Date(c.Older.StartedAt));
        }
        else
        {
            ChangesCard.IsEnabled = false;
            IncludeChanges.IsChecked = false;
            ChangesCard.Description = Strings.Rp_NoEarlier;
        }
    }

    private void Excel_Click(object sender, RoutedEventArgs e) => Save(pdf: false);
    private void Pdf_Click(object sender, RoutedEventArgs e) => Save(pdf: true);

    private async void Save(bool pdf)
    {
        if (_busy || _state.Scan is not { } scan) return;
        var ext = pdf ? ".pdf" : ".xlsx";
        var dialog = new SaveFileDialog
        {
            Title = pdf ? Strings.Rp_SavePdfTitle : Strings.Rp_SaveExcelTitle,
            Filter = pdf ? Strings.Rp_PdfFilter : Strings.Rp_ExcelFilter,
            FileName = Labels.ReportFileName(scan) + ext,
            DefaultExt = ext,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = _lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var path = dialog.FileName;
        var comparison = IncludeChanges.IsChecked == true ? _state.Comparison : null;
        SetBusy(true, path);
        // Die Berichte melden ihre Arbeitsschritte aus dem Hintergrund; Progress bringt sie auf den UI-Thread
        var phase = new Progress<string>(text => { if (_busy) ResultPhase.Text = text; });
        var clock = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                if (pdf) PdfReport.Write(scan, comparison, path, phase);
                else ExcelReport.Write(scan, comparison, path, phase);
            });
            _lastPath = path;
            _lastFolder = Path.GetDirectoryName(path);
            ShowDone(path, pdf, clock.Elapsed);
        }
        catch (Exception ex) when (!_state.Strict)
        {
            ResultCard.Visibility = Visibility.Collapsed;
            _state.Report(NoticeKind.Error, Texts.Format(Strings.Rp_SaveFailed, ErrorCodes.Describe(ex, ErrorCodes.ReportWrite)));
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void SetBusy(bool busy, string? path)
    {
        _busy = busy;
        ExcelButton.IsEnabled = PdfButton.IsEnabled = !busy && _state.Scan is not null;
        Mouse.OverrideCursor = busy ? Cursors.AppStarting : null;
        if (!busy) return;
        ResultCard.Visibility = Visibility.Visible;
        ResultIcon.Text = "\uE916";
        ResultIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        ResultTitle.Text = Strings.Rp_Creating;
        ResultPath.Text = path ?? "";
        ResultButtons.Visibility = Visibility.Collapsed;
        ResultPhase.Text = Strings.Rp_Preparing;
        ResultWorking.Visibility = Visibility.Visible;
    }

    private void ShowDone(string path, bool pdf, TimeSpan duration)
    {
        ResultCard.Visibility = Visibility.Visible;
        ResultWorking.Visibility = Visibility.Collapsed;
        ResultIcon.Text = "\uE73E";
        ResultIcon.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
        // Die Dauer erst ab ein paar Sekunden nennen – bei großen Läufen erklärt sie das Warten
        var took = duration.TotalSeconds >= 5 ? Texts.Format(Strings.Rp_After, Labels.Duration(duration)) : "";
        ResultTitle.Text = (pdf ? Strings.Rp_PdfSaved : Strings.Rp_ExcelSaved) + took;
        ResultPath.Text = path;
        ResultButtons.Visibility = Visibility.Visible;
    }

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPath is null) return;
        try { Process.Start(new ProcessStartInfo(_lastPath) { UseShellExecute = true }); }
        catch (Exception ex) { _state.Report(NoticeKind.Warning, Texts.Format(Strings.Rp_OpenFailed, ex.Message)); }
    }

    private void ShowResult_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPath is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _state.Report(NoticeKind.Warning, Texts.Format(Strings.Rp_FolderOpenFailed, ex.Message)); }
    }
}
