using System.Security.AccessControl;
using Zutritt.Core.Model;

namespace Zutritt.Core.Reports;

/// <summary>
/// Fertig aufbereiteter Inhalt eines Berichts: Namen statt SIDs, sortiert, effektive Rechte bereits
/// berechnet. Excel- und PDF-Bericht formatieren nur noch – so zeigen beide garantiert dasselbe, und
/// die Renderer lassen sich ohne Auswertungslogik testen. Erzeugt von <see cref="ReportDataBuilder"/>.
/// </summary>
internal sealed class ReportData
{
    public required ReportHeader Header { get; init; }
    /// <summary>Nach Schwere absteigend, dann Regel und Pfad.</summary>
    public required IReadOnlyList<FindingRow> Findings { get; init; }
    /// <summary>Alle Prüfregeln (der Prüfumfang) mit der Anzahl ihrer Befunde in diesem Lauf.</summary>
    public required IReadOnlyList<RuleRow> Rules { get; init; }
    /// <summary>Gespeicherte Ordner (Wurzeln, eigene Rechte, Fehler) in Baum-Reihenfolge.</summary>
    public required IReadOnlyList<FolderRow> Folders { get; init; }
    /// <summary>Alle Benutzerkonten mit ihren effektiven Zugriffen.</summary>
    public required IReadOnlyList<PersonRow> People { get; init; }
    public required IReadOnlyList<GroupRow> Groups { get; init; }
    public required IReadOnlyList<ShareRow> Shares { get; init; }
    public required IReadOnlyList<ScanError> Errors { get; init; }
    /// <summary>Hinweise zum Prüfumfang und Lesen des Berichts (Warnungen zuerst).</summary>
    public required IReadOnlyList<ReportNote> Notes { get; init; }
    /// <summary>Unterschiede zum vorherigen Lauf, null ohne Vergleich.</summary>
    public ComparisonData? Comparison { get; init; }

    public int CountFindings(Severity severity) => Findings.Count(f => f.Severity == severity);

    /// <summary>Ordner, die nicht gelesen werden konnten – dort ist nichts geprüft.</summary>
    public int UnreadableFolders => Folders.Count(f => f.Error is not null);

    public int PeopleWithAccess => People.Count(p => p.FolderCount > 0);

    /// <summary>Für mindestens eine Person fehlen Einzelzugriffe (Begrenzung gegen Speicherüberlauf).</summary>
    public bool PersonGrantsTruncated => People.Any(p => !p.GrantsComplete);
}

/// <summary>Kopfangaben: was, wann, wo, von wem, womit.</summary>
internal sealed record ReportHeader
{
    public required IReadOnlyList<string> Targets { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Machine { get; init; } = "";
    public string ScannedBy { get; init; } = "";
    /// <summary>Zutritt-Version, mit der gescannt wurde.</summary>
    public string ToolVersion { get; init; } = "";
    /// <summary>Zutritt-Version, die den Bericht erstellt hat.</summary>
    public string ReportVersion { get; init; } = "";
    public required ScanRequest Request { get; init; }
    public long FoldersVisited { get; init; }
    public long FilesVisited { get; init; }
    public int FoldersStored { get; init; }
    public int FilesStored { get; init; }
    public int Principals { get; init; }
}

internal sealed record FindingRow(
    Severity Severity, string RuleId, string Title, string Path, string Account, string? Sid, string Message, string Recommendation);

internal sealed record RuleRow(string Id, Severity Severity, string Title, string Explanation, string Recommendation, int Count);

internal sealed record FolderRow
{
    public required string Path { get; init; }
    public int Depth { get; init; }
    public bool IsFile { get; init; }
    public bool InheritanceBroken { get; init; }
    /// <summary>Geerbte Einträge passen nicht zum Elternordner – meist verschoben (siehe <see cref="FolderNode.InheritanceDiffers"/>, Regel ZT14).</summary>
    public bool InheritanceDiffers { get; init; }
    /// <summary>Name des Besitzers, leer wenn unbekannt.</summary>
    public string Owner { get; init; } = "";
    public string OwnerSid { get; init; } = "";
    public string? ShareUnc { get; init; }
    public string? Error { get; init; }
    /// <summary>In ACL-Reihenfolge (so wertet Windows sie aus).</summary>
    public IReadOnlyList<AceRow> Aces { get; init; } = [];
}

internal sealed record AceRow(
    string Account, string Sid, PrincipalKind Kind, AccessLevel Level, FileSystemRights Rights,
    bool IsDeny, bool IsInherited, string AppliesTo)
{
    /// <summary>Recht für Menschen; bei Verweigerungen, was gesperrt ist (siehe <see cref="Model.Rights.EntryLabel"/>).</summary>
    public string Right => Model.Rights.EntryLabel(Rights, IsDeny);
}

internal sealed record PersonRow
{
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";
    public required string Sid { get; init; }
    public bool IsDisabled { get; init; }
    /// <summary>Gespeicherte Ordner mit mindestens Auflisten.</summary>
    public int FolderCount { get; init; }
    public AccessLevel Highest { get; init; }
    /// <summary>Gruppen, über die die Person irgendwo Zugriff bekommt (alphabetisch).</summary>
    public IReadOnlyList<string> ViaGroups { get; init; } = [];
    /// <summary>Mindestens ein Recht ist direkt an die Person vergeben.</summary>
    public bool HasDirect { get; init; }
    /// <summary>Die einzelnen Zugriffe, nach Pfad sortiert (ggf. gekürzt, siehe <see cref="GrantsComplete"/>).</summary>
    public IReadOnlyList<PersonGrantRow> Grants { get; init; } = [];
    public bool GrantsComplete { get; init; } = true;
}

internal sealed record PersonGrantRow(string Path, AccessLevel Effective, AccessLevel Ntfs, AccessLevel? ViaShare, string Via, bool ReducedByDeny);

internal sealed record GroupRow
{
    public required string Name { get; init; }
    public required string Sid { get; init; }
    public int DirectCount { get; init; }
    public IReadOnlyList<string> DirectMembers { get; init; } = [];
    /// <summary>Benutzerkonten, die direkt oder über verschachtelte Gruppen Mitglied sind.</summary>
    public int TotalPeople { get; init; }
    /// <summary>Alle Mitglieder (auch verschachtelter Gruppen) waren lesbar.</summary>
    public bool Complete { get; init; } = true;
    /// <summary>Anzahl gespeicherter Ordner, in deren ACL die Gruppe selbst steht.</summary>
    public int UsedInFolders { get; init; }
    public string Note { get; init; } = "";
}

internal sealed record ShareRow
{
    public required string Unc { get; init; }
    public string LocalPath { get; init; } = "";
    public string Remark { get; init; } = "";
    public bool IsAdminShare { get; init; }
    public bool Readable { get; init; } = true;
    public IReadOnlyList<ShareAceRow> Permissions { get; init; } = [];
    /// <summary>Alle Einträge in einer Zeile, z. B. „Jeder: Lesen; VORDEFINIERT\Administratoren: Vollzugriff“.</summary>
    public required string PermissionsText { get; init; }
}

internal sealed record ShareAceRow(string Account, string Sid, ShareAccess Access, bool IsDeny)
{
    /// <summary>Recht für Menschen; Verweigern ausgeschrieben („Verweigert: alles (ab Lesen)“, siehe <see cref="Rights.DenyLabel"/>).</summary>
    public string Right => IsDeny ? Rights.DenyLabel(Access) : Rights.Label(Access);
}

internal enum NoteKind { Hinweis, Warnung }

internal sealed record ReportNote(NoteKind Kind, string Text);

/// <summary>Vergleich mit dem vorherigen Lauf, als Zeilen für eine gemeinsame Tabelle.</summary>
internal sealed record ComparisonData
{
    public DateTimeOffset OlderStartedAt { get; init; }
    public int NewFindings { get; init; }
    public int ResolvedFindings { get; init; }
    public int MoreAccess { get; init; }
    public int LessAccess { get; init; }
    public int AceChanges { get; init; }
    public int FoldersAdded { get; init; }
    public int FoldersRemoved { get; init; }
    public int MembershipChanges { get; init; }
    public required IReadOnlyList<ChangeRow> Rows { get; init; }
    public bool HasChanges => Rows.Count > 0;
}

/// <summary>Eine Änderung: Bereich (siehe <see cref="ChangeArea"/>), was, wo, wer, vorher/nachher.</summary>
internal sealed record ChangeRow(string Area, string Change, string Subject, string Account, string Before, string After);

/// <summary>
/// Bereiche der Änderungen in der Reihenfolge, in der die Berichte sie zeigen: erst die Befunde, dann die
/// Ursachen (Einträge, Mitgliedschaften, Ordner), zuletzt die Folgen für einzelne Personen – die sind oft
/// hundertfach und würden sonst das Wesentliche verdrängen.
/// </summary>
internal static class ChangeArea
{
    public const string Finding = "Befund";
    public const string Ace = "Berechtigung";
    public const string Membership = "Mitgliedschaft";
    public const string Folder = "Ordner";
    public const string Access = "Zugriff";

    public static IReadOnlyList<string> Order { get; } = [Finding, Ace, Membership, Folder, Access];
}

/// <summary>
/// Werte der Spalte „Änderung“: am Zellanfang groß wie „Hinzugefügt“/„Geändert“, damit die Spalte einheitlich aussieht
/// und der Excel-Filter gleiche Werte zusammenfasst.
/// </summary>
internal static class ChangeText
{
    public const string New = "Neu";
    public const string Resolved = "Behoben";
    public const string Joined = "Aufgenommen";
    public const string FolderAdded = "Neu mit eigenen Rechten";
    public const string FolderRemoved = "Keine eigenen Rechte mehr";
    public const string MoreAccess = "Mehr Zugriff";
    public const string LessAccess = "Weniger Zugriff";

    /// <summary>Mitgliedschaft: wer in eine Gruppe kommt, wird „aufgenommen“ – sonst wie Einträge.</summary>
    public static string Membership(ChangeKind kind) => kind == ChangeKind.Hinzugefuegt ? Joined : ReportText.Change(kind);
}
