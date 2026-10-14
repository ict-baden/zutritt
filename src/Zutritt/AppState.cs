using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Storage;
using Zutritt.Demo;

namespace Zutritt;

public enum NoticeKind { Info, Success, Warning, Error }

/// <summary>
/// Gemeinsamer Zustand aller Seiten: Datenbank, geladener Lauf und Vergleich. Alle Methoden werden vom
/// UI-Thread aufgerufen; Datenbankzugriffe laufen nacheinander im Hintergrund, weil eine SQLite-Verbindung
/// nicht von zwei Threads gleichzeitig benutzt werden darf.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly SemaphoreSlim _storeGate = new(1, 1);
    private readonly string? _tempDatabase;
    private Dictionary<string, RuleInfo>? _rules;

    private AppState(ScanStore? store, string databasePath, bool isDemo, bool strict, string? tempDatabase)
    {
        Store = store;
        DatabasePath = databasePath;
        IsDemo = isDemo;
        Strict = strict;
        _tempDatabase = tempDatabase;
    }

    public ScanStore? Store { get; }
    public string DatabasePath { get; }
    public bool IsDemo { get; }

    /// <summary>
    /// Screenshot-Modus: Fehler nicht als Meldung abfangen, sondern durchreichen, damit ein unvollständiges
    /// Bild nie unbemerkt entsteht.
    /// </summary>
    public bool Strict { get; }

    /// <summary>Problem beim Start (z. B. Datenbank nicht zu öffnen), wird nach dem Öffnen des Fensters gemeldet.</summary>
    public string? StartupProblem { get; private set; }

    public ScanResult? Scan { get; private set; }
    public ComparisonResult? Comparison { get; private set; }

    /// <summary>Ein anderer Lauf ist geladen (oder keiner mehr).</summary>
    public event Action? ScanChanged;
    /// <summary>Vergleich neu berechnet (anderer älterer Lauf gewählt).</summary>
    public event Action? ComparisonChanged;
    /// <summary>Gespeicherte Läufe haben sich geändert (neuer Lauf, gelöscht).</summary>
    public event Action? RunsChanged;
    public event Action<string>? FolderRequested;
    public event Action<string>? PageRequested;
    public event Action<NoticeKind, string>? Notice;

    // ================= Öffnen =================

    /// <summary>Normaler Start mit der Datenbank unter %LocalAppData%\Zutritt.</summary>
    public static AppState Open(string? databasePath = null)
    {
        string path = databasePath ?? "";
        try
        {
            if (path.Length == 0) path = ScanStore.DefaultPath;
            return new AppState(new ScanStore(path), path, isDemo: false, strict: false, tempDatabase: null);
        }
        catch (Exception ex)
        {
            // Ohne Datenbank bleibt Zutritt benutzbar (scannen, ansehen, Bericht), speichert aber nichts.
            var where = path.Length > 0 ? $" „{path}“" : "";
            return new AppState(null, path, isDemo: false, strict: false, tempDatabase: null)
            {
                StartupProblem = $"Die Datenbank{where} lässt sich nicht öffnen ({ex.Message}). Läufe werden in dieser Sitzung nicht gespeichert.",
            };
        }
    }

    /// <summary>
    /// Demo: zwei eingebaute Läufe in einer temporären Datenbank, damit die echte unberührt bleibt.
    /// <paramref name="strict"/> (Screenshots) reicht Fehler durch, sonst werden sie gemeldet.
    /// </summary>
    public static AppState OpenDemo(bool strict)
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Zutritt-Demo-{Environment.ProcessId}-{Guid.NewGuid():N}.db");
        var (older, newer) = DemoData.Create();
        if (strict) return CreateDemo(temp, older, newer, strict: true);
        try
        {
            return CreateDemo(temp, older, newer, strict: false);
        }
        catch (Exception ex)
        {
            DeleteDatabaseFiles(temp);
            var state = new AppState(null, "", isDemo: true, strict: false, tempDatabase: null)
            {
                StartupProblem = $"Die Demo-Datenbank ließ sich nicht anlegen ({ex.Message}). Der Demo-Lauf wird ohne Speicher und ohne Vergleich gezeigt.",
            };
            state.SetScan(newer, null);
            return state;
        }
    }

    /// <summary>
    /// Leere temporäre Datenbank, Fehler werden durchgereicht – für die versteckte Messung mit großen Demo-Läufen.
    /// Die Datei wird beim Beenden gelöscht.
    /// </summary>
    internal static AppState OpenTemporary()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Zutritt-Messung-{Environment.ProcessId}-{Guid.NewGuid():N}.db");
        return new AppState(new ScanStore(temp), temp, isDemo: true, strict: true, temp);
    }

    private static AppState CreateDemo(string temp, ScanResult older, ScanResult newer, bool strict)
    {
        var store = new ScanStore(temp);
        var state = new AppState(store, temp, isDemo: true, strict, temp);
        try
        {
            store.Save(older);
            store.Save(newer);
        }
        catch
        {
            state.Dispose();
            throw;
        }

        ComparisonResult? comparison = null;
        if (strict) comparison = ScanComparer.Compare(older, newer);
        else
        {
            try { comparison = ScanComparer.Compare(older, newer); }
            catch (Exception ex) { state.StartupProblem = $"Der Vergleich der Demo-Läufe ist fehlgeschlagen ({ex.Message})."; }
        }
        state.SetScan(newer, comparison);
        return state;
    }

    // ================= Läufe =================

    public void SetScan(ScanResult? scan, ComparisonResult? comparison)
    {
        Scan = scan;
        Comparison = comparison;
        ScanChanged?.Invoke();
    }

    /// <summary>Datenbankarbeit im Hintergrund, nacheinander.</summary>
    public async Task<T> WithStoreAsync<T>(Func<ScanStore, T> work)
    {
        var store = Store ?? throw new InvalidOperationException("Es ist keine Datenbank geöffnet.");
        await _storeGate.WaitAsync();
        try { return await Task.Run(() => work(store)); }
        finally { _storeGate.Release(); }
    }

    public Task<IReadOnlyList<ScanSummary>> ListRunsAsync() =>
        Store is null ? Task.FromResult<IReadOnlyList<ScanSummary>>([]) : WithStoreAsync(s => s.List());

    /// <summary>Gespeicherten Lauf laden und mit seinem Vorgänger (gleiche Ziele) vergleichen.</summary>
    public async Task OpenRunAsync(long id)
    {
        var (scan, comparison) = await WithStoreAsync(store =>
        {
            var scan = store.Load(id) ?? throw new InvalidOperationException($"Lauf {id} gibt es nicht mehr.");
            return (scan, CompareWithPrevious(store, scan));
        });
        SetScan(scan, comparison);
    }

    /// <summary>Frisch gescannten Lauf speichern, mit dem Vorgänger vergleichen und laden.</summary>
    public async Task AdoptAsync(ScanResult scan)
    {
        ComparisonResult? comparison = null;
        if (Store is not null)
        {
            comparison = await WithStoreAsync(store =>
            {
                store.Save(scan);
                return CompareWithPrevious(store, scan);
            });
            RunsChanged?.Invoke();
        }
        SetScan(scan, comparison);
    }

    /// <summary>Den geladenen Lauf mit einem frei gewählten älteren Lauf vergleichen.</summary>
    public async Task CompareWithAsync(long olderId)
    {
        if (Scan is not { } scan) return;
        var comparison = await WithStoreAsync(store => store.Load(olderId) is { } older ? ScanComparer.Compare(older, scan) : null);
        if (!ReferenceEquals(scan, Scan)) return; // inzwischen anderer Lauf geladen
        Comparison = comparison;
        ComparisonChanged?.Invoke();
    }

    public async Task DeleteRunAsync(long id)
    {
        await WithStoreAsync(store => { store.Delete(id); return true; });
        if (Scan is { } scan && scan.Id == id) SetScan(null, null);
        else if (Comparison is { } c && c.Older.Id == id)
        {
            Comparison = null;
            ComparisonChanged?.Invoke();
        }
        RunsChanged?.Invoke();
    }

    private static ComparisonResult? CompareWithPrevious(ScanStore store, ScanResult scan)
    {
        var previous = store.FindPrevious(scan);
        if (previous is null) return null;
        var older = store.Load(previous.Id);
        return older is null ? null : ScanComparer.Compare(older, scan);
    }

    // ================= Namen und Regeln =================

    /// <summary>Lesbarer Name zu einer SID im geladenen Lauf (DOMÄNE\name), sonst die SID selbst.</summary>
    public string NameOf(string? sid) => NameIn(Scan, sid);

    public static string NameIn(ScanResult? scan, string? sid)
    {
        if (string.IsNullOrEmpty(sid)) return "";
        return scan is not null && scan.Principals.TryGetValue(sid, out var p) ? p.Label : sid;
    }

    /// <summary>
    /// Kurzer Name für enge Spalten: Anzeigename („Anna Becker“, „QC-Team“), sonst der Kontoname ohne Domäne.
    /// Verwaiste und unbekannte SIDs bleiben SIDs – dort gibt es nichts Kürzeres, das stimmt.
    /// </summary>
    public string ShortNameOf(string? sid) => ShortNameIn(Scan, sid);

    public static string ShortNameIn(ScanResult? scan, string? sid)
    {
        if (string.IsNullOrEmpty(sid)) return "";
        if (scan is null || !scan.Principals.TryGetValue(sid, out var p) || p.Kind == PrincipalKind.Verwaist) return sid;
        return p.DisplayName.Length > 0 ? p.DisplayName : Labels.WithoutDomain(p.Label);
    }

    /// <summary>Name in einem Vergleich: zuerst im neueren, dann im älteren Lauf (gelöschte Konten).</summary>
    public static string NameIn(ComparisonResult c, string sid) =>
        c.Newer.Principals.TryGetValue(sid, out var p) || c.Older.Principals.TryGetValue(sid, out p) ? p.Label : sid;

    public Principal? PrincipalOf(string? sid) =>
        sid is not null && Scan is not null && Scan.Principals.TryGetValue(sid, out var p) ? p : null;

    /// <summary>Beschreibung einer Prüfregel (Titel, Erklärung, Empfehlung) aus <see cref="FindingRules.All"/>.</summary>
    public RuleInfo? Rule(string ruleId)
    {
        _rules ??= FindingRules.All.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        return _rules.GetValueOrDefault(ruleId);
    }

    public IReadOnlyCollection<RuleInfo> Rules
    {
        get
        {
            _rules ??= FindingRules.All.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
            return _rules.Values;
        }
    }

    // ================= Zwischen Seiten =================

    public void ShowFolder(string path) => FolderRequested?.Invoke(path);
    public void ShowPage(string slug) => PageRequested?.Invoke(slug);
    public void Report(NoticeKind kind, string text) => Notice?.Invoke(kind, text);

    public void Dispose()
    {
        Store?.Dispose();
        _storeGate.Dispose();
        if (_tempDatabase is not null) DeleteDatabaseFiles(_tempDatabase);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var f in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* Temp-Ordner räumt Windows irgendwann selbst auf */ }
        }
    }
}
