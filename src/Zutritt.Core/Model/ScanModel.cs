// SPDX-License-Identifier: MIT
namespace Zutritt.Core.Model;

/// <summary>Was gescannt werden soll.</summary>
public sealed record ScanRequest
{
    /// <summary>
    /// Ziele: lokaler Ordner (C:\Daten), UNC-Pfad (\\server\freigabe), ganzer Server (\\server
    /// = alle Datenträger-Freigaben) oder „.“ für alle Freigaben dieses Rechners.
    /// </summary>
    public required IReadOnlyList<string> Targets { get; init; }

    /// <summary>Auch Dateien mit eigenen (nicht geerbten) Rechten erfassen. Standard: nur Ordner.</summary>
    public bool IncludeFiles { get; init; }

    /// <summary>Maximale Tiefe unter dem Ziel, null = unbegrenzt.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Administrative Freigaben (C$, ADMIN$, IPC$, print$) überspringen.</summary>
    public bool SkipAdminShares { get; init; } = true;

    /// <summary>Gruppen bis zur Person auflösen (lokal und, falls erreichbar, im AD).</summary>
    public bool ResolveGroups { get; init; } = true;

    /// <summary>Ab dieser Tiefe gilt unterbrochene Vererbung als „tief versteckte Ausnahme“.</summary>
    public int DeepInheritanceBreakDepth { get; init; } = 3;
}

/// <summary>Ein vollständiger Scan-Lauf.</summary>
public sealed class ScanResult
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    /// <summary>Rechner, auf dem gescannt wurde, und Person, die gescannt hat (DOMÄNE\name).</summary>
    public string Machine { get; set; } = Environment.MachineName;
    public string ScannedBy { get; set; } = "";
    public string ToolVersion { get; set; } = "";
    public ScanRequest Request { get; set; } = new() { Targets = [] };

    public List<ShareInfo> Shares { get; } = [];
    /// <summary>
    /// Nur Ordner/Dateien mit eigener Berechtigung (explizite Einträge, unterbrochene Vererbung, andere geerbte Rechte
    /// als vom Elternordner zu erwarten), mit Fehler und alle Wurzeln.
    /// </summary>
    public List<FolderNode> Folders { get; } = [];
    /// <summary>Alle vorkommenden Konten/Gruppen, Schlüssel = SID.</summary>
    public Dictionary<string, Principal> Principals { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Finding> Findings { get; } = [];
    public List<ScanError> Errors { get; } = [];

    /// <summary>Gezählte Ordner insgesamt (auch die ohne eigene Rechte, die nicht gespeichert werden).</summary>
    public long FoldersVisited { get; set; }
    public long FilesVisited { get; set; }

    /// <summary>
    /// Ziele und Freigaben, die gar nicht erst geprüft werden konnten, weil sie keine Wurzel ergaben: ungültiges Ziel,
    /// Freigabeliste eines Servers nicht lesbar oder ohne Datenträger-Freigaben, lokaler Pfad einer Freigabe unbekannt.
    /// Der Grund steht in <see cref="Errors"/>. Nur im frischen Lauf gesetzt und bewusst nicht gespeichert (deshalb
    /// internal) – dient der Zusammenfassung der Befehlszeile (<see cref="Analysis.ScanCoverage"/>).
    /// </summary>
    internal List<string> TargetsWithoutRoot { get; } = [];
}

/// <summary>Eine SMB-Freigabe.</summary>
public sealed record ShareInfo
{
    public required string Server { get; init; }
    public required string Name { get; init; }
    /// <summary>Lokaler Pfad auf dem Server (z. B. D:\Daten), soweit bekannt.</summary>
    public string LocalPath { get; init; } = "";
    /// <summary>UNC-Pfad \\server\name.</summary>
    public string UncPath => WindowsPath.Unc(Server, Name);
    public string Remark { get; init; } = "";
    public bool IsAdminShare { get; init; }
    /// <summary>Freigabe-Berechtigungen. Leer und <see cref="PermissionsReadable"/> = false, wenn nicht lesbar.</summary>
    public List<ShareAce> Permissions { get; init; } = [];
    public bool PermissionsReadable { get; init; } = true;
}

/// <summary>Ein Eintrag der Freigabe-Berechtigungen (SMB), vereinfacht auf eine <see cref="ShareAccess"/>-Stufe.</summary>
/// <param name="Sid">Konto oder Gruppe.</param>
/// <param name="Access">
/// Erlauben (<paramref name="IsDeny"/> = false): die höchste Stufe, die der Eintrag gewährt.
/// <para>
/// Verweigern (<paramref name="IsDeny"/> = true): die <b>niedrigste Stufe, ab der verweigert wird</b> – diese Stufe und
/// alle höheren sind gesperrt, die darunter bleiben möglich. Die Stufen bauen aufeinander auf (Ändern enthält Lesen,
/// Vollzugriff enthält Ändern), deshalb lässt sich jede Sperre so beschreiben:
/// Verweigern „Vollzugriff“ der Windows-Oberfläche (0x1F01FF, alle drei Häkchen) sperrt schon das Lesen und steht als
/// <see cref="ShareAccess.Lesen"/> hier (kein Zugriff über die Freigabe), Verweigern „Ändern“ (0x10116) als
/// <see cref="ShareAccess.Aendern"/> (Lesen bleibt erlaubt), ein Eintrag, der nur „Berechtigungen ändern“ und „Besitz
/// übernehmen“ sperrt, als <see cref="ShareAccess.Vollzugriff"/> (Ändern bleibt erlaubt). Einträge, die keine Stufe
/// vollständig sperren (etwa nur „Löschen“), legt der Scanner gar nicht erst an – sonst sähe der Bericht weniger
/// Zugriff, als tatsächlich besteht.
/// </para>
/// </param>
/// <param name="IsDeny">Verweigern- statt Erlauben-Eintrag.</param>
public sealed record ShareAce(string Sid, ShareAccess Access, bool IsDeny);

/// <summary>Ein Ordner (oder eine Datei) mit Berechtigungen.</summary>
public sealed class FolderNode
{
    public long Id { get; set; }
    /// <summary>Voller Pfad, wie gescannt (lokal oder UNC).</summary>
    public required string Path { get; init; }
    /// <summary>Tiefe unter der Scan-Wurzel, Wurzel = 0.</summary>
    public int Depth { get; init; }
    public bool IsFile { get; init; }
    public bool IsRoot => Depth == 0;
    /// <summary>Zugehörige Freigabe (UNC), falls der Pfad über eine Freigabe erreichbar ist.</summary>
    public string? ShareUnc { get; set; }
    public string OwnerSid { get; set; } = "";
    /// <summary>Vererbung von oben ist abgeschaltet (Schutz der DACL).</summary>
    public bool InheritanceBroken { get; init; }
    /// <summary>
    /// Die geerbten Einträge passen nicht zu dem, was der übergeordnete Ordner vererbt – typisch für verschobene
    /// Ordner und Dateien, die ihre alten geerbten Rechte mitgenommen haben. Nur gesetzt, wenn der Scanner das sicher
    /// feststellen konnte: nie bei Wurzeln, unterbrochener Vererbung oder einem nicht lesbaren Elternordner.
    /// </summary>
    public bool InheritanceDiffers { get; set; }
    public bool HasExplicitAces => Aces.Any(a => !a.IsInherited);
    public List<AceEntry> Aces { get; } = [];
    /// <summary>Der Ordner konnte nicht gelesen werden (Zugriff verweigert, Pfad zu lang …).</summary>
    public string? Error { get; set; }
}

/// <summary>Ein Eintrag der NTFS-Zugriffssteuerungsliste.</summary>
public sealed record AceEntry
{
    public required string Sid { get; init; }
    public required FileRights Rights { get; init; }
    public bool IsDeny { get; init; }
    public bool IsInherited { get; init; }
    public AceInheritance Inheritance { get; init; }
    public AcePropagation Propagation { get; init; }
    public AccessLevel Level => Model.Rights.ToLevel(Rights);
    /// <summary>Gilt nur für Unterordner/Dateien, nicht für diesen Ordner selbst.</summary>
    public bool InheritOnly => (Propagation & AcePropagation.InheritOnly) != 0;
}

public enum PrincipalKind
{
    Benutzer,
    Gruppe,
    /// <summary>Bekannte Sicherheitsprinzipale wie Jeder, SYSTEM, ERSTELLER-BESITZER.</summary>
    Bekannt,
    Computer,
    /// <summary>SID ohne Konto (gelöschtes Konto, fremde Domäne).</summary>
    Verwaist,
    Unbekannt,
}

/// <summary>Konto oder Gruppe.</summary>
public sealed class Principal
{
    public required string Sid { get; init; }
    /// <summary>DOMÄNE\name bzw. bei verwaisten SIDs die SID selbst.</summary>
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public PrincipalKind Kind { get; set; } = PrincipalKind.Unbekannt;
    /// <summary>Lokales Konto dieses Rechners bzw. des gescannten Servers (statt Domäne).</summary>
    public bool IsLocal { get; set; }
    public bool IsDisabled { get; set; }
    /// <summary>Direkte Mitglieder (SIDs), nur bei Gruppen und nur wenn auflösbar.</summary>
    public List<string> Members { get; } = [];
    /// <summary>Mitglieder konnten nicht gelesen werden (z. B. kein AD erreichbar).</summary>
    public bool MembersUnknown { get; set; }
    public string Label => string.IsNullOrEmpty(Name) ? Sid : Name;
}

public enum Severity
{
    Info = 0,
    Niedrig = 1,
    Mittel = 2,
    Hoch = 3,
}

/// <summary>Ein Befund, z. B. „Jeder hat Schreibrecht“.</summary>
public sealed record Finding
{
    /// <summary>Regel-ID, z. B. ZT01. Stabil über Versionen, damit Vergleiche funktionieren.</summary>
    public required string RuleId { get; init; }
    public required Severity Severity { get; init; }
    public required string Path { get; init; }
    /// <summary>Betroffenes Konto (SID), falls die Regel sich auf eines bezieht.</summary>
    public string? Sid { get; init; }
    /// <summary>Kurzer deutscher Satz für Menschen.</summary>
    public required string Message { get; init; }
    /// <summary>Stabiler Schlüssel für den Vergleich zweier Läufe.</summary>
    public string Key => $"{RuleId}|{Path}|{Sid}".ToUpperInvariant();
}

public sealed record ScanError(string Path, string Message);

/// <summary>Fortschritt für Oberfläche und Befehlszeile.</summary>
public sealed record ScanProgress(string Phase, string CurrentPath, long FoldersVisited, long FoldersStored, int Errors);
