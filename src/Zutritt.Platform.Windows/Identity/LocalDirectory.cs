using System.Runtime.InteropServices;
using System.Security.Principal;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.Platform.Windows.Identity;

/// <summary>
/// Lokale Kontendatenbank (SAM) dieses Rechners oder eines Servers: lokale Benutzer und Gruppen sowie die
/// VORDEFINIERT-Gruppen. Über NetApi statt <c>AccountManagement</c>, weil das bei großen Gruppen langsam ist
/// und Mitglieder als SID liefert, auch wenn sie sich nicht in Namen übersetzen lassen (verwaiste Einträge).
/// </summary>
public sealed class LocalDirectory : IAuthoritativeDirectory, IMachineDirectory
{
    // Ein nicht erreichbarer Server wird nicht bei jeder SID erneut gefragt (jede Anfrage kann Sekunden dauern).
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly WindowsAccountLookup _lsa;
    private AccountDomain? _accountDomain;
    private string? _accountDomainError;
    private DateTime _retryAtUtc;

    /// <summary>Lokale Kontendatenbank dieses Rechners.</summary>
    public LocalDirectory() : this(null) { }

    /// <summary>Lokale Kontendatenbank eines (entfernten) Servers, null oder leer = dieser Rechner.</summary>
    public LocalDirectory(string? server)
    {
        Server = MachineNames.Strip(server);
        _lsa = new WindowsAccountLookup(Server);
    }

    /// <summary>Servername, null = dieser Rechner.</summary>
    public string? Server { get; }

    /// <summary>Quelle für Fehlermeldungen, z. B. „\\FS01“.</summary>
    public string SourceName => WindowsPath.Server(Server ?? Environment.MachineName);

    /// <summary>Name der lokalen Kontendatenbank (= Rechnername), null wenn nicht lesbar.</summary>
    public string? AccountDomainName => GetAccountDomain(out _)?.Name;

    /// <summary>SID der lokalen Kontendatenbank (S-1-5-21-…), Präfix aller lokalen Konten; null wenn nicht lesbar.</summary>
    public string? AccountDomainSid => GetAccountDomain(out _)?.Sid;

    /// <summary>
    /// Der Rechner ist ein Domänencontroller: Seine Kontendatenbank ist die Domäne selbst. Dann antwortet dieses
    /// Verzeichnis nur für VORDEFINIERT-Gruppen; Domänenkonten und ihre Gruppen kommen aus dem AD.
    /// Null, wenn nicht lesbar.
    /// </summary>
    public bool? IsDomainController => GetAccountDomain(out _)?.IsDomain;

    private string? NetServer => Server is null ? null : WindowsPath.Server(Server);

    public Principal? Lookup(string sid) => Find(sid).Principal;

    public IReadOnlyList<string>? GetMembers(string groupSid) => FindMembers(groupSid).Members;

    LookupOutcome IAuthoritativeDirectory.Find(string sid) => Find(sid);

    MembersOutcome IAuthoritativeDirectory.FindMembers(string groupSid) => FindMembers(groupSid);

    internal LookupOutcome Find(string sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        if (!IsResponsible(sid, out var domainError)) return LookupOutcome.NotResponsible;
        if (domainError is not null) return LookupOutcome.Unreachable(SamProblem(domainError));

        var r = _lsa.LookupRaw(sid);
        if (r.Status == LookupStatus.NichtZugeordnet) return LookupOutcome.Missing;
        if (r.Status == LookupStatus.Fehler) return LookupOutcome.Unreachable(SamProblem(Native.Describe(r.Error)));

        var p = new Principal { Sid = sid, Name = r.FullName, DisplayName = r.Name, Kind = r.Kind, IsLocal = true };
        if (p.Kind == PrincipalKind.Verwaist)
        {
            p.Name = sid;
            p.DisplayName = "";
        }
        else if (p.Kind == PrincipalKind.Benutzer)
        {
            FillUserDetails(p, r.Name);
        }
        return LookupOutcome.Found(p);
    }

    internal MembersOutcome FindMembers(string groupSid)
    {
        ArgumentNullException.ThrowIfNull(groupSid);
        if (!IsResponsible(groupSid, out var domainError)) return MembersOutcome.NotResponsible;
        if (domainError is not null) return MembersOutcome.Unreachable(SamProblem(domainError));

        var r = _lsa.LookupRaw(groupSid);
        if (r.Status == LookupStatus.NichtZugeordnet) return MembersOutcome.Missing;
        if (r.Status == LookupStatus.Fehler) return MembersOutcome.Unreachable(SamProblem(Native.Describe(r.Error)));

        return r.Use switch
        {
            Native.SidNameUse.Alias => LocalGroupMembers(r.Name, r.FullName),
            // Globale Gruppen der lokalen SAM (z. B. „Kein“) enthalten nur lokale Benutzer.
            Native.SidNameUse.Group => GlobalGroupMembers(r.Name, r.FullName),
            _ => MembersOutcome.Missing,
        };
    }

    /// <summary>
    /// Zuständig für VORDEFINIERT-Gruppen und alle Konten mit der SID-Kennung dieser Kontendatenbank. Ist die
    /// Kontendatenbank nicht lesbar, kann jede Konto-SID von hier stammen – dann zuständig, aber mit Fehler.
    /// </summary>
    private bool IsResponsible(string sid, out string? domainError)
    {
        domainError = null;
        var builtin = WellKnownSids.IsBuiltin(sid);
        if (!builtin && !WellKnownSids.IsAccountSid(sid)) return false;
        var domain = GetAccountDomain(out domainError);
        if (domain is null) return true;
        if (builtin) return true;
        // NetGroupGetUsers kennt auf einem DC keine verschachtelten Gruppen – Domänenkonten deshalb dem AD überlassen.
        return !domain.IsDomain && string.Equals(WellKnownSids.AccountDomainOf(sid), domain.Sid, StringComparison.OrdinalIgnoreCase);
    }

    private unsafe AccountDomain? GetAccountDomain(out string? error)
    {
        lock (_gate)
        {
            error = null;
            if (_accountDomain is not null) return _accountDomain;
            if (DateTime.UtcNow < _retryAtUtc)
            {
                error = _accountDomainError;
                return null;
            }

            var rc = Native.NetUserModalsGet(NetServer, 2, out var buffer);
            try
            {
                if (rc == Native.ERROR_SUCCESS && buffer != 0)
                {
                    var info = (Native.USER_MODALS_INFO_2*)buffer;
                    var name = Marshal.PtrToStringUni(info->DomainName) ?? "";
                    var sid = new SecurityIdentifier(info->DomainId).Value;
                    _accountDomain = new AccountDomain(name, sid, IsDomainControllerCore(name));
                    return _accountDomain;
                }
            }
            finally
            {
                Native.Free(buffer);
            }

            _accountDomainError = Native.Describe(rc == Native.ERROR_SUCCESS ? Native.ERROR_BAD_NETPATH : rc);
            _retryAtUtc = DateTime.UtcNow + RetryAfterFailure;
            error = _accountDomainError;
            return null;
        }
    }

    /// <summary>
    /// DC-Erkennung über den Servertyp; falls der nicht lesbar ist: Auf einem DC heißt die Kontendatenbank wie die
    /// Domäne, auf allen anderen Rechnern wie der Rechner selbst.
    /// </summary>
    private unsafe bool IsDomainControllerCore(string accountDomainName)
    {
        var rc = Native.NetServerGetInfo(NetServer, 101, out var buffer);
        try
        {
            if (rc == Native.ERROR_SUCCESS && buffer != 0)
            {
                var info = (Native.SERVER_INFO_101*)buffer;
                return (info->Type & (Native.SV_TYPE_DOMAIN_CTRL | Native.SV_TYPE_DOMAIN_BAKCTRL)) != 0;
            }
        }
        finally
        {
            Native.Free(buffer);
        }
        var machine = (Server ?? Environment.MachineName).Split('.')[0];
        return !string.Equals(accountDomainName, machine, StringComparison.OrdinalIgnoreCase);
    }

    private unsafe void FillUserDetails(Principal p, string userName)
    {
        var rc = Native.NetUserGetInfo(NetServer, userName, 23, out var buffer);
        try
        {
            // Ohne Details bleibt das Konto gültig; nur Anzeigename und „deaktiviert“ fehlen dann.
            if (rc != Native.ERROR_SUCCESS || buffer == 0) return;
            var info = (Native.USER_INFO_23*)buffer;
            var fullName = Marshal.PtrToStringUni(info->FullName);
            if (!string.IsNullOrWhiteSpace(fullName)) p.DisplayName = fullName.Trim();
            p.IsDisabled = (info->Flags & Native.UF_ACCOUNTDISABLE) != 0;
        }
        finally
        {
            Native.Free(buffer);
        }
    }

    private unsafe MembersOutcome LocalGroupMembers(string groupName, string label)
    {
        var members = new List<string>();
        nuint resume = 0;
        while (true)
        {
            var rc = Native.NetLocalGroupGetMembers(NetServer, groupName, 0, out var buffer, Native.MAX_PREFERRED_LENGTH, out var read, out _, ref resume);
            try
            {
                if (rc == Native.NERR_GroupNotFound || rc == Native.ERROR_NO_SUCH_ALIAS) return MembersOutcome.Missing;
                if (rc != Native.ERROR_SUCCESS && rc != Native.ERROR_MORE_DATA)
                    return MembersOutcome.Unreachable(MembersProblem(label, rc));
                var entries = (Native.LOCALGROUP_MEMBERS_INFO_0*)buffer;
                for (var i = 0; i < read; i++)
                    if (entries[i].Sid != 0) members.Add(new SecurityIdentifier(entries[i].Sid).Value);
            }
            finally
            {
                Native.Free(buffer);
            }
            if (rc != Native.ERROR_MORE_DATA) return MembersOutcome.Found(members);
        }
    }

    private unsafe MembersOutcome GlobalGroupMembers(string groupName, string label)
    {
        var names = new List<string>();
        nuint resume = 0;
        while (true)
        {
            var rc = Native.NetGroupGetUsers(NetServer, groupName, 0, out var buffer, Native.MAX_PREFERRED_LENGTH, out var read, out _, ref resume);
            try
            {
                if (rc == Native.NERR_GroupNotFound) return MembersOutcome.Missing;
                if (rc != Native.ERROR_SUCCESS && rc != Native.ERROR_MORE_DATA)
                    return MembersOutcome.Unreachable(MembersProblem(label, rc));
                var entries = (Native.GROUP_USERS_INFO_0*)buffer;
                for (var i = 0; i < read; i++)
                {
                    var name = Marshal.PtrToStringUni(entries[i].Name);
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }
            finally
            {
                Native.Free(buffer);
            }
            if (rc != Native.ERROR_MORE_DATA) break;
        }

        // NetGroupGetUsers liefert nur Namen; die SID kommt aus dem Benutzerkonto selbst.
        var members = new List<string>(names.Count);
        foreach (var name in names)
        {
            var rc = Native.NetUserGetInfo(NetServer, name, 23, out var buffer);
            try
            {
                if (rc != Native.ERROR_SUCCESS || buffer == 0)
                    return MembersOutcome.Unreachable(MembersProblem(label, rc == Native.ERROR_SUCCESS ? Native.NERR_UserNotFound : rc));
                var info = (Native.USER_INFO_23*)buffer;
                if (info->UserSid != 0) members.Add(new SecurityIdentifier(info->UserSid).Value);
            }
            finally
            {
                Native.Free(buffer);
            }
        }
        return MembersOutcome.Found(members);
    }

    private sealed record AccountDomain(string Name, string Sid, bool IsDomain);

    private static string SamProblem(string error) =>
        Texts.Format(Strings.Local_Unreadable, error);

    private static string MembersProblem(string group, int code) =>
        Texts.Format(Strings.Local_MembersUnreadable, group, Native.Describe(code));
}
