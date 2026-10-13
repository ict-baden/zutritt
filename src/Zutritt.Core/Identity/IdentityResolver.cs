using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>
/// Löst SIDs zu Konten auf, erkennt bekannte Prinzipale und verwaiste SIDs und füllt Gruppen bis zur
/// Person auf. Fragt die Verzeichnisse der Reihe nach und merkt sich Ergebnisse.
/// </summary>
public sealed class IdentityResolver
{
    public IdentityResolver(IEnumerable<IDirectory> directories) => throw new NotImplementedException();

    /// <summary>Standard: lokales Verzeichnis dieses Rechners plus AD, falls der Rechner in einer Domäne ist.</summary>
    public static IdentityResolver CreateDefault() => throw new NotImplementedException();

    public Principal Resolve(string sid) => throw new NotImplementedException();

    /// <summary>Alle SIDs in <paramref name="result"/> auflösen und Gruppen rekursiv expandieren (in result.Principals).</summary>
    public void ResolveAll(ScanResult result, bool expandGroups, CancellationToken ct = default) => throw new NotImplementedException();
}
