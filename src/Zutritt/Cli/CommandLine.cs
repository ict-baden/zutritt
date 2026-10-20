using Zutritt.Core.Analysis;
using Zutritt.Core.Diagnostics;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Core.Scanning;
using Zutritt.Core.Storage;
using Zutritt.Platform.Windows;

namespace Zutritt.Cli;

/// <summary>
/// Befehlszeile derselben Exe: scan, liste, bericht, --hilfe, --version, --info, --lizenzen. Für geplante Aufgaben
/// zählt der Exitcode (0 = nichts ab „Mittel“, 1 = „Mittel“, 2 = „Hoch“, 3 = Fehler oder Abbruch).
/// </summary>
internal static class CommandLine
{
    public const int ExitClean = 0;
    public const int ExitMedium = 1;
    public const int ExitHigh = 2;
    public const int ExitError = 3;

    private static readonly string[] HelpWords = ["--hilfe", "-hilfe", "hilfe", "--help", "-h", "-?", "/?", "/h"];
    private static readonly string[] VersionWords = ["--version", "-v", "/v"];
    private static readonly string[] InfoWords = ["--info", "/info"];
    private static readonly string[] LicenseWords = ["--lizenzen", "--licenses"];

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
            if (VersionWords.Contains(command, StringComparer.OrdinalIgnoreCase)) return Single(rest, command, PrintVersion);
            if (InfoWords.Contains(command, StringComparer.OrdinalIgnoreCase)) return Single(rest, command, PrintInfo);
            if (LicenseWords.Contains(command, StringComparer.OrdinalIgnoreCase)) return Single(rest, command, PrintLicenses);

            if (command.ToLowerInvariant() is "scan" or "liste" or "bericht" or "--demo-bericht")
                AppDiagnostics.Start("Befehlszeile: " + command.ToLowerInvariant());
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
        catch (PreconditionException ex)
        {
            Console.Error.WriteLine($"Fehler: {ex.Message}");
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
            Log.Error("Befehlszeile abgebrochen", ex, withStack: ex is not (InvalidOperationException or IOException or UnauthorizedAccessException));
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            return ExitError;
        }
    }

    /// <summary>Befehl ohne weitere Angaben (--version, --info, --lizenzen).</summary>
    private static int Single(string[] rest, string command, Action print)
    {
        if (rest.Any(r => r.Trim().Length > 0)) throw new UsageException($"„{command}“ erwartet keine weiteren Angaben.");
        print();
        return ExitClean;
    }

    // ================= --version, --info, --lizenzen =================

    private static void PrintVersion() => Console.WriteLine($"{AppInfo.Name} {AppInfo.VersionWithBuild}");

    /// <summary>Dieselben Angaben wie die Info-Seite (Richtlinie 07), als Text.</summary>
    internal static void PrintInfo()
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.VersionWithBuild} – .NET {Environment.Version.ToString(3)}");
        Console.WriteLine("Zeigt, wer auf Dateifreigaben worauf zugreifen darf – mit Befunden, Vergleich und Bericht.");
        Console.WriteLine();
        Console.WriteLine($"{AppInfo.Copyright} · {AppInfo.LicenseName}");
        Console.WriteLine($"Lizenztext:             {LicenseTexts.LicenseFile} (Zutritt.exe --lizenzen, {AppInfo.LicenseUrl})");
        Console.WriteLine($"Drittanbieter-Lizenzen: {LicenseTexts.NoticesFile} (Zutritt.exe --lizenzen, auch unter Info in der App)");
        Console.WriteLine();
        Console.WriteLine($"Quellcode (GitHub):     {AppInfo.RepositoryUrl}");
        Console.WriteLine($"Hilfe & Support:        {AppInfo.SupportUrl}");
        Console.WriteLine($"Datenschutz:            {AppInfo.PrivacyUrl}");
        Console.WriteLine($"Impressum:              {AppInfo.ImpressumUrl}");
        Console.WriteLine($"Webseite:               {AppInfo.WebsiteUrl}");
        Console.WriteLine();
        Console.WriteLine($"Datenbank:              {Labels.WithVariables(ScanStore.DefaultPath)}");
        Console.WriteLine($"Protokoll:              {Labels.WithVariables(Log.DefaultFolder)}");
        Console.WriteLine();
        Console.WriteLine(AppInfo.NoDataSentence);
    }

    /// <summary>Lizenztext von Zutritt und die der mitgelieferten Bibliotheken (eingebettet, auch in der einzelnen Exe).</summary>
    private static void PrintLicenses()
    {
        var license = LicenseTexts.License();
        var notices = LicenseTexts.ThirdPartyNotices();
        if (license is null && notices is null)
            throw new InvalidOperationException($"Die Lizenztexte fehlen in dieser Ausgabe – sie stehen unter {AppInfo.RepositoryUrl}.");
        if (license is not null)
        {
            Console.WriteLine(license.TrimEnd());
            Console.WriteLine();
        }
        if (notices is not null) Console.WriteLine(notices.TrimEnd());
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

        // Vorher prüfen, was am Ende gebraucht wird: Ein Scan kann Stunden dauern und darf nicht erst danach an der
        // Datenbank oder am Berichtsordner scheitern.
        using var store = OpenStoreForScan(database);
        foreach (var report in reports) CheckReportTarget(report);

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
            return FinishScan(store, scan, a.Has("--vergleich"), reports);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// Öffnet die Datenbank vor dem Scan und prüft, ob sich ein Lauf darin speichern ließe. Ist sie nur gerade
    /// gesperrt, läuft der Scan trotzdem (beim Speichern wartet Zutritt bis zu 30 s); sonst bricht Zutritt ab,
    /// bevor gescannt wird.
    /// </summary>
    internal static ScanStore OpenStoreForScan(string database)
    {
        ScanStore store;
        try
        {
            store = new ScanStore(database);
        }
        catch (ScanStoreException ex)
        {
            throw new PreconditionException($"{ex.Message} Es wurde nicht gescannt. Mit --db <pfad> lässt sich eine andere Datenbank wählen.");
        }
        try
        {
            store.CheckWritable();
        }
        catch (ScanStoreException ex) when (ex.IsLocked)
        {
            Console.Error.WriteLine($"Hinweis: {ex.Message} Der Scan läuft trotzdem; gespeichert wird am Ende.");
        }
        catch (ScanStoreException ex)
        {
            store.Dispose();
            throw new PreconditionException($"{ex.Message} ({database}) Es wurde nicht gescannt. Mit --db <pfad> lässt sich eine andere Datenbank wählen.");
        }
        return store;
    }

    private static void CheckReportTarget(string path)
    {
        try
        {
            ReportTarget.Check(path);
        }
        catch (ReportWriteException ex)
        {
            throw new PreconditionException($"{ex.Message} Es wurde nicht gescannt.");
        }
    }

    /// <summary>
    /// Nach dem Scan: speichern, vergleichen, Berichte schreiben, Zusammenfassung. Scheitert das Speichern, werden die
    /// Berichte trotzdem geschrieben – der Lauf ist dann wenigstens dort festgehalten – und der Exitcode ist 3.
    /// </summary>
    internal static int FinishScan(ScanStore store, ScanResult scan, bool compare, IReadOnlyList<string> reports)
    {
        var problems = new List<string>();
        try
        {
            store.Save(scan);
            Console.WriteLine($"Lauf {scan.Id} gespeichert in {store.DatabasePath}");
        }
        catch (ScanStoreException ex)
        {
            Log.Error("Lauf nicht gespeichert", ex, withStack: false);
            Console.Error.WriteLine($"Fehler: {ex.Message}");
            Console.Error.WriteLine(reports.Count > 0
                ? "Der Lauf ist nicht in der Datenbank – die Berichte werden trotzdem geschrieben."
                : "Der Lauf ist nicht in der Datenbank und geht verloren. Mit --bericht <datei> hält Zutritt ihn auch dann in einem Bericht fest.");
            problems.Add("der Lauf wurde nicht gespeichert");
        }

        ComparisonResult? comparison = null;
        if (compare)
        {
            try
            {
                comparison = CompareWithPrevious(store, scan);
            }
            catch (ScanStoreException ex)
            {
                // Der Vergleich ist eine Zugabe – ohne ihn sind die Berichte immer noch richtig
                Console.Error.WriteLine($"Vergleich nicht möglich: {ex.Message}");
            }
        }
        if (!WriteReports(scan, comparison, reports)) problems.Add("mindestens ein Bericht ließ sich nicht schreiben");
        PrintSummary(scan, comparison);

        int code = problems.Count > 0 ? ExitError : ExitCodeFor(scan);
        Console.WriteLine();
        Console.WriteLine($"Exitcode {code}: {(problems.Count > 0 ? string.Join("; ", problems) : ExitMeaning(code))}");
        return code;
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
            if (comparison.ScopeDiffers) Console.WriteLine($"Achtung: {ScanScope.Describe(comparison)}");
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
              Zutritt.exe --version               Version und Build
              Zutritt.exe --info                  Version, Lizenz, Links zu Hilfe, Datenschutz, Impressum
              Zutritt.exe --lizenzen              Lizenztext und Drittanbieter-Lizenzen

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
            {{AppInfo.NoDataSentence}}
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

    /// <summary>Etwas, das der Scan am Ende braucht (Datenbank, Berichtsordner), ist nicht nutzbar – vor dem Scan gemeldet.</summary>
    internal sealed class PreconditionException(string message) : Exception(message);

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
