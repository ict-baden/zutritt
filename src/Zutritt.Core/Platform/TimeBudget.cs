using System.Runtime.ExceptionServices;

namespace Zutritt.Core.Platform;

/// <summary>
/// Zeitlimit für Aufrufe, die an fremden Systemen hängen können (Domänencontroller, Kontendatenbank eines Servers,
/// Freigabeliste). Ein Server, der nie antwortet, darf den Lauf nicht anhalten: Nach Ablauf gilt die Quelle als nicht
/// erreichbar, der Aufruf läuft im Hintergrund weiter und sein Ergebnis wird verworfen.
/// </summary>
internal static class TimeBudget
{
    /// <summary>Standard für Verzeichnisse und Kontenauflösung je Aufruf.</summary>
    public static readonly TimeSpan Directory = TimeSpan.FromSeconds(60);

    /// <summary>Standard für Freigabelisten und einzelne Freigaben je Server.</summary>
    public static readonly TimeSpan Shares = TimeSpan.FromMinutes(2);

    /// <summary>„60 Sekunden“, „2 Minuten“ – für Hinweise im Lauf.</summary>
    public static string Describe(TimeSpan limit) => limit.TotalSeconds >= 120 && limit.TotalSeconds % 60 == 0
        ? $"{limit.TotalMinutes:0} Minuten"
        : limit.TotalSeconds >= 1 ? $"{limit.TotalSeconds:0} Sekunden" : $"{limit.TotalMilliseconds:0} Millisekunden";

    /// <summary>
    /// <paramref name="work"/> mit Zeitlimit ausführen. Ausnahmen der Arbeit kommen unverändert beim Aufrufer an.
    /// </summary>
    /// <returns>false bei Zeitüberschreitung (<paramref name="result"/> ist dann der Standardwert).</returns>
    public static bool TryRun<T>(Func<T> work, TimeSpan limit, out T result)
    {
        if (limit == Timeout.InfiniteTimeSpan)
        {
            result = work();
            return true;
        }
        var task = Task.Run(work);
        try
        {
            if (!task.Wait(limit))
            {
                // Späte Fehler der verworfenen Arbeit beobachten, damit sie nicht als unbeobachtet gemeldet werden
                _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                result = default!;
                return false;
            }
        }
        catch (AggregateException e) when (e.InnerExceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
        }
        result = task.Result;
        return true;
    }
}
