// SPDX-License-Identifier: MIT
namespace Zutritt.Tests.TestSupport;

/// <summary>Temporärer Ordnerbaum für Tests, wird beim Dispose samt Inhalt gelöscht.</summary>
/// <remarks>Rechte setzen (Vererbung, Einträge) nur unter Windows: Windows/TestSupport/TempFolder.Windows.cs.</remarks>
public sealed partial class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zutritt-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Eigene Rechte zurückholen, falls ein Test sich ausgesperrt hat (nur unter Windows).</summary>
    partial void RestoreOwnAccess();

    public void Dispose()
    {
        try
        {
            RestoreOwnAccess();
            Directory.Delete(Path, true);
        }
        catch { /* Aufräumen ist best effort */ }
    }
}
