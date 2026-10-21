// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Zutritt.Core;
using Zutritt.Core.Diagnostics;
using Zutritt.Core.Localization;
using Zutritt.Core.Storage;
using Zutritt.Core.Resources;

namespace Zutritt.UI.Pages;

/// <summary>
/// Info (Richtlinie 07): Version und Build, Copyright und MIT-Lizenz, Drittanbieter-Lizenzen, Datenbank, Diagnosepaket,
/// Links zu Hilfe &amp; Support, Quellcode, Datenschutz, Impressum und Webseite, Befehlszeile.
/// </summary>
public partial class InfoPage : UserControl, IPage
{
    /// <summary>Eintrag der Sprachauswahl: "" = keine eigene Wahl (Vorgabe des Rechners, sonst wie Windows).</summary>
    public sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly AppState _state;
    private string? _lastFolder;
    private bool _languageReady;
    /// <summary>Sprache ohne eigene Wahl: Vorgabe des Rechners (%ProgramData%), sonst die von Windows.</summary>
    private string _defaultLanguage = UiLanguage.Fallback;

    public InfoPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        VersionText.Text = AppInfo.Commit is { } c
            ? Texts.Format(Strings.Info_VersionBuild, AppInfo.Version, c, Environment.Version.ToString(3))
            : Texts.Format(Strings.Info_Version, AppInfo.Version, Environment.Version.ToString(3));
        CopyrightText.Text = $"{AppInfo.Copyright} · {AppInfo.LicenseName}";
        LicenseCard.Header = $"{AppInfo.LicenseName} · {AppInfo.Copyright}";
        ReadOnlyCard.Description = Texts.Format(Strings.Info_ReadOnlyDescription, AppInfo.NoDataSentence);
        ImpressumCard.Description = AppInfo.Publisher;
        CommandExample.Text = Strings.Info_CliExample;
        SetUpLanguages();
    }

    // ================= Sprache (Richtlinie 13, Regel 8) =================

    private void SetUpLanguages()
    {
        // Der erste Eintrag nennt, was ohne eigene Wahl gilt: Gibt es eine Vorgabe für den Rechner, ist das sie –
        // „Wie Windows“ wäre dann falsch
        var (fallback, fromMachine) = UiLanguage.WithoutUserChoice(UiLanguage.MachineSetting(), UiLanguage.System);
        _defaultLanguage = fallback;
        var first = Texts.Format(fromMachine ? Strings.Info_LanguageMachine : Strings.Info_LanguageSystem, UiLanguage.NativeName(fallback));
        var choices = new List<LanguageChoice> { new("", first) };
        choices.AddRange(UiLanguage.Supported.Select(code => new LanguageChoice(code, UiLanguage.ChoiceName(code))));
        LanguageBox.ItemsSource = choices;
        var current = UiLanguage.UserSetting();
        LanguageBox.SelectedItem = choices.FirstOrDefault(c => c.Code == current) ?? choices[0];
        if (UiLanguage.Normalize(Environment.GetEnvironmentVariable(UiLanguage.EnvironmentVariable)).Length > 0)
            LanguageCard.Description = Texts.Format(Strings.Info_LanguageForced, UiLanguage.EnvironmentVariable);
        _languageReady = true;
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady || LanguageBox.SelectedItem is not LanguageChoice choice) return;
        try
        {
            UiLanguage.SaveUserSetting(choice.Code);
            // Der Hinweis gleich in der gewählten Sprache – die Oberfläche selbst wechselt erst beim nächsten Start
            var culture = System.Globalization.CultureInfo.GetCultureInfo(choice.Code.Length > 0 ? choice.Code : _defaultLanguage);
            _state.Report(NoticeKind.Info, Strings.ResourceManager.GetString(nameof(Strings.Info_LanguageRestart), culture) ?? Strings.Info_LanguageRestart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state.Report(NoticeKind.Error, Texts.Format(Strings.Info_LanguageNotSaved, ErrorCodes.Describe(ex, ErrorCodes.SettingsSave)));
        }
    }

    public void OnShown()
    {
        // Pfade im eigenen Profil mit Umgebungsvariable: kein Benutzername auf der Seite (und auf Screenshots)
        var shown = Labels.WithVariables(_state.DatabasePath);
        if (_state.Store is null)
        {
            DatabaseCard.Description = _state.DatabasePath.Length > 0
                ? Texts.Format(Strings.Info_DbNotOpened, shown)
                : Strings.Info_NoDb;
            DatabaseButton.IsEnabled = _state.DatabasePath.Length > 0;
        }
        else
        {
            DatabaseCard.Description = _state.IsDemo ? Texts.Format(Strings.Info_DemoDb, shown) : shown;
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
            _state.Report(NoticeKind.Warning, Texts.Format(Strings.Rp_FolderOpenFailed, ex.Message));
        }
    }

    private void ShowLicense_Click(object sender, RoutedEventArgs e) =>
        ShowText(Strings.Info_License, LicenseTexts.License(), Strings.Info_LicenseMissing, AppInfo.LicenseUrl);

    private void ShowNotices_Click(object sender, RoutedEventArgs e) =>
        ShowText(Strings.Cli_InfoNotices, LicenseTexts.ThirdPartyNotices(), Strings.Info_NoticesMissing,
            AppInfo.RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.txt");

    private void ShowText(string title, string? text, string missing, string url)
    {
        if (text is null)
        {
            _state.Report(NoticeKind.Warning, Texts.Format(Strings.Info_MissingAt, missing, url));
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
            Title = Strings.Info_SaveDiagnostics,
            Filter = Strings.Info_ZipFilter,
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
        // Zahl der Läufe über die Warteschlange des Speichers – nicht im UI-Thread und nicht neben einem laufenden Speichern
        int? runs = null;
        try
        {
            if (_state.Store is not null) runs = (await _state.ListRunsAsync()).Count;
        }
        catch (Exception ex) when (ex is ScanStoreException or IOException)
        {
        }
        var info = AppDiagnostics.Info(_state, runs);
        try
        {
            await Task.Run(() => DiagnosticsPackage.Write(path, info, anonymize));
            Log.Info($"Diagnosepaket erstellt ({(anonymize ? "ohne" : "mit")} Namen und Adressen)");
            _state.Report(NoticeKind.Success, anonymize
                ? Texts.Format(Strings.Info_DiagSavedAnon, path)
                : Texts.Format(Strings.Info_DiagSavedFull, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Error("Diagnosepaket nicht erstellt", ex);
            _state.Report(NoticeKind.Error, Texts.Format(Strings.Info_DiagFailed, ErrorCodes.Describe(ex, ErrorCodes.DiagnosticsPackage)));
        }
    }
}
