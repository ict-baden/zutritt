using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Zutritt.UI.Pages;

/// <summary>Info: Version, Datenbank, Herausgeber, Lizenz (noch offen), Repo und Befehlszeile.</summary>
public partial class InfoPage : UserControl, IPage
{
    private readonly AppState _state;

    public InfoPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        VersionText.Text = $"Version {AppInfo.Version} · .NET {Environment.Version.ToString(3)}";
        CommandExample.Text = "\"C:\\Program Files\\Zutritt\\Zutritt.exe\" scan \\\\FS01 --vergleich --bericht D:\\Berichte\\Zutritt.pdf";
    }

    public void OnShown()
    {
        if (_state.Store is null)
        {
            DatabaseCard.Description = _state.DatabasePath.Length > 0
                ? $"{_state.DatabasePath} – konnte nicht geöffnet werden, Läufe werden nicht gespeichert"
                : "Keine Datenbank geöffnet – Läufe werden nicht gespeichert";
            DatabaseButton.IsEnabled = _state.DatabasePath.Length > 0;
        }
        else
        {
            DatabaseCard.Description = _state.IsDemo ? $"{_state.DatabasePath} (Demo, wird beim Beenden gelöscht)" : _state.DatabasePath;
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

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception ex) { _state.Report(NoticeKind.Warning, $"Der Browser ließ sich nicht öffnen: {ex.Message}"); }
    }
}
