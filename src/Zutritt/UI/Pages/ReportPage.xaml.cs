using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;

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
            HeroTitle.Text = "Kein Lauf geladen";
            HeroText.Text = "Laden Sie auf der Übersicht einen Lauf oder starten Sie einen Scan – dann lässt sich hier der Bericht speichern.";
            HeroFile.Visibility = Visibility.Collapsed;
            ExcelButton.IsEnabled = PdfButton.IsEnabled = false;
            ChangesCard.IsEnabled = false;
            return;
        }

        int high = scan.Findings.Count(f => f.Severity == Severity.Hoch);
        HeroTitle.Text = $"Bericht für {Labels.Targets(scan.Request.Targets)}";
        HeroText.Text = $"Lauf vom {Labels.Date(scan.StartedAt)}: {Labels.Number(scan.FoldersVisited)} Ordner geprüft, " +
            $"{Labels.Number(scan.Findings.Count)} {(scan.Findings.Count == 1 ? "Befund" : "Befunde")}" + (high > 0 ? $", davon {Labels.Number(high)} „Hoch“." : ".");
        HeroFile.Text = $"Vorschlag für den Dateinamen: {Labels.ReportFileName(scan)}";
        HeroFile.Visibility = Visibility.Visible;
        ExcelButton.IsEnabled = PdfButton.IsEnabled = !_busy;

        if (_state.Comparison is { } c)
        {
            ChangesCard.IsEnabled = true;
            IncludeChanges.IsChecked = _wantChanges;
            ChangesCard.Description = $"Vergleich mit dem Lauf vom {Labels.Date(c.Older.StartedAt)}: neue und behobene Befunde, geänderte Rechte und Mitgliedschaften als eigener Abschnitt.";
        }
        else
        {
            ChangesCard.IsEnabled = false;
            IncludeChanges.IsChecked = false;
            ChangesCard.Description = "Es gibt keinen früheren Lauf zum Vergleichen – der Bericht enthält dann keinen Abschnitt „Änderungen“.";
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
            Title = pdf ? "PDF-Bericht speichern" : "Excel-Bericht speichern",
            Filter = pdf ? "PDF-Dokument (*.pdf)|*.pdf" : "Excel-Arbeitsmappe (*.xlsx)|*.xlsx",
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
            _state.Report(NoticeKind.Error, $"Der Bericht ließ sich nicht speichern: {ex.Message}");
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
        ResultTitle.Text = "Bericht wird erstellt …";
        ResultPath.Text = path ?? "";
        ResultButtons.Visibility = Visibility.Collapsed;
        ResultPhase.Text = "Wird vorbereitet …";
        ResultWorking.Visibility = Visibility.Visible;
    }

    private void ShowDone(string path, bool pdf, TimeSpan duration)
    {
        ResultCard.Visibility = Visibility.Visible;
        ResultWorking.Visibility = Visibility.Collapsed;
        ResultIcon.Text = "\uE73E";
        ResultIcon.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
        // Die Dauer erst ab ein paar Sekunden nennen – bei großen Läufen erklärt sie das Warten
        var took = duration.TotalSeconds >= 5 ? $" nach {Labels.Duration(duration)}" : "";
        ResultTitle.Text = (pdf ? "PDF-Bericht gespeichert" : "Excel-Bericht gespeichert") + took;
        ResultPath.Text = path;
        ResultButtons.Visibility = Visibility.Visible;
    }

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPath is null) return;
        try { Process.Start(new ProcessStartInfo(_lastPath) { UseShellExecute = true }); }
        catch (Exception ex) { _state.Report(NoticeKind.Warning, $"Die Datei ließ sich nicht öffnen: {ex.Message}"); }
    }

    private void ShowResult_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPath is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _state.Report(NoticeKind.Warning, $"Der Ordner ließ sich nicht öffnen: {ex.Message}"); }
    }
}
