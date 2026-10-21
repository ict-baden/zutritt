// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
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

/// <summary>Echte lokale Kontendatenbank dieses Rechners (ohne Änderungen am System).</summary>
public class LocalDirectoryTests
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private static bool IsLocalAccount(LocalDirectory dir) =>
        string.Equals(Me.AccountDomainSid?.Value, dir.AccountDomainSid, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Kennt_Rechnername_und_SID_der_lokalen_Kontendatenbank()
    {
        var dir = new LocalDirectory();

        Assert.Null(dir.Server);
        Assert.Equal(Environment.MachineName, dir.AccountDomainName, ignoreCase: true);
        Assert.StartsWith("S-1-5-21-", dir.AccountDomainSid);
        Assert.Equal(@"\\" + Environment.MachineName, dir.SourceName);
        // Ein Arbeitsplatzrechner ist kein DC: seine Kontendatenbank ist für die eigenen Konten zuständig.
        Assert.False(dir.IsDomainController);
        Assert.False(new LocalDirectory("localhost").IsDomainController);
    }

    [Fact]
    public void VORDEFINIERT_Benutzer_enthaelt_INTERAKTIV_und_Authentifizierte_Benutzer()
    {
        var members = new LocalDirectory().GetMembers(WellKnownSids.BuiltinUsers);

        Assert.NotNull(members);
        Assert.Contains(WellKnownSids.Interactive, members);
        Assert.Contains(WellKnownSids.AuthenticatedUsers, members);
    }

    [Fact]
    public void VORDEFINIERT_Administratoren_stimmt_mit_AccountManagement_ueberein()
    {
        // Gegenprobe mit einer unabhängigen Implementierung (WinNT-Provider statt NetApi).
        List<string> expected;
        try
        {
            using var ctx = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(ctx, IdentityType.Sid, WellKnownSids.BuiltinAdministrators)!;
            using var members = group.GetMembers(false);
            expected = [.. members.Select(m => m.Sid.Value)];
        }
        catch (PrincipalOperationException)
        {
            return; // AccountManagement scheitert an verwaisten oder Domänen-Mitgliedern – dann keine Gegenprobe möglich
        }

        var actual = new LocalDirectory().GetMembers(WellKnownSids.BuiltinAdministrators);

        Assert.NotNull(actual);
        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), actual.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(new LocalDirectory().AccountDomainSid + "-500", actual);
    }

    [Fact]
    public void Eigenes_Konto_ist_ein_aktiver_lokaler_Benutzer()
    {
        var dir = new LocalDirectory();
        if (!IsLocalAccount(dir)) return;

        var p = dir.Lookup(Me.Value);

        Assert.NotNull(p);
        Assert.Equal(PrincipalKind.Benutzer, p.Kind);
        Assert.True(p.IsLocal);
        Assert.False(p.IsDisabled);
        Assert.Equal(WindowsIdentity.GetCurrent().Name, p.Name, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
    }

    [Fact]
    public void Vordefinierte_Gruppe_wird_als_lokale_Gruppe_gefunden()
    {
        var p = new LocalDirectory().Lookup(WellKnownSids.BuiltinUsers);

        Assert.NotNull(p);
        Assert.Equal(PrincipalKind.Gruppe, p.Kind);
        Assert.True(p.IsLocal);
        Assert.Contains('\\', p.Name);
    }

    [Fact]
    public void Globale_Gruppe_Kein_enthaelt_die_lokalen_Benutzer()
    {
        var dir = new LocalDirectory();
        var none = dir.AccountDomainSid + "-513";

        var group = dir.Lookup(none);
        var members = dir.GetMembers(none);

        Assert.Equal(PrincipalKind.Gruppe, group?.Kind);
        Assert.NotNull(members);
        Assert.Contains(dir.AccountDomainSid + "-500", members); // Administrator-Konto
        if (IsLocalAccount(dir)) Assert.Contains(Me.Value, members);
    }

    [Fact]
    public void Nicht_vorhandenes_lokales_Konto_ist_verbindlich_nicht_vorhanden()
    {
        var dir = new LocalDirectory();
        var sid = dir.AccountDomainSid + "-987654";

        Assert.Equal(LookupState.NichtVorhanden, dir.Find(sid).State);
        Assert.Equal(LookupState.NichtVorhanden, dir.FindMembers(sid).State);
        Assert.Null(dir.Lookup(sid));
        Assert.Null(dir.GetMembers(sid));
    }

    [Theory]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1001")]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-15-2-1")]
    public void Fremde_und_bekannte_SIDs_sind_nicht_Sache_der_lokalen_Kontendatenbank(string sid)
    {
        var dir = new LocalDirectory();

        Assert.Equal(LookupState.NichtZustaendig, dir.Find(sid).State);
        Assert.Equal(LookupState.NichtZustaendig, dir.FindMembers(sid).State);
    }

    [Fact]
    public void Benutzer_hat_keine_Mitglieder()
    {
        var dir = new LocalDirectory();
        Assert.Null(dir.GetMembers(dir.AccountDomainSid + "-500"));
    }

    [Fact]
    public void Ueber_localhost_wie_ein_entfernter_Server_gleiche_Antworten()
    {
        var local = new LocalDirectory();
        var remote = new LocalDirectory(@"\\localhost\");

        Assert.Equal("localhost", remote.Server);
        Assert.Equal(@"\\localhost", remote.SourceName);
        Assert.Equal(local.AccountDomainSid, remote.AccountDomainSid);
        Assert.Equal(local.GetMembers(WellKnownSids.BuiltinUsers)!.Order(), remote.GetMembers(WellKnownSids.BuiltinUsers)!.Order());
        Assert.Equal(local.Lookup(local.AccountDomainSid + "-500")!.Name, remote.Lookup(local.AccountDomainSid + "-500")!.Name, ignoreCase: true);
    }

    [Fact]
    public void Nicht_erreichbarer_Server_meldet_das_ohne_Ausnahme_und_fragt_nicht_staendig_neu()
    {
        var dir = new LocalDirectory("zt-test-" + Guid.NewGuid().ToString("N")[..8]);

        var first = dir.Find(WellKnownSids.BuiltinUsers);
        var sw = Stopwatch.StartNew();
        var again = dir.FindMembers(WellKnownSids.BuiltinUsers);
        var account = dir.Find("S-1-5-21-1111111111-2222222222-3333333333-1001");
        sw.Stop();

        Assert.Equal(LookupState.NichtErreichbar, first.State);
        Assert.Contains("nicht lesbar", first.Error);
        Assert.Equal(LookupState.NichtErreichbar, again.State);
        // Ohne lesbare Kontendatenbank kann jede Konto-SID von dort stammen.
        Assert.Equal(LookupState.NichtErreichbar, account.State);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Zweite Anfrage dauerte {sw.ElapsedMilliseconds} ms");
        Assert.Null(dir.AccountDomainSid);
        Assert.Null(dir.Lookup(WellKnownSids.BuiltinUsers));
        Assert.Null(dir.GetMembers(WellKnownSids.BuiltinUsers));
    }
}
