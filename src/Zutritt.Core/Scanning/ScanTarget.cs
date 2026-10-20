// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Art eines Scan-Ziels.</summary>
public enum TargetKind
{
    /// <summary>Lokaler Ordner, z. B. D:\Daten.</summary>
    LocalPath,
    /// <summary>\server\freigabe[\unterordner].</summary>
    UncPath,
    /// <summary>\server – alle Datenträger-Freigaben.</summary>
    Server,
    /// <summary>„.“ – alle Datenträger-Freigaben dieses Rechners, über ihre lokalen Pfade.</summary>
    LocalShares,
}

/// <summary>Ein aufgelöstes Scan-Ziel (zerlegt von <see cref="Platform.IScanPlatform.ParseTarget"/>).</summary>
/// <param name="Path">Normalisierter Pfad (lokal oder UNC), bei <see cref="TargetKind.Server"/> „\server“, bei „.“ leer.</param>
public sealed record ScanTarget(TargetKind Kind, string Path, string? Server = null, string? Share = null)
{
    public string ShareUnc => WindowsPath.Unc(Server ?? "", Share ?? "");
}
