using Zutritt.Core.Analysis;
using Zutritt.Core.Model;

namespace Zutritt.Tests.Reports;

/// <summary>
/// Test, der die echte Auswertung (<see cref="EffectiveAccess"/>, <see cref="FindingRules"/>,
/// <see cref="ScanComparer"/>) braucht. Solange der Baustein „analysis“ in diesem Zweig nur ein Stub ist,
/// wird der Test übersprungen – nach dem Zusammenführen läuft er automatisch mit.
/// </summary>
public sealed class AnalysisFactAttribute : FactAttribute
{
    public AnalysisFactAttribute()
    {
        if (!IsAvailable) Skip = "Baustein „analysis“ ist noch nicht zusammengeführt (Stub wirft NotImplementedException).";
    }

    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        var empty = new ScanResult();
        try
        {
            _ = FindingRules.All;
            _ = EffectiveAccess.ForPerson(empty, "S-1-1-0");
            _ = ScanComparer.Compare(empty, new ScanResult());
            return true;
        }
        catch (NotImplementedException)
        {
            return false;
        }
        catch
        {
            // Anderer Fehler: nicht überspringen, der Test soll ihn zeigen
            return true;
        }
    }
}
