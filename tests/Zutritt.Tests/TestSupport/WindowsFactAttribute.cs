namespace Zutritt.Tests.TestSupport;

/// <summary>
/// Test, der Verhalten von Windows prüft, das andere Systeme nicht haben (z. B. Dateisperren: FileShare.None sperrt
/// unter Linux nur beratend). Auf anderen Systemen übersprungen.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Nur unter Windows: " + reason;
    }
}
