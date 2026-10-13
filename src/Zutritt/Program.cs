using Zutritt.Cli;

namespace Zutritt;

/// <summary>
/// Einstieg. Mit Befehl (scan, liste, bericht, --hilfe) läuft Zutritt als Befehlszeilenprogramm ganz ohne WPF,
/// sonst startet die Oberfläche – normal, mit Demo-Daten oder zum Erzeugen der Screenshots.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartOptions.Parse(args);
        if (options.Mode == AppMode.CommandLine) return CommandLine.Run(args);

        var app = new App(options);
        app.InitializeComponent();
        return app.Run();
    }
}

internal enum AppMode { Normal, Demo, Screenshots, CommandLine }

internal sealed record StartOptions(AppMode Mode, string ScreenshotFolder = "", string Theme = "beide")
{
    public static StartOptions Parse(string[] args)
    {
        if (args.Length == 0) return new(AppMode.Normal);
        var first = args[0].Trim();
        if (first.Equals("--demo", StringComparison.OrdinalIgnoreCase) && args.Length == 1) return new(AppMode.Demo);

        if (first.Equals("--screenshots", StringComparison.OrdinalIgnoreCase))
        {
            // Falsche Angaben erklärt die Befehlszeile (sie hat eine Konsole für die Meldung)
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) return new(AppMode.CommandLine);
            var theme = "beide";
            if (args.Length == 4 && args[2].Equals("--theme", StringComparison.OrdinalIgnoreCase)) theme = args[3].Trim().ToLowerInvariant();
            else if (args.Length != 2) return new(AppMode.CommandLine);
            if (theme is not ("hell" or "dunkel" or "beide")) return new(AppMode.CommandLine);
            return new(AppMode.Screenshots, Path.GetFullPath(args[1]), theme);
        }
        return new(AppMode.CommandLine);
    }
}
