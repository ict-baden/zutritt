// SPDX-License-Identifier: MIT
using Zutritt.Cli;
using Zutritt.Core.Localization;

namespace Zutritt;

/// <summary>
/// Einstieg. Mit Befehl (scan, liste, bericht, --hilfe, --version, --info, --lizenzen) läuft Zutritt als
/// Befehlszeilenprogramm ganz ohne WPF, sonst startet die Oberfläche – normal, mit Demo-Daten oder zum Erzeugen der Screenshots.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Sprache vor dem ersten Text: Umgebungsvariable, Einstellung, Vorgabe für den Rechner, sonst wie Windows
        UiLanguage.Apply();
        var options = StartOptions.Parse(args);
        if (options.Mode == AppMode.CommandLine) return CommandLine.Run(args);

        var app = new App(options);
        app.InitializeComponent();
        return app.Run();
    }
}

internal enum AppMode { Normal, Demo, Screenshots, Benchmark, CommandLine }

/// <param name="BenchmarkFolders">Gespeicherte Ordner des Messlaufs (nur <see cref="AppMode.Benchmark"/>).</param>
/// <param name="BenchmarkPeople">Personen des Messlaufs (nur <see cref="AppMode.Benchmark"/>).</param>
internal sealed record StartOptions(AppMode Mode, string ScreenshotFolder = "", string Theme = "beide",
    int BenchmarkFolders = 50_000, int BenchmarkPeople = 3_000)
{
    public static StartOptions Parse(string[] args)
    {
        if (args.Length == 0) return new(AppMode.Normal);
        var first = args[0].Trim();
        if (first.Equals("--demo", StringComparison.OrdinalIgnoreCase) && args.Length == 1) return new(AppMode.Demo);

        // Verstecktes Messen (bewusst nicht in --hilfe): großer Demo-Lauf, Zeiten je Seite, optional Bilder
        // Zutritt.exe --messen [<ordner>] [--ordner N] [--personen N]
        if (first.Equals("--messen", StringComparison.OrdinalIgnoreCase)) return ParseBenchmark(args) ?? new(AppMode.CommandLine);

        if (first.Equals("--screenshots", StringComparison.OrdinalIgnoreCase))
        {
            // Falsche Angaben erklärt die Befehlszeile (sie hat eine Konsole für die Meldung)
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) return new(AppMode.CommandLine);
            var theme = "beide";
            if (args.Length == 4 && args[2].Equals("--theme", StringComparison.OrdinalIgnoreCase)) theme = args[3].Trim().ToLowerInvariant();
            else if (args.Length != 2) return new(AppMode.CommandLine);
            // Englische Schreibweisen wie in der Hilfe auf Englisch
            theme = theme switch { "light" => "hell", "dark" => "dunkel", "both" => "beide", _ => theme };
            if (theme is not ("hell" or "dunkel" or "beide")) return new(AppMode.CommandLine);
            return new(AppMode.Screenshots, Path.GetFullPath(args[1]), theme);
        }
        return new(AppMode.CommandLine);
    }

    private static StartOptions? ParseBenchmark(string[] args)
    {
        string folder = "";
        int folders = 50_000, people = 3_000;
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            bool hasValue = i + 1 < args.Length;
            if (arg.Equals("--ordner", StringComparison.OrdinalIgnoreCase) && hasValue && int.TryParse(args[i + 1], out var f) && f > 0) { folders = f; i++; }
            else if (arg.Equals("--personen", StringComparison.OrdinalIgnoreCase) && hasValue && int.TryParse(args[i + 1], out var p) && p > 0) { people = p; i++; }
            else if (!arg.StartsWith("--", StringComparison.Ordinal) && folder.Length == 0) folder = Path.GetFullPath(arg);
            else return null;
        }
        return new(AppMode.Benchmark, folder, "hell", folders, people);
    }
}
