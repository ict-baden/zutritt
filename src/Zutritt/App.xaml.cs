using System.Windows;
using System.Windows.Threading;
using Zutritt.Cli;
using Zutritt.UI;
using Zutritt.UI.Theme;

namespace Zutritt;

public partial class App : Application
{
    private readonly StartOptions _options;
    private AppState? _state;

    internal App(StartOptions options) => _options = options;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        if (_options.Mode is AppMode.Screenshots or AppMode.Benchmark)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ConsoleSession.Attach();
            Dispatcher.InvokeAsync(RunOffscreenAsync);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        ThemeManager.Initialize();
        _state = _options.Mode == AppMode.Demo ? AppState.OpenDemo(strict: false) : AppState.Open();
        var window = new MainWindow(_state);
        MainWindow = window;
        window.Show();
    }

    private async Task RunOffscreenAsync()
    {
        int code;
        try
        {
            code = _options.Mode == AppMode.Benchmark
                ? await Benchmark.RunAsync(_options.ScreenshotFolder, _options.BenchmarkFolders, _options.BenchmarkPeople)
                : await Screenshots.RunAsync(_options.ScreenshotFolder, _options.Theme);
        }
        catch (Exception ex)
        {
            // Bewusst nicht verschluckt: Fehler (auch fehlende Core-Bausteine) sichtbar melden und mit 3 enden
            Console.Error.WriteLine($"{(_options.Mode == AppMode.Benchmark ? "Messung" : "Screenshots")} fehlgeschlagen: {ex}");
            code = CommandLine.ExitError;
        }
        Shutdown(code);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Niemals wegen eines Fehlers abstürzen – stattdessen freundlich melden.
        e.Handled = true;
        if (_options.Mode is AppMode.Screenshots or AppMode.Benchmark)
        {
            Console.Error.WriteLine($"{(_options.Mode == AppMode.Benchmark ? "Messung" : "Screenshots")} fehlgeschlagen: {e.Exception}");
            Shutdown(CommandLine.ExitError);
            return;
        }
        _state?.Report(NoticeKind.Error, $"Unerwarteter Fehler: {e.Exception.Message}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Dispose();
        base.OnExit(e);
    }
}
