using System.Runtime.InteropServices;
using Zutritt.Core.Diagnostics;
using Zutritt.Core.Storage;
using Zutritt.Platform.Windows.Identity;

namespace Zutritt;

/// <summary>
/// Protokoll und Diagnosepaket der App (Richtlinie 10): Start mit Version und System, unbehandelte Fehler aller Art,
/// Angaben für das Diagnosepaket. Inhalte von Läufen (Rechte, Mitglieder, Kontennamen) kommen nie ins Protokoll.
/// </summary>
internal static class AppDiagnostics
{
    private static bool _hooked;

    /// <summary>Protokoll einschalten und den Start vermerken (<paramref name="mode"/>: „Oberfläche“, „Befehlszeile: scan“ …).</summary>
    public static void Start(string mode)
    {
        Log.Start(Log.DefaultFolder);
        Log.Info($"Start {AppInfo.Name} {AppInfo.VersionWithBuild} – {mode} – {RuntimeInformation.OSDescription} " +
                 $"({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}, Domäne: {(IsDomainJoined() ? "ja" : "nein")}");
        HookUnhandled();
    }

    /// <summary>Ausnahmen, die sonst spurlos verschwänden: im Hintergrund unbeobachtete Aufgaben und Abstürze.</summary>
    private static void HookUnhandled()
    {
        if (_hooked) return;
        _hooked = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error(e.IsTerminating ? "Absturz" : "Unbehandelter Fehler", e.ExceptionObject as Exception, withStack: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unbeobachteter Fehler einer Hintergrundaufgabe", e.Exception, withStack: true);
            e.SetObserved();
        };
    }

    /// <summary>Gehört dieser Rechner zu einer Domäne? (Nur ja/nein, kein Name.)</summary>
    public static bool IsDomainJoined()
    {
        try { return ComputerDomain.Query() is not null; }
        catch (Exception e) when (e is not OutOfMemoryException) { return false; }
    }

    /// <summary>
    /// Angaben für das Diagnosepaket – Version, Build, Domäne ja/nein, Datenbank mit Schema und Zahl der Läufe. Das Paket
    /// ist für den Support und bleibt deutsch; <paramref name="runCount"/> liest der Aufrufer vorher über die Warteschlange
    /// des Speichers (null = nicht lesbar).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Info(AppState? state, int? runCount = null)
    {
        var info = new List<KeyValuePair<string, string>>
        {
            new("Version", AppInfo.Version),
            new("Build", AppInfo.Commit ?? "unbekannt"),
            new("Herausgeber", AppInfo.Publisher),
            new("Ausführung", Environment.ProcessPath is { } exe ? Labels.WithVariables(exe) : "unbekannt"),
            new("Rechner in einer Domäne", IsDomainJoined() ? "ja" : "nein"),
            new("Datenbankschema", ScanStore.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (state is not null)
        {
            info.Add(new("Datenbank", state.DatabasePath.Length > 0 ? Labels.WithVariables(state.DatabasePath) : "keine"));
            info.Add(new("Demo-Modus", state.IsDemo ? "ja" : "nein"));
            if (state.Store is not null)
                info.Add(new("Gespeicherte Läufe", runCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nicht lesbar"));
        }
        return info;
    }
}
