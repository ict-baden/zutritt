// SPDX-License-Identifier: MIT
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Core.Platform;

/// <summary>
/// Systemanbindung des Scanners: Ziele zerlegen, Freigaben und NTFS-Rechte lesen, Ordner auflisten, Konten auflösen.
/// Unter Windows <c>Zutritt.Platform.Windows.WindowsPlatform</c>; auf anderen Systemen gibt es keine – dort laufen
/// Auswertung, Vergleich, Speicher und Berichte, aber kein Scan (<see cref="Scanner.RunAsync"/> meldet das).
/// </summary>
/// <remarks>
/// Fehler kommen nie als Ausnahme, sondern als deutscher Text im Ergebnis (<see cref="FolderNode.Error"/>,
/// <see cref="ShareListing.Error"/> …) – ohne Fehlernummern des Systems, die Core nicht kennt.
/// Gescannte Pfade sind Windows-Pfade (<see cref="WindowsPath"/>), egal auf welchem System die Auswertung läuft.
/// </remarks>
public interface IScanPlatform
{
    /// <summary>Konto, unter dem gescannt wird („DOMÄNE\name“), für <see cref="ScanResult.ScannedBy"/>.</summary>
    string CurrentUserName { get; }

    /// <summary>
    /// Kontenauflösung für einen Lauf: lokale Kontendatenbank, Active Directory, Kontenauflösung jedes gescannten
    /// Servers. Jeder Lauf bekommt einen neuen Resolver (frische Daten); der Aufrufer gibt ihn frei.
    /// </summary>
    IdentityResolver CreateIdentityResolver();

    /// <summary>Ziel erkennen und normalisieren; bei ungültigen Angaben null und ein deutscher Fehlertext.</summary>
    ScanTarget? ParseTarget(string? raw, out string? error);

    /// <summary>Lokalen Pfad dieses Rechners (z. B. den einer Freigabe) vollständig machen; null, wenn er ungültig ist.</summary>
    string? FullLocalPath(string path);

    /// <summary>Alle Datenträger-Freigaben eines Servers („.“ = dieser Rechner), auch administrative.</summary>
    ShareListing EnumerateShares(string server);

    /// <summary>Eine einzelne Freigabe \server\name.</summary>
    ShareLookup LookupShare(string server, string share);

    /// <summary>Ob unter dem Zielpfad ein Ordner oder eine Datei liegt – oder warum er nicht erreichbar ist.</summary>
    TargetProbe Probe(string path);

    /// <summary>
    /// Besitzer, Vererbungsschutz und NTFS-Einträge lesen (SIDs, keine Namen). Fehler stehen in <see cref="FolderNode.Error"/>.
    /// </summary>
    /// <param name="followLinks">Verknüpfungen verfolgen (nur für ausdrücklich genannte Wurzeln).</param>
    /// <param name="parentDescriptor">Rohe Sicherheitsbeschreibung des Elternordners, wenn schon gelesen.</param>
    /// <param name="readParent">Ohne <paramref name="parentDescriptor"/> den Elternordner bei Bedarf selbst lesen.</param>
    /// <param name="wantDescriptor">Rohe Beschreibung dieses Objekts zurückgeben (für seine Kinder).</param>
    /// <param name="descriptor">Rohe Beschreibung, null bei Fehler oder wenn nicht gewünscht.</param>
    FolderNode ReadSecurity(string path, int depth, bool isFile, bool followLinks, byte[]? parentDescriptor, bool readParent,
        bool wantDescriptor, out byte[]? descriptor);

    /// <summary>Inhalt eines Ordners an <paramref name="entries"/> anhängen.</summary>
    /// <returns>null, wenn vollständig gelesen; sonst der Grund (bis dahin gelesene Einträge bleiben erhalten).</returns>
    string? ListDirectory(string directory, List<DirEntry> entries);
}
