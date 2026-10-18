using System.Security.AccessControl;
using System.Security.Principal;
using Zutritt.Platform.Windows;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;
using Zutritt.Platform.Windows.Identity;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Scanning;

namespace Zutritt.Tests.Windows.Identity;

/// <summary>Echte Auflösung auf diesem Rechner (Arbeitsgruppe): Windows-LSA, lokale Kontendatenbank, echte ACLs.</summary>
public class IdentityResolverIntegrationTests
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    [Fact]
    public void Standard_ohne_Domaene_nur_lokal()
    {
        // Dieser Testrechner ist in keiner Domäne (WORKGROUP).
        if (ComputerDomain.Query() is not null) return;
        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();

        var p = resolver.Resolve(Me.Value);

        Assert.Equal(PrincipalKind.Benutzer, p.Kind);
        Assert.Equal(WindowsIdentity.GetCurrent().Name, p.Name, ignoreCase: true);
    }

    [Theory]
    [InlineData(WellKnownSids.Everyone, "Jeder", PrincipalKind.Bekannt)]
    [InlineData(WellKnownSids.LocalSystem, @"NT-AUTORITÄT\SYSTEM", PrincipalKind.Bekannt)]
    [InlineData(WellKnownSids.TrustedInstaller, @"NT SERVICE\TrustedInstaller", PrincipalKind.Bekannt)]
    [InlineData(WellKnownSids.BuiltinAdministrators, @"VORDEFINIERT\Administratoren", PrincipalKind.Gruppe)]
    public void Bekannte_SIDs_haben_deutsche_Namen_auch_auf_englischem_Windows(string sid, string name, PrincipalKind kind)
    {
        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        var p = resolver.Resolve(sid);

        Assert.Equal(name, p.Name);
        Assert.Equal(kind, p.Kind);
    }

    [Fact]
    public void Unbekannte_Domaenen_SID_ist_auf_einem_Arbeitsgruppenrechner_verwaist()
    {
        if (ComputerDomain.Query() is not null) return;
        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        var sid = $"S-1-5-21-{Random.Shared.Next(1, int.MaxValue)}-{Random.Shared.Next(1, int.MaxValue)}-{Random.Shared.Next(1, int.MaxValue)}-1105";

        var p = resolver.Resolve(sid);

        Assert.Equal(PrincipalKind.Verwaist, p.Kind);
        Assert.Equal(sid, p.Name);
    }

    [Fact]
    public void Nicht_vorhandenes_lokales_Konto_ist_verwaist()
    {
        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        var sid = new LocalDirectory().AccountDomainSid + "-987655";

        Assert.Equal(PrincipalKind.Verwaist, resolver.Resolve(sid).Kind);
    }

    [Fact]
    public void Dienst_SID_eines_nicht_installierten_Dienstes_ist_nicht_verwaist_sondern_bekannt()
    {
        // Windows trägt in seine Ordner auch Dienste ein, die hier nicht installiert sind (gefunden in
        // C:\ProgramData\Microsoft\Network\Connections und C:\Windows\System32\SleepStudy) – kein gelöschtes Konto.
        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();

        var service = resolver.Resolve("S-1-5-80-1111111111-2222222222-3333333333-4444444444-1234567890");
        Assert.Equal(PrincipalKind.Bekannt, service.Kind);
        Assert.StartsWith("Nicht installierter Dienst", service.Name);
        var capability = resolver.Resolve("S-1-15-3-1024-1065365936-1281604716-3511738428-1654721687-432734479-3232135806-4053264122-3456934681");
        Assert.Equal(PrincipalKind.Bekannt, capability.Kind);
    }

    [Theory]
    // Fähigkeitsgruppe aus C:\ProgramData\Microsoft\Provisioning – sah aus wie eine VORDEFINIERT-Gruppe und war „Unbekannt“
    [InlineData("S-1-5-32-989523358-1667815365-316973287-129935811-3358257576-2796868252-2781088163-715581871", "Fähigkeitsgruppe")]
    // Fenster-Manager und Schriftartentreiber: Windows meldet sie (falls die Sitzung läuft) als Benutzer
    [InlineData("S-1-5-90-0-1", null)]
    [InlineData("S-1-5-96-0-0", null)]
    [InlineData("S-1-5-87-1-2-3-4-5", "Geplante Aufgabe")]
    public void Windows_eigene_Kennungen_sind_weder_Personen_noch_verwaist(string sid, string? namePrefix)
    {
        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        var p = resolver.Resolve(sid);
        Assert.Equal(PrincipalKind.Bekannt, p.Kind);
        if (namePrefix is not null) Assert.StartsWith(namePrefix, p.Name);
        Assert.Empty(p.Members);
    }

    [Fact]
    public void VORDEFINIERT_SIDs_ohne_Gruppe_auf_diesem_Rechner_sind_nie_verwaist()
    {
        // S-1-5-32-575…577 (RDS-Gruppen) gibt es nur auf Servern; Windows trägt sie trotzdem in Systemordner ein.
        var scan = Scans.On(@"C:\Daten", "S-1-5-32-575", "S-1-5-32-576", "S-1-5-32-577", "S-1-5-32-999");

        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Equal(@"VORDEFINIERT\RDS-Remotezugriffsserver", scan.Principals["S-1-5-32-575"].Name);
        Assert.Equal(PrincipalKind.Gruppe, scan.Principals["S-1-5-32-575"].Kind);
        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals["S-1-5-32-999"].Kind);
        Assert.All(scan.Principals.Values, p => Assert.NotEqual(PrincipalKind.Verwaist, p.Kind));
        Assert.Equal(PrincipalKind.Unbekannt, resolver.Resolve("S-1-5-32-999").Kind);
    }

    [Fact]
    public void Echte_ACL_eines_Ordners_wird_vollstaendig_aufgeloest()
    {
        using var temp = new TempFolder();
        var sub = temp.Sub("Daten");
        TempFolder.Grant(sub, new SecurityIdentifier(WellKnownSids.BuiltinUsers), FileSystemRights.ReadAndExecute);
        var scan = ScanOf(sub);

        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        resolver.ResolveAll(scan, expandGroups: true);

        // Jede SID aus ACL und Besitzer ist aufgelöst, keine ist verwaist oder unbekannt.
        foreach (var sid in scan.Folders.SelectMany(f => f.Aces.Select(a => a.Sid).Append(f.OwnerSid)))
        {
            Assert.True(scan.Principals.TryGetValue(sid, out var p), sid);
            Assert.DoesNotContain(p.Kind, new[] { PrincipalKind.Verwaist, PrincipalKind.Unbekannt });
            Assert.False(string.IsNullOrEmpty(p.Name));
        }
        var users = scan.Principals[WellKnownSids.BuiltinUsers];
        Assert.Equal(@"VORDEFINIERT\Benutzer", users.Name);
        Assert.False(users.MembersUnknown);
        Assert.Contains(WellKnownSids.Interactive, users.Members);
        Assert.Contains(WellKnownSids.AuthenticatedUsers, users.Members);
        Assert.Equal(@"NT-AUTORITÄT\INTERAKTIV", scan.Principals[WellKnownSids.Interactive].Name);
        Assert.Equal(PrincipalKind.Bekannt, scan.Principals[WellKnownSids.AuthenticatedUsers].Kind);
        // Mitglieder der Gruppen sind selbst aufgelöst (bis zur Person).
        foreach (var member in scan.Principals.Values.SelectMany(p => p.Members))
            Assert.True(scan.Principals.ContainsKey(member), member);
        Assert.Empty(scan.Errors);
    }

    [Fact]
    public void Lokale_Administratoren_bis_zur_Person()
    {
        var local = new LocalDirectory();
        var scan = Scans.On(@"C:\Daten", WellKnownSids.BuiltinAdministrators);

        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        resolver.ResolveAll(scan, expandGroups: true);

        var admins = scan.Principals[WellKnownSids.BuiltinAdministrators];
        Assert.Equal(local.GetMembers(WellKnownSids.BuiltinAdministrators)!.Order(StringComparer.OrdinalIgnoreCase), admins.Members);
        var builtinAdmin = scan.Principals[local.AccountDomainSid + "-500"];
        Assert.Equal(PrincipalKind.Benutzer, builtinAdmin.Kind);
        Assert.True(builtinAdmin.IsLocal);
    }

    [Fact]
    public void Lauf_auf_nicht_erreichbarem_Server_nimmt_nicht_die_lokalen_Gruppen()
    {
        var server = "zt-test-" + Guid.NewGuid().ToString("N")[..8];
        var local = new LocalDirectory();
        var localUser = local.AccountDomainSid + "-500";
        var scan = Scans.On($@"\\{server}\Daten", WellKnownSids.BuiltinAdministrators, WellKnownSids.Everyone, localUser);

        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        resolver.ResolveAll(scan, expandGroups: true);

        var admins = scan.Principals[WellKnownSids.BuiltinAdministrators];
        Assert.Equal(@"VORDEFINIERT\Administratoren", admins.Name);
        Assert.True(admins.MembersUnknown);
        Assert.Empty(admins.Members);
        // Die SID ist global eindeutig: dieser Rechner kann sie trotzdem benennen.
        Assert.Equal(PrincipalKind.Benutzer, scan.Principals[localUser].Kind);
        Assert.Contains(scan.Errors, e => e.Path.Equals($@"\\{server}", StringComparison.OrdinalIgnoreCase) && e.Message.Contains("nicht lesbar"));
    }

    [Fact]
    public void Lauf_ueber_localhost_gilt_als_dieser_Rechner()
    {
        var scan = Scans.On(@"\\localhost\Daten", WellKnownSids.BuiltinUsers);

        using var resolver = WindowsPlatform.Instance.CreateIdentityResolver();
        resolver.ResolveAll(scan, expandGroups: true);

        Assert.False(scan.Principals[WellKnownSids.BuiltinUsers].MembersUnknown);
        Assert.Contains(WellKnownSids.AuthenticatedUsers, scan.Principals[WellKnownSids.BuiltinUsers].Members);
        Assert.Empty(scan.Errors);
    }

    /// <summary>Lauf mit der echten ACL eines Ordners (ohne Scanner, direkt aus Windows gelesen).</summary>
    private static ScanResult ScanOf(string dir)
    {
        var sec = new DirectoryInfo(dir).GetAccessControl();
        var folder = new FolderNode { Path = dir, OwnerSid = sec.GetOwner(typeof(SecurityIdentifier))!.Value };
        foreach (FileSystemAccessRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            folder.Aces.Add(new AceEntry
            {
                Sid = rule.IdentityReference.Value,
                Rights = (FileRights)unchecked((uint)rule.FileSystemRights),
                IsDeny = rule.AccessControlType == AccessControlType.Deny,
                IsInherited = rule.IsInherited,
                Inheritance = (AceInheritance)rule.InheritanceFlags,
                Propagation = (AcePropagation)rule.PropagationFlags,
            });
        }
        var scan = new ScanResult { Request = new ScanRequest { Targets = [dir] } };
        scan.Folders.Add(folder);
        return scan;
    }
}
