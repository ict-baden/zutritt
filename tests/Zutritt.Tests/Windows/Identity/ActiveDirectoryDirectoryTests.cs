// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Platform.Windows.Identity;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Scanning;

namespace Zutritt.Tests.Windows.Identity;

/// <summary>
/// Ohne Domäne lässt sich nur das Verhalten ohne DC prüfen: keine Ausnahmen, ausdrücklich „nicht erreichbar“,
/// kein falsches „verwaist“. Die eigentliche AD-Abfrage ist hier nicht testbar.
/// </summary>
public class ActiveDirectoryDirectoryTests
{
    private const string DomainSid = "S-1-5-21-1234567890-1234567890-1234567890";

    private static string Unreachable() => "zt-test-" + Guid.NewGuid().ToString("N")[..8] + ".invalid";

    [Fact]
    public void Ohne_Domaene_antwortet_das_Verzeichnis_nie()
    {
        using var ad = new ActiveDirectoryDirectory((ComputerDomainInfo?)null);

        Assert.False(ad.IsConfigured);
        Assert.Equal(LookupState.NichtZustaendig, ad.Find(DomainSid + "-1001").State);
        Assert.Equal(LookupState.NichtZustaendig, ad.FindMembers(DomainSid + "-513").State);
        Assert.Null(ad.Lookup(DomainSid + "-1001"));
        Assert.Null(ad.GetMembers(DomainSid + "-513"));
    }

    [Fact]
    public void Standard_auf_Arbeitsgruppenrechner_ist_ohne_Funktion()
    {
        if (ComputerDomain.Query() is not null) return;
        using var ad = new ActiveDirectoryDirectory();
        Assert.False(ad.IsConfigured);
        Assert.Null(ad.DomainSid);
    }

    [Fact]
    public void Nicht_erreichbare_Domaene_meldet_das_fuer_Konto_SIDs_ohne_Ausnahme()
    {
        using var ad = new ActiveDirectoryDirectory(new ComputerDomainInfo("ZTTEST", Unreachable(), DomainSid));

        var first = ad.Find(DomainSid + "-1001");
        var sw = Stopwatch.StartNew();
        var foreign = ad.Find("S-1-5-21-1-2-3-1001");
        var members = ad.FindMembers(DomainSid + "-513");
        sw.Stop();

        Assert.Equal(LookupState.NichtErreichbar, first.State);
        Assert.Contains("nicht erreichbar", first.Error);
        Assert.Contains("„Unbekannt“", first.Error);
        // Ohne DC sind auch Konten vertrauter Domänen nicht prüfbar.
        Assert.Equal(LookupState.NichtErreichbar, foreign.State);
        Assert.Equal(LookupState.NichtErreichbar, members.State);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Folgeanfragen dauerten {sw.ElapsedMilliseconds} ms – der Verbindungsversuch wurde wiederholt");
        Assert.Equal(DomainSid, ad.DomainSid);
    }

    [Theory]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-12-1-1-2-3-4")]
    public void Nur_Konto_SIDs_gehen_an_das_AD(string sid)
    {
        using var ad = new ActiveDirectoryDirectory(new ComputerDomainInfo("ZTTEST", Unreachable(), DomainSid));
        Assert.Equal(LookupState.NichtZustaendig, ad.Find(sid).State);
        Assert.Equal(LookupState.NichtZustaendig, ad.FindMembers(sid).State);
    }

    [Fact]
    public void Domaene_nur_mit_Namen_nicht_erreichbar()
    {
        using var ad = new ActiveDirectoryDirectory(Unreachable());

        Assert.True(ad.IsConfigured);
        Assert.Equal(LookupState.NichtErreichbar, ad.Find(DomainSid + "-1001").State);
        Assert.Null(ad.DomainSid);
    }

    [Fact]
    public void Nach_Dispose_nicht_erreichbar_statt_Ausnahme()
    {
        var ad = new ActiveDirectoryDirectory(new ComputerDomainInfo("ZTTEST", Unreachable(), DomainSid));
        ad.Dispose();

        var r = ad.Find(DomainSid + "-1001");
        Assert.Equal(LookupState.NichtErreichbar, r.State);
        Assert.Contains("geschlossen", r.Error);
    }

    [Fact]
    public void Resolver_mit_nicht_erreichbarem_AD_meldet_unbekannt_statt_verwaist()
    {
        using var ad = new ActiveDirectoryDirectory(new ComputerDomainInfo("ZTTEST", Unreachable(), DomainSid));
        var group = DomainSid + "-1101";
        var scan = Scans.On(@"C:\Daten", group, DomainSid + "-2001", WellKnownSids.Everyone);

        // Echte LSA dieses Rechners: Sie kennt die erfundene Domäne nicht (ERROR_NONE_MAPPED).
        new IdentityResolver([new LocalDirectory(), ad]).ResolveAll(scan, expandGroups: true);

        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals[group].Kind);
        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals[DomainSid + "-2001"].Kind);
        Assert.Equal("Jeder", scan.Principals[WellKnownSids.Everyone].Name);
        var note = Assert.Single(scan.Errors);
        Assert.StartsWith("zt-test-", note.Path);
        Assert.Contains("Active Directory", note.Message);
    }
}
