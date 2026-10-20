// SPDX-License-Identifier: MIT
namespace Zutritt.Core.Model;

/// <summary>
/// Effektives Recht eines Kontos auf einen Ordner – „wer darf hier rein“ bzw. „wo darf Max hin“.
/// </summary>
public sealed record AccessGrant
{
    public required string Path { get; init; }
    /// <summary>Das Konto, um das es geht (bei der Personen-Sicht die Person, sonst der Eintrag in der ACL).</summary>
    public required string Sid { get; init; }
    /// <summary>NTFS-Recht nach Abzug von Verweigerungen.</summary>
    public required AccessLevel Ntfs { get; init; }
    /// <summary>Über die Freigabe (Minimum aus Freigabe und NTFS), null wenn der Ordner unter keiner Freigabe liegt.</summary>
    public AccessLevel? ViaShare { get; init; }
    /// <summary>Über welche Gruppen das Recht kommt, z. B. ["LAB\QC-Team", "LAB\Labor-Alle"]. Leer = direkt.</summary>
    public IReadOnlyList<string> Via { get; init; } = [];
    /// <summary>Mindestens ein passender Verweigern-Eintrag hat Rechte entzogen.</summary>
    public bool ReducedByDeny { get; init; }
    public AccessLevel Effective => ViaShare ?? Ntfs;
}

/// <summary>Unterschiede zwischen zwei Läufen über dieselben Ziele.</summary>
public sealed class ComparisonResult
{
    public required ScanResult Older { get; init; }
    public required ScanResult Newer { get; init; }

    /// <summary>
    /// Einstellungen, in denen sich die Läufe unterscheiden (z. B. nur einer mit Dateien). Nicht leer: verglichen ist
    /// nur der gemeinsame Umfang, siehe <see cref="Analysis.ScanScope"/>.
    /// </summary>
    public IReadOnlyList<Analysis.ScopeDifference> ScopeDifferences { get; init; } = [];

    /// <summary>Die Läufe hatten einen anderen Umfang – Oberfläche und Berichte weisen darauf hin.</summary>
    public bool ScopeDiffers => ScopeDifferences.Count > 0;

    public List<AceChange> AceChanges { get; } = [];
    /// <summary>Ordner, die neu eigene Rechte haben bzw. keine mehr haben.</summary>
    public List<string> FoldersAdded { get; } = [];
    public List<string> FoldersRemoved { get; } = [];
    public List<Finding> NewFindings { get; } = [];
    public List<Finding> ResolvedFindings { get; } = [];
    /// <summary>Wer irgendwo mehr bzw. weniger darf als vorher (effektiv, nach Gruppenauflösung).</summary>
    public List<AccessChange> AccessChanges { get; } = [];
    /// <summary>Gruppenmitgliedschaften, die dazugekommen oder weggefallen sind.</summary>
    public List<MembershipChange> MembershipChanges { get; } = [];

    public bool HasChanges => AceChanges.Count + FoldersAdded.Count + FoldersRemoved.Count + NewFindings.Count
        + ResolvedFindings.Count + AccessChanges.Count + MembershipChanges.Count > 0;
}

public enum ChangeKind { Hinzugefuegt, Entfernt, Geaendert }

public sealed record AceChange(string Path, ChangeKind Kind, string Sid, AceEntry? Before, AceEntry? After);

public sealed record AccessChange(string Path, string Sid, AccessLevel Before, AccessLevel After);

public sealed record MembershipChange(string GroupSid, string MemberSid, ChangeKind Kind);

/// <summary>Kurzinfo für die Liste der gespeicherten Läufe.</summary>
/// <param name="Folders">Gespeicherte Ordner (Wurzeln, eigene Rechte, Fehler) – nicht alle geprüften.</param>
/// <param name="FoldersVisited">Alle geprüften Ordner, wie <see cref="ScanResult.FoldersVisited"/>.</param>
public sealed record ScanSummary(long Id, DateTimeOffset StartedAt, string Targets, int Folders, int Findings, int HighFindings, string ScannedBy,
    long FoldersVisited = 0);

/// <summary>Beschreibung einer Prüfregel für Oberfläche, Bericht und Doku.</summary>
public sealed record RuleInfo(string Id, Severity Severity, string Title, string Explanation, string Recommendation);
