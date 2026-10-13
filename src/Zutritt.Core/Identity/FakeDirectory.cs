using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>
/// Verzeichnis im Speicher – für Tests und die Demo ohne Domäne. Gibt immer Kopien heraus, damit der
/// Resolver nichts an den hinterlegten Daten ändern kann.
/// </summary>
public sealed class FakeDirectory : IDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Principal> _principals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _members = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Konto oder Gruppe hinterlegen (ersetzt einen vorhandenen Eintrag mit derselben SID).</summary>
    public FakeDirectory Add(string sid, string name, PrincipalKind kind, params string[] members)
    {
        var p = new Principal { Sid = sid, Name = name, DisplayName = name.Split('\\')[^1], Kind = kind };
        p.Members.AddRange(members);
        return Add(p);
    }

    /// <summary>Konto übernehmen (als Kopie). Bei Gruppen gelten <see cref="Principal.Members"/> als direkte Mitglieder.</summary>
    public FakeDirectory Add(Principal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        lock (_gate)
        {
            _principals[principal.Sid] = Copy(principal);
            if (principal.Kind == PrincipalKind.Gruppe || principal.Members.Count > 0)
                _members[principal.Sid] = [.. principal.Members];
            if (principal.MembersUnknown) _unreadable.Add(principal.Sid);
            else _unreadable.Remove(principal.Sid);
        }
        return this;
    }

    /// <summary>Weiteres direktes Mitglied einer Gruppe.</summary>
    public FakeDirectory AddMember(string groupSid, string memberSid)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(groupSid, out var list)) _members[groupSid] = list = [];
            if (!list.Contains(memberSid, StringComparer.OrdinalIgnoreCase)) list.Add(memberSid);
        }
        return this;
    }

    /// <summary>Mitglieder dieser Gruppe als „nicht lesbar“ melden (wie eine Gruppe in einer nicht erreichbaren Domäne).</summary>
    public FakeDirectory MarkMembersUnreadable(string groupSid)
    {
        lock (_gate) _unreadable.Add(groupSid);
        return this;
    }

    /// <summary>Verzeichnis aus den Konten eines vorhandenen Laufs (z. B. für die Demo mit Beispieldaten).</summary>
    public static FakeDirectory FromScan(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var fake = new FakeDirectory();
        foreach (var p in scan.Principals.Values) fake.Add(p);
        return fake;
    }

    public Principal? Lookup(string sid)
    {
        lock (_gate) return _principals.TryGetValue(sid, out var p) ? Copy(p) : null;
    }

    public IReadOnlyList<string>? GetMembers(string groupSid)
    {
        lock (_gate)
        {
            if (_unreadable.Contains(groupSid)) return null;
            if (_members.TryGetValue(groupSid, out var list)) return [.. list];
            // Bekannte Gruppe ohne hinterlegte Mitglieder = leere Gruppe; alles andere kennt dieses Verzeichnis nicht.
            return _principals.TryGetValue(groupSid, out var p) && p.Kind == PrincipalKind.Gruppe ? [] : null;
        }
    }

    private static Principal Copy(Principal p) => new()
    {
        Sid = p.Sid,
        Name = p.Name,
        DisplayName = p.DisplayName,
        Kind = p.Kind,
        IsLocal = p.IsLocal,
        IsDisabled = p.IsDisabled,
    };
}
