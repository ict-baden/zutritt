using Zutritt.Core.Identity;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Führt einen Lauf aus: Ziele auflösen (Ordner, UNC, Server, „.“), Freigaben lesen, Baum ablaufen,
/// Konten auflösen, Befunde berechnen.
/// </summary>
public sealed class Scanner
{
    public Scanner(IdentityResolver identities) => throw new NotImplementedException();

    public Task<ScanResult> RunAsync(ScanRequest request, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => throw new NotImplementedException();
}
