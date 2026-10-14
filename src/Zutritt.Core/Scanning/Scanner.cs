using System.Reflection;
using System.Security.Principal;
using Zutritt.Core.Analysis;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;

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
/// </remarks>
public sealed class Scanner
{
    private readonly Action<ScanResult, CancellationToken>? _resolve;
    private readonly Func<ScanResult, IEnumerable<Finding>>? _evaluate;

    /// <summary>Konten über <paramref name="identities"/> auflösen, Befunde mit <see cref="FindingRules.Evaluate"/> berechnen.</summary>
    public Scanner(IdentityResolver identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        _resolve = (result, ct) => identities.ResolveAll(result, result.Request.ResolveGroups, ct);
        _evaluate = FindingRules.Evaluate;
    }

    /// <summary>
    /// Mit eigenen Schritten für Kontenauflösung und Befunde (Tests, Sonderfälle).
    /// null = Standard: <see cref="IdentityResolver.CreateDefault"/> bzw. <see cref="FindingRules.Evaluate"/>.
    /// Zum Auslassen eines Schritts eine leere Funktion übergeben.
    /// </summary>
    public Scanner(Action<ScanResult, CancellationToken>? resolve, Func<ScanResult, IEnumerable<Finding>>? evaluate)
    {
        _resolve = resolve;
        _evaluate = evaluate;
    }

    /// <summary>
    /// Version dieses Werkzeugs, wie sie im Lauf und im Bericht steht (AssemblyInformationalVersion, sonst AssemblyVersion).
    /// </summary>
    internal static string ToolVersion { get; } = ReadVersion();

    /// <summary>
    /// Mindestabstand der Fortschrittsmeldungen: höchstens etwa zehn pro Sekunde, sonst bremst die Oberfläche den Scan.
    /// Phasenwechsel werden immer gemeldet. Tests setzen 0, um jede Meldung zu sehen.
    /// </summary>
    internal long ProgressIntervalMs { get; init; } = 100;

    /// <summary>Lauf im Hintergrund ausführen.</summary>
    /// <exception cref="ArgumentException">Keine Ziele angegeben oder negative Tiefe – ein Fehler des Aufrufers.</exception>
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

        return Task.Run(() => Run(request, progress, ct), ct);
    }

    private ScanResult Run(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var result = new ScanResult
        {
            StartedAt = DateTimeOffset.Now,
            Machine = Environment.MachineName,
            ScannedBy = CurrentUser(),
            ToolVersion = ToolVersion,
            Request = request,
        };

        var run = new ScanRun(result, progress, ProgressIntervalMs, ct);
        run.Execute();

        run.SetPhase(ScanRun.PhaseAccounts, "");
        var resolve = _resolve ?? ((r, token) => IdentityResolver.CreateDefault().ResolveAll(r, r.Request.ResolveGroups, token));
        resolve(result, ct);
        ct.ThrowIfCancellationRequested();

        run.SetPhase(ScanRun.PhaseFindings, "");
        var evaluate = _evaluate ?? FindingRules.Evaluate;
        result.Findings.AddRange(evaluate(result));

        result.FinishedAt = DateTimeOffset.Now;
        run.SetPhase(ScanRun.PhaseDone, "");
        return result;
    }

    private static string CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    private static string ReadVersion()
    {
        var assembly = typeof(Scanner).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "";
    }
}
