using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;
using Use = Zutritt.Core.Identity.Native.SidNameUse;

namespace Zutritt.Tests.Identity;

/// <summary>Auflösung und Gruppenexpansion mit Attrappen – ohne Windows-Kontenabfragen, deterministisch.</summary>
public class IdentityResolverTests
{
    private const string Dom = "S-1-5-21-1000000001-1000000002-1000000003";
    private const string Max = Dom + "-2001";
    private const string Erika = Dom + "-2002";
    private const string GroupA = Dom + "-1101";
    private const string GroupB = Dom + "-1102";
    private const string Machine = "S-1-5-21-2000000001-2000000002-2000000003";

    // ---------- Bekannte SIDs ----------

    [Theory]
    [InlineData("S-1-1-0", "Jeder", PrincipalKind.Bekannt)]
    [InlineData("S-1-5-11", @"NT-AUTORITÄT\Authentifizierte Benutzer", PrincipalKind.Bekannt)]
    [InlineData("S-1-5-18", @"NT-AUTORITÄT\SYSTEM", PrincipalKind.Bekannt)]
    [InlineData("S-1-3-0", "ERSTELLER-BESITZER", PrincipalKind.Bekannt)]
    [InlineData("S-1-5-32-544", @"VORDEFINIERT\Administratoren", PrincipalKind.Gruppe)]
    [InlineData("S-1-5-32-545", @"VORDEFINIERT\Benutzer", PrincipalKind.Gruppe)]
    public void Bekannte_SIDs_kommen_aus_der_Tabelle_ohne_Verzeichnis(string sid, string name, PrincipalKind kind)
    {
        var lookup = new FakeLookup();
        var dir = new CountingDirectory(new FakeDirectory().Add(sid, "FALSCH", PrincipalKind.Benutzer));
        var p = Scans.Resolver(lookup, dir).Resolve(sid);

        Assert.Equal(name, p.Name);
        Assert.Equal(kind, p.Kind);
        Assert.Equal(name.Split('\\')[^1], p.DisplayName);
        Assert.Empty(dir.Lookups);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public void Vordefinierte_Gruppen_gelten_als_lokal_Pseudokonten_nicht()
    {
        var r = Scans.Resolver(new FakeLookup());
        Assert.True(r.Resolve(WellKnownSids.BuiltinAdministrators).IsLocal);
        Assert.False(r.Resolve(WellKnownSids.Everyone).IsLocal);
    }

    // ---------- Reihenfolge, Zwischenspeicher, Kopien ----------

    [Fact]
    public void Verzeichnisse_werden_der_Reihe_nach_gefragt_der_erste_Treffer_gilt()
    {
        var first = new CountingDirectory(new FakeDirectory().Add(Max, @"LAB\max.erst", PrincipalKind.Benutzer));
        var second = new CountingDirectory(new FakeDirectory().Add(Max, @"LAB\max.zweit", PrincipalKind.Benutzer)
            .Add(Erika, @"LAB\erika", PrincipalKind.Benutzer));
        var r = Scans.Resolver(new FakeLookup(), first, second);

        Assert.Equal(@"LAB\max.erst", r.Resolve(Max).Name);
        Assert.Empty(second.Lookups);
        Assert.Equal(@"LAB\erika", r.Resolve(Erika).Name);
        Assert.Single(second.Lookups);
    }

    [Fact]
    public void Resolve_merkt_sich_Ergebnisse_und_gibt_Kopien_heraus()
    {
        var dir = new CountingDirectory(new FakeDirectory().Add(Max, @"LAB\max", PrincipalKind.Benutzer));
        var r = Scans.Resolver(new FakeLookup(), dir);

        var a = r.Resolve(Max);
        a.Name = "verändert";
        a.Members.Add("S-1-1-0");
        var b = r.Resolve(Max.ToLowerInvariant());

        Assert.Equal(@"LAB\max", b.Name);
        Assert.Empty(b.Members);
        Assert.Equal(Max, b.Sid);
        Assert.Single(dir.Lookups);
    }

    [Fact]
    public void Jeder_Lauf_fragt_neu_damit_Aenderungen_nicht_im_Zwischenspeicher_haengen()
    {
        var fake = new FakeDirectory().Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var dir = new CountingDirectory(fake);
        var r = Scans.Resolver(new FakeLookup(), dir);

        var scan = Scans.OwnedBy(@"C:\Daten", Max, Max);
        r.ResolveAll(scan, expandGroups: true);
        Assert.False(scan.Principals[Max].IsDisabled);

        fake.Add(new Principal { Sid = Max, Name = @"LAB\max", Kind = PrincipalKind.Benutzer, IsDisabled = true });
        r.ResolveAll(scan, expandGroups: true);

        Assert.True(scan.Principals[Max].IsDisabled);
        Assert.Equal(2, dir.Lookups.Count);
    }

    // ---------- Rückfall LSA: Arten, verwaist, unbekannt ----------

    [Theory]
    [InlineData((int)Use.User, PrincipalKind.Benutzer)]
    [InlineData((int)Use.Group, PrincipalKind.Gruppe)]
    [InlineData((int)Use.Alias, PrincipalKind.Gruppe)]
    [InlineData((int)Use.WellKnownGroup, PrincipalKind.Bekannt)]
    [InlineData((int)Use.Computer, PrincipalKind.Computer)]
    [InlineData((int)Use.Label, PrincipalKind.Bekannt)]
    [InlineData((int)Use.Unknown, PrincipalKind.Unbekannt)]
    [InlineData((int)Use.Invalid, PrincipalKind.Unbekannt)]
    public void LSA_Rueckfall_bildet_die_Kontoart_ab(int use, PrincipalKind kind)
    {
        var lookup = new FakeLookup().Map(Max, "LAB", "max", (Use)use);
        var p = Scans.Resolver(lookup, new FakeDirectory()).Resolve(Max);

        Assert.Equal(kind, p.Kind);
        Assert.Equal(@"LAB\max", p.Name);
        Assert.Equal("max", p.DisplayName);
    }

    [Theory]
    [InlineData("S-1-5-90-0-2", "Window Manager", "DWM-2")]
    [InlineData("S-1-5-96-0-0", "Font Driver Host", "UMFD-0")]
    [InlineData("S-1-5-82-1-2-3-4-5", "IIS APPPOOL", "DefaultAppPool")]
    public void Virtuelle_Konten_sind_keine_Personen_auch_wenn_die_LSA_Benutzer_sagt(string sid, string domain, string name)
    {
        // So gefunden in C:\ProgramData\Microsoft\Windows\WER: „Window Manager\DWM-2“ als Benutzer → ZT03/ZT10
        var lookup = new FakeLookup().Map(sid, domain, name, Use.User);
        var p = Scans.Resolver(lookup).Resolve(sid);

        Assert.Equal(PrincipalKind.Bekannt, p.Kind);
        Assert.Equal($@"{domain}\{name}", p.Name);
    }

    [Fact]
    public void Nicht_uebersetzbare_Dienst_SID_ist_bekannt_nicht_verwaist()
    {
        const string service = "S-1-5-80-3906544942-1489856346-3706913989-164347954-1900376235";
        var p = Scans.Resolver(new FakeLookup().Fail(service, Native.ERROR_NONE_MAPPED)).Resolve(service);

        Assert.Equal(PrincipalKind.Bekannt, p.Kind);
        Assert.Equal($"Nicht installierter Dienst ({service})", p.Name);
    }

    [Theory]
    [InlineData(Native.RPC_S_SERVER_UNAVAILABLE)]
    [InlineData(Native.ERROR_ACCESS_DENIED)]
    public void Dienst_SID_ohne_sichere_Antwort_heisst_nicht_nicht_installiert(int error)
    {
        // Der Server war nicht erreichbar: Dort kann der Dienst installiert sein – bekannt bleibt die SID trotzdem
        const string service = "S-1-5-80-3906544942-1489856346-3706913989-164347954-1900376235";
        var p = Scans.Resolver(new FakeLookup().Fail(service, error)).Resolve(service);

        Assert.Equal(PrincipalKind.Bekannt, p.Kind);
        Assert.Equal($"Dienst ({service})", p.Name);
    }

    [Fact]
    public void Faehigkeitsgruppe_ist_bekannt_und_wird_nicht_als_Gruppe_aufgeklappt()
    {
        const string capabilityGroup = "S-1-5-32-989523358-1667815365-316973287-129935811-3358257576-2796868252-2781088163-715581871";
        var lookup = new FakeLookup().Fail(capabilityGroup, Native.ERROR_NONE_MAPPED);
        var machine = new FakeMachineDirectory(null, Machine);
        var scan = Scans.On(@"C:\Daten", capabilityGroup);

        Scans.Resolver(lookup, machine).ResolveAll(scan, expandGroups: true);

        var p = scan.Principals[capabilityGroup];
        Assert.Equal(PrincipalKind.Bekannt, p.Kind);
        Assert.StartsWith("Fähigkeitsgruppe", p.Name);
        Assert.False(p.MembersUnknown);
        Assert.Empty(p.Members);
    }

    [Fact]
    public void Geloeschtes_Konto_laut_LSA_ist_verwaist_mit_SID_als_Name()
    {
        var lookup = new FakeLookup().Map(Max, "LAB", "irgendwas", Use.DeletedAccount);
        var p = Scans.Resolver(lookup).Resolve(Max);

        Assert.Equal(PrincipalKind.Verwaist, p.Kind);
        Assert.Equal(Max, p.Name);
    }

    [Fact]
    public void Nicht_zugeordnete_SID_ist_verwaist()
    {
        var p = Scans.Resolver(new FakeLookup().Fail(Max, Native.ERROR_NONE_MAPPED)).Resolve(Max);

        Assert.Equal(PrincipalKind.Verwaist, p.Kind);
        Assert.Equal(Max, p.Name);
        Assert.Equal(Max, p.Label);
        Assert.Equal("", p.DisplayName);
    }

    [Theory]
    [InlineData(Native.ERROR_TRUSTED_DOMAIN_FAILURE)]
    [InlineData(Native.ERROR_TRUSTED_RELATIONSHIP_FAILURE)]
    [InlineData(Native.RPC_S_SERVER_UNAVAILABLE)]
    [InlineData(Native.ERROR_NO_LOGON_SERVERS)]
    [InlineData(Native.ERROR_ACCESS_DENIED)]
    public void Fremde_Domaene_nicht_erreichbar_ist_unbekannt_nicht_verwaist(int error)
    {
        var scan = Scans.On(@"C:\Daten", Max);
        Scans.Resolver(new FakeLookup().Fail(Max, error)).ResolveAll(scan, expandGroups: true);

        var p = scan.Principals[Max];
        Assert.Equal(PrincipalKind.Unbekannt, p.Kind);
        Assert.Equal(Max, p.Name);
        var note = Assert.Single(scan.Errors);
        Assert.Equal(@"\\" + Environment.MachineName, note.Path);
        Assert.Contains($"Fehler {error}", note.Message);
    }

    [Fact]
    public void Zustaendiges_Verzeichnis_nicht_erreichbar_ergibt_unbekannt_auch_wenn_die_LSA_nichts_kennt()
    {
        var ad = new FakeAuthoritativeDirectory("lab.local", Dom + "-") { Unreachable = true };
        var scan = Scans.On(@"C:\Daten", Max);
        Scans.Resolver(new FakeLookup(), ad).ResolveAll(scan, expandGroups: true);

        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals[Max].Kind);
        var note = Assert.Single(scan.Errors);
        Assert.Equal("lab.local", note.Path);
    }

    [Fact]
    public void Zustaendiges_Verzeichnis_erreichbar_ohne_Konto_ist_verwaist_auch_wenn_die_LSA_versagt()
    {
        var ad = new FakeAuthoritativeDirectory("lab.local", Dom + "-");
        var p = Scans.Resolver(new FakeLookup().Fail(Max, Native.RPC_S_SERVER_UNAVAILABLE), ad).Resolve(Max);

        Assert.Equal(PrincipalKind.Verwaist, p.Kind);
    }

    [Fact]
    public void Verbindliches_nicht_vorhanden_schlaegt_ein_allgemeines_nicht_erreichbar()
    {
        // Wie ein Notebook ohne DC: das AD meldet „nicht erreichbar“ für alle Konto-SIDs, die lokale
        // Kontendatenbank weiß aber sicher, dass es das lokale Konto nicht mehr gibt.
        var local = new FakeMachineDirectory(null, Machine);
        var ad = new FakeAuthoritativeDirectory("lab.local", "S-1-5-21-") { Unreachable = true };
        var p = Scans.Resolver(new FakeLookup(), local, ad).Resolve(Machine + "-1005");

        Assert.Equal(PrincipalKind.Verwaist, p.Kind);
    }

    [Fact]
    public void LSA_Treffer_gilt_auch_wenn_das_Verzeichnis_das_Konto_nicht_kennt()
    {
        // Typisch für SID-Historie nach einer Domänenmigration: Die alte SID gehört zu einem lebenden Konto.
        var ad = new FakeAuthoritativeDirectory("lab.local", Dom + "-");
        var lookup = new FakeLookup().Map(Max, "NEU", "max.muster", Use.User);
        var p = Scans.Resolver(lookup, ad).Resolve(Max);

        Assert.Equal(PrincipalKind.Benutzer, p.Kind);
        Assert.Equal(@"NEU\max.muster", p.Name);
    }

    [Theory]
    [InlineData("S-1-15-3-1024-1065365936-1281604716-3511738428-1654721687-432734479-3232135806-4053264122-3456934681", "Anwendungsfähigkeit")]
    [InlineData("S-1-15-2-3624051433-2125758914-1423191267-1740899205-1073925389-3782572162-737981194", "Anwendungspaket")]
    [InlineData("S-1-16-12288", "Verbindlichkeitsstufe")]
    [InlineData("S-1-5-5-0-123456", "Anmeldesitzung")]
    [InlineData("S-1-5-90-0-3", "Virtuelles Systemkonto")]
    public void SIDs_ohne_Konto_sind_nie_verwaist(string sid, string prefix)
    {
        var p = Scans.Resolver(new FakeLookup()).Resolve(sid);

        Assert.Equal(PrincipalKind.Bekannt, p.Kind);
        Assert.StartsWith(prefix, p.Name);
        Assert.Contains(sid, p.Name);
    }

    [Fact]
    public void Fehlende_VORDEFINIERT_Gruppe_ist_nie_verwaist()
    {
        // Die Kontendatenbank sagt verbindlich „gibt es nicht“ – trotzdem kein gelöschtes Konto, sondern eine
        // von Windows vergebene SID, die es nur auf anderen Windows-Ausgaben gibt.
        var local = new FakeMachineDirectory(null, Machine);
        var p = Scans.Resolver(new FakeLookup(), local).Resolve("S-1-5-32-999");

        Assert.Equal(PrincipalKind.Unbekannt, p.Kind);
        Assert.Equal("S-1-5-32-999", p.Name);
        Assert.True(p.IsLocal);
    }

    [Fact]
    public void RDS_Gruppen_aus_dem_Windows_Abbild_sind_VORDEFINIERT_Gruppen_auch_ohne_Konto()
    {
        // Auf Arbeitsplatzrechnern gibt es diese Gruppen nicht, sie stehen aber in Systemordner-ACLs.
        var local = new FakeMachineDirectory(null, Machine);
        var scan = Scans.On(@"C:\Windows\System32\Ordner", "S-1-5-32-575", "S-1-5-32-576", "S-1-5-32-577");

        Scans.Resolver(new FakeLookup(), local).ResolveAll(scan, expandGroups: true);

        var rds = scan.Principals["S-1-5-32-575"];
        Assert.Equal(@"VORDEFINIERT\RDS-Remotezugriffsserver", rds.Name);
        Assert.Equal(PrincipalKind.Gruppe, rds.Kind);
        // Gruppe nicht vorhanden: Mitglieder unbekannt, nicht „leer“ (sonst schlüge „Leere Gruppe berechtigt“ an).
        Assert.True(rds.MembersUnknown);
        Assert.All(scan.Principals.Values, p => Assert.NotEqual(PrincipalKind.Verwaist, p.Kind));
    }

    [Fact]
    public void Entfernter_Server_ohne_eigene_LSA_meldet_unbekannt_statt_verwaist()
    {
        // Öffentlicher Konstruktor: nur die LSA dieses Rechners. Ein lokales Konto von FS01 kennt sie nicht –
        // das beweist nicht, dass es gelöscht ist.
        const string fs01Account = "S-1-5-21-3000000001-3000000002-3000000003-1001";
        var scan = Scans.OwnedBy(@"\\FS01\Daten", fs01Account, fs01Account, WellKnownSids.Everyone);

        Scans.Resolver(new FakeLookup()).ResolveAll(scan, expandGroups: true);

        var p = scan.Principals[fs01Account];
        Assert.Equal(PrincipalKind.Unbekannt, p.Kind);
        Assert.Equal(fs01Account, p.Name);
        var note = Assert.Single(scan.Errors);
        Assert.Equal(@"\\FS01", note.Path);
        Assert.Contains("„Unbekannt“ statt „Verwaist“", note.Message);

        // Gegenprobe: Auf diesem Rechner ist dieselbe Antwort der LSA verbindlich.
        var local = Scans.On(@"C:\Daten", fs01Account);
        Scans.Resolver(new FakeLookup()).ResolveAll(local, expandGroups: true);
        Assert.Equal(PrincipalKind.Verwaist, local.Principals[fs01Account].Kind);
        Assert.Empty(local.Errors);
    }

    [Fact]
    public void Entfernter_Server_mit_eigener_LSA_erkennt_verwaiste_Konten()
    {
        const string fs01Account = "S-1-5-21-3000000001-3000000002-3000000003-1001";
        var lookups = new Dictionary<string, FakeLookup>(StringComparer.OrdinalIgnoreCase) { ["FS01"] = new("FS01"), [""] = new() };
        var resolver = new IdentityResolver([], s => lookups[s ?? ""], machineDirectoryFactory: null, askScannedServers: true, owned: []);
        var scan = Scans.On(@"\\FS01\Daten", fs01Account);

        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Equal(PrincipalKind.Verwaist, scan.Principals[fs01Account].Kind);
        Assert.Empty(scan.Errors);
        Assert.Equal(1, lookups["FS01"].Calls);
    }

    [Fact]
    public void Zweiter_Lauf_ueber_dasselbe_Ergebnis_haengt_Hinweise_nicht_doppelt_an()
    {
        var scan = Scans.On(@"C:\Daten", Max);
        scan.Errors.Add(new ScanError(@"C:\Daten\Geheim", "Zugriff verweigert"));
        var resolver = Scans.Resolver(new FakeLookup().Fail(Max, Native.RPC_S_SERVER_UNAVAILABLE));

        resolver.ResolveAll(scan, expandGroups: true);
        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Equal(2, scan.Errors.Count);
        Assert.Equal(@"C:\Daten\Geheim", scan.Errors[0].Path);
        Assert.Equal(@"\\" + Environment.MachineName, scan.Errors[1].Path);
    }

    [Fact]
    public void Grosser_Lauf_fragt_jede_SID_nur_einmal_und_bleibt_schnell()
    {
        // 50.000 Ordner mit je 5 Einträgen aus 40 verschiedenen Konten.
        var fake = new FakeDirectory();
        var sids = Enumerable.Range(0, 40).Select(i => $"{Dom}-{7000 + i}").ToList();
        foreach (var sid in sids) fake.Add(sid, $@"LAB\konto{sid[^4..]}", PrincipalKind.Benutzer);
        var dir = new CountingDirectory(fake);
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"D:\Daten"] } };
        for (var i = 0; i < 50_000; i++)
            scan.Folders.Add(Scans.Folder($@"D:\Daten\{i}", sids[i % 40], sids[i % 40], sids[(i + 1) % 40], sids[(i + 2) % 40],
                WellKnownSids.LocalSystem, WellKnownSids.BuiltinAdministrators));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Scans.Resolver(new FakeLookup(), dir).ResolveAll(scan, expandGroups: true);
        sw.Stop();

        Assert.Equal(42, scan.Principals.Count);
        Assert.Equal(40, dir.Lookups.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Auflösen dauerte {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Nicht_uebersetzbares_EntraID_Konto_ist_unbekannt()
    {
        var p = Scans.Resolver(new FakeLookup()).Resolve("S-1-12-1-1234567890-1234567890-1234567890-1234567890");
        Assert.Equal(PrincipalKind.Unbekannt, p.Kind);
    }

    [Theory]
    [InlineData("keine-sid")]
    [InlineData("S-1-5-21-abc")]
    [InlineData("S-1")]
    public void Ungueltige_SID_ergibt_unbekannt_ohne_Ausnahme(string sid)
    {
        var lookup = new FakeLookup();
        var p = Scans.Resolver(lookup).Resolve(sid);

        Assert.Equal(PrincipalKind.Unbekannt, p.Kind);
        Assert.Equal(sid, p.Name);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public void Leere_SID_ist_ein_Programmierfehler()
    {
        var r = Scans.Resolver(new FakeLookup());
        Assert.Throws<ArgumentException>(() => r.Resolve(" "));
        Assert.ThrowsAny<ArgumentException>(() => r.Resolve(null!));
        Assert.Throws<ArgumentNullException>(() => r.ResolveAll(null!, true));
    }

    [Fact]
    public void Fehlerhaftes_Verzeichnis_bricht_den_Lauf_nicht_ab()
    {
        var scan = Scans.On(@"C:\Daten", Max, GroupA);
        var fallback = new FakeDirectory().Add(GroupA, @"LAB\A", PrincipalKind.Gruppe, Erika).Add(Erika, @"LAB\erika", PrincipalKind.Benutzer);
        Scans.Resolver(new FakeLookup(), new ThrowingDirectory(), fallback).ResolveAll(scan, expandGroups: true);

        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals[Max].Kind);
        Assert.Equal(PrincipalKind.Gruppe, scan.Principals[GroupA].Kind);
        Assert.Equal([Erika], scan.Principals[GroupA].Members);
        Assert.Contains(scan.Errors, e => e.Path == nameof(ThrowingDirectory) && e.Message.Contains("Testfehler"));
    }

    [Fact]
    public void Lokales_Konto_aus_der_LSA_wird_als_lokal_erkannt()
    {
        var lookup = new FakeLookup()
            .Map(Machine + "-1001", Environment.MachineName.ToLowerInvariant(), "anna", Use.User)
            .Map(Max, "LAB", "max", Use.User);
        var r = Scans.Resolver(lookup);

        Assert.True(r.Resolve(Machine + "-1001").IsLocal);
        Assert.False(r.Resolve(Max).IsLocal);
    }

    // ---------- ResolveAll: welche SIDs ----------

    [Fact]
    public void ResolveAll_nimmt_Besitzer_ACEs_und_Freigabe_ACEs_und_ersetzt_alte_Eintraege()
    {
        var fake = new FakeDirectory()
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer)
            .Add(Erika, @"LAB\erika", PrincipalKind.Benutzer)
            .Add(GroupA, @"LAB\A", PrincipalKind.Gruppe);
        var scan = Scans.OwnedBy(@"\\FS01\Daten", Max, GroupA);
        scan.Folders.Add(Scans.Folder(@"\\FS01\Daten\Leer", owner: "", WellKnownSids.LocalSystem));
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Daten", Permissions = [new ShareAce(Erika, ShareAccess.Lesen, false)] });
        scan.Principals["S-1-5-21-9-9-9-9"] = new Principal { Sid = "S-1-5-21-9-9-9-9", Name = "alt" };

        Scans.Resolver(new FakeLookup(), fake).ResolveAll(scan, expandGroups: true);

        Assert.Equal(
            new[] { Max, Erika, GroupA, WellKnownSids.LocalSystem }.Order(StringComparer.OrdinalIgnoreCase),
            scan.Principals.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(scan.Principals, kv => Assert.Equal(kv.Key, kv.Value.Sid));
        Assert.Empty(scan.Errors);
    }

    [Fact]
    public void ResolveAll_mit_SampleScan_ergibt_die_Konten_des_Beispiels()
    {
        var sample = SampleScan.Create();
        var scan = SampleScan.Create();
        scan.Principals.Clear();

        Scans.Resolver(new FakeLookup(), FakeDirectory.FromScan(sample)).ResolveAll(scan, expandGroups: true);

        // Domänen-Benutzer und VORDEFINIERT\Benutzer hängen an keinem Eintrag und keiner aufgelösten Gruppe.
        var expected = sample.Principals.Keys.Except([SampleScan.BuiltinUsers, SampleScan.DomainUsers]).Order(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected, scan.Principals.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (sid, p) in scan.Principals)
        {
            var e = sample.Principals[sid];
            Assert.Equal(e.Name, p.Name);
            Assert.Equal(e.Kind, p.Kind);
            Assert.Equal(e.IsDisabled, p.IsDisabled);
            Assert.Equal(e.Members.Order(StringComparer.OrdinalIgnoreCase), p.Members);
            Assert.False(p.MembersUnknown);
            if (e.Kind is not (PrincipalKind.Verwaist or PrincipalKind.Bekannt)) Assert.Equal(e.DisplayName, p.DisplayName);
        }
        Assert.Equal("Klaus Alt", scan.Principals[SampleScan.Klaus].DisplayName);
        Assert.Equal(PrincipalKind.Verwaist, scan.Principals[SampleScan.Verwaist].Kind);
        Assert.Empty(scan.Principals[SampleScan.LeereGruppe].Members);
        // Nur der Ordnerfehler aus dem Beispiel, nichts von der Kontenauflösung.
        Assert.Equal(sample.Errors, scan.Errors);
    }

    // ---------- Gruppen ----------

    [Fact]
    public void Gruppen_werden_verschachtelt_bis_zur_Person_aufgeloest()
    {
        var fake = new FakeDirectory()
            .Add(GroupA, @"LAB\Labor-Alle", PrincipalKind.Gruppe, GroupB, Max)
            .Add(GroupB, @"LAB\QC-Team", PrincipalKind.Gruppe, Erika, WellKnownSids.Interactive)
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer)
            .Add(Erika, @"LAB\erika", PrincipalKind.Benutzer);
        var dir = new CountingDirectory(fake);
        var scan = Scans.On(@"C:\Daten", GroupA, WellKnownSids.Everyone);

        Scans.Resolver(new FakeLookup(), dir).ResolveAll(scan, expandGroups: true);

        Assert.Equal(new[] { GroupB, Max }.Order(StringComparer.OrdinalIgnoreCase), scan.Principals[GroupA].Members);
        Assert.Equal(new[] { Erika, WellKnownSids.Interactive }.Order(StringComparer.OrdinalIgnoreCase), scan.Principals[GroupB].Members);
        Assert.Equal(PrincipalKind.Benutzer, scan.Principals[Erika].Kind);
        Assert.Equal(PrincipalKind.Bekannt, scan.Principals[WellKnownSids.Interactive].Kind);
        // Nur Gruppen werden nach Mitgliedern gefragt, Benutzer und bekannte SIDs nicht.
        Assert.Equal(new[] { GroupA, GroupB }.Order(), dir.MemberQueries.Order());
        Assert.Empty(scan.Principals[Max].Members);
        Assert.False(scan.Principals[Max].MembersUnknown);
    }

    [Fact]
    public void Zyklus_A_in_B_in_A_wird_erkannt()
    {
        var fake = new FakeDirectory()
            .Add(GroupA, @"LAB\A", PrincipalKind.Gruppe, GroupB, Max)
            .Add(GroupB, @"LAB\B", PrincipalKind.Gruppe, GroupA)
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var dir = new CountingDirectory(fake);
        var scan = Scans.On(@"C:\Daten", GroupA);

        Scans.Resolver(new FakeLookup(), dir).ResolveAll(scan, expandGroups: true);

        Assert.Equal(new[] { GroupB, Max }.Order(StringComparer.OrdinalIgnoreCase), scan.Principals[GroupA].Members);
        Assert.Equal([GroupA], scan.Principals[GroupB].Members);
        Assert.False(scan.Principals[GroupA].MembersUnknown);
        Assert.False(scan.Principals[GroupB].MembersUnknown);
        Assert.Equal(2, dir.MemberQueries.Count);
        Assert.Empty(scan.Errors);
    }

    [Fact]
    public void Gruppe_die_sich_selbst_enthaelt_haengt_nicht()
    {
        var fake = new FakeDirectory().Add(GroupA, @"LAB\A", PrincipalKind.Gruppe, GroupA, Max).Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var scan = Scans.On(@"C:\Daten", GroupA);

        Scans.Resolver(new FakeLookup(), fake).ResolveAll(scan, expandGroups: true);

        Assert.Equal(new[] { GroupA, Max }.Order(StringComparer.OrdinalIgnoreCase), scan.Principals[GroupA].Members);
    }

    [Fact]
    public void Doppelte_Mitglieder_werden_zusammengefasst()
    {
        var fake = new FakeDirectory().Add(GroupA, @"LAB\A", PrincipalKind.Gruppe, Max, Max.ToLowerInvariant(), " " + Max)
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var scan = Scans.On(@"C:\Daten", GroupA);

        Scans.Resolver(new FakeLookup(), fake).ResolveAll(scan, expandGroups: true);

        Assert.Equal([Max], scan.Principals[GroupA].Members);
    }

    [Fact]
    public void Tiefe_ist_auf_20_Ebenen_begrenzt()
    {
        var fake = new FakeDirectory();
        string G(int i) => $"{Dom}-{5000 + i}";
        for (var i = 0; i < 25; i++) fake.Add(G(i), $@"LAB\G{i}", PrincipalKind.Gruppe, G(i + 1));
        fake.Add(G(25), @"LAB\G25", PrincipalKind.Gruppe, Max).Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var scan = Scans.On(@"C:\Daten", G(0));

        var resolver = Scans.Resolver(new FakeLookup(), fake);
        Assert.Equal(20, resolver.MaxGroupDepth);
        resolver.ResolveAll(scan, expandGroups: true);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal([G(i + 1)], scan.Principals[G(i)].Members);
            Assert.False(scan.Principals[G(i)].MembersUnknown);
        }
        Assert.True(scan.Principals[G(20)].MembersUnknown);
        Assert.Empty(scan.Principals[G(20)].Members);
        Assert.False(scan.Principals.ContainsKey(G(21)));
        Assert.False(scan.Principals.ContainsKey(Max));
        var note = Assert.Single(scan.Errors);
        Assert.Equal(@"LAB\G20", note.Path);
        Assert.Contains("20 Ebenen", note.Message);
    }

    [Fact]
    public void Tiefe_ist_einstellbar_und_kuerzester_Weg_zaehlt()
    {
        // G0 → G1 → G2 → G3, zusätzlich G0 → G3 direkt: G3 liegt auf Tiefe 1 und wird trotz Grenze 2 aufgelöst.
        var fake = new FakeDirectory()
            .Add(GroupA, @"LAB\G0", PrincipalKind.Gruppe, Dom + "-6001", Dom + "-6003")
            .Add(Dom + "-6001", @"LAB\G1", PrincipalKind.Gruppe, Dom + "-6002")
            .Add(Dom + "-6002", @"LAB\G2", PrincipalKind.Gruppe, Dom + "-6003")
            .Add(Dom + "-6003", @"LAB\G3", PrincipalKind.Gruppe, Max)
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var scan = Scans.On(@"C:\Daten", GroupA);

        var resolver = new IdentityResolver([fake]) { MaxGroupDepth = 2 };
        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Equal([Max], scan.Principals[Dom + "-6003"].Members);
        Assert.True(scan.Principals[Dom + "-6002"].MembersUnknown);
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityResolver([]) { MaxGroupDepth = -1 });
    }

    [Fact]
    public void Nicht_lesbare_Gruppe_wird_markiert_andere_bleiben_vollstaendig()
    {
        var fake = new FakeDirectory()
            .Add(GroupA, @"LAB\A", PrincipalKind.Gruppe, Max)
            .Add(GroupB, @"LAB\B", PrincipalKind.Gruppe, Erika)
            .MarkMembersUnreadable(GroupB)
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var scan = Scans.On(@"C:\Daten", GroupA, GroupB);

        Scans.Resolver(new FakeLookup(), fake).ResolveAll(scan, expandGroups: true);

        Assert.True(scan.Principals[GroupB].MembersUnknown);
        Assert.Empty(scan.Principals[GroupB].Members);
        Assert.False(scan.Principals.ContainsKey(Erika));
        Assert.False(scan.Principals[GroupA].MembersUnknown);
        Assert.Equal([Max], scan.Principals[GroupA].Members);
    }

    [Fact]
    public void Gruppe_die_kein_Verzeichnis_kennt_hat_unbekannte_Mitglieder()
    {
        // Gruppe nur über die LSA bekannt (z. B. aus einer vertrauten Domäne ohne eigenes Verzeichnis).
        var lookup = new FakeLookup().Map(GroupA, "FREMD", "Gruppe", Use.Group);
        var scan = Scans.On(@"C:\Daten", GroupA);

        Scans.Resolver(lookup).ResolveAll(scan, expandGroups: true);

        Assert.Equal(PrincipalKind.Gruppe, scan.Principals[GroupA].Kind);
        Assert.True(scan.Principals[GroupA].MembersUnknown);
    }

    [Fact]
    public void Leere_Gruppe_ist_leer_und_nicht_unbekannt()
    {
        var fake = new FakeDirectory().Add(GroupA, @"LAB\Alt-Projekt", PrincipalKind.Gruppe);
        var scan = Scans.On(@"C:\Daten", GroupA);

        Scans.Resolver(new FakeLookup(), fake).ResolveAll(scan, expandGroups: true);

        Assert.Empty(scan.Principals[GroupA].Members);
        Assert.False(scan.Principals[GroupA].MembersUnknown);
    }

    [Fact]
    public void Ohne_Gruppenaufloesung_sind_Gruppen_als_unbekannt_markiert_nicht_als_leer()
    {
        var fake = new FakeDirectory()
            .Add(GroupA, @"LAB\A", PrincipalKind.Gruppe, Max)
            .Add(Max, @"LAB\max", PrincipalKind.Benutzer);
        var dir = new CountingDirectory(fake);
        var scan = Scans.On(@"C:\Daten", GroupA, WellKnownSids.BuiltinUsers, WellKnownSids.Everyone);

        Scans.Resolver(new FakeLookup(), dir).ResolveAll(scan, expandGroups: false);

        Assert.True(scan.Principals[GroupA].MembersUnknown);
        Assert.True(scan.Principals[WellKnownSids.BuiltinUsers].MembersUnknown);
        Assert.False(scan.Principals[WellKnownSids.Everyone].MembersUnknown);
        Assert.Empty(scan.Principals[GroupA].Members);
        Assert.False(scan.Principals.ContainsKey(Max));
        Assert.Empty(dir.MemberQueries);
    }

    // ---------- Abbruch ----------

    [Fact]
    public void Abbruch_vor_dem_Start_laesst_das_Ergebnis_unveraendert()
    {
        var scan = SampleScan.Create();
        var before = scan.Principals.Count;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => Scans.Resolver(new FakeLookup(), new FakeDirectory()).ResolveAll(scan, true, cts.Token));
        Assert.Equal(before, scan.Principals.Count);
    }

    [Fact]
    public void Abbruch_mitten_im_Lauf_laesst_das_Ergebnis_unveraendert()
    {
        var sample = SampleScan.Create();
        var scan = SampleScan.Create();
        var before = scan.Principals.Values.Select(p => (p.Sid, p.Name, p.Members.Count)).ToList();
        using var cts = new CancellationTokenSource();
        var dir = new CountingDirectory(FakeDirectory.FromScan(sample));
        dir.OnLookup = _ =>
        {
            if (dir.Lookups.Count == 3) cts.Cancel();
        };

        Assert.Throws<OperationCanceledException>(() => Scans.Resolver(new FakeLookup(), dir).ResolveAll(scan, true, cts.Token));
        Assert.Equal(before, scan.Principals.Values.Select(p => (p.Sid, p.Name, p.Members.Count)).ToList());
        Assert.DoesNotContain(scan.Errors, e => e.Path != @"\\FS01\Labor\Personal");
    }

    // ---------- Rechnerbezug: VORDEFINIERT-Gruppen und lokale Konten ----------

    private const string Fs01 = "S-1-5-21-3000000001-3000000002-3000000003";
    private const string Fs02 = "S-1-5-21-4000000001-4000000002-4000000003";

    private static IdentityResolver MachineAware(FakeLookup lookup, Func<string?, IDirectory>? factory, params IDirectory[] directories) =>
        new(directories, _ => lookup, factory, askScannedServers: false, owned: []);

    [Fact]
    public void Lauf_auf_entferntem_Server_nutzt_nicht_die_Gruppen_dieses_Rechners()
    {
        var thisPc = new FakeMachineDirectory(null, Machine, new FakeDirectory().AddMember(WellKnownSids.BuiltinAdministrators, Machine + "-1001"));
        var fs01 = new FakeMachineDirectory("FS01", Fs01, new FakeDirectory().AddMember(WellKnownSids.BuiltinAdministrators, Dom + "-512"));
        var scan = Scans.On(@"\\FS01\Labor", WellKnownSids.BuiltinAdministrators);

        MachineAware(new FakeLookup(), null, thisPc, fs01).ResolveAll(scan, expandGroups: true);

        Assert.Equal([Dom + "-512"], scan.Principals[WellKnownSids.BuiltinAdministrators].Members);
        Assert.Equal(0, thisPc.MemberCalls);
        Assert.Equal(0, thisPc.FindCalls);
    }

    [Fact]
    public void Ohne_Verzeichnis_des_Servers_bleiben_VORDEFINIERT_Mitglieder_unbekannt()
    {
        var thisPc = new FakeMachineDirectory(null, Machine, new FakeDirectory().AddMember(WellKnownSids.BuiltinAdministrators, Machine + "-1001"));
        var scan = Scans.On(@"\\?\UNC\FS01\Labor\Ordner", WellKnownSids.BuiltinAdministrators);

        MachineAware(new FakeLookup(), null, thisPc).ResolveAll(scan, expandGroups: true);

        Assert.True(scan.Principals[WellKnownSids.BuiltinAdministrators].MembersUnknown);
        Assert.Empty(scan.Principals[WellKnownSids.BuiltinAdministrators].Members);
    }

    [Fact]
    public void Verzeichnis_fuer_gescannten_Server_wird_bei_Bedarf_angelegt()
    {
        var created = new List<string?>();
        IDirectory Factory(string? server)
        {
            created.Add(server);
            return new FakeMachineDirectory(server, Fs01, new FakeDirectory().AddMember(WellKnownSids.BuiltinUsers, WellKnownSids.AuthenticatedUsers));
        }
        var thisPc = new FakeMachineDirectory(null, Machine);
        var scan = Scans.On(@"\\fs01\Labor", WellKnownSids.BuiltinUsers);

        MachineAware(new FakeLookup(), Factory, thisPc).ResolveAll(scan, expandGroups: true);

        Assert.Equal(["FS01"], created);
        Assert.Equal([WellKnownSids.AuthenticatedUsers], scan.Principals[WellKnownSids.BuiltinUsers].Members);
    }

    [Fact]
    public void Lokale_Pfade_und_localhost_nutzen_das_Verzeichnis_dieses_Rechners()
    {
        var thisPc = new FakeMachineDirectory(null, Machine, new FakeDirectory().AddMember(WellKnownSids.BuiltinAdministrators, Machine + "-1001"));
        var scan = Scans.On(@"\\localhost\Daten", WellKnownSids.BuiltinAdministrators);
        scan.Folders.Add(Scans.Folder(@"C:\Daten\Unter", "", WellKnownSids.BuiltinAdministrators));
        scan.Folders.Add(Scans.Folder($@"\\{Environment.MachineName.ToLowerInvariant()}\Daten\X", "", WellKnownSids.BuiltinAdministrators));

        MachineAware(new FakeLookup(), _ => throw new InvalidOperationException("darf nicht angelegt werden"), thisPc).ResolveAll(scan, true);

        Assert.Equal([Machine + "-1001"], scan.Principals[WellKnownSids.BuiltinAdministrators].Members);
    }

    [Fact]
    public void Mehrere_Server_VORDEFINIERT_Mitglieder_werden_zusammengefasst_und_vermerkt()
    {
        var fs01 = new FakeMachineDirectory("FS01", Fs01, new FakeDirectory().AddMember(WellKnownSids.BuiltinAdministrators, Dom + "-512"));
        var fs02 = new FakeMachineDirectory("FS02", Fs02, new FakeDirectory()
            .AddMember(WellKnownSids.BuiltinAdministrators, Dom + "-512")
            .AddMember(WellKnownSids.BuiltinAdministrators, Fs02 + "-500"));
        var scan = Scans.On(@"\\FS01\A", WellKnownSids.BuiltinAdministrators);
        scan.Folders.Add(Scans.Folder(@"\\FS02\B", "", WellKnownSids.BuiltinAdministrators));
        var lookup = new FakeLookup().Map(Dom + "-512", "LAB", "Domänen-Admins", Use.Group);

        MachineAware(lookup, null, fs01, fs02).ResolveAll(scan, expandGroups: true);

        var admins = scan.Principals[WellKnownSids.BuiltinAdministrators];
        Assert.Equal(new[] { Dom + "-512", Fs02 + "-500" }.Order(StringComparer.OrdinalIgnoreCase), admins.Members);
        Assert.False(admins.MembersUnknown);
        var note = Assert.Single(scan.Errors);
        Assert.Equal(@"VORDEFINIERT\Administratoren", note.Path);
        Assert.Contains("zusammengefasst", note.Message);
        // Das lokale Konto von FS02 wird von FS02 aufgelöst (nicht von FS01): dort nicht vorhanden = verwaist.
        Assert.Equal(PrincipalKind.Verwaist, scan.Principals[Fs02 + "-500"].Kind);
    }

    [Fact]
    public void Mehrere_Server_gleiche_Mitglieder_ohne_Hinweis_ein_Server_nicht_erreichbar_unvollstaendig()
    {
        var members = new FakeDirectory().AddMember(WellKnownSids.BuiltinUsers, WellKnownSids.AuthenticatedUsers);
        var fs01 = new FakeMachineDirectory("FS01", Fs01, members);
        var fs02 = new FakeMachineDirectory("FS02", Fs02, members);
        var fs03 = new FakeMachineDirectory("FS03", "S-1-5-21-5-5-5") { Unreachable = true };
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"\\FS01", @"\\FS02"] } };
        scan.Folders.Add(Scans.Folder(@"\\FS01\A", "", WellKnownSids.BuiltinUsers));
        scan.Folders.Add(Scans.Folder(@"\\FS02\B", "", WellKnownSids.BuiltinUsers));

        MachineAware(new FakeLookup(), null, fs01, fs02).ResolveAll(scan, expandGroups: true);
        Assert.Equal([WellKnownSids.AuthenticatedUsers], scan.Principals[WellKnownSids.BuiltinUsers].Members);
        Assert.False(scan.Principals[WellKnownSids.BuiltinUsers].MembersUnknown);
        Assert.Empty(scan.Errors);

        scan.Folders.Add(Scans.Folder(@"\\FS03\C", "", WellKnownSids.BuiltinUsers));
        MachineAware(new FakeLookup(), null, fs01, fs02, fs03).ResolveAll(scan, expandGroups: true);
        Assert.Equal([WellKnownSids.AuthenticatedUsers], scan.Principals[WellKnownSids.BuiltinUsers].Members);
        Assert.True(scan.Principals[WellKnownSids.BuiltinUsers].MembersUnknown);
        Assert.Contains(scan.Errors, e => e.Path == @"\\FS03");
    }

    [Fact]
    public void LSA_der_gescannten_Server_wird_gefragt_und_bei_Ausfall_nicht_wiederholt()
    {
        var lookups = new Dictionary<string, FakeLookup>(StringComparer.OrdinalIgnoreCase)
        {
            ["FS01"] = new FakeLookup("FS01") { DefaultError = Native.RPC_S_SERVER_UNAVAILABLE },
            [""] = new FakeLookup().Map(Max, "LAB", "max", Use.User),
        };
        var resolver = new IdentityResolver([], s => lookups[s ?? ""], machineDirectoryFactory: null, askScannedServers: true, owned: []);
        var scan = Scans.On(@"\\FS01\Labor", Max, Erika, Dom + "-2003");

        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Equal(@"LAB\max", scan.Principals[Max].Name);
        // Erika: FS01 nicht erreichbar, dieser Rechner kennt sie nicht → nicht prüfbar → unbekannt.
        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals[Erika].Kind);
        Assert.Equal(PrincipalKind.Unbekannt, scan.Principals[Dom + "-2003"].Kind);
        Assert.Equal(1, lookups["FS01"].Calls);
        Assert.Equal(3, lookups[""].Calls);
        var note = Assert.Single(scan.Errors);
        Assert.Equal(@"\\FS01", note.Path);
        Assert.Contains("RPC-Server nicht verfügbar", note.Message);
    }

    [Fact]
    public void Dispose_schliesst_nur_eigene_Verzeichnisse()
    {
        var owned = new DisposeProbe();
        var foreign = new DisposeProbe();
        var resolver = new IdentityResolver([foreign], null, null, false, [owned]);

        resolver.Dispose();

        Assert.True(owned.Disposed);
        Assert.False(foreign.Disposed);
    }

    private sealed class DisposeProbe : IDirectory, IDisposable
    {
        public bool Disposed { get; private set; }
        public Principal? Lookup(string sid) => null;
        public IReadOnlyList<string>? GetMembers(string groupSid) => null;
        public void Dispose() => Disposed = true;
    }
}
