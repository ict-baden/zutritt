using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Demo;
using Zutritt.UI.Pages;

namespace Zutritt.UI;

/// <summary>
/// Verstecktes <c>Zutritt.exe --messen [&lt;ordner&gt;] [--ordner N] [--personen N]</c> (nicht in der Hilfe): erzeugt einen
/// großen Demo-Lauf, öffnet ihn wie die Oberfläche und misst je Schritt die Dauer und die längste Zeit, in der der
/// UI-Thread nichts anderes tun konnte („blockiert“ – das spürt man als Ruckeln). Mit Ordner zusätzlich Bilder jeder
/// Seite. Das Fenster liegt wie bei den Screenshots außerhalb des Bildschirms.
/// </summary>
internal static class Benchmark
{
    /// <summary>Ab hier fühlt sich die Oberfläche nicht mehr flüssig an.</summary>
    private const double SmoothMs = 100;

    private static readonly List<(string Step, TimeSpan Duration, double? BlockedMs, string Note)> Rows = [];

    public static async Task<int> RunAsync(string folder, int folders, int people)
    {
        Console.WriteLine($"Zutritt {AppInfo.Version} – Messung mit {Labels.Number(folders)} gespeicherten Ordnern und {Labels.Number(people)} Personen");
        Console.WriteLine();
        Console.WriteLine($"  {"Schritt",-60} {"Dauer",9}  {"UI blockiert",12}");
        if (folder.Length > 0) Directory.CreateDirectory(folder);

        var sw = Stopwatch.StartNew();
        var (older, newer) = await Task.Run(() => LargeDemo.Create(folders, people));
        Add("Läufe erzeugen (mit Befundregeln)", sw.Elapsed,
            $"{Labels.Number(newer.Folders.Count)} Ordner, {Labels.Number(newer.Folders.Sum(f => f.Aces.Count))} Einträge, " +
            $"{Labels.Number(newer.Principals.Values.Count(p => p.Kind == PrincipalKind.Benutzer))} Personen, {Labels.Number(newer.Findings.Count)} Befunde");

        using var state = AppState.OpenTemporary();
        var store = state.Store!;
        sw.Restart();
        await Task.Run(() => { store.Save(older); store.Save(newer); });
        Add("Beide Läufe speichern", sw.Elapsed, $"{new FileInfo(state.DatabasePath).Length / 1024 / 1024} MB");
        sw.Restart();
        var loaded = await Task.Run(() => store.Load(newer.Id)!);
        Add("Lauf laden (Datenbank)", sw.Elapsed);
        sw.Restart();
        var comparison = await Task.Run(() => ScanComparer.Compare(older, loaded));
        Add("Vergleich berechnen", sw.Elapsed,
            $"{Labels.Number(comparison.AccessChanges.Count)} Zugriffs-, {Labels.Number(comparison.AceChanges.Count)} ACL-Änderungen");

        var window = Screenshots.CreateWindow(state, dark: false);
        using var beat = new Heartbeat();
        try
        {
            await Step(beat, "Lauf öffnen (laden + vergleichen im Hintergrund)", () => state.OpenRunAsync(newer.Id));
            // Zum Eingrenzen beim Entwickeln: ZUTRITT_MESSEN_SEITEN=befunde,ordner misst nur diese Seiten (ohne Berichte,
            // außer „berichte“ steht mit in der Liste)
            var only = Environment.GetEnvironmentVariable("ZUTRITT_MESSEN_SEITEN")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var slug in MainWindow.PageSlugs.Where(s => only is null || only.Contains(s)))
            {
                var page = window.PageFor(slug);
                await Step(beat, $"{Title(slug)}: Seite zeigen", () => { window.ShowPage(slug); return Task.CompletedTask; });
                await Step(beat, $"{Title(slug)}: alles berechnet", ((IPage)page).PrepareScreenshotAsync);
                if (folder.Length > 0)
                {
                    await Task.Delay(400);
                    await Ui.IdleAsync();
                    Screenshots.Save(window, Path.Combine(folder, $"gross-{slug}.png"));
                }
                await InteractAsync(beat, window, page, folder, loaded);
            }
        }
        finally
        {
            window.Close();
        }

        // Berichte mit, wenn nicht eingegrenzt – oder ausdrücklich: ZUTRITT_MESSEN_SEITEN=berichte misst nur sie
        var pages = Environment.GetEnvironmentVariable("ZUTRITT_MESSEN_SEITEN");
        if (pages is null || pages.Contains("berichte", StringComparison.OrdinalIgnoreCase)) await ReportsAsync(loaded, comparison);
        Print();
        return 0;
    }

    // ================= Bedienen wie ein Mensch =================

    private static async Task InteractAsync(Heartbeat beat, MainWindow window, UserControl page, string folder, ScanResult scan)
    {
        switch (page)
        {
            case FindingsPage f:
                Add("Befunde: angelegte Zeilen", TimeSpan.Zero, $"{CountRows(f.FindingsList)} von {Labels.Number(scan.Findings.Count)} (virtualisiert)");
                await Step(beat, "Befunde: nur Hoch und Mittel", () =>
                {
                    Toggle(f.ChipLow, false);
                    Toggle(f.ChipInfo, false);
                    return Task.CompletedTask;
                });
                await Step(beat, "Befunde: ans Ende blättern", () => ScrollToEnd(f.FindingsList));
                await Step(beat, "Befunde: Suche „Kunden“ (inkl. 250 ms Tippause)", () => TypeAsync(f.SearchBox, "Kunden"));
                await Step(beat, "Befunde: Suche leeren", () => TypeAsync(f.SearchBox, ""));
                Toggle(f.ChipLow, true);
                Toggle(f.ChipInfo, true);
                await Ui.IdleAsync();
                break;

            case FoldersPage o:
                var deepest = scan.Folders.Where(x => x.Error is null).MaxBy(x => x.Depth)!;
                await Step(beat, $"Ordner: Pfad in Ebene {deepest.Depth} öffnen", () => o.SelectPathAsync(deepest.Path));
                await Step(beat, "Ordner: … und „Wer darf hier rein“", () => { o.Tabs.SelectedItem = o.TabPeople; return o.PeopleTask; });
                var wideFolder = scan.Folders.FirstOrDefault(x => x.Path.EndsWith(@"\Vertrieb\Kundenakten", StringComparison.Ordinal));
                if (wideFolder is not null)
                {
                    int children = scan.Folders.Count(x => x.Path.StartsWith(wideFolder.Path + @"\", StringComparison.Ordinal));
                    await Step(beat, "Ordner: großen Ordner zeigen (zugeklappt)", () => o.SelectPathAsync(wideFolder.Path));
                    // Wie ein Klick auf den Pfeil: aufklappen, die sichtbaren Unterordner entstehen
                    await Step(beat, $"Ordner: … aufklappen ({Labels.Number(children)} Unterordner)", () =>
                    {
                        if (o.Tree.SelectedItem is FolderTreeNode node) node.IsExpanded = true;
                        return Task.CompletedTask;
                    });
                    var last = scan.Folders.Last(x => x.Path.StartsWith(wideFolder.Path + @"\", StringComparison.Ordinal)).Path;
                    await Step(beat, "Ordner: letzten Unterordner darin zeigen", () => o.SelectPathAsync(last));
                    await Step(beat, "Ordner: … und „Wer darf hier rein“", () => o.PeopleTask);
                    if (folder.Length > 0) await SaveAsync(window, folder, "gross-ordner-breit");
                }
                await Step(beat, "Ordner: Suche „Messdaten“ (inkl. 250 ms Tippause)", () => TypeAsync(o.SearchBox, "Messdaten"));
                if (folder.Length > 0) await SaveAsync(window, folder, "gross-ordner-suche");
                await Step(beat, "Ordner: Suche leeren", () => TypeAsync(o.SearchBox, ""));
                o.Tabs.SelectedItem = o.TabAces;
                await Step(beat, "Ordner: Tab „Einträge“", () => Task.CompletedTask);
                if (folder.Length > 0) await SaveAsync(window, folder, "gross-ordner-eintraege");
                break;

            case PeoplePage p:
                await Step(beat, "Personen: ans Ende blättern", () => ScrollToEnd(p.PeopleList));
                var items = p.PeopleList.Items.Cast<object>().ToList();
                foreach (var index in new[] { items.Count / 3, items.Count / 2, items.Count - 1 })
                    await Step(beat, $"Personen: Person Nr. {index + 1} wählen", () => { p.PeopleList.SelectedItem = items[index]; return p.DetailTask; });
                await Step(beat, "Personen: Suche „Müller“ (inkl. 250 ms Tippause)", () => TypeAsync(p.SearchBox, "Müller"));
                await Step(beat, "Personen: Suche leeren", () => TypeAsync(p.SearchBox, ""));
                break;

            case ComparisonPage c:
                foreach (var (tile, name) in new[] { (c.TileAccess, "Zugriffsänderungen"), (c.TileAces, "ACL-Änderungen"), (c.TileMembers, "Mitgliedschaften") })
                    await Step(beat, $"Vergleich: Liste „{name}“", () => { tile.IsChecked = true; return Task.CompletedTask; });
                c.TileAccess.IsChecked = true;
                await Ui.IdleAsync();
                await Step(beat, $"Vergleich: {Labels.Number(c.AccessGrid.Items.Count)} Zugriffsänderungen nach Pfad sortieren", () => ClickHeader(c.AccessGrid, "Pfad"));
                if (folder.Length > 0) await SaveAsync(window, folder, "gross-vergleich-zugriffe");
                break;
        }
    }

    private static async Task ReportsAsync(ScanResult scan, ComparisonResult comparison)
    {
        // Berichte laufen in der Oberfläche im Hintergrund – hier nur, wie lange man wartet, und wofür (je Arbeitsschritt)
        var dir = Path.Combine(Path.GetTempPath(), $"Zutritt-Messung-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (with, label) in new[] { (comparison, "mit Vergleich"), ((ComparisonResult?)null, "ohne Vergleich") })
            {
                var xlsx = Path.Combine(dir, "bericht.xlsx");
                await ReportAsync($"Excel-Bericht {label} (Hintergrund)", xlsx, progress => ExcelReport.Write(scan, with, xlsx, progress));
                var pdf = Path.Combine(dir, "bericht.pdf");
                await ReportAsync($"PDF-Bericht {label} (Hintergrund)", pdf, progress => PdfReport.Write(scan, with, pdf, progress));
            }
        }
        finally
        {
            // Virenscanner halten frische Dateien manchmal kurz offen – dann einmal nachfassen
            for (int attempt = 0; attempt < 5 && Directory.Exists(dir); attempt++)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(500); }
            }
        }
    }

    /// <summary>Schreibt einen Bericht im Hintergrund und notiert die Gesamtdauer und die Dauer jedes gemeldeten Schritts.</summary>
    private static async Task ReportAsync(string name, string file, Action<IProgress<string>> write)
    {
        var phases = new PhaseClock();
        var sw = Stopwatch.StartNew();
        await Task.Run(() => write(phases));
        sw.Stop();
        phases.Stop();
        Add(name, sw.Elapsed, $"{new FileInfo(file).Length / 1024} KB");
        foreach (var (phase, duration) in phases.Phases) Add($"  · {phase.TrimEnd(' ', '…')}", duration);
    }

    /// <summary>Misst die Zeit zwischen zwei Fortschrittsmeldungen – synchron, damit nichts über den Dispatcher läuft.</summary>
    private sealed class PhaseClock : IProgress<string>
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private string? _current;
        private TimeSpan _since;

        public List<(string Phase, TimeSpan Duration)> Phases { get; } = [];

        public void Report(string value)
        {
            Stop();
            _current = value;
            _since = _clock.Elapsed;
        }

        public void Stop()
        {
            if (_current is not null) Phases.Add((_current, _clock.Elapsed - _since));
            _current = null;
        }
    }

    private static void Toggle(ToggleButton chip, bool on)
    {
        chip.IsChecked = on;
        chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, chip));
    }

    /// <summary>Text eintippen und warten, bis die Suche (nach der Tippause der Seite) angewendet ist.</summary>
    private static async Task TypeAsync(TextBox box, string text)
    {
        box.Text = text;
        await Task.Delay(300);
    }

    /// <summary>Was ein Klick auf einen DataGrid-Spaltenkopf auslöst; wartet, bis die sortierte Liste da ist.</summary>
    private static async Task ClickHeader(DataGrid grid, string title)
    {
        var column = grid.Columns.First(c => c.Header as string == title);
        var before = grid.ItemsSource;
        typeof(DataGrid).GetMethod("OnSorting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(grid, [new DataGridSortingEventArgs(column)]);
        // Die Seite sortiert im Hintergrund und tauscht dann die Liste
        var sw = Stopwatch.StartNew();
        while (ReferenceEquals(grid.ItemsSource, before) && sw.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(10);
    }

    private static Task ScrollToEnd(DependencyObject list)
    {
        Ui.FindChild<ScrollViewer>(list)?.ScrollToEnd();
        return Task.CompletedTask;
    }

    private static int CountRows(DependencyObject grid)
    {
        int rows = 0;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(grid);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is DataGridRow or ListBoxItem)
            {
                rows++;
                continue;
            }
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); i++)
                queue.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
        }
        return rows;
    }

    private static async Task SaveAsync(Window window, string folder, string name)
    {
        await Task.Delay(400);
        await Ui.IdleAsync();
        Screenshots.Save(window, Path.Combine(folder, name + ".png"));
    }

    // ================= Messen =================

    /// <summary>Führt einen Schritt aus, wartet auf Layout und Zeichnen und notiert Dauer und längste Blockade.</summary>
    private static async Task Step(Heartbeat beat, string name, Func<Task> work)
    {
        await Ui.IdleAsync();
        beat.Reset();
        var sw = Stopwatch.StartNew();
        var task = work();
        var direct = sw.Elapsed;
        await task;
        await Ui.IdleAsync();
        sw.Stop();
        // „direkt“: Zeit im Aufruf selbst, der Rest ist Hintergrundarbeit, Layout und Zeichnen danach
        Add(name, sw.Elapsed, beat.MaxGapMs(), direct.TotalMilliseconds >= 20 ? $"direkt {direct.TotalMilliseconds.ToString("0", Labels.German)} ms" : "");
    }

    private static void Add(string step, TimeSpan duration, string note = "") => Add(step, duration, null, note);

    /// <summary>Notiert eine Zeile und gibt sie gleich aus – lange Läufe zeigen so, wo sie gerade stehen.</summary>
    private static void Add(string step, TimeSpan duration, double? blocked, string note)
    {
        Rows.Add((step, duration, blocked, note));
        Console.WriteLine(Format(step, duration, blocked, note));
    }

    private static string Format(string step, TimeSpan duration, double? blocked, string note)
    {
        var time = duration == TimeSpan.Zero ? "" : duration.TotalSeconds >= 1
            ? duration.TotalSeconds.ToString("0.00 's'", Labels.German)
            : duration.TotalMilliseconds.ToString("0 'ms'", Labels.German);
        var block = blocked is { } b ? $"{b.ToString("0", Labels.German)} ms{(b > SmoothMs ? " !" : "")}" : "";
        return $"  {step,-60} {time,9}  {block,12}  {note}".TrimEnd();
    }

    private static string Title(string slug) => slug switch
    {
        "uebersicht" => "Übersicht",
        "befunde" => "Befunde",
        "ordner" => "Ordner",
        "personen" => "Personen",
        "vergleich" => "Vergleich",
        "bericht" => "Bericht",
        _ => "Info",
    };

    private static void Print()
    {
        var worst = Rows.Where(r => r.BlockedMs is not null).MaxBy(r => r.BlockedMs!.Value);
        Console.WriteLine();
        Console.WriteLine(worst.BlockedMs is { } max
            ? $"Längste Blockade: {max.ToString("0", Labels.German)} ms bei „{worst.Step}“ – {(max <= SmoothMs ? "flüssig" : $"über {SmoothMs:0} ms, spürbar")}."
            : "Keine Schritte gemessen.");
        var slow = Rows.Where(r => r.BlockedMs > SmoothMs).ToList();
        if (slow.Count > 0) Console.WriteLine($"{slow.Count} Schritte über {SmoothMs:0} ms.");
    }

    /// <summary>
    /// Tickt mit Eingabe-Priorität alle 10 ms. Die längste Lücke zwischen zwei Ticks ist die Zeit, in der Klicks und
    /// Tastendrücke liegen geblieben wären.
    /// </summary>
    private sealed class Heartbeat : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _last;
        private double _max;

        public Heartbeat()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
            _timer.Tick += (_, _) => Beat();
            _timer.Start();
        }

        public void Reset()
        {
            _last = _clock.Elapsed.TotalMilliseconds;
            _max = 0;
        }

        public double MaxGapMs()
        {
            Beat();
            return _max;
        }

        private void Beat()
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            _max = Math.Max(_max, now - _last);
            _last = now;
        }

        public void Dispose() => _timer.Stop();
    }
}
