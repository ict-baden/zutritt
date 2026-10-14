namespace Zutritt.Core.Analysis;

/// <summary>
/// Bekannte SIDs und ihre Einordnung („breit“, „admin-artig“, Platzhalter) für Rechteberechnung und Prüfregeln.
/// </summary>
public static class SidCatalog
{
    public const string Everyone = "S-1-1-0";
    public const string NullSid = "S-1-0-0";
    public const string CreatorOwner = "S-1-3-0";
    public const string CreatorGroup = "S-1-3-1";
    public const string CreatorOwnerServer = "S-1-3-2";
    public const string CreatorGroupServer = "S-1-3-3";
    public const string OwnerRights = "S-1-3-4";
    public const string Network = "S-1-5-2";
    public const string Interactive = "S-1-5-4";
    public const string AnonymousLogon = "S-1-5-7";
    public const string AuthenticatedUsers = "S-1-5-11";
    public const string Restricted = "S-1-5-12";
    public const string WriteRestricted = "S-1-5-33";
    public const string LocalSystem = "S-1-5-18";
    public const string LocalService = "S-1-5-19";
    public const string NetworkService = "S-1-5-20";
    public const string Administrators = "S-1-5-32-544";
    public const string Users = "S-1-5-32-545";
    public const string Guests = "S-1-5-32-546";
    public const string PowerUsers = "S-1-5-32-547";
    public const string BackupOperators = "S-1-5-32-551";
    public const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    private const int RidAdministrator = 500;
    private const int RidDomainAdmins = 512;
    private const int RidDomainUsers = 513;
    private const int RidDomainGuests = 514;
    private const int RidEnterpriseAdmins = 519;

    private static readonly HashSet<string> BroadFixed = new(StringComparer.OrdinalIgnoreCase)
    {
        Everyone, AuthenticatedUsers, Users, Guests, Interactive, Network,
    };

    private static readonly HashSet<string> AdminLikeFixed = new(StringComparer.OrdinalIgnoreCase)
    {
        LocalSystem, Administrators, TrustedInstaller, CreatorOwner, BackupOperators,
    };

    // Fallback, falls ein Prinzipal nicht in ScanResult.Principals steht (z. B. implizites „Jeder“).
    private static readonly Dictionary<string, string> GermanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [Everyone] = "Jeder",
        [NullSid] = "NULL SID",
        [CreatorOwner] = "ERSTELLER-BESITZER",
        [CreatorGroup] = "ERSTELLERGRUPPE",
        [CreatorOwnerServer] = "ERSTELLER-BESITZER-SERVER",
        [CreatorGroupServer] = "ERSTELLERGRUPPE-SERVER",
        [OwnerRights] = "BESITZERRECHTE",
        [Network] = @"NT-AUTORITÄT\NETZWERK",
        [Interactive] = @"NT-AUTORITÄT\INTERAKTIV",
        [AnonymousLogon] = @"NT-AUTORITÄT\ANONYMOUS-ANMELDUNG",
        [AuthenticatedUsers] = @"NT-AUTORITÄT\Authentifizierte Benutzer",
        [LocalSystem] = @"NT-AUTORITÄT\SYSTEM",
        [LocalService] = @"NT-AUTORITÄT\LOKALER DIENST",
        [NetworkService] = @"NT-AUTORITÄT\NETZWERKDIENST",
        [Administrators] = @"VORDEFINIERT\Administratoren",
        [Users] = @"VORDEFINIERT\Benutzer",
        [Guests] = @"VORDEFINIERT\Gäste",
        [PowerUsers] = @"VORDEFINIERT\Hauptbenutzer",
        [BackupOperators] = @"VORDEFINIERT\Sicherungs-Operatoren",
        [TrustedInstaller] = @"NT SERVICE\TrustedInstaller",
    };

    /// <summary>
    /// „Breite Gruppe“: umfasst praktisch alle Personen (Jeder, Authentifizierte Benutzer, VORDEFINIERT\Benutzer
    /// und \Gäste, Domänen-Benutzer und -Gäste, INTERAKTIV, NETZWERK).
    /// </summary>
    public static bool IsBroad(string sid) =>
        BroadFixed.Contains(sid) || DomainRid(sid) is RidDomainUsers or RidDomainGuests;

    /// <summary>
    /// „Admin-artig“: SYSTEM, Administratoren, Domänen- und Organisations-Admins, TrustedInstaller,
    /// Sicherungs-Operatoren und der Platzhalter ERSTELLER-BESITZER.
    /// </summary>
    public static bool IsAdminLike(string sid) =>
        AdminLikeFixed.Contains(sid) || DomainRid(sid) is RidDomainAdmins or RidEnterpriseAdmins;

    /// <summary>Das eingebaute Administrator-Konto (RID 500) eines Rechners oder einer Domäne.</summary>
    public static bool IsBuiltinAdministrator(string sid) => DomainRid(sid) == RidAdministrator;

    /// <summary>
    /// Platzhalter, die nur beim Vererben an Kindobjekte durch den Ersteller ersetzt werden und selbst nie
    /// in einem Zugriffstoken stehen.
    /// </summary>
    public static bool IsCreatorPlaceholder(string sid) =>
        sid.Equals(CreatorOwner, StringComparison.OrdinalIgnoreCase)
        || sid.Equals(CreatorGroup, StringComparison.OrdinalIgnoreCase)
        || sid.Equals(CreatorOwnerServer, StringComparison.OrdinalIgnoreCase)
        || sid.Equals(CreatorGroupServer, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sonder-Identität statt Konto oder Gruppe: Jeder, NT-AUTORITÄT\… (eine Unterautorität), Dienst-SIDs und andere
    /// virtuelle Konten, Platzhalter, App-Pakete und Fähigkeiten, Integritätsstufen. Domänen- und VORDEFINIERT-SIDs
    /// gehören nicht dazu – wohl aber die Fähigkeitsgruppen, die nur so aussehen (S-1-5-32-x-y-…).
    /// </summary>
    internal static bool IsSpecialIdentity(string sid)
    {
        var parts = sid.Split('-');
        if (parts.Length < 3 || !parts[0].Equals("S", StringComparison.OrdinalIgnoreCase) || parts[1] != "1") return false;
        switch (parts[2])
        {
            case "0" or "1" or "2" or "3" or "15" or "16" or "18":
                return true;
            case "5":
                if (parts.Length == 4) return true; // S-1-5-18, S-1-5-11 …
                if (IsCapabilityGroup(sid)) return true;
                // Anmeldesitzung (S-1-5-5-x-y), Dienste, Authentifizierungspakete, „Lokales Konto“, IIS, VMs, Treiber,
                // geplante Aufgaben, Fenster-Manager, Schriftartentreiber
                return parts[3] is "5" or "64" or "80" or "82" or "83" or "84" or "87" or "90" or "96" or "113" or "114";
            default:
                return false;
        }
    }

    /// <summary>
    /// Fähigkeitsgruppe (S-1-5-32-x-y-…, mehr als eine Zahl nach 32): Windows leitet sie wie die Fähigkeits-SIDs
    /// S-1-15-3-… aus dem Namen einer Fähigkeit ab. Sie sieht aus wie eine VORDEFINIERT-Gruppe, hat aber keine
    /// Mitglieder und lässt sich nicht in einen Namen übersetzen.
    /// </summary>
    internal static bool IsCapabilityGroup(string sid)
        => sid.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase) && sid.AsSpan(9).Contains('-');

    /// <summary>Echte VORDEFINIERT-Gruppe dieses Rechners (S-1-5-32-RID), keine Fähigkeitsgruppe.</summary>
    internal static bool IsBuiltinGroup(string sid)
        => sid.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase) && !IsCapabilityGroup(sid);

    /// <summary>
    /// Kennung, die nur Windows selbst in Zugriffslisten einträgt – nie ein Administrator auf Datenordnern:
    /// TrustedInstaller, App-Pakete und Fähigkeiten (S-1-15-2-…, S-1-15-3-…), Fähigkeitsgruppen, EINGESCHRÄNKT (S-1-5-12)
    /// und SCHREIBGESCHÜTZT (S-1-5-33). Dienst-SIDs gehören bewusst nicht dazu: Die vergeben Administratoren auch selbst.
    /// </summary>
    internal static bool IsWindowsInternal(string sid)
        => sid.Equals(TrustedInstaller, StringComparison.OrdinalIgnoreCase)
           || sid.StartsWith("S-1-15-2-", StringComparison.OrdinalIgnoreCase)
           || sid.StartsWith("S-1-15-3-", StringComparison.OrdinalIgnoreCase)
           || IsCapabilityGroup(sid)
           || sid.Equals(Restricted, StringComparison.OrdinalIgnoreCase)
           || sid.Equals(WriteRestricted, StringComparison.OrdinalIgnoreCase);

    /// <summary>Deutscher Anzeigename für bekannte SIDs, sonst null.</summary>
    public static string? GermanName(string sid) => GermanNames.GetValueOrDefault(sid);

    /// <summary>RID einer Konto-SID der Form S-1-5-21-a-b-c-RID (Domäne oder Rechner), sonst null.</summary>
    public static int? DomainRid(string sid)
    {
        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = sid.Split('-');
        // S, 1, 5, 21, a, b, c, RID
        if (parts.Length != 8) return null;
        return int.TryParse(parts[7], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rid)
            ? rid : null;
    }
}
