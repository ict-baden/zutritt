using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>
/// Bekannte Sicherheitsprinzipale mit festen deutschen Namen (so, wie ein deutsches Windows sie anzeigt).
/// Damit sehen Berichte auf deutschen und englischen Installationen gleich aus und hängen für diese SIDs
/// nicht von der Namensauflösung ab.
/// </summary>
public static class WellKnownSids
{
    public const string Everyone = "S-1-1-0";
    public const string CreatorOwner = "S-1-3-0";
    public const string CreatorGroup = "S-1-3-1";
    public const string OwnerRights = "S-1-3-4";
    public const string Network = "S-1-5-2";
    public const string Interactive = "S-1-5-4";
    public const string AuthenticatedUsers = "S-1-5-11";
    public const string LocalSystem = "S-1-5-18";
    public const string LocalService = "S-1-5-19";
    public const string NetworkService = "S-1-5-20";
    public const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    public const string BuiltinAdministrators = "S-1-5-32-544";
    public const string BuiltinUsers = "S-1-5-32-545";
    public const string BuiltinGuests = "S-1-5-32-546";
    public const string BuiltinPowerUsers = "S-1-5-32-547";
    public const string BuiltinBackupOperators = "S-1-5-32-551";

    /// <summary>Präfix der VORDEFINIERT-Gruppen (Aliase der lokalen Kontendatenbank jedes Rechners).</summary>
    public const string BuiltinPrefix = "S-1-5-32-";

    private const string Nt = @"NT-AUTORITÄT\";
    private const string Builtin = @"VORDEFINIERT\";
    private const string AppPackages = @"ZERTIFIZIERUNGSSTELLE FÜR ANWENDUNGSPAKETE\";

    // Pseudo-Konten ohne Mitgliederliste: Windows bestimmt die Zugehörigkeit erst bei der Anmeldung.
    private static readonly Dictionary<string, string> Pseudo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-0-0"] = "NULL-SID",
        [Everyone] = "Jeder",
        ["S-1-2-0"] = "LOKAL",
        ["S-1-2-1"] = "KONSOLENANMELDUNG",
        [CreatorOwner] = "ERSTELLER-BESITZER",
        [CreatorGroup] = "ERSTELLERGRUPPE",
        ["S-1-3-2"] = "ERSTELLER-BESITZER-SERVER",
        ["S-1-3-3"] = "ERSTELLERGRUPPE-SERVER",
        [OwnerRights] = "BESITZERRECHTE",
        [Network] = Nt + "NETZWERK",
        ["S-1-5-3"] = Nt + "BATCH",
        [Interactive] = Nt + "INTERAKTIV",
        ["S-1-5-6"] = Nt + "DIENST",
        ["S-1-5-7"] = Nt + "ANONYMOUS-ANMELDUNG",
        ["S-1-5-9"] = Nt + "DOMÄNENCONTROLLER DER ORGANISATION",
        ["S-1-5-10"] = Nt + "SELBST",
        [AuthenticatedUsers] = Nt + "Authentifizierte Benutzer",
        ["S-1-5-12"] = Nt + "EINGESCHRÄNKT",
        ["S-1-5-13"] = Nt + "TERMINALSERVERBENUTZER",
        ["S-1-5-14"] = Nt + "REMOTE INTERAKTIVE ANMELDUNG",
        ["S-1-5-15"] = Nt + "Diese Organisation",
        ["S-1-5-17"] = Nt + "IUSR",
        [LocalSystem] = Nt + "SYSTEM",
        [LocalService] = Nt + "LOKALER DIENST",
        [NetworkService] = Nt + "NETZWERKDIENST",
        ["S-1-5-64-10"] = Nt + "NTLM-Authentifizierung",
        ["S-1-5-113"] = Nt + "Lokales Konto",
        ["S-1-5-114"] = Nt + "Lokales Konto und Mitglied der Gruppe „Administratoren“",
        ["S-1-5-1000"] = Nt + "Andere Organisation",
        ["S-1-5-80-0"] = @"NT SERVICE\ALLE DIENSTE",
        [TrustedInstaller] = @"NT SERVICE\TrustedInstaller",
        ["S-1-15-2-1"] = AppPackages + "ALLE ANWENDUNGSPAKETE",
        ["S-1-15-2-2"] = AppPackages + "ALLE EINGESCHRÄNKTEN ANWENDUNGSPAKETE",
    };

    // VORDEFINIERT-Gruppen sind echte Gruppen der lokalen Kontendatenbank mit Mitgliedern (Kind = Gruppe).
    // Einige gibt es nur auf Servern oder DCs (z. B. die RDS-Gruppen); Windows trägt sie trotzdem in die ACLs
    // von Systemordnern auch auf Arbeitsplatzrechnern ein – deshalb stehen sie hier, statt als „verwaist“ aufzufallen.
    private static readonly Dictionary<string, string> BuiltinGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        [BuiltinAdministrators] = Builtin + "Administratoren",
        [BuiltinUsers] = Builtin + "Benutzer",
        [BuiltinGuests] = Builtin + "Gäste",
        [BuiltinPowerUsers] = Builtin + "Hauptbenutzer",
        ["S-1-5-32-548"] = Builtin + "Konten-Operatoren",
        ["S-1-5-32-549"] = Builtin + "Server-Operatoren",
        ["S-1-5-32-550"] = Builtin + "Druck-Operatoren",
        [BuiltinBackupOperators] = Builtin + "Sicherungs-Operatoren",
        ["S-1-5-32-552"] = Builtin + "Replikations-Operator",
        ["S-1-5-32-554"] = Builtin + "Prä-Windows 2000 kompatibler Zugriff",
        ["S-1-5-32-555"] = Builtin + "Remotedesktopbenutzer",
        ["S-1-5-32-556"] = Builtin + "Netzwerkkonfigurations-Operatoren",
        ["S-1-5-32-557"] = Builtin + "Erstellungen eingehender Gesamtstrukturvertrauensstellung",
        ["S-1-5-32-558"] = Builtin + "Leistungsüberwachungsbenutzer",
        ["S-1-5-32-559"] = Builtin + "Leistungsprotokollbenutzer",
        ["S-1-5-32-560"] = Builtin + "Windows-Autorisierungszugriffsgruppe",
        ["S-1-5-32-561"] = Builtin + "Terminalserver-Lizenzserver",
        ["S-1-5-32-562"] = Builtin + "Distributed COM-Benutzer",
        ["S-1-5-32-568"] = Builtin + "IIS_IUSRS",
        ["S-1-5-32-569"] = Builtin + "Kryptografie-Operatoren",
        ["S-1-5-32-573"] = Builtin + "Ereignisprotokollleser",
        ["S-1-5-32-574"] = Builtin + "Zertifikatdienst-DCOM-Zugriff",
        ["S-1-5-32-575"] = Builtin + "RDS-Remotezugriffsserver",
        ["S-1-5-32-576"] = Builtin + "RDS-Endpunktserver",
        ["S-1-5-32-577"] = Builtin + "RDS-Verwaltungsserver",
        ["S-1-5-32-578"] = Builtin + "Hyper-V-Administratoren",
        ["S-1-5-32-579"] = Builtin + "Zugriffssteuerungs-Unterstützungsoperatoren",
        ["S-1-5-32-580"] = Builtin + "Remoteverwaltungsbenutzer",
    };

    /// <summary>
    /// Fester deutscher Name und Art einer bekannten SID. Pseudo-Konten (Jeder, SYSTEM …) sind
    /// <see cref="PrincipalKind.Bekannt"/>, VORDEFINIERT-Gruppen <see cref="PrincipalKind.Gruppe"/>.
    /// </summary>
    public static bool TryGet(string sid, out string name, out PrincipalKind kind)
    {
        if (Pseudo.TryGetValue(sid, out var n))
        {
            (name, kind) = (n, PrincipalKind.Bekannt);
            return true;
        }
        if (BuiltinGroups.TryGetValue(sid, out n))
        {
            (name, kind) = (n, PrincipalKind.Gruppe);
            return true;
        }
        (name, kind) = ("", PrincipalKind.Unbekannt);
        return false;
    }

    /// <summary>
    /// VORDEFINIERT-Gruppe: gleiche SID auf jedem Rechner, aber Mitglieder je Rechner verschieden. Diese SIDs legt
    /// Windows selbst fest – fehlt eine auf einem Rechner, wurde kein Konto gelöscht; sie ist deshalb nie „verwaist“.
    /// Fähigkeitsgruppen (<see cref="IsCapabilityGroup"/>) sehen nur so aus und gehören nicht dazu.
    /// </summary>
    public static bool IsBuiltin(string sid) => sid.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase) && !IsCapabilityGroup(sid);

    /// <summary>
    /// Fähigkeitsgruppe (S-1-5-32-x-y-…): Windows leitet sie zusammen mit der Fähigkeits-SID S-1-15-3-… aus dem Namen
    /// einer Fähigkeit ab (DeriveCapabilitySidsFromName). Keine VORDEFINIERT-Gruppe, ohne Mitglieder und ohne Namen.
    /// </summary>
    public static bool IsCapabilityGroup(string sid)
        => sid.StartsWith(BuiltinPrefix, StringComparison.OrdinalIgnoreCase) && sid.AsSpan(BuiltinPrefix.Length).Contains('-');

    /// <summary>
    /// Virtuelles Konto, das Windows für Dienste, IIS-Anwendungspools, virtuelle Computer, Treiber, geplante Aufgaben,
    /// den Fenster-Manager (DWM-1 …) oder den Schriftartentreiber (UMFD-0 …) anlegt. Gehört nie einer Person – auch
    /// wenn Windows manche davon als „Benutzer“ meldet.
    /// </summary>
    public static bool IsVirtualAccount(string sid)
    {
        if (!sid.StartsWith("S-1-5-", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = sid.AsSpan(6);
        int dash = rest.IndexOf('-');
        if (dash < 0) return false;
        return rest[..dash] is "80" or "82" or "83" or "84" or "87" or "90" or "96";
    }

    /// <summary>Konto einer Domäne oder einer lokalen Kontendatenbank (S-1-5-21-…).</summary>
    public static bool IsAccountSid(string sid) => sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Domänenteil einer Konto-SID (S-1-5-21-a-b-c), null bei anderen SIDs.</summary>
    public static string? AccountDomainOf(string sid)
    {
        if (!IsAccountSid(sid)) return null;
        return SidText.AccountDomainOf(sid);
    }

    /// <summary>
    /// Name für SIDs, die keine Konten sind und deshalb nie „verwaist“ sein können (App-Fähigkeiten und
    /// Fähigkeitsgruppen, Anwendungspakete, Verbindlichkeitsstufen, Sitzungen, virtuelle Konten). Null für alle anderen.
    /// Windows kann viele davon nicht in Namen übersetzen; als verwaist gemeldet wären sie Fehlalarme.
    /// </summary>
    /// <remarks>
    /// Dienst-SIDs (NT SERVICE\…) leitet Windows aus dem Dienstnamen ab; übersetzen lassen sie sich nur, solange der
    /// Dienst installiert ist. Windows trägt in seine eigenen Ordner auch Dienste ein, die es auf diesem Rechner (noch)
    /// nicht gibt – das ist kein gelöschtes Konto, und den Eintrag zu entfernen wäre der falsche Rat. Wird der Dienst
    /// installiert, gilt der Eintrag wieder.
    /// </remarks>
    public static string? NonAccountName(string sid)
    {
        static bool Has(string s, string prefix) => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        if (Has(sid, "S-1-15-3-")) return $"Anwendungsfähigkeit ({sid})";
        if (Has(sid, "S-1-15-2-")) return $"Anwendungspaket ({sid})";
        if (IsCapabilityGroup(sid)) return $"Fähigkeitsgruppe ({sid})";
        if (Has(sid, "S-1-16-")) return $"Verbindlichkeitsstufe ({sid})";
        if (Has(sid, "S-1-5-5-")) return $"Anmeldesitzung ({sid})";
        if (Has(sid, "S-1-5-80-")) return $"Nicht installierter Dienst ({sid})";
        if (Has(sid, "S-1-5-82-")) return $"IIS-Anwendungspool ({sid})";
        if (Has(sid, "S-1-5-83-")) return $"Virtueller Computer ({sid})";
        if (Has(sid, "S-1-5-84-")) return $"Treiber im Benutzermodus ({sid})";
        if (Has(sid, "S-1-5-87-")) return $"Geplante Aufgabe ({sid})";
        if (Has(sid, "S-1-5-90-") || Has(sid, "S-1-5-96-")) return $"Virtuelles Systemkonto ({sid})";
        return null;
    }

    /// <summary>
    /// SIDs, deren Konten sich von hier aus grundsätzlich nicht prüfen lassen: Entra-ID-Konten (S-1-12-1-…)
    /// kennt Windows nur, wenn sie sich hier schon angemeldet haben. Nicht übersetzbar heißt dann „unbekannt“, nicht „verwaist“.
    /// </summary>
    public static bool IsUnverifiable(string sid) => sid.StartsWith("S-1-12-", StringComparison.OrdinalIgnoreCase);
}
