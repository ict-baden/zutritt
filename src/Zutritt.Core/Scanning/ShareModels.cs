// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Eine gelesene Freigabe plus Hinweis, warum ihre Berechtigungen nicht (vollständig) lesbar sind.</summary>
public sealed record ShareRead(ShareInfo Share, string? Note);

/// <summary>Alle Datenträger-Freigaben eines Servers oder der Grund, warum die Liste nicht lesbar war.</summary>
/// <param name="ServerServiceStopped">
/// Auf dem Rechner läuft kein Serverdienst: Dann gibt es keine Freigaben – das ist kein Fehler, sondern das Ergebnis.
/// </param>
public sealed record ShareListing(IReadOnlyList<ShareRead> Shares, string? Error, bool ServerServiceStopped = false);

/// <summary>Ergebnis der Suche nach einer einzelnen Freigabe.</summary>
/// <param name="NotFound">Der Server meldet ausdrücklich, dass es die Freigabe nicht gibt.</param>
public sealed record ShareLookup(ShareRead? Read, string? Error, bool NotFound);

/// <summary>Ein Eintrag eines Ordners.</summary>
/// <param name="IsLink">
/// Verweis auf etwas anderes (Junction, symbolische Verknüpfung, Bereitstellungspunkt) – wird nicht verfolgt.
/// Andere Reparse-Punkte (z. B. OneDrive-Platzhalter) sind echte Ordner und werden normal durchlaufen.
/// </param>
public readonly record struct DirEntry(string Name, bool IsDirectory, bool IsLink);

/// <summary>Was unter einem Zielpfad liegt.</summary>
/// <param name="Error">Ziel nicht erreichbar (deutscher Text), sonst null.</param>
public readonly record struct TargetProbe(bool IsDirectory, string? Error)
{
    public static TargetProbe Directory { get; } = new(true, null);
    public static TargetProbe File { get; } = new(false, null);
    public static TargetProbe Unreachable(string error) => new(false, error);
}
