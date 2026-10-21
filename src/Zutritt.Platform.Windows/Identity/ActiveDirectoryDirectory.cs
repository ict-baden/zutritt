// SPDX-License-Identifier: MIT
using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;
using Ad = System.DirectoryServices.AccountManagement;

namespace Zutritt.Platform.Windows.Identity;

/// <summary>
/// Konten, Gruppen und Computer einer Active-Directory-Domäne über <c>AccountManagement</c>. Antwortet nur für
/// SIDs dieser Domäne; Konten vertrauter Domänen löst der Resolver über die LSA auf. Ist kein Domänencontroller
/// erreichbar, meldet das Verzeichnis das ausdrücklich, damit gültige Konten nicht als „verwaist“ erscheinen.
/// </summary>
public sealed class ActiveDirectoryDirectory : IAuthoritativeDirectory, IDisposable
{
    // Ein Verbindungsversuch zu einem nicht erreichbaren DC kann lange dauern – nicht bei jeder SID wiederholen.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    // Mitglieder, die beim Aufzählen einer Gruppe schon geladen wurden: Der Resolver fragt sie gleich danach
    // einzeln ab – ohne diesen kurzen Zwischenspeicher wäre das bei großen Gruppen eine LDAP-Abfrage je Mitglied.
    private static readonly TimeSpan RecentLifetime = TimeSpan.FromMinutes(1);

    // Mehrere Fehler in Folge deuten auf eine gestörte Verbindung, auch wenn AccountManagement sie nicht als solche meldet.
    private const int FailuresUntilOffline = 3;

    private readonly object _gate = new();
    private Ad.PrincipalContext? _context;
    private string? _domainSid;
    private string? _netBiosName;
    private string? _lastError;
    private DateTime _retryAtUtc;
    private int _consecutiveFailures;
    // volatile: Dispose setzt es auch ohne _gate (siehe dort)
    private volatile bool _disposed;
    private readonly Dictionary<string, (Zutritt.Core.Model.Principal Principal, DateTime AtUtc)> _recent = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Domäne dieses Rechners; in einer Arbeitsgruppe ohne Funktion (<see cref="IsConfigured"/> = false).</summary>
    public ActiveDirectoryDirectory() : this(ComputerDomain.Query()) { }

    /// <summary>Bestimmte Domäne (DNS- oder NetBIOS-Name), z. B. für eine vertraute Domäne.</summary>
    public ActiveDirectoryDirectory(string domainName)
    {
        ArgumentNullException.ThrowIfNull(domainName);
        DomainName = domainName.Trim();
    }

    /// <summary>Domäne mit bekannter SID (aus <see cref="ComputerDomain.Query"/>); null = ohne Funktion.</summary>
    public ActiveDirectoryDirectory(ComputerDomainInfo? domain)
    {
        if (domain is null)
        {
            DomainName = "";
            return;
        }
        DomainName = string.IsNullOrEmpty(domain.DnsName) ? domain.NetBiosName : domain.DnsName;
        _netBiosName = domain.NetBiosName;
        _domainSid = domain.Sid;
    }

    public string DomainName { get; }

    /// <summary>Eine Domäne ist angegeben (sonst antwortet das Verzeichnis nie).</summary>
    public bool IsConfigured => DomainName.Length > 0;

    /// <summary>SID der Domäne, sobald bekannt. Ohne sie meldet das Verzeichnis nur Treffer, nie „gibt es nicht“.</summary>
    public string? DomainSid
    {
        get { lock (_gate) return _domainSid; }
    }

    string IAuthoritativeDirectory.SourceName => DomainName;

    public Zutritt.Core.Model.Principal? Lookup(string sid) => Find(sid).Principal;

    public IReadOnlyList<string>? GetMembers(string groupSid) => FindMembers(groupSid).Members;

    LookupOutcome IAuthoritativeDirectory.Find(string sid) => Find(sid);

    MembersOutcome IAuthoritativeDirectory.FindMembers(string groupSid) => FindMembers(groupSid);

    internal LookupOutcome Find(string sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        if (!IsConfigured || !WellKnownSids.IsAccountSid(sid)) return LookupOutcome.NotResponsible;
        lock (_gate)
        {
            try
            {
                return FindLocked(sid);
            }
            finally
            {
                CloseIfDisposed();
            }
        }
    }

    private LookupOutcome FindLocked(string sid)
    {
        var context = GetContext(out var error);
        // Ohne DC lässt sich weder ein Konto dieser Domäne noch eines einer vertrauten Domäne prüfen.
        if (context is null) return LookupOutcome.Unreachable(Problem(error));
        if (!MayBelongHere(sid)) return LookupOutcome.NotResponsible;
        // Einmal verwenden und vergessen: danach merkt sich der Resolver das Konto selbst.
        if (_recent.Remove(sid, out var recent) && DateTime.UtcNow - recent.AtUtc < RecentLifetime)
            return LookupOutcome.Found(recent.Principal);
        try
        {
            using var found = Ad.Principal.FindByIdentity(context, Ad.IdentityType.Sid, sid);
            var mapped = found is null ? null : Map(found, sid);
            _consecutiveFailures = 0;
            if (found is null) return _domainSid is null ? LookupOutcome.NotResponsible : LookupOutcome.Missing;
            return mapped is null ? LookupOutcome.NotResponsible : LookupOutcome.Found(mapped);
        }
        catch (Exception ex) when (IsDirectoryError(ex))
        {
            return LookupOutcome.Unreachable(Failed(ex)
                ? Problem(ex.Message)
                : Texts.Format(Strings.Ad_AccountUnreadable, sid, DomainName, ex.Message));
        }
    }

    internal MembersOutcome FindMembers(string groupSid)
    {
        ArgumentNullException.ThrowIfNull(groupSid);
        if (!IsConfigured || !WellKnownSids.IsAccountSid(groupSid)) return MembersOutcome.NotResponsible;
        lock (_gate)
        {
            try
            {
                return FindMembersLocked(groupSid);
            }
            finally
            {
                CloseIfDisposed();
            }
        }
    }

    private MembersOutcome FindMembersLocked(string groupSid)
    {
        var context = GetContext(out var error);
        if (context is null) return MembersOutcome.Unreachable(Problem(error));
        if (!MayBelongHere(groupSid)) return MembersOutcome.NotResponsible;
        string label = groupSid;
        try
        {
            using var group = Ad.GroupPrincipal.FindByIdentity(context, Ad.IdentityType.Sid, groupSid);
            if (group is null) return _domainSid is null ? MembersOutcome.NotResponsible : MembersOutcome.Missing;
            label = group.SamAccountName ?? group.Name ?? groupSid;

            // Nur direkte Mitglieder: die Verschachtelung löst der Resolver selbst auf (mit Zyklenschutz).
            // AccountManagement liefert dabei auch Mitglieder über die primäre Gruppe (z. B. Domänen-Benutzer).
            var members = new List<string>();
            ForgetExpired();
            using var result = group.GetMembers(false);
            foreach (var member in result)
            {
                using (member)
                {
                    if (member.Sid is null) continue;
                    members.Add(member.Sid.Value);
                    Remember(member);
                }
            }
            _consecutiveFailures = 0;
            return MembersOutcome.Found(members);
        }
        catch (Exception ex) when (IsDirectoryError(ex))
        {
            Failed(ex);
            // Teilweise gelesene Mitgliederlisten wären für ein Audit irreführend – lieber „unbekannt“.
            return MembersOutcome.Unreachable(Texts.Format(Strings.Ad_MembersUnreadable, label, DomainName, ex.Message));
        }
    }

    /// <summary>
    /// Blockiert nie: Hängt noch eine LDAP-Abfrage, auf die das Zeitlimit (<see cref="TimeBudget"/>) schon nicht mehr
    /// wartet, hält sie <c>_gate</c> – mit <c>lock</c> hinge dann das Ende des Scans (und die geplante Aufgabe) an einem
    /// DC, der nie antwortet. Dann schließt die Abfrage die Verbindung selbst, sobald sie zurückkommt.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            CloseIfDisposed();
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <summary>Nur mit <c>_gate</c>: Verbindung und Zwischenspeicher freigeben, wenn <see cref="Dispose"/> schon lief.</summary>
    private void CloseIfDisposed()
    {
        if (!_disposed) return;
        _context?.Dispose();
        _context = null;
        _recent.Clear();
    }

    private void Remember(Ad.Principal member)
    {
        var sid = member.Sid.Value;
        // Nur Konten dieser Domäne – nur für sie stimmt der Domänenname im Kontonamen.
        if (_domainSid is null || !MayBelongHere(sid)) return;
        try
        {
            var mapped = Map(member, sid);
            if (mapped is not null) _recent[sid] = (mapped, DateTime.UtcNow);
        }
        catch (Exception ex) when (IsDirectoryError(ex))
        {
            // Dann eben später einzeln nachschlagen.
        }
    }

    private void ForgetExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var sid in _recent.Where(kv => now - kv.Value.AtUtc >= RecentLifetime).Select(kv => kv.Key).ToList())
            _recent.Remove(sid);
    }

    private bool MayBelongHere(string sid) =>
        _domainSid is null || string.Equals(WellKnownSids.AccountDomainOf(sid), _domainSid, StringComparison.OrdinalIgnoreCase);

    private Zutritt.Core.Model.Principal? Map(Ad.Principal found, string sid)
    {
        PrincipalKind? kind = found switch
        {
            Ad.UserPrincipal => PrincipalKind.Benutzer,
            Ad.ComputerPrincipal => PrincipalKind.Computer,
            Ad.GroupPrincipal => PrincipalKind.Gruppe,
            _ => null,
        };
        if (kind is null) return null;

        var account = FirstNonEmpty(found.SamAccountName, found.Name) ?? sid;
        var netBios = NetBiosName();
        var display = FirstNonEmpty(found.DisplayName,
            found is Ad.UserPrincipal u ? $"{u.GivenName} {u.Surname}".Trim() : null,
            account);
        return new Zutritt.Core.Model.Principal
        {
            Sid = sid,
            Name = netBios is null ? account : $@"{netBios}\{account}",
            DisplayName = display ?? account,
            Kind = kind.Value,
            IsLocal = false,
            IsDisabled = found is Ad.AuthenticablePrincipal auth && auth.Enabled == false,
        };
    }

    private Ad.PrincipalContext? GetContext(out string? error)
    {
        error = null;
        if (_disposed)
        {
            error = Strings.Ad_Closed;
            return null;
        }
        if (_context is not null) return _context;
        if (DateTime.UtcNow < _retryAtUtc)
        {
            error = _lastError;
            return null;
        }

        Ad.PrincipalContext? context = null;
        try
        {
            context = new Ad.PrincipalContext(Ad.ContextType.Domain, DomainName);
            // Erzwingt die Verbindung zum DC jetzt statt beim ersten Suchaufruf.
            _ = context.ConnectedServer;
            _domainSid ??= ReadDomainSid();
            _context = context;
            return context;
        }
        catch (Exception ex) when (IsDirectoryError(ex))
        {
            context?.Dispose();
            _lastError = ex.Message;
            _retryAtUtc = DateTime.UtcNow + RetryAfterFailure;
            error = _lastError;
            return null;
        }
    }

    /// <summary>SID der Domäne aus dem Domänenobjekt; null, wenn nicht lesbar (dann nur positive Treffer).</summary>
    private string? ReadDomainSid()
    {
        try
        {
            using var root = new DirectoryEntry($"LDAP://{DomainName}");
            return root.Properties["objectSid"].Value is byte[] bytes ? new SecurityIdentifier(bytes, 0).Value : null;
        }
        catch (Exception ex) when (IsDirectoryError(ex))
        {
            return null;
        }
    }

    private string? NetBiosName()
    {
        if (_netBiosName is not null) return _netBiosName;
        // Notlösung: erster Teil des DNS-Namens – stimmt in den allermeisten Domänen.
        var first = DomainName.Split('.')[0];
        var fallback = first.Length == 0 ? null : first.ToUpperInvariant();
        if (_domainSid is null) return fallback;

        var r = new WindowsAccountLookup(null).LookupRaw(_domainSid);
        // Für eine Domänen-SID liefert Windows den Kurznamen je nach Version als Domäne oder als Namen.
        var name = r.Domain.Length > 0 ? r.Domain : r.Name;
        // Einmal nachschlagen genügt – sonst käme bei großen Gruppen eine LSA-Abfrage je Mitglied dazu.
        _netBiosName = r.Status == LookupStatus.Gefunden && r.Use == Native.SidNameUse.Domain && name.Length > 0 ? name : fallback;
        return _netBiosName;
    }

    /// <summary>
    /// Fehler verbuchen. Bei Verbindungsverlust (oder mehreren Fehlern in Folge) gilt das AD bis zum nächsten
    /// Verbindungsversuch als nicht erreichbar – sonst liefe jede weitere SID erneut in die Zeitüberschreitung.
    /// </summary>
    /// <returns>true, wenn das AD jetzt als nicht erreichbar gilt.</returns>
    private bool Failed(Exception ex)
    {
        _consecutiveFailures++;
        var offline = ex is Ad.PrincipalServerDownException or ActiveDirectoryServerDownException
            || _consecutiveFailures >= FailuresUntilOffline;
        if (!offline) return false;
        _context?.Dispose();
        _context = null;
        _consecutiveFailures = 0;
        _lastError = ex.Message;
        _retryAtUtc = DateTime.UtcNow + RetryAfterFailure;
        return true;
    }

    private string Problem(string? error) =>
        Texts.Format(Strings.Ad_Unreachable, DomainName, error ?? Strings.Identity_UnknownError);

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>Erwartbare Verzeichnisfehler (kein DC, keine Rechte, Netzwerk) – keine Programmierfehler.</summary>
    private static bool IsDirectoryError(Exception ex) => ex is Ad.PrincipalException
        or ExternalException // COMException, DirectoryServicesCOMException
        or ActiveDirectoryObjectNotFoundException
        or ActiveDirectoryOperationException
        or ActiveDirectoryServerDownException
        or System.DirectoryServices.Protocols.DirectoryException
        or InvalidOperationException
        or UnauthorizedAccessException
        or ArgumentException;
}
