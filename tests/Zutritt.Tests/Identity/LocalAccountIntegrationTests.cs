using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Identity;

/// <summary>
/// Mit echten lokalen Testkonten und -gruppen (zt-test-…), die jeder Test selbst anlegt und wieder löscht.
/// Vorhandene Konten und Gruppen bleiben unberührt.
/// </summary>
public class LocalAccountIntegrationTests
{
    [AdminFact]
    public void Lokale_Gruppe_mit_Benutzern_wird_bis_zur_Person_aufgeloest()
    {
        using var accounts = new LocalAccounts();
        var group = accounts.CreateGroup();
        var anna = accounts.CreateUser(fullName: "Anna Test");
        var bert = accounts.CreateUser(disabled: true);
        LocalAccounts.AddMember(group, anna);
        LocalAccounts.AddMember(group, bert);
        LocalAccounts.AddMember(group, @"NT AUTHORITY\INTERACTIVE");
        var groupSid = LocalAccounts.SidOf(group);
        var annaSid = LocalAccounts.SidOf(anna);
        var bertSid = LocalAccounts.SidOf(bert);

        var scan = Scans.On(@"C:\Daten", groupSid);
        using var resolver = IdentityResolver.CreateDefault();
        resolver.ResolveAll(scan, expandGroups: true);

        var g = scan.Principals[groupSid];
        Assert.Equal(PrincipalKind.Gruppe, g.Kind);
        Assert.True(g.IsLocal);
        Assert.Equal($@"{Environment.MachineName}\{group}", g.Name, ignoreCase: true);
        Assert.Equal(group, g.DisplayName);
        Assert.False(g.MembersUnknown);
        Assert.Equal(new[] { annaSid, bertSid, WellKnownSids.Interactive }.Order(StringComparer.OrdinalIgnoreCase), g.Members);

        var a = scan.Principals[annaSid];
        Assert.Equal(PrincipalKind.Benutzer, a.Kind);
        Assert.Equal("Anna Test", a.DisplayName);
        Assert.Equal($@"{Environment.MachineName}\{anna}", a.Name, ignoreCase: true);
        Assert.True(a.IsLocal);
        Assert.False(a.IsDisabled);

        var b = scan.Principals[bertSid];
        Assert.True(b.IsDisabled);
        Assert.Equal(bert, b.DisplayName);

        Assert.Equal(PrincipalKind.Bekannt, scan.Principals[WellKnownSids.Interactive].Kind);
        Assert.Empty(scan.Errors);
    }

    [AdminFact]
    public void Leere_lokale_Gruppe_ist_leer_nicht_unbekannt()
    {
        using var accounts = new LocalAccounts();
        var groupSid = LocalAccounts.SidOf(accounts.CreateGroup());
        var scan = Scans.On(@"C:\Daten", groupSid);

        using var resolver = IdentityResolver.CreateDefault();
        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Empty(scan.Principals[groupSid].Members);
        Assert.False(scan.Principals[groupSid].MembersUnknown);
    }

    [AdminFact]
    public void Geloeschter_Benutzer_wird_als_verwaist_erkannt()
    {
        using var accounts = new LocalAccounts();
        var group = accounts.CreateGroup();
        var user = accounts.CreateUser(fullName: "Wird Gelöscht");
        LocalAccounts.AddMember(group, user);
        var groupSid = LocalAccounts.SidOf(group);
        var userSid = LocalAccounts.SidOf(user);
        // Wie eine ACL, in der das Konto direkt und über die Gruppe berechtigt ist.
        var scan = Scans.OwnedBy(@"C:\Daten", userSid, groupSid, userSid);
        using var resolver = IdentityResolver.CreateDefault();

        resolver.ResolveAll(scan, expandGroups: true);
        Assert.Equal(PrincipalKind.Benutzer, scan.Principals[userSid].Kind);
        Assert.Equal([userSid], scan.Principals[groupSid].Members);
        Assert.Equal(PrincipalKind.Benutzer, resolver.Resolve(userSid).Kind);

        accounts.DeleteUser(user);

        // Derselbe Resolver: ein neuer Lauf darf nichts aus dem alten Zwischenspeicher nehmen.
        resolver.ResolveAll(scan, expandGroups: true);
        var orphan = scan.Principals[userSid];
        Assert.Equal(PrincipalKind.Verwaist, orphan.Kind);
        Assert.Equal(userSid, orphan.Name);
        Assert.Empty(scan.Principals[groupSid].Members);
        Assert.Empty(scan.Errors);

        using var fresh = IdentityResolver.CreateDefault();
        Assert.Equal(PrincipalKind.Verwaist, fresh.Resolve(userSid).Kind);
        Assert.Equal(PrincipalKind.Verwaist, new IdentityResolver([new LocalDirectory()]).Resolve(userSid).Kind);
    }

    [AdminFact]
    public void Lokale_Gruppe_ueber_localhost_als_entfernter_Server()
    {
        using var accounts = new LocalAccounts();
        var group = accounts.CreateGroup();
        var user = accounts.CreateUser(disabled: true);
        LocalAccounts.AddMember(group, user);
        var groupSid = LocalAccounts.SidOf(group);
        var userSid = LocalAccounts.SidOf(user);

        // Über die Netzwerk-Schnittstelle (RPC) statt direkt: wie bei einem Dateiserver.
        var remote = new LocalDirectory("localhost");
        Assert.Equal([userSid], remote.GetMembers(groupSid));
        var p = remote.Lookup(userSid);
        Assert.NotNull(p);
        Assert.Equal(PrincipalKind.Benutzer, p.Kind);
        Assert.True(p.IsDisabled);
    }
}
