using System.Globalization;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.Core.Identity;

/// <summary>
/// Bekannte Sicherheitsprinzipale mit festen Namen in der Sprache der Oberfläche (so, wie ein Windows dieser Sprache sie
/// anzeigt). Damit hängen Berichte für diese SIDs nicht von der Sprache des scannenden Windows und der Namensauflösung ab.
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

    // Namen aus Strings.resx (Schlüssel Sid_…): so, wie Windows sie in der Sprache der Oberfläche anzeigt. Vorangestellt
    // wird die Quelle („NT-AUTORITÄT\“, „VORDEFINIERT\“ …), ebenfalls übersetzt.
    private enum Source { None, Nt, Builtin, AppPackages, NtService }

    // Pseudo-Konten ohne Mitgliederliste: Windows bestimmt die Zugehörigkeit erst bei der Anmeldung.
    private static readonly Dictionary<string, (Source Source, string Key)> Pseudo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-0-0"] = (Source.None, nameof(Strings.Sid_NullSid)),
        [Everyone] = (Source.None, nameof(Strings.Sid_Everyone)),
        ["S-1-2-0"] = (Source.None, nameof(Strings.Sid_Local)),
        ["S-1-2-1"] = (Source.None, nameof(Strings.Sid_ConsoleLogon)),
        [CreatorOwner] = (Source.None, nameof(Strings.Sid_CreatorOwner)),
        [CreatorGroup] = (Source.None, nameof(Strings.Sid_CreatorGroup)),
        ["S-1-3-2"] = (Source.None, nameof(Strings.Sid_CreatorOwnerServer)),
        ["S-1-3-3"] = (Source.None, nameof(Strings.Sid_CreatorGroupServer)),
        [OwnerRights] = (Source.None, nameof(Strings.Sid_OwnerRights)),
        [Network] = (Source.Nt, nameof(Strings.Sid_Network)),
        ["S-1-5-3"] = (Source.Nt, nameof(Strings.Sid_Batch)),
        [Interactive] = (Source.Nt, nameof(Strings.Sid_Interactive)),
        ["S-1-5-6"] = (Source.Nt, nameof(Strings.Sid_Service)),
        ["S-1-5-7"] = (Source.Nt, nameof(Strings.Sid_AnonymousLogon)),
        ["S-1-5-9"] = (Source.Nt, nameof(Strings.Sid_EnterpriseDomainControllers)),
        ["S-1-5-10"] = (Source.Nt, nameof(Strings.Sid_Self)),
        [AuthenticatedUsers] = (Source.Nt, nameof(Strings.Sid_AuthenticatedUsers)),
        ["S-1-5-12"] = (Source.Nt, nameof(Strings.Sid_Restricted)),
        ["S-1-5-13"] = (Source.Nt, nameof(Strings.Sid_TerminalServerUser)),
        ["S-1-5-14"] = (Source.Nt, nameof(Strings.Sid_RemoteInteractiveLogon)),
        ["S-1-5-15"] = (Source.Nt, nameof(Strings.Sid_ThisOrganization)),
        ["S-1-5-17"] = (Source.Nt, "IUSR"),
        [LocalSystem] = (Source.Nt, "SYSTEM"),
        [LocalService] = (Source.Nt, nameof(Strings.Sid_LocalService)),
        [NetworkService] = (Source.Nt, nameof(Strings.Sid_NetworkService)),
        ["S-1-5-64-10"] = (Source.Nt, nameof(Strings.Sid_NtlmAuthentication)),
        ["S-1-5-113"] = (Source.Nt, nameof(Strings.Sid_LocalAccount)),
        ["S-1-5-114"] = (Source.Nt, nameof(Strings.Sid_LocalAdminAccount)),
        ["S-1-5-1000"] = (Source.Nt, nameof(Strings.Sid_OtherOrganization)),
        ["S-1-5-80-0"] = (Source.NtService, nameof(Strings.Sid_AllServices)),
        [TrustedInstaller] = (Source.NtService, "TrustedInstaller"),
        ["S-1-15-2-1"] = (Source.AppPackages, nameof(Strings.Sid_AllAppPackages)),
        ["S-1-15-2-2"] = (Source.AppPackages, nameof(Strings.Sid_AllRestrictedAppPackages)),
    };

    // VORDEFINIERT-Gruppen sind echte Gruppen der lokalen Kontendatenbank mit Mitgliedern (Kind = Gruppe).
    // Einige gibt es nur auf Servern oder DCs (z. B. die RDS-Gruppen); Windows trägt sie trotzdem in die ACLs
    // von Systemordnern auch auf Arbeitsplatzrechnern ein – deshalb stehen sie hier, statt als „verwaist“ aufzufallen.
    private static readonly Dictionary<string, string> BuiltinGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        [BuiltinAdministrators] = nameof(Strings.Sid_Administrators),
        [BuiltinUsers] = nameof(Strings.Sid_Users),
        [BuiltinGuests] = nameof(Strings.Sid_Guests),
        [BuiltinPowerUsers] = nameof(Strings.Sid_PowerUsers),
        ["S-1-5-32-548"] = nameof(Strings.Sid_AccountOperators),
        ["S-1-5-32-549"] = nameof(Strings.Sid_ServerOperators),
        ["S-1-5-32-550"] = nameof(Strings.Sid_PrintOperators),
        [BuiltinBackupOperators] = nameof(Strings.Sid_BackupOperators),
        ["S-1-5-32-552"] = nameof(Strings.Sid_Replicator),
        ["S-1-5-32-554"] = nameof(Strings.Sid_PreWindows2000),
        ["S-1-5-32-555"] = nameof(Strings.Sid_RemoteDesktopUsers),
        ["S-1-5-32-556"] = nameof(Strings.Sid_NetworkConfigurationOperators),
        ["S-1-5-32-557"] = nameof(Strings.Sid_IncomingForestTrustBuilders),
        ["S-1-5-32-558"] = nameof(Strings.Sid_PerformanceMonitorUsers),
        ["S-1-5-32-559"] = nameof(Strings.Sid_PerformanceLogUsers),
        ["S-1-5-32-560"] = nameof(Strings.Sid_WindowsAuthorizationAccess),
        ["S-1-5-32-561"] = nameof(Strings.Sid_TerminalServerLicenseServers),
        ["S-1-5-32-562"] = nameof(Strings.Sid_DistributedComUsers),
        ["S-1-5-32-568"] = "IIS_IUSRS",
        ["S-1-5-32-569"] = nameof(Strings.Sid_CryptographicOperators),
        ["S-1-5-32-573"] = nameof(Strings.Sid_EventLogReaders),
        ["S-1-5-32-574"] = nameof(Strings.Sid_CertificateServiceDcomAccess),
        ["S-1-5-32-575"] = nameof(Strings.Sid_RdsRemoteAccessServers),
        ["S-1-5-32-576"] = nameof(Strings.Sid_RdsEndpointServers),
        ["S-1-5-32-577"] = nameof(Strings.Sid_RdsManagementServers),
        ["S-1-5-32-578"] = nameof(Strings.Sid_HyperVAdministrators),
        ["S-1-5-32-579"] = nameof(Strings.Sid_AccessControlAssistanceOperators),
        ["S-1-5-32-580"] = nameof(Strings.Sid_RemoteManagementUsers),
    };

    /// <summary>Text zu einem Schlüssel aus Strings.resx; Namen, die in jeder Sprache gleich sind (SYSTEM, IUSR), stehen direkt da.</summary>
    private static string Text(string keyOrName) =>
        keyOrName.StartsWith("Sid_", StringComparison.Ordinal)
            ? Strings.ResourceManager.GetString(keyOrName, CultureInfo.CurrentUICulture) ?? keyOrName
            : keyOrName;

    private static string Name(Source source, string key) => source switch
    {
        Source.Nt => Strings.Sid_PrefixNt,
        Source.Builtin => Strings.Sid_PrefixBuiltin,
        Source.AppPackages => Strings.Sid_PrefixAppPackages,
        Source.NtService => @"NT SERVICE\",
        _ => "",
    } + Text(key);

    /// <summary>
    /// Fester Name (in der Sprache der Oberfläche, wie Windows ihn dort zeigt) und Art einer bekannten SID. Pseudo-Konten
    /// (Jeder, SYSTEM …) sind <see cref="PrincipalKind.Bekannt"/>, VORDEFINIERT-Gruppen <see cref="PrincipalKind.Gruppe"/>.
    /// </summary>
    public static bool TryGet(string sid, out string name, out PrincipalKind kind)
    {
        if (Pseudo.TryGetValue(sid, out var p))
        {
            (name, kind) = (Name(p.Source, p.Key), PrincipalKind.Bekannt);
            return true;
        }
        if (BuiltinGroups.TryGetValue(sid, out var key))
        {
            (name, kind) = (Name(Source.Builtin, key), PrincipalKind.Gruppe);
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
        if (Has(sid, "S-1-15-3-")) return Texts.Format(Strings.Sid_AppCapability, sid);
        if (Has(sid, "S-1-15-2-")) return Texts.Format(Strings.Sid_AppPackage, sid);
        if (IsCapabilityGroup(sid)) return Texts.Format(Strings.Sid_CapabilityGroup, sid);
        if (Has(sid, "S-1-16-")) return Texts.Format(Strings.Sid_IntegrityLevel, sid);
        if (Has(sid, "S-1-5-5-")) return Texts.Format(Strings.Sid_LogonSession, sid);
        if (Has(sid, "S-1-5-80-")) return Texts.Format(Strings.Sid_ServiceNotInstalled, sid);
        if (Has(sid, "S-1-5-82-")) return Texts.Format(Strings.Sid_IisAppPool, sid);
        if (Has(sid, "S-1-5-83-")) return Texts.Format(Strings.Sid_VirtualMachine, sid);
        if (Has(sid, "S-1-5-84-")) return Texts.Format(Strings.Sid_UserModeDriver, sid);
        if (Has(sid, "S-1-5-87-")) return Texts.Format(Strings.Sid_ScheduledTask, sid);
        if (Has(sid, "S-1-5-90-") || Has(sid, "S-1-5-96-")) return Texts.Format(Strings.Sid_VirtualSystemAccount, sid);
        return null;
    }

    /// <summary>
    /// SIDs, deren Konten sich von hier aus grundsätzlich nicht prüfen lassen: Entra-ID-Konten (S-1-12-1-…)
    /// kennt Windows nur, wenn sie sich hier schon angemeldet haben. Nicht übersetzbar heißt dann „unbekannt“, nicht „verwaist“.
    /// </summary>
    public static bool IsUnverifiable(string sid) => sid.StartsWith("S-1-12-", StringComparison.OrdinalIgnoreCase);
}
