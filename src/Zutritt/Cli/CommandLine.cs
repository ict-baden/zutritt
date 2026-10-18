using Zutritt.Core.Analysis;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Core.Scanning;
using Zutritt.Core.Storage;
using Zutritt.Platform.Windows;

namespace Zutritt.Cli;

/// <summary>
/// Befehlszeile derselben Exe: scan, liste, bericht, --hilfe. Für geplante Aufgaben zählt der Exitcode
/// (0 = nichts ab „Mittel“, 1 = „Mittel“, 2 = „Hoch“, 3 = Fehler oder Abbruch).
/// </summary>
internal static class CommandLine
{
    public const int ExitClean = 0;
    public const int ExitMedium = 1;
    public const int ExitHigh = 2;
    public const int ExitError = 3;

    private static readonly string[] HelpWords = ["--hilfe", "-hilfe", "hilfe", "--help", "-h", "-?", "/?", "/h"];

    public static int Run(string[] args)
    {
        ConsoleSession.Attach();
        try
        {
            var command = args.Length == 0 ? "--hilfe" : args[0].Trim();
            var rest = args.Skip(1).ToArray();
            if (HelpWords.Contains(command, StringComparer.OrdinalIgnoreCase))
            {
                PrintHelp();
                return ExitClean;
            }
            return command.ToLowerInvariant() switch
            {
                "scan" => Scan(Arguments.Parse(rest,
                    flags: ["--dateien", "--ohne-gruppen", "--mit-admin-freigaben", "--vergleich"],
                    values: ["--tiefe", "--ausnahme-ab", "--bericht", "--db"])),
                "liste" => List(Arguments.Parse(rest, flags: [], values: ["--db"])),
                "bericht" => Report(Arguments.Parse(rest, flags: ["--vergleich"], values: ["--bericht", "--db"])),
                "--screenshots" => throw new UsageException("Aufruf: Zutritt.exe --screenshots <ordner> [--theme hell|dunkel|beide]"),
                "--demo" => throw new UsageException("„--demo“ steht allein: Zutritt.exe --demo"),
                // Versteckt (nicht in --hilfe): Beispielberichte aus den Demo-Daten für Doku und Vorführung
                "--demo-bericht" => DemoReport(Arguments.Parse(rest, flags: ["--vergleich"], values: ["--bericht"])),
                _ => throw new UsageException($"Unbekannter Befehl „{command}“."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            Console.Error.WriteLine("Hilfe: Zutritt.exe --hilfe");
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Abgebrochen.");
            return ExitError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            return ExitError;
        }
    }

    // ================= scan =================

    private static int Scan(Arguments a)
    {
        if (a.Positional.Count == 0)
            throw new UsageException(@"Bitte mindestens ein Ziel angeben, z. B. Zutritt.exe scan \\server\freigabe");
        var reports = a.All("--bericht").Select(CheckReportPath).ToList();
        var request = new ScanRequest
        {
            Targets = a.Positional,
            IncludeFiles = a.Has("--dateien"),
            MaxDepth = a.Int("--tiefe", min: 0),
            SkipAdminShares = !a.Has("--mit-admin-freigaben"),
            ResolveGroups = !a.Has("--ohne-gruppen"),
            DeepInheritanceBreakDepth = a.Int("--ausnahme-ab", min: 1) ?? 3,
        };
        var database = DatabasePath(a);

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // Strg+C: sauber abbrechen (Exitcode 3) statt den Prozess hart zu beenden
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            Console.WriteLine($"Zutritt {AppInfo.Version} – Scan von {string.Join(", ", request.Targets.Select(Labels.TargetName))}");
            var progress = new ConsoleProgress();
            var scanner = WindowsPlatform.CreateScanner();
            var scan = scanner.RunAsync(request, progress, cts.Token).GetAwaiter().GetResult();
            progress.Finish();

            using var store = new ScanStore(database);
            store.Save(scan);
            Console.WriteLine($"Lauf {scan.Id} gespeichert in {database}");

            var comparison = a.Has("--vergleich") ? CompareWithPrevious(store, scan) : null;
            bool reportFailed = !WriteReports(scan, comparison, reports);
            PrintSummary(scan, comparison);

            int code = reportFailed ? ExitError : ExitCodeFor(scan);
            Console.WriteLine();
            Console.WriteLine($"Exitcode {code}: {ExitMeaning(code)}");
            return code;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    // ================= liste =================

    private static int List(Arguments a)
    {
        if (a.Positional.Count > 0) throw new UsageException($"„liste“ erwartet keine weiteren Angaben („{a.Positional[0]}“).");
        var database = DatabasePath(a);
        if (!File.Exists(database))
        {
            Console.WriteLine($"Noch keine Datenbank vorhanden ({database}).");
            return ExitClean;
        }
        using var store = new ScanStore(database);
        var runs = store.List();
        if (runs.Count == 0)
        {
            Console.WriteLine($"Keine gespeicherten Läufe in {database}.");
            return ExitClean;
        }

        Console.WriteLine($"Gespeicherte Läufe in {database}:");
        Console.WriteLine();
        Console.WriteLine($"{"Id",6}  {"Datum",-18}  {"Ordner",7}  {"Befunde",7}  {"Hoch",5}  Ziele");
        foreach (var r in runs)
            Console.WriteLine($"{r.Id,6}  {Labels.Date(r.StartedAt),-18}  {Labels.Number(r.FoldersVisited),7}  {Labels.Number(r.Findings),7}  {Labels.Number(r.HighFindings),5}  {r.Targets}");
        return ExitClean;
    }

    // ================= bericht =================

    private static int Report(Arguments a)
    {
        if (a.Positional.Count != 1 || !long.TryParse(a.Positional[0], out var id))
            throw new UsageException("Aufruf: Zutritt.exe bericht <id> --bericht <datei.xlsx|datei.pdf> [--vergleich] [--db <pfad>] – die Id zeigt „Zutritt.exe liste“.");
        var reports = a.All("--bericht").Select(CheckReportPath).ToList();
        if (reports.Count == 0) throw new UsageException("Bitte mit --bericht <datei.xlsx|datei.pdf> angeben, wohin der Bericht soll.");

        var database = DatabasePath(a);
        if (!File.Exists(database)) throw new InvalidOperationException($"Die Datenbank {database} gibt es nicht.");
        using var store = new ScanStore(database);
        var scan = store.Load(id) ?? throw new InvalidOperationException($"Lauf {id} gibt es nicht. „Zutritt.exe liste“ zeigt die gespeicherten Läufe.");
        var comparison = a.Has("--vergleich") ? CompareWithPrevious(store, scan) : null;
        return WriteReports(scan, comparison, reports) ? ExitClean : ExitError;
    }

    // ================= Demo-Bericht =================

    /// <summary>Zutritt.exe --demo-bericht --bericht demo.pdf [--vergleich] – Berichte aus den eingebauten Demo-Daten.</summary>
    private static int DemoReport(Arguments a)
    {
        var reports = a.All("--bericht").Select(CheckReportPath).ToList();
        if (reports.Count == 0) throw new UsageException("Bitte mit --bericht <datei.xlsx|datei.pdf> angeben, wohin der Bericht soll.");
        var (older, newer) = Demo.DemoData.Create();
        var comparison = a.Has("--vergleich") ? ScanComparer.Compare(older, newer) : null;
        return WriteReports(newer, comparison, reports) ? ExitClean : ExitError;
    }

    // ================= Bausteine =================

    private static string DatabasePath(Arguments a) =>
        a.Value("--db") is { } db ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(db)) : ScanStore.DefaultPath;

    private static string CheckReportPath(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".xlsx" or ".pdf"))
            throw new UsageException($"Bericht „{path}“: Bitte .xlsx (Excel) oder .pdf angeben.");
        return full;
    }

    private static ComparisonResult? CompareWithPrevious(ScanStore store, ScanResult scan)
    {
        var previous = store.FindPrevious(scan);
        var older = previous is null ? null : store.Load(previous.Id);
        if (older is null)
        {
            Console.WriteLine("Vergleich: Es gibt noch keinen früheren Lauf mit denselben Zielen.");
            return null;
        }
        return ScanComparer.Compare(older, scan);
    }

    /// <summary>Schreibt alle Berichte; ein fehlgeschlagener hält die anderen nicht auf. false = mindestens einer fehlgeschlagen.</summary>
    private static bool WriteReports(ScanResult scan, ComparisonResult? comparison, IReadOnlyList<string> paths)
    {
        bool ok = true;
        foreach (var path in paths)
        {
            try
            {
                if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
                if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) PdfReport.Write(scan, comparison, path);
                else ExcelReport.Write(scan, comparison, path);
                Console.WriteLine($"Bericht geschrieben: {path}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Bericht {path} konnte nicht geschrieben werden: {ex.Message}");
                ok = false;
            }
        }
        return ok;
    }

    private static void PrintSummary(ScanResult scan, ComparisonResult? comparison)
    {
        int Count(Severity s) => scan.Findings.Count(f => f.Severity == s);
        int unreadable = scan.Folders.Count(f => f.Error is not null);

        Console.WriteLine();
        Console.WriteLine($"  Ziele            {string.Join(", ", scan.Request.Targets.Select(Labels.TargetName))}");
        Console.WriteLine($"  Dauer            {Labels.Duration(scan.FinishedAt - scan.StartedAt)}");
        Console.WriteLine($"  Ordner geprüft   {Labels.Number(scan.FoldersVisited)}, davon {Labels.Number(scan.Folders.Count)} mit eigenen Rechten");
        if (scan.FilesVisited > 0) Console.WriteLine($"  Dateien geprüft  {Labels.Number(scan.FilesVisited)}");
        Console.WriteLine($"  Nicht lesbar     {Labels.Number(unreadable)}");
        Console.WriteLine($"  Befunde          Hoch {Count(Severity.Hoch)} · Mittel {Count(Severity.Mittel)} · Niedrig {Count(Severity.Niedrig)} · Info {Count(Severity.Info)}");

        var important = scan.Findings.Where(f => f.Severity >= Severity.Mittel)
            .OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (important.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Wichtigste Befunde:");
            foreach (var f in important.Take(15))
                Console.WriteLine($"  {Labels.Of(f.Severity),-7} {f.RuleId,-5} {f.Path}");
            if (important.Count > 15) Console.WriteLine($"  … und {important.Count - 15} weitere (alle im Bericht)");
        }

        if (comparison is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"Seit dem Lauf vom {Labels.Date(comparison.Older.StartedAt)}: {comparison.NewFindings.Count} neue, " +
                $"{comparison.ResolvedFindings.Count} behobene Befunde, {comparison.AccessChanges.Count} Zugriffsänderungen, " +
                $"{comparison.AceChanges.Count} ACL-Änderungen, {comparison.MembershipChanges.Count} Mitgliedschaftsänderungen.");
        }
    }

    internal static int ExitCodeFor(ScanResult scan)
    {
        if (scan.Findings.Count == 0) return ExitClean;
        return scan.Findings.Max(f => f.Severity) switch
        {
            Severity.Hoch => ExitHigh,
            Severity.Mittel => ExitMedium,
            _ => ExitClean,
        };
    }

    private static string ExitMeaning(int code) => code switch
    {
        ExitError => "mindestens ein Bericht ließ sich nicht schreiben",
        ExitHigh => "mindestens ein Befund „Hoch“",
        ExitMedium => "Befunde „Mittel“, keine „Hoch“",
        _ => "keine Befunde ab „Mittel“",
    };

    private static void PrintHelp()
    {
        Console.WriteLine($$"""
            Zutritt {{AppInfo.Version}} – wer darf auf Dateifreigaben wohin?

            Aufruf:
              Zutritt.exe                         Oberfläche starten
              Zutritt.exe scan <ziel> [<ziel> …] [Optionen]
              Zutritt.exe liste [--db <pfad>]
              Zutritt.exe bericht <id> --bericht <datei> [--vergleich] [--db <pfad>]
              Zutritt.exe --demo                  Oberfläche mit eingebauten Demo-Daten
              Zutritt.exe --screenshots <ordner> [--theme hell|dunkel|beide]
              Zutritt.exe --hilfe

            Ziele:
              \\server\freigabe[\ordner]    eine Freigabe oder ein Ordner darin
              \\server                      alle Datenträger-Freigaben eines Servers
              D:\Daten                      ein lokaler Ordner
              .                             alle Freigaben dieses Rechners

            Optionen für „scan“:
              --dateien                auch Dateien mit eigenen Rechten erfassen (dauert länger)
              --tiefe N                höchstens N Ebenen unter dem Ziel (Standard: unbegrenzt)
              --ausnahme-ab N          unterbrochene Vererbung ab Ebene N als „tief versteckte
                                       Ausnahme“ melden (Standard: 3)
              --ohne-gruppen           Gruppen nicht bis zur Person auflösen
              --mit-admin-freigaben    auch C$, ADMIN$ usw. prüfen
              --bericht <datei>        Bericht schreiben, .xlsx oder .pdf (mehrfach möglich)
              --vergleich              Änderungen seit dem letzten Lauf mit denselben Zielen
                                       ermitteln und in die Berichte aufnehmen
              --db <pfad>              andere Datenbank (Standard: %LocalAppData%\Zutritt\zutritt.db,
                                       also pro Benutzer)

            „liste“ zeigt die gespeicherten Läufe mit ihrer Id, „bericht“ schreibt für einen davon
            nachträglich Berichte.

            Exitcodes (für geplante Aufgaben):
              0  keine Befunde ab „Mittel“
              1  Befunde „Mittel“, aber keine „Hoch“
              2  mindestens ein Befund „Hoch“
              3  Fehler oder Abbruch

            Beispiel – jeden Montag um 6 Uhr mit Vergleich und PDF zum Abzeichnen:
              schtasks /create /tn "Zutritt Wochenbericht" /sc weekly /d MO /st 06:00 /tr ^
                "\"C:\Program Files\Zutritt\Zutritt.exe\" scan \\FS01 --vergleich --bericht D:\Berichte\Zutritt.pdf"

            Zutritt.exe ist ein Fensterprogramm; damit die Eingabeaufforderung auf das Ende wartet:
              start /wait Zutritt.exe scan …            (cmd)
              Zutritt.exe scan … | Out-Default          (PowerShell)
            Protokoll für geplante Aufgaben: in einer .cmd-Datei umleiten – dort wartet cmd auf das
            Ende, und die Datei wird UTF-8 (mit BOM):
              "C:\Program Files\Zutritt\Zutritt.exe" scan \\FS01 --vergleich > D:\Berichte\zutritt.log 2>&1
            Es wird nur gelesen – an Rechten und Dateien ändert Zutritt nichts.
            """);
    }

    /// <summary>Fortschritt in einer Zeile, die sich überschreibt; bei Umleitung nur die Phasen.</summary>
    private sealed class ConsoleProgress : IProgress<ScanProgress>
    {
        private readonly Lock _gate = new();
        private DateTime _last = DateTime.MinValue;
        private string _phase = "";
        private int _width;

        public void Report(ScanProgress p)
        {
            lock (_gate)
            {
                if (!ConsoleSession.IsInteractive)
                {
                    if (p.Phase == _phase) return;
                    _phase = p.Phase;
                    Console.WriteLine($"{p.Phase} …");
                    return;
                }
                var now = DateTime.UtcNow;
                if (p.Phase == _phase && now - _last < TimeSpan.FromMilliseconds(200)) return;
                _phase = p.Phase;
                _last = now;
                var line = $"{p.Phase}: {Labels.Number(p.FoldersVisited)} Ordner, {Labels.Number(p.FoldersStored)} mit eigenen Rechten, {p.Errors} Fehler";
                int max = Math.Max(40, ConsoleWidth() - 1);
                if (p.CurrentPath.Length > 0 && line.Length + 3 < max)
                    line += " – " + Labels.ShortenPath(p.CurrentPath, max - line.Length - 3);
                if (line.Length > max) line = line[..max];
                Console.Write("\r" + line.PadRight(Math.Max(_width, line.Length)));
                _width = line.Length;
            }
        }

        public void Finish()
        {
            lock (_gate)
            {
                if (ConsoleSession.IsInteractive && _width > 0) Console.WriteLine();
                _width = 0;
            }
        }

        private static int ConsoleWidth()
        {
            try { return Console.WindowWidth; } catch { return 100; }
        }
    }

    private sealed class UsageException(string message) : Exception(message);

    /// <summary>Einfache Zerlegung: Optionen mit „--“, alles andere sind Ziele bzw. Ids.</summary>
    private sealed class Arguments
    {
        private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Positional { get; } = [];

        public static Arguments Parse(string[] args, string[] flags, string[] values)
        {
            var a = new Arguments();
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].Trim();
                if (arg.Length == 0) continue;
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    a.Positional.Add(arg);
                    continue;
                }
                if (values.Contains(arg, StringComparer.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        throw new UsageException($"„{arg}“ braucht einen Wert.");
                    if (!a._values.TryGetValue(arg, out var list)) a._values[arg] = list = [];
                    list.Add(args[++i].Trim());
                }
                else if (flags.Contains(arg, StringComparer.OrdinalIgnoreCase)) a._flags.Add(arg);
                else throw new UsageException($"Unbekannte Option „{arg}“.");
            }
            return a;
        }

        public bool Has(string flag) => _flags.Contains(flag);
        public string? Value(string name) => _values.TryGetValue(name, out var l) ? l[^1] : null;
        public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var l) ? l : [];

        public int? Int(string name, int min)
        {
            if (Value(name) is not { } text) return null;
            if (!int.TryParse(text, out var n) || n < min)
                throw new UsageException($"„{name}“ erwartet eine ganze Zahl ab {min}, nicht „{text}“.");
            return n;
        }
    }
}
