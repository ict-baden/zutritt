// SPDX-License-Identifier: MIT
using Zutritt.Core;
using Zutritt.Core.Analysis;
using Zutritt.Core.Diagnostics;
using Zutritt.Core.Localization;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Core.Resources;
using Zutritt.Core.Storage;
using Zutritt.Platform.Windows;

namespace Zutritt.Cli;

/// <summary>
/// Befehlszeile derselben Exe: scan, liste, bericht, --hilfe, --version, --info, --lizenzen – jeweils auch mit englischer
/// Schreibweise (list, report, --help, --files …). Für geplante Aufgaben zählt der Exitcode (0 = nichts ab „Mittel“,
/// 1 = „Mittel“, 2 = „Hoch“, 3 = Fehler oder Abbruch, auch wenn kein Ziel geprüft werden konnte). Fehler zeigen
/// zusätzlich einen Code ZUT-xxxx (Richtlinie 13, Regel 9).
/// </summary>
internal static class CommandLine
{
    public const int ExitClean = 0;
    public const int ExitMedium = 1;
    public const int ExitHigh = 2;
    public const int ExitError = 3;

    private static readonly string[] HelpWords = ["--hilfe", "-hilfe", "hilfe", "-h", "-?", "/?", "/h"];
    private static readonly string[] VersionWords = ["--version", "-v", "/v"];
    private static readonly string[] InfoWords = ["--info", "/info"];
    private static readonly string[] LicenseWords = ["--lizenzen"];

    /// <summary>
    /// Englische Schreibweisen, gleichwertig zu den deutschen (Skripte und geplante Aufgaben in beiden Sprachen).
    /// Intern wird mit den deutschen Namen gearbeitet.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["help"] = "hilfe",
        ["--help"] = "--hilfe",
        ["--licenses"] = "--lizenzen",
        ["list"] = "liste",
        ["report"] = "bericht",
        ["--demo-report"] = "--demo-bericht",
        ["--files"] = "--dateien",
        ["--depth"] = "--tiefe",
        ["--deep-from"] = "--ausnahme-ab",
        ["--no-groups"] = "--ohne-gruppen",
        ["--include-admin-shares"] = "--mit-admin-freigaben",
        ["--report"] = "--bericht",
        ["--compare"] = "--vergleich",
    };

    internal static string Canonical(string word) => Aliases.TryGetValue(word, out var german) ? german : word;

    public static int Run(string[] args)
    {
        ConsoleSession.Attach();
        try
        {
            var typed = args.Length == 0 ? "--hilfe" : args[0].Trim();
            var command = Canonical(typed);
            var rest = args.Skip(1).ToArray();
            if (HelpWords.Contains(command, StringComparer.OrdinalIgnoreCase) || command.Equals("--hilfe", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                return ExitClean;
            }
            if (VersionWords.Contains(command, StringComparer.OrdinalIgnoreCase)) return Single(rest, typed, PrintVersion);
            if (InfoWords.Contains(command, StringComparer.OrdinalIgnoreCase)) return Single(rest, typed, PrintInfo);
            if (LicenseWords.Contains(command, StringComparer.OrdinalIgnoreCase)) return Single(rest, typed, PrintLicenses);

            if (command.ToLowerInvariant() is "scan" or "liste" or "bericht" or "--demo-bericht")
                AppDiagnostics.Start("Befehlszeile: " + command.ToLowerInvariant());
            return command.ToLowerInvariant() switch
            {
                "scan" => Scan(Arguments.Parse(rest,
                    flags: ["--dateien", "--ohne-gruppen", "--mit-admin-freigaben", "--vergleich"],
                    values: ["--tiefe", "--ausnahme-ab", "--bericht", "--db"])),
                "liste" => List(Arguments.Parse(rest, flags: [], values: ["--db"])),
                "bericht" => Report(Arguments.Parse(rest, flags: ["--vergleich"], values: ["--bericht", "--db"])),
                "--screenshots" => throw new UsageException(Strings.Cli_UsageScreenshots),
                "--demo" => throw new UsageException(Strings.Cli_DemoAlone),
                // Versteckt (nicht in --hilfe): Beispielberichte aus den Demo-Daten für Doku und Vorführung
                "--demo-bericht" => DemoReport(Arguments.Parse(rest, flags: ["--vergleich"], values: ["--bericht"])),
                _ => throw new UsageException(Texts.Format(Strings.Cli_UnknownCommand, typed)),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ErrorLine(ex.Message, ErrorCodes.Usage));
            Console.Error.WriteLine(Strings.Cli_HelpHint);
            return ExitError;
        }
        catch (PreconditionException ex)
        {
            Console.Error.WriteLine(ErrorLine(ex.Message, ex.ErrorCode));
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(Strings.Cli_Cancelled);
            return ExitError;
        }
        catch (Exception ex)
        {
            Log.Error("Befehlszeile abgebrochen", ex, withStack: ex is not (InvalidOperationException or IOException or UnauthorizedAccessException));
            Console.Error.WriteLine();
            Console.Error.WriteLine(ErrorLine(ex.Message, ErrorCodes.Of(ex)));
            return ExitError;
        }
    }

    /// <summary>„Fehler ZUT-0203: …“ in der Sprache der Ausgabe.</summary>
    internal static string ErrorLine(string message, string code) => Texts.Format(Strings.Cli_ErrorLine, message, code);

    /// <summary>Befehl ohne weitere Angaben (--version, --info, --lizenzen).</summary>
    private static int Single(string[] rest, string command, Action print)
    {
        if (rest.Any(r => r.Trim().Length > 0)) throw new UsageException(Texts.Format(Strings.Cli_NoArguments, command));
        print();
        return ExitClean;
    }

    // ================= --version, --info, --lizenzen =================

    private static void PrintVersion() => Console.WriteLine($"{AppInfo.Name} {AppInfo.VersionWithBuild}");

    /// <summary>Dieselben Angaben wie die Info-Seite (Richtlinie 07), als Text.</summary>
    internal static void PrintInfo()
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.VersionWithBuild} – .NET {Environment.Version.ToString(3)}");
        Console.WriteLine(Strings.App_Tagline);
        Console.WriteLine();
        (string Label, string Value)[] lines =
        [
            (Strings.Cli_InfoLicense, Texts.Format(Strings.Cli_InfoLicenseValue, LicenseTexts.LicenseFile, AppInfo.LicenseUrl)),
            (Strings.Cli_InfoNotices, Texts.Format(Strings.Cli_InfoNoticesValue, LicenseTexts.NoticesFile)),
            ("", ""),
            (Strings.Cli_InfoSource, AppInfo.RepositoryUrl),
            (Strings.Cli_InfoSupport, AppInfo.SupportUrl),
            (Strings.Cli_InfoPrivacy, AppInfo.PrivacyUrl),
            (Strings.Cli_InfoImprint, AppInfo.ImpressumUrl),
            (Strings.Cli_InfoWebsite, AppInfo.WebsiteUrl),
            ("", ""),
            (Strings.Cli_InfoDatabase, Labels.WithVariables(ScanStore.DefaultPath)),
            (Strings.Cli_InfoLog, Labels.WithVariables(Log.DefaultFolder)),
            (Strings.Cli_InfoLanguage, Texts.Format(Strings.Cli_InfoLanguageValue,
                UiLanguage.NativeName(System.Globalization.CultureInfo.CurrentUICulture.Name), UiLanguage.EnvironmentVariable)),
        ];
        Console.WriteLine($"{AppInfo.Copyright} · {AppInfo.LicenseName}");
        PrintAligned(lines);
        Console.WriteLine();
        Console.WriteLine(AppInfo.NoDataSentence);
    }

    /// <summary>„Bezeichnung:  Wert“, die Werte untereinander – die Bezeichnungen sind je Sprache verschieden lang.</summary>
    private static void PrintAligned(IReadOnlyList<(string Label, string Value)> lines, string indent = "")
    {
        int width = lines.Max(l => l.Label.Length) + 2;
        foreach (var (label, value) in lines)
            Console.WriteLine(label.Length == 0 ? "" : $"{indent}{(label + ":").PadRight(width)}{value}");
    }

    /// <summary>Lizenztext von Zutritt und die der mitgelieferten Bibliotheken (eingebettet, auch in der einzelnen Exe).</summary>
    private static void PrintLicenses()
    {
        var license = LicenseTexts.License();
        var notices = LicenseTexts.ThirdPartyNotices();
        if (license is null && notices is null)
            throw new InvalidOperationException(Texts.Format(Strings.Cli_LicensesMissing, AppInfo.RepositoryUrl));
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
            throw new UsageException(Strings.Cli_NeedTarget);
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
            Console.WriteLine(Texts.Format(Strings.Cli_ScanStart, AppInfo.Version, string.Join(", ", request.Targets.Select(Labels.TargetName))));
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
            throw new PreconditionException(Texts.Format(Strings.Cli_NotScannedDb, ex.Message), ex.ErrorCode);
        }
        try
        {
            store.CheckWritable();
        }
        catch (ScanStoreException ex) when (ex.IsLocked)
        {
            Console.Error.WriteLine(Texts.Format(Strings.Cli_LockedHint, ex.Message));
        }
        catch (ScanStoreException ex)
        {
            store.Dispose();
            throw new PreconditionException(Texts.Format(Strings.Cli_NotScannedDbPath, ex.Message, database), ex.ErrorCode);
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
            throw new PreconditionException(Texts.Format(Strings.Cli_NotScanned, ex.Message), ex.ErrorCode);
        }
    }

    /// <summary>
    /// Nach dem Scan: speichern, vergleichen, Berichte schreiben, Zusammenfassung. Scheitert das Speichern, werden die
    /// Berichte trotzdem geschrieben – der Lauf ist dann wenigstens dort festgehalten – und der Exitcode ist 3. Ebenso 3,
    /// wenn kein einziges Ziel geprüft werden konnte (Server aus, Freigabe nicht lesbar): Ein solcher Lauf darf in der
    /// Aufgabenplanung nicht wie ein sauberer aussehen.
    /// </summary>
    internal static int FinishScan(ScanStore store, ScanResult scan, bool compare, IReadOnlyList<string> reports)
    {
        var problems = new List<string>();
        try
        {
            store.Save(scan);
            Console.WriteLine(Texts.Format(Strings.Cli_RunSaved, scan.Id, store.DatabasePath));
        }
        catch (ScanStoreException ex)
        {
            Log.Error("Lauf nicht gespeichert", ex, withStack: false);
            Console.Error.WriteLine(ErrorLine(ex.Message, ex.ErrorCode));
            Console.Error.WriteLine(reports.Count > 0 ? Strings.Cli_NotSavedReportsFollow : Strings.Cli_NotSavedLost);
            problems.Add(Strings.Cli_ProblemNotSaved);
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
                Console.Error.WriteLine(Texts.Format(Strings.Cli_CompareFailed, ErrorCodes.Describe(ex)));
            }
        }
        if (!WriteReports(scan, comparison, reports)) problems.Add(Strings.Cli_ProblemReport);
        var (unchecked_, targets) = ScanCoverage.Count(scan);
        if (targets == 0 || unchecked_ == targets) problems.Add(Strings.Cli_ProblemNothingChecked);
        PrintSummary(scan, comparison, unchecked_, targets);

        int code = problems.Count > 0 ? ExitError : ExitCodeFor(scan);
        Console.WriteLine();
        Console.WriteLine(Texts.Format(Strings.Cli_ExitCode, code, problems.Count > 0 ? string.Join("; ", problems) : ExitMeaning(code)));
        return code;
    }

    // ================= liste =================

    private static int List(Arguments a)
    {
        if (a.Positional.Count > 0) throw new UsageException(Texts.Format(Strings.Cli_ListNoArgs, a.Positional[0]));
        var database = DatabasePath(a);
        if (!File.Exists(database))
        {
            Console.WriteLine(Texts.Format(Strings.Cli_NoDatabaseYet, database));
            return ExitClean;
        }
        using var store = new ScanStore(database);
        var runs = store.List();
        if (runs.Count == 0)
        {
            Console.WriteLine(Texts.Format(Strings.Cli_NoRuns, database));
            return ExitClean;
        }

        Console.WriteLine(Texts.Format(Strings.Cli_RunsIn, database));
        Console.WriteLine();
        Console.WriteLine($"{"Id",6}  {Strings.Pdf_Date,-18}  {Strings.Col_Folders,7}  {Strings.Col_Findings,7}  {Strings.Severity_High,5}  {Strings.Excel_Targets}");
        foreach (var r in runs)
            Console.WriteLine($"{r.Id,6}  {Labels.Date(r.StartedAt),-18}  {Labels.Number(r.FoldersVisited),7}  {Labels.Number(r.Findings),7}  {Labels.Number(r.HighFindings),5}  {r.Targets}");
        return ExitClean;
    }

    // ================= bericht =================

    private static int Report(Arguments a)
    {
        if (a.Positional.Count != 1 || !long.TryParse(a.Positional[0], out var id))
            throw new UsageException(Strings.Cli_UsageReport);
        var reports = a.All("--bericht").Select(CheckReportPath).ToList();
        if (reports.Count == 0) throw new UsageException(Strings.Cli_NeedReport);

        var database = DatabasePath(a);
        if (!File.Exists(database)) throw new ScanStoreException(Texts.Format(Strings.Cli_NoSuchDatabase, database), ErrorCodes.DatabaseOpen);
        using var store = new ScanStore(database);
        var scan = store.Load(id) ?? throw new ScanStoreException(Texts.Format(Strings.Cli_NoSuchRun, id), ErrorCodes.DatabaseRead);
        var comparison = a.Has("--vergleich") ? CompareWithPrevious(store, scan) : null;
        return WriteReports(scan, comparison, reports) ? ExitClean : ExitError;
    }

    // ================= Demo-Bericht =================

    /// <summary>Zutritt.exe --demo-bericht --bericht demo.pdf [--vergleich] – Berichte aus den eingebauten Demo-Daten.</summary>
    private static int DemoReport(Arguments a)
    {
        var reports = a.All("--bericht").Select(CheckReportPath).ToList();
        if (reports.Count == 0) throw new UsageException(Strings.Cli_NeedReport);
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
            throw new UsageException(Texts.Format(Strings.Cli_ReportExtension, path));
        return full;
    }

    private static ComparisonResult? CompareWithPrevious(ScanStore store, ScanResult scan)
    {
        var previous = store.FindPrevious(scan);
        var older = previous is null ? null : store.Load(previous.Id);
        if (older is null)
        {
            Console.WriteLine(Strings.Cli_NoPreviousRun);
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
                Console.WriteLine(Texts.Format(Strings.Cli_ReportWritten, path));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Texts.Format(Strings.Cli_ReportFailed, path, ErrorCodes.Describe(ex, ErrorCodes.ReportWrite)));
                ok = false;
            }
        }
        return ok;
    }

    private static void PrintSummary(ScanResult scan, ComparisonResult? comparison, int uncheckedTargets, int targets)
    {
        string Count(Severity s) => $"{Labels.Of(s)} {scan.Findings.Count(f => f.Severity == s)}";
        int unreadable = scan.Folders.Count(f => f.Error is not null);

        Console.WriteLine();
        var lines = new List<(string, string)>
        {
            (Strings.Excel_Targets, string.Join(", ", scan.Request.Targets.Select(Labels.TargetName))),
            (Strings.Cli_SumDuration, Labels.Duration(scan.FinishedAt - scan.StartedAt)),
            (Strings.Pdf_FoldersChecked, Texts.Format(Strings.Cli_SumFoldersValue, Labels.Number(scan.FoldersVisited), Labels.Number(scan.Folders.Count))),
        };
        if (scan.FilesVisited > 0) lines.Add((Strings.Pdf_FilesChecked, Labels.Number(scan.FilesVisited)));
        lines.Add((Strings.Cli_SumUnreadable, Labels.Number(unreadable)));
        lines.Add((Strings.Col_Findings, string.Join(" · ", new[] { Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info }.Select(Count))));
        PrintAligned(lines, indent: "  ");
        if (uncheckedTargets > 0)
            Console.WriteLine(Texts.Format(Strings.Cli_UnreachableTargets, uncheckedTargets, targets));

        var important = scan.Findings.Where(f => f.Severity >= Severity.Mittel)
            .OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (important.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(Strings.Cli_ImportantFindings);
            foreach (var f in important.Take(15))
                Console.WriteLine($"  {Labels.Of(f.Severity),-7} {f.RuleId,-5} {f.Path}");
            if (important.Count > 15) Console.WriteLine(Texts.Format(Strings.Cli_AndMore, important.Count - 15));
        }

        if (comparison is not null)
        {
            Console.WriteLine();
            Console.WriteLine(Texts.Format(Strings.Cli_SinceRun, Labels.Date(comparison.Older.StartedAt), comparison.NewFindings.Count,
                comparison.ResolvedFindings.Count, comparison.AccessChanges.Count, comparison.AceChanges.Count, comparison.MembershipChanges.Count));
            if (comparison.ScopeDiffers) Console.WriteLine(Texts.Format(Strings.Cli_Attention, ScanScope.Describe(comparison)));
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
        ExitHigh => Strings.Cli_ExitHigh,
        ExitMedium => Strings.Cli_ExitMedium,
        _ => Strings.Cli_ExitClean,
    };

    private static void PrintHelp() => Console.WriteLine(Texts.Format(Strings.Cli_Help, AppInfo.Version, AppInfo.NoDataSentence));

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
                // Die Anzeige läuft im Scan-Thread: Ein Fehler hier (z. B. eine sehr schmale Konsole) darf den Scan
                // nie abbrechen – dann eben ohne Fortschrittszeile.
                try
                {
                    Write(p);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
                {
                }
            }
        }

        private void Write(ScanProgress p)
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
            var line = ProgressLine(p, ConsoleWidth());
            Console.Write("\r" + line.PadRight(Math.Max(_width, line.Length)));
            _width = line.Length;
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

    /// <summary>
    /// Die Fortschrittszeile für eine Konsole mit <paramref name="consoleWidth"/> Spalten: Zahlen und, wenn noch
    /// mindestens 10 Zeichen frei sind, der gekürzte aktuelle Pfad.
    /// </summary>
    internal static string ProgressLine(ScanProgress p, int consoleWidth)
    {
        var line = Texts.Format(Strings.Cli_Progress, p.Phase, Labels.Number(p.FoldersVisited), Labels.Number(p.FoldersStored), p.Errors);
        int max = Math.Max(40, consoleWidth - 1);
        const string separator = " – ";
        if (p.CurrentPath.Length > 0 && max - line.Length - separator.Length >= 10)
            line += separator + Labels.ShortenPath(p.CurrentPath, max - line.Length - separator.Length);
        return line.Length > max ? line[..max] : line;
    }

    private sealed class UsageException(string message) : Exception(message);

    /// <summary>Etwas, das der Scan am Ende braucht (Datenbank, Berichtsordner), ist nicht nutzbar – vor dem Scan gemeldet.</summary>
    internal sealed class PreconditionException(string message, string errorCode) : Exception(message), IHasErrorCode
    {
        public string ErrorCode { get; } = errorCode;
    }

    /// <summary>Einfache Zerlegung: Optionen mit „--“ (deutsch oder englisch), alles andere sind Ziele bzw. Ids.</summary>
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
                var typed = args[i].Trim();
                if (typed.Length == 0) continue;
                if (!typed.StartsWith("--", StringComparison.Ordinal))
                {
                    a.Positional.Add(typed);
                    continue;
                }
                var arg = Canonical(typed);
                if (values.Contains(arg, StringComparer.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        throw new UsageException(Texts.Format(Strings.Cli_NeedsValue, typed));
                    if (!a._values.TryGetValue(arg, out var list)) a._values[arg] = list = [];
                    list.Add(args[++i].Trim());
                }
                else if (flags.Contains(arg, StringComparer.OrdinalIgnoreCase)) a._flags.Add(arg);
                else throw new UsageException(Texts.Format(Strings.Cli_UnknownOption, typed));
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
                throw new UsageException(Texts.Format(Strings.Cli_NeedsInteger, name, min, text));
            return n;
        }
    }
}
