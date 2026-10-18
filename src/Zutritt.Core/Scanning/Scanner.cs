using System.Globalization;
using System.Reflection;
using Zutritt.Core.Analysis;
using Zutritt.Core.Diagnostics;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Platform;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Führt einen Lauf aus: Ziele auflösen (Ordner, UNC, Server, „.“), Freigaben lesen, Baum ablaufen,
/// Konten auflösen, Befunde berechnen.
/// </summary>
/// <remarks>
/// Gespeichert werden (in <see cref="ScanResult.Folders"/>) die Wurzel jedes Ziels, jeder Knoten mit unterbrochener
/// Vererbung, expliziten Einträgen oder Fehler – und zusätzlich Ordner, deren wirksame oder weitergegebene Einträge
/// sich trotz reiner Vererbung vom Elternordner unterscheiden (ERSTELLER-BESITZER, „nur dieser Ordner“, verschobene
/// Ordner) bzw. deren Elternordner einen Fehler hatte. Alle anderen Ordner erben genau vom nächsthöheren gespeicherten
/// und werden nur gezählt. Mit <see cref="ScanRequest.IncludeFiles"/> ebenso Dateien mit eigenen Einträgen oder mit
/// anderen Rechten, als sie vom Ordner erben würden (verschobene Dateien). Verknüpfungen (Junctions, symbolische Links)
/// unterhalb der Wurzel werden erfasst, aber nicht verfolgt.
/// <para>
/// Passen die geerbten Einträge eines Ordners oder einer Datei nicht zu dem, was der Elternordner nach den Regeln von
/// Windows vererbt, setzt der Scanner <see cref="FolderNode.InheritanceDiffers"/> (und speichert den Knoten) – das
/// unterscheidet echte Abweichungen (verschobene Ordner) von den normalen Fällen oben.
/// </para>
/// <para>
/// Gelesen wird über die Systemanbindung (<see cref="IScanPlatform"/>, unter Windows <c>WindowsPlatform</c>). Ohne sie –
/// auf anderen Systemen – meldet <see cref="RunAsync"/> „Scannen ist nur unter Windows möglich“; Auswertung, Vergleich,
/// Speicher und Berichte gespeicherter Läufe gehen trotzdem.
/// </para>
/// </remarks>
public sealed class Scanner
{
    /// <summary>Meldung, wenn es keine Systemanbindung gibt.</summary>
    public const string NotSupportedMessage = "Scannen ist nur unter Windows möglich (NTFS-Rechte, Freigaben, Active Directory).";

    private readonly IScanPlatform? _platform;
    private readonly Action<ScanResult, CancellationToken>? _resolve;
    private readonly Func<ScanResult, IEnumerable<Finding>>? _evaluate;

    /// <summary>Konten über die Plattform auflösen (je Lauf frisch), Befunde mit <see cref="FindingRules.Evaluate"/> berechnen.</summary>
    /// <param name="platform">Systemanbindung; null = keine (dann ist kein Scan möglich).</param>
    public Scanner(IScanPlatform? platform)
        : this(platform, resolve: null, evaluate: null) { }

    /// <summary>Konten über <paramref name="identities"/> auflösen, Befunde mit <see cref="FindingRules.Evaluate"/> berechnen.</summary>
    public Scanner(IScanPlatform? platform, IdentityResolver identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        _platform = platform;
        _resolve = (result, ct) => identities.ResolveAll(result, result.Request.ResolveGroups, ct);
        _evaluate = FindingRules.Evaluate;
    }

    /// <summary>
    /// Mit eigenen Schritten für Kontenauflösung und Befunde (Tests, Sonderfälle).
    /// null = Standard: <see cref="IScanPlatform.CreateIdentityResolver"/> bzw. <see cref="FindingRules.Evaluate"/>.
    /// Zum Auslassen eines Schritts eine leere Funktion übergeben.
    /// </summary>
    public Scanner(IScanPlatform? platform, Action<ScanResult, CancellationToken>? resolve, Func<ScanResult, IEnumerable<Finding>>? evaluate)
    {
        _platform = platform;
        _resolve = resolve;
        _evaluate = evaluate;
    }

    /// <summary>Ob es eine Systemanbindung gibt, also gescannt werden kann.</summary>
    public bool IsSupported => _platform is not null;

    /// <summary>
    /// Version dieses Werkzeugs, wie sie im Lauf und im Bericht steht (AssemblyInformationalVersion, sonst AssemblyVersion).
    /// </summary>
    internal static string ToolVersion { get; } = ReadVersion();

    /// <summary>
    /// Mindestabstand der Fortschrittsmeldungen: höchstens etwa zehn pro Sekunde, sonst bremst die Oberfläche den Scan.
    /// Phasenwechsel werden immer gemeldet. Tests setzen 0, um jede Meldung zu sehen.
    /// </summary>
    internal long ProgressIntervalMs { get; init; } = 100;

    /// <summary>Zeitlimit je Freigabeliste bzw. einzelner Freigabe eines Servers (Tests setzen es kurz).</summary>
    internal TimeSpan ShareTimeout { get; init; } = TimeBudget.Shares;

    /// <summary>Lauf im Hintergrund ausführen.</summary>
    /// <exception cref="ArgumentException">Keine Ziele angegeben oder negative Tiefe – ein Fehler des Aufrufers.</exception>
    /// <exception cref="PlatformNotSupportedException">Keine Systemanbindung (nicht unter Windows).</exception>
    /// <exception cref="OperationCanceledException">Abgebrochen über <paramref name="ct"/>.</exception>
    /// <remarks>
    /// Erwartbare Probleme (Ziel nicht erreichbar, Zugriff verweigert, ungültiges Ziel) brechen den Lauf nicht ab,
    /// sondern landen in <see cref="ScanResult.Errors"/> bzw. <see cref="FolderNode.Error"/>.
    /// </remarks>
    public Task<ScanResult> RunAsync(ScanRequest request, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Targets is null || request.Targets.Count == 0)
            throw new ArgumentException("Keine Ziele angegeben.", nameof(request));
        if (request.MaxDepth is < 0)
            throw new ArgumentException("Die maximale Tiefe darf nicht negativ sein.", nameof(request));
        var platform = _platform ?? throw new PlatformNotSupportedException(NotSupportedMessage);

        return Task.Run(() => Run(platform, request, progress, ct), ct);
    }

    private ScanResult Run(IScanPlatform platform, ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        // Ins Protokoll nur Zahlen und Einstellungen – keine Ziele, Pfade, Rechte oder Konten
        Log.Info($"Scan gestartet: {request.Targets.Count} Ziel(e), Dateien {(request.IncludeFiles ? "ja" : "nein")}, " +
                 $"Tiefe {(request.MaxDepth?.ToString(CultureInfo.InvariantCulture) ?? "unbegrenzt")}, Gruppen {(request.ResolveGroups ? "ja" : "nein")}, " +
                 $"administrative Freigaben {(request.SkipAdminShares ? "nein" : "ja")}");
        try
        {
            var done = RunCore(platform, request, progress, ct);
            LogFinished(done);
            return done;
        }
        catch (OperationCanceledException)
        {
            Log.Info("Scan abgebrochen");
            throw;
        }
        catch (Exception e)
        {
            Log.Error("Scan fehlgeschlagen", e, withStack: true);
            throw;
        }
    }

    internal static void LogFinished(ScanResult r)
    {
        int Count(Severity s) => r.Findings.Count(f => f.Severity == s);
        Log.Info($"Scan beendet nach {(r.FinishedAt - r.StartedAt).TotalSeconds:0.0} s: {r.FoldersVisited} Ordner geprüft, " +
                 $"{r.Folders.Count} gespeichert, {r.FilesVisited} Dateien, {r.Shares.Count} Freigaben, {r.Principals.Count} Konten, " +
                 $"{r.Errors.Count} Fehler und Hinweise, Befunde Hoch {Count(Severity.Hoch)} · Mittel {Count(Severity.Mittel)} · " +
                 $"Niedrig {Count(Severity.Niedrig)} · Info {Count(Severity.Info)}");
        // Häufigste Fehlerarten, damit der Support weiß, wonach er fragen muss. Die Meldungen nennen teils Gruppen,
        // Domänen oder lokale Pfade („Mitglieder von „MUSTER\Labor“ nicht lesbar“) – die ersetzt der Anonymizer, bevor
        // gezählt wird. So landen keine Konten- oder Pfadnamen im Protokoll, und gleiche Fehlerarten werden zusammengefasst.
        var anonymizer = new Anonymizer();
        var kinds = r.Errors
            .GroupBy(e => e.Message, StringComparer.Ordinal)
            .GroupBy(g => anonymizer.Apply(g.Key), g => g.Count(), StringComparer.Ordinal)
            .Select(g => (Message: g.Key, Count: g.Sum()))
            .OrderByDescending(k => k.Count)
            .ThenBy(k => k.Message, StringComparer.Ordinal)
            .Take(5);
        foreach (var (message, count) in kinds)
            Log.Warning($"{count}× {message}");
    }

    private ScanResult RunCore(IScanPlatform platform, ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var result = new ScanResult
        {
            StartedAt = DateTimeOffset.Now,
            Machine = Environment.MachineName,
            ScannedBy = platform.CurrentUserName,
            ToolVersion = ToolVersion,
            Request = request,
        };

        var run = new ScanRun(platform, result, progress, ProgressIntervalMs, ct) { ShareTimeout = ShareTimeout };
        run.Execute();

        run.SetPhase(ScanRun.PhaseAccounts, "");
        if (_resolve is not null) _resolve(result, ct);
        else
        {
            using var identities = platform.CreateIdentityResolver();
            identities.ResolveAll(result, result.Request.ResolveGroups, ct);
        }
        ct.ThrowIfCancellationRequested();

        run.SetPhase(ScanRun.PhaseFindings, "");
        var evaluate = _evaluate ?? FindingRules.Evaluate;
        result.Findings.AddRange(evaluate(result));

        result.FinishedAt = DateTimeOffset.Now;
        run.SetPhase(ScanRun.PhaseDone, "");
        return result;
    }

    private static string ReadVersion()
    {
        var assembly = typeof(Scanner).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "";
    }
}
