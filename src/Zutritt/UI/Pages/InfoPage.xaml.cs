using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Zutritt.Core.Diagnostics;

namespace Zutritt.UI.Pages;

/// <summary>
/// Info (Richtlinie 07): Version und Build, Copyright und MIT-Lizenz, Drittanbieter-Lizenzen, Datenbank, Diagnosepaket,
/// Links zu Hilfe &amp; Support, Quellcode, Datenschutz, Impressum und Webseite, Befehlszeile.
/// </summary>
public partial class InfoPage : UserControl, IPage
{
    private readonly AppState _state;
    private string? _lastFolder;

    public InfoPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        VersionText.Text = $"Version {AppInfo.Version}{(AppInfo.Commit is { } c ? $" · Build {c}" : "")} · .NET {Environment.Version.ToString(3)}";
        CopyrightText.Text = $"{AppInfo.Copyright} · {AppInfo.LicenseName}";
        LicenseCard.Header = $"{AppInfo.LicenseName} · {AppInfo.Copyright}";
        ReadOnlyCard.Description = "Zutritt liest Freigaben, NTFS-Rechte und Gruppenmitgliedschaften. An Rechten, Dateien und Konten ändert es nichts. "
                                   + AppInfo.NoDataSentence;
        ImpressumCard.Description = AppInfo.Publisher;
        CommandExample.Text = "\"C:\\Program Files\\Zutritt\\Zutritt.exe\" scan \\\\FS01 --vergleich --bericht D:\\Berichte\\Zutritt.pdf";
    }

    public void OnShown()
    {
        // Pfade im eigenen Profil mit Umgebungsvariable: kein Benutzername auf der Seite (und auf Screenshots)
        var shown = Labels.WithVariables(_state.DatabasePath);
        if (_state.Store is null)
        {
            DatabaseCard.Description = _state.DatabasePath.Length > 0
                ? $"{shown} – konnte nicht geöffnet werden, Läufe werden nicht gespeichert"
                : "Keine Datenbank geöffnet – Läufe werden nicht gespeichert";
            DatabaseButton.IsEnabled = _state.DatabasePath.Length > 0;
        }
        else
        {
            DatabaseCard.Description = _state.IsDemo ? $"{shown} (Demo, wird beim Beenden gelöscht)" : shown;
            DatabaseButton.IsEnabled = true;
        }
    }

    public Task PrepareScreenshotAsync()
    {
        OnShown();
        return Ui.IdleAsync();
    }

    private void OpenDatabaseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_state.DatabasePath);
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            var arguments = File.Exists(_state.DatabasePath) ? $"/select,\"{_state.DatabasePath}\"" : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _state.Report(NoticeKind.Warning, $"Der Ordner ließ sich nicht öffnen: {ex.Message}");
        }
    }

    private void ShowLicense_Click(object sender, RoutedEventArgs e) =>
        ShowText("Lizenz", LicenseTexts.License(), "Der Lizenztext fehlt in dieser Ausgabe von Zutritt", AppInfo.LicenseUrl);

    private void ShowNotices_Click(object sender, RoutedEventArgs e) =>
        ShowText("Drittanbieter-Lizenzen", LicenseTexts.ThirdPartyNotices(), "Die Drittanbieter-Lizenzen fehlen in dieser Ausgabe von Zutritt",
            AppInfo.RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.txt");

    private void ShowText(string title, string? text, string missing, string url)
    {
        if (text is null)
        {
            _state.Report(NoticeKind.Warning, $"{missing} – sie stehen unter {url}.");
            return;
        }
        TextViewerWindow.Show(Window.GetWindow(this), title, text);
    }

    private void OpenSupport_Click(object sender, RoutedEventArgs e) => Open(AppInfo.SupportUrl);

    private void OpenRepository_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepositoryUrl);

    private void OpenPrivacy_Click(object sender, RoutedEventArgs e) => Open(AppInfo.PrivacyUrl);

    private void OpenImpressum_Click(object sender, RoutedEventArgs e) => Open(AppInfo.ImpressumUrl);

    private void OpenWebsite_Click(object sender, RoutedEventArgs e) => Open(AppInfo.WebsiteUrl);

    private void Open(string url)
    {
        if (Links.Open(url) is { } problem) _state.Report(NoticeKind.Warning, problem);
    }

    private async void CreateDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Diagnosepaket speichern",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
            FileName = DiagnosticsPackage.SuggestedFileName(DateTime.Now),
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = _lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var path = dialog.FileName;
        _lastFolder = Path.GetDirectoryName(path);
        bool anonymize = AnonymizeDiagnostics.IsChecked == true;
        var info = AppDiagnostics.Info(_state);
        try
        {
            await Task.Run(() => DiagnosticsPackage.Write(path, info, anonymize));
            Log.Info($"Diagnosepaket erstellt ({(anonymize ? "ohne" : "mit")} Namen und Adressen)");
            _state.Report(NoticeKind.Success, anonymize
                ? $"Diagnosepaket gespeichert: {path} – ohne Namen, Pfade und Adressen."
                : $"Diagnosepaket gespeichert: {path} – mit Namen und Pfaden, bitte nur vertraulich weitergeben.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Error("Diagnosepaket nicht erstellt", ex);
            _state.Report(NoticeKind.Error, $"Das Diagnosepaket ließ sich nicht speichern: {ex.Message}");
        }
    }
}
