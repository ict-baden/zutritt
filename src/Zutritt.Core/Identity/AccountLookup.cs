using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

internal enum LookupStatus
{
    Gefunden,
    /// <summary>Das System kennt zu dieser SID sicher kein Konto (unter Windows: ERROR_NONE_MAPPED).</summary>
    NichtZugeordnet,
    /// <summary>Jeder andere Fehler (Domäne/Server nicht erreichbar, Vertrauensstellung, Zugriff verweigert).</summary>
    Fehler,
}

/// <summary>Antwort der Kontenauflösung eines Systems (unter Windows die LSA) – ohne Fehlernummern des Systems.</summary>
/// <param name="Kind">Art des Kontos, wie das System sie meldet.</param>
/// <param name="Error">Bei <see cref="LookupStatus.Fehler"/>: deutscher Text für den Hinweis im Lauf.</param>
/// <param name="SourceDown">
/// Der Fehler betrifft die ganze Quelle (Server nicht erreichbar, Zugriff verweigert): Weitere Anfragen dort liefen nur
/// jeweils in dieselbe Zeitüberschreitung und werden deshalb in diesem Lauf nicht mehr gestellt.
/// </param>
internal readonly record struct AccountLookupResult(LookupStatus Status, string Domain, string Name, PrincipalKind Kind,
    string? Error = null, bool SourceDown = false)
{
    /// <summary>
    /// „DOMÄNE\name“ bzw. nur „name“, wenn das System keine Domäne liefert (z. B. „Jeder“), oder nur die Domäne,
    /// wenn die SID die einer Domäne ist.
    /// </summary>
    public string FullName => AccountName.Join(Domain, Name);

    /// <summary>Das System kennt die SID sicher nicht.</summary>
    public static AccountLookupResult NotMapped { get; } = new(LookupStatus.NichtZugeordnet, "", "", PrincipalKind.Unbekannt);

    /// <summary>Nachschlagen fehlgeschlagen.</summary>
    public static AccountLookupResult Failed(string error, bool sourceDown = false)
        => new(LookupStatus.Fehler, "", "", PrincipalKind.Unbekannt, error, sourceDown);
}

/// <summary>SID → Kontoname über die Kontenauflösung eines Rechners (unter Windows die LSA; austauschbar für Tests).</summary>
internal interface IAccountLookup
{
    /// <summary>Server, dessen Kontenauflösung gefragt wird, null = dieser Rechner.</summary>
    string? Server { get; }

    AccountLookupResult Lookup(string sid);
}
