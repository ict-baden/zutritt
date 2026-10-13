using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>Ergebnis einer Nachfrage bei einem Verzeichnis, das „gibt es nicht“ von „konnte nicht nachsehen“ unterscheidet.</summary>
internal enum LookupState
{
    /// <summary>Die SID gehört nicht in den Bereich dieses Verzeichnisses.</summary>
    NichtZustaendig,
    Gefunden,
    /// <summary>Zuständig, erreichbar, und das Konto bzw. die Gruppe existiert dort nicht.</summary>
    NichtVorhanden,
    /// <summary>Zuständig, aber nicht erreichbar oder nicht lesbar – über die SID lässt sich nichts sagen.</summary>
    NichtErreichbar,
}

internal sealed record LookupOutcome(LookupState State, Principal? Principal = null, string? Error = null)
{
    public static readonly LookupOutcome NotResponsible = new(LookupState.NichtZustaendig);
    public static readonly LookupOutcome Missing = new(LookupState.NichtVorhanden);
    public static LookupOutcome Found(Principal p) => new(LookupState.Gefunden, p);
    public static LookupOutcome Unreachable(string error) => new(LookupState.NichtErreichbar, Error: error);
}

internal sealed record MembersOutcome(LookupState State, IReadOnlyList<string>? Members = null, string? Error = null)
{
    public static readonly MembersOutcome NotResponsible = new(LookupState.NichtZustaendig);
    public static readonly MembersOutcome Missing = new(LookupState.NichtVorhanden);
    public static MembersOutcome Found(IReadOnlyList<string> members) => new(LookupState.Gefunden, members);
    public static MembersOutcome Unreachable(string error) => new(LookupState.NichtErreichbar, Error: error);
}

/// <summary>
/// Verzeichnis, das für einen Bereich von SIDs verbindlich Auskunft gibt. Erst damit lässt sich „verwaist“
/// (Konto gelöscht) sicher von „unbekannt“ (Domäne gerade nicht erreichbar) trennen – ein falsches „verwaist“
/// würde im Audit zum Löschen gültiger Einträge verleiten.
/// </summary>
internal interface IAuthoritativeDirectory : IDirectory
{
    /// <summary>Quelle für Fehlermeldungen, z. B. „\\FS01“ oder „lab.local“.</summary>
    string SourceName { get; }

    LookupOutcome Find(string sid);

    MembersOutcome FindMembers(string groupSid);
}

/// <summary>
/// Verzeichnis, das nur für einen Rechner gilt (dessen lokale Kontendatenbank). VORDEFINIERT-Gruppen haben auf
/// jedem Rechner dieselbe SID, aber andere Mitglieder – deshalb darf nur das Verzeichnis des gescannten Rechners antworten.
/// </summary>
internal interface IMachineDirectory : IDirectory
{
    /// <summary>Servername, null = dieser Rechner.</summary>
    string? Server { get; }
}
