// SPDX-License-Identifier: MIT
using Zutritt.Core.Identity;
using Zutritt.Core.Model;

namespace Zutritt.Tests.Identity;

public class WellKnownSidsTests
{
    [Theory]
    [InlineData("S-1-1-0", "Jeder")]
    [InlineData("S-1-5-11", @"NT-AUTORITÄT\Authentifizierte Benutzer")]
    [InlineData("S-1-5-18", @"NT-AUTORITÄT\SYSTEM")]
    [InlineData("S-1-5-19", @"NT-AUTORITÄT\LOKALER DIENST")]
    [InlineData("S-1-5-20", @"NT-AUTORITÄT\NETZWERKDIENST")]
    [InlineData("S-1-3-0", "ERSTELLER-BESITZER")]
    [InlineData("S-1-3-1", "ERSTELLERGRUPPE")]
    [InlineData("S-1-3-4", "BESITZERRECHTE")]
    [InlineData("S-1-5-4", @"NT-AUTORITÄT\INTERAKTIV")]
    [InlineData("S-1-5-2", @"NT-AUTORITÄT\NETZWERK")]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", @"NT SERVICE\TrustedInstaller")]
    public void Pseudokonten_aus_der_Spezifikation_sind_bekannt_mit_deutschem_Namen(string sid, string name)
    {
        Assert.True(WellKnownSids.TryGet(sid, out var n, out var kind));
        Assert.Equal(name, n);
        Assert.Equal(PrincipalKind.Bekannt, kind);
    }

    [Theory]
    [InlineData("S-1-5-32-544", @"VORDEFINIERT\Administratoren")]
    [InlineData("S-1-5-32-545", @"VORDEFINIERT\Benutzer")]
    [InlineData("S-1-5-32-546", @"VORDEFINIERT\Gäste")]
    [InlineData("S-1-5-32-547", @"VORDEFINIERT\Hauptbenutzer")]
    [InlineData("S-1-5-32-551", @"VORDEFINIERT\Sicherungs-Operatoren")]
    [InlineData("S-1-5-32-575", @"VORDEFINIERT\RDS-Remotezugriffsserver")]
    [InlineData("S-1-5-32-576", @"VORDEFINIERT\RDS-Endpunktserver")]
    [InlineData("S-1-5-32-577", @"VORDEFINIERT\RDS-Verwaltungsserver")]
    public void Vordefinierte_Gruppen_sind_echte_Gruppen_mit_deutschem_Namen(string sid, string name)
    {
        Assert.True(WellKnownSids.TryGet(sid, out var n, out var kind));
        Assert.Equal(name, n);
        // Gruppe statt „Bekannt“: Sie haben Mitglieder, die für die Personen-Sicht aufgelöst werden müssen.
        Assert.Equal(PrincipalKind.Gruppe, kind);
        Assert.True(WellKnownSids.IsBuiltin(sid));
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-512")]
    [InlineData("S-1-5-21-1-2-3-513")]
    [InlineData("S-1-5-21-1-2-3-500")]
    [InlineData("S-1-5-32-999")]
    public void Domaenen_und_unbekannte_SIDs_stehen_nicht_in_der_Tabelle(string sid)
    {
        Assert.False(WellKnownSids.TryGet(sid, out var name, out var kind));
        Assert.Equal("", name);
        Assert.Equal(PrincipalKind.Unbekannt, kind);
    }

    [Fact]
    public void Tabelle_ist_unabhaengig_von_Gross_und_Kleinschreibung()
    {
        Assert.True(WellKnownSids.TryGet("s-1-5-32-544", out var name, out _));
        Assert.Equal(@"VORDEFINIERT\Administratoren", name);
    }

    [Fact]
    public void Konto_SIDs_und_ihr_Domaenenteil()
    {
        Assert.True(WellKnownSids.IsAccountSid("S-1-5-21-1-2-3-1001"));
        Assert.False(WellKnownSids.IsAccountSid("S-1-5-32-544"));
        Assert.False(WellKnownSids.IsAccountSid("S-1-5-18"));
        Assert.Equal("S-1-5-21-1-2-3", WellKnownSids.AccountDomainOf("S-1-5-21-1-2-3-1001"));
        Assert.Null(WellKnownSids.AccountDomainOf("S-1-5-32-544"));
        Assert.Null(WellKnownSids.AccountDomainOf("S-1-5-21-kaputt"));
    }

    [Theory]
    [InlineData("S-1-15-3-1024-1065365936-1281604716-3511738428-1654721687-432734479-3232135806-4053264122-3456934681", "Anwendungsfähigkeit")]
    [InlineData("S-1-15-2-3624051433-2125758914-1423191267-1740899205-1073925389-3782572162-737981194", "Anwendungspaket")]
    [InlineData("S-1-16-8192", "Verbindlichkeitsstufe")]
    [InlineData("S-1-5-5-0-99", "Anmeldesitzung")]
    [InlineData("S-1-5-96-0-1", "Virtuelles Systemkonto")]
    [InlineData("S-1-5-80-1-2-3-4-5", "Nicht installierter Dienst")]
    [InlineData("S-1-5-82-1-2-3-4-5", "IIS-Anwendungspool")]
    [InlineData("S-1-5-83-0-1-2-3-4", "Virtueller Computer")]
    [InlineData("S-1-5-84-0-0-0-0-0", "Treiber im Benutzermodus")]
    [InlineData("S-1-5-87-1-2-3-4-5", "Geplante Aufgabe")]
    [InlineData("S-1-5-32-70329670-3585251103-1149629936-3204666536-4257240719-2613976102-450591375-1666047922", "Fähigkeitsgruppe")]
    public void SIDs_ohne_Konto_haben_einen_beschreibenden_Namen(string sid, string prefix)
    {
        var name = WellKnownSids.NonAccountName(sid);
        Assert.NotNull(name);
        Assert.Equal($"{prefix} ({sid})", name);
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1001")]
    [InlineData("S-1-5-32-544")]
    public void Konten_koennen_verwaisen(string sid) => Assert.Null(WellKnownSids.NonAccountName(sid));

    [Theory]
    [InlineData("S-1-5-32-544", true, false)]
    [InlineData("S-1-5-32-999", true, false)]
    [InlineData("S-1-5-32-70329670-3585251103-1149629936-3204666536-4257240719-2613976102-450591375-1666047922", false, true)]
    [InlineData("S-1-5-21-1-2-3-544", false, false)]
    public void Faehigkeitsgruppen_sind_keine_VORDEFINIERT_Gruppen(string sid, bool builtin, bool capabilityGroup)
    {
        Assert.Equal(builtin, WellKnownSids.IsBuiltin(sid));
        Assert.Equal(capabilityGroup, WellKnownSids.IsCapabilityGroup(sid));
    }

    [Theory]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", true)] // TrustedInstaller
    [InlineData("S-1-5-90-0-2", true)]   // Window Manager\DWM-2
    [InlineData("S-1-5-96-0-0", true)]   // Font Driver Host\UMFD-0
    [InlineData("S-1-5-82-1-2-3-4-5", true)]
    [InlineData("S-1-5-87-1-2-3-4-5", true)]
    [InlineData("S-1-5-18", false)]
    [InlineData("S-1-5-21-1-2-3-1001", false)]
    [InlineData("S-1-5-800-1", false)]
    public void Virtuelle_Konten(string sid, bool expected) => Assert.Equal(expected, WellKnownSids.IsVirtualAccount(sid));

    [Fact]
    public void EntraID_Konten_sind_nicht_pruefbar()
    {
        Assert.True(WellKnownSids.IsUnverifiable("S-1-12-1-1-2-3-4"));
        Assert.False(WellKnownSids.IsUnverifiable("S-1-5-21-1-2-3-4"));
    }
}
