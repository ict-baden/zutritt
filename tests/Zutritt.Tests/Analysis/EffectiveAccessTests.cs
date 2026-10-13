using System.Security.AccessControl;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

public sealed class EffectiveAccessTests
{
    private const FileSystemRights Read = S.Read;
    private const FileSystemRights Modify = S.Modify;
    private const FileSystemRights Full = S.Full;

    private static FolderNode Node(ScanResult scan, string relative) =>
        scan.Folders.Single(f => f.Path.Equals(@"\\FS01\Labor" + relative, StringComparison.OrdinalIgnoreCase));

    // ---------- GroupsOf ----------

    [Fact]
    public void GroupsOf_Benutzer_transitiv_mit_Jeder_und_Authentifizierten()
    {
        var groups = EffectiveAccess.GroupsOf(S.Create(), S.Erika);
        Assert.Equal(
            new[] { S.Erika, S.QcTeam, S.LaborAlle, S.DomainUsers, S.BuiltinUsers, S.Everyone, S.AuthenticatedUsers }.Order(),
            groups.Order());
    }

    [Fact]
    public void GroupsOf_Gruppe_ohne_implizite_Mitgliedschaften()
    {
        var scan = S.Create();
        Assert.Equal(new[] { S.QcTeam, S.LaborAlle }.Order(), EffectiveAccess.GroupsOf(scan, S.QcTeam).Order());
        Assert.Equal(new[] { S.Everyone }, EffectiveAccess.GroupsOf(scan, S.Everyone));
        // Authentifizierte Benutzer steht als Mitglied in VORDEFINIERT\Benutzer – das zählt, aber kein implizites „Jeder“
        Assert.Equal(new[] { S.AuthenticatedUsers, S.BuiltinUsers }.Order(), EffectiveAccess.GroupsOf(scan, S.AuthenticatedUsers).Order());
    }

    [Fact]
    public void GroupsOf_verwaiste_und_unbekannte_SIDs_gelten_als_Konto()
    {
        var scan = S.Create();
        var expected = new[] { S.Everyone, S.AuthenticatedUsers, S.BuiltinUsers };
        Assert.Equal(expected.Append(S.Verwaist).Order(), EffectiveAccess.GroupsOf(scan, S.Verwaist).Order());
        const string fremd = "S-1-5-21-9-9-9-4711";
        Assert.Equal(expected.Append(fremd).Order(), EffectiveAccess.GroupsOf(scan, fremd).Order());
    }

    [Fact]
    public void GroupsOf_ignoriert_Gross_Klein_der_SID()
    {
        var groups = EffectiveAccess.GroupsOf(S.Create(), S.Max.ToLowerInvariant());
        Assert.Contains(S.LaborAlle, groups);
        Assert.Contains(S.Max, groups);
    }

    [Fact]
    public void GroupsOf_Zyklus_terminiert()
    {
        var a = Sid(1001); var b = Sid(1002); var u = Sid(2001);
        var builder = new ScanBuilder().User(u, @"T\u").Group(a, @"T\A", u, b).Group(b, @"T\B", a);
        var groups = EffectiveAccess.GroupsOf(builder.Scan, u);
        Assert.Contains(a, groups);
        Assert.Contains(b, groups);
        Assert.Equal(new[] { a, b }.Order(), EffectiveAccess.GroupsOf(builder.Scan, a).Order());
    }

    [Fact]
    public void GroupsOf_tiefe_Verschachtelung_ohne_Rekursion()
    {
        // 20.000 Ebenen würden jede rekursive Lösung sprengen
        var builder = new ScanBuilder();
        var u = Sid(1);
        builder.User(u, @"T\u");
        string member = u;
        for (int i = 0; i < 20_000; i++)
        {
            var g = Sid(100_000 + i);
            builder.Group(g, $@"T\G{i}", member);
            member = g;
        }
        var top = builder.Folder("", 0, Allow(member, Read));
        var groups = EffectiveAccess.GroupsOf(builder.Scan, u);
        Assert.Equal(20_000 + 3, groups.Count); // Gruppen + Konto + Jeder + Authentifizierte Benutzer

        var grant = Assert.Single(EffectiveAccess.ForPerson(builder.Scan, u));
        Assert.Equal(AccessLevel.Lesen, grant.Ntfs);
        Assert.Equal(20_000, grant.Via.Count);
        Assert.Equal(@"T\G0", grant.Via[0]);
        Assert.Equal(@"T\G19999", grant.Via[^1]);
        Assert.Equal(top.Path, grant.Path);
    }

    // ---------- ForPerson ----------

    [Fact]
    public void ForPerson_Erika_ueber_Gruppenkette_sortiert_nach_Pfad()
    {
        var grants = EffectiveAccess.ForPerson(S.Create(), S.Erika);
        Assert.Equal(
            [@"\\FS01\Labor", @"\\FS01\Labor\Archiv", @"\\FS01\Labor\Austausch", @"\\FS01\Labor\QC"],
            grants.Select(g => g.Path));
        Assert.All(grants, g => Assert.Equal(AccessLevel.Aendern, g.Effective));
        Assert.All(grants, g => Assert.Equal(S.Erika, g.Sid));

        var root = grants[0];
        Assert.Equal([@"LAB\QC-Team", @"LAB\Labor-Alle"], root.Via);
        Assert.Equal(AccessLevel.Aendern, root.ViaShare);
        Assert.False(root.ReducedByDeny);

        Assert.Equal([@"LAB\QC-Team"], grants[3].Via);
        Assert.Equal(["Jeder"], grants[2].Via); // Austausch: „Jeder“ steht explizit vor den geerbten Einträgen
    }

    [Fact]
    public void ForPerson_Max_direkter_Vollzugriff_und_Verweigerung_im_Archiv()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.ForPerson(scan, S.Max);
        Assert.Equal([@"\\FS01\Labor", @"\\FS01\Labor\Austausch", @"\\FS01\Labor\QC"], grants.Select(g => g.Path));

        var qc = grants[2];
        Assert.Equal(AccessLevel.Vollzugriff, qc.Ntfs);
        Assert.Empty(qc.Via);
        Assert.Equal([@"LAB\Labor-Alle"], grants[0].Via);

        // Archiv: Max wird explizit „Ändern“ verweigert – das sperrt auch Lesen über Authentifizierte Benutzer
        var archiv = EffectiveAccessInternals.Grant(scan, Node(scan, @"\Archiv"), S.Max);
        Assert.Equal(AccessLevel.Keine, archiv.Effective);
        Assert.True(archiv.ReducedByDeny);
    }

    [Fact]
    public void ForPerson_deaktiviertes_Konto_ueber_breite_Gruppe_und_direkt()
    {
        var grants = EffectiveAccess.ForPerson(S.Create(), S.Klaus);
        Assert.Equal(
            [@"\\FS01\Labor\Archiv", @"\\FS01\Labor\Austausch", @"\\FS01\Labor\Projekte\2019\Alt\Rohdaten"],
            grants.Select(g => g.Path));
        Assert.Equal([AccessLevel.Lesen, AccessLevel.Aendern, AccessLevel.Lesen], grants.Select(g => g.Effective));
        Assert.Equal([@"NT-AUTORITÄT\Authentifizierte Benutzer"], grants[0].Via);
        Assert.Empty(grants[2].Via);
    }

    [Fact]
    public void ForPerson_ohne_nicht_lesbare_Ordner_und_ab_Auflisten()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u").WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));
        b.Folder("", 0, Allow(u, FileSystemRights.Traverse | FileSystemRights.Synchronize));
        b.Folder(@"\Leer", 1, Allow(u, FileSystemRights.Synchronize));
        b.Scan.Folders.Add(new FolderNode { Path = Share + @"\Gesperrt", Depth = 1, ShareUnc = Share, Error = "Zugriff verweigert" });

        var grant = Assert.Single(EffectiveAccess.ForPerson(b.Scan, u));
        Assert.Equal(Share, grant.Path);
        Assert.Equal(AccessLevel.Auflisten, grant.Effective);
    }

    // ---------- Reihenfolge von Erlauben und Verweigern ----------

    [Fact]
    public void Explizites_Erlauben_schlaegt_geerbte_Verweigerung()
    {
        var u = Sid(2001); var g = Sid(1001);
        var b = new ScanBuilder().User(u, @"T\u").Group(g, @"T\G", u);
        var f = b.LocalFolder(@"D:\Daten\X", 0,
            Allow(u, Modify),
            Deny(g, FileSystemRights.Write, inherited: true));
        var grant = EffectiveAccessInternals.Grant(b.Scan, f, u);
        Assert.Equal(AccessLevel.Aendern, grant.Ntfs);
        Assert.False(grant.ReducedByDeny);
    }

    [Fact]
    public void Explizites_Verweigern_gewinnt_unabhaengig_von_der_Speicherreihenfolge()
    {
        var u = Sid(2001); var g = Sid(1001);
        var b = new ScanBuilder().User(u, @"T\u").Group(g, @"T\G", u);
        // Erlauben steht (nicht kanonisch) vor Verweigern
        var f = b.LocalFolder(@"D:\Daten\X", 0, Allow(g, Modify), Deny(u, FileSystemRights.Write));
        var grant = EffectiveAccessInternals.Grant(b.Scan, f, u);
        Assert.Equal(AccessLevel.Lesen, grant.Ntfs);
        Assert.True(grant.ReducedByDeny);
        Assert.Equal([@"T\G"], grant.Via);
    }

    [Fact]
    public void Geerbt_naeherer_Vorfahr_vor_fernerem()
    {
        // Wie Windows: Erlauben vom Elternordner steht vor Verweigern vom Großelternordner
        var u = Sid(2001); var g = Sid(1001);
        var b = new ScanBuilder().User(u, @"T\u").Group(g, @"T\G", u);
        var closerAllow = b.LocalFolder(@"D:\A", 0, Allow(u, Modify, inherited: true), Deny(g, FileSystemRights.Write, inherited: true));
        Assert.Equal(AccessLevel.Aendern, EffectiveAccessInternals.Grant(b.Scan, closerAllow, u).Ntfs);

        // Gleicher Vorfahr: kanonisch steht Verweigern vorn und gewinnt
        var sameLevel = b.LocalFolder(@"D:\B", 0, Deny(u, FileSystemRights.Write, inherited: true), Allow(g, Modify, inherited: true));
        var grant = EffectiveAccessInternals.Grant(b.Scan, sameLevel, u);
        Assert.Equal(AccessLevel.Lesen, grant.Ntfs);
        Assert.True(grant.ReducedByDeny);
    }

    [Fact]
    public void Verweigern_nur_teilweise_ueberlappend_setzt_ReducedByDeny()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        var f = b.LocalFolder(@"D:\X", 0, Deny(u, FileSystemRights.Delete), Allow(u, Modify));
        var grant = EffectiveAccessInternals.Grant(b.Scan, f, u);
        Assert.Equal(AccessLevel.Schreiben, grant.Ntfs); // ohne Löschen
        Assert.True(grant.ReducedByDeny);

        var g = b.LocalFolder(@"D:\Y", 0, Deny(u, FileSystemRights.TakeOwnership), Allow(u, Read));
        Assert.False(EffectiveAccessInternals.Grant(b.Scan, g, u).ReducedByDeny); // nichts erlaubt, was verweigert wird
    }

    [Fact]
    public void Nur_vererbende_Eintraege_und_Ersteller_Besitzer_zaehlen_nicht()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        var f = b.LocalFolder(@"D:\X", 0,
            Allow(u, Full, propagation: PropagationFlags.InheritOnly),
            Allow(S.CreatorOwner, Full));
        Assert.Equal(AccessLevel.Keine, EffectiveAccessInternals.Grant(b.Scan, f, u).Ntfs);
        Assert.Empty(EffectiveAccess.ForFolder(b.Scan, f));
    }

    [Fact]
    public void Generische_Rechte_werden_abgebildet()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        var read = b.LocalFolder(@"D:\R", 0, Allow(u, unchecked((FileSystemRights)0x80000000)));
        var all = b.LocalFolder(@"D:\A", 0, Allow(u, (FileSystemRights)0x10000000));
        Assert.Equal(AccessLevel.Lesen, EffectiveAccessInternals.Grant(b.Scan, read, u).Ntfs);
        Assert.Equal(AccessLevel.Vollzugriff, EffectiveAccessInternals.Grant(b.Scan, all, u).Ntfs);
    }

    [Fact]
    public void Besitzerrechte_gelten_fuer_den_Besitzer_auch_ueber_Gruppe()
    {
        var u = Sid(2001); var v = Sid(2002); var g = Sid(1001);
        var b = new ScanBuilder().User(u, @"T\u").User(v, @"T\v").Group(g, @"T\Projekt", u);
        var direct = b.LocalFolder(@"D:\X", 0, Allow(SidCatalog.OwnerRights, Read));
        direct.OwnerSid = u;
        var viaGroup = b.LocalFolder(@"D:\Y", 0, Allow(SidCatalog.OwnerRights, Modify));
        viaGroup.OwnerSid = g;

        var own = EffectiveAccessInternals.Grant(b.Scan, direct, u);
        Assert.Equal(AccessLevel.Lesen, own.Ntfs);
        Assert.Equal(["BESITZERRECHTE"], own.Via);
        Assert.Equal(AccessLevel.Keine, EffectiveAccessInternals.Grant(b.Scan, direct, v).Ntfs);

        var group = EffectiveAccessInternals.Grant(b.Scan, viaGroup, u);
        Assert.Equal(AccessLevel.Aendern, group.Ntfs);
        Assert.Equal([@"T\Projekt", "BESITZERRECHTE"], group.Via);
    }

    [Fact]
    public void Netzwerk_und_Interaktiv_haengen_vom_Zugriffsweg_ab()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u").WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));
        var shared = b.Folder("", 0, Allow(u, Modify), Deny(SidCatalog.Network, FileSystemRights.Write));
        var local = b.LocalFolder(@"D:\Lokal", 0, Allow(u, Modify), Deny(SidCatalog.Network, FileSystemRights.Write));
        var interactiveOnly = b.LocalFolder(@"D:\Konsole", 0, Allow(SidCatalog.Interactive, Read));

        Assert.Equal(AccessLevel.Lesen, EffectiveAccessInternals.Grant(b.Scan, shared, u).Effective);
        Assert.Equal(AccessLevel.Aendern, EffectiveAccessInternals.Grant(b.Scan, local, u).Effective);
        Assert.Equal(AccessLevel.Lesen, EffectiveAccessInternals.Grant(b.Scan, interactiveOnly, u).Effective);
    }

    // ---------- Freigabe ----------

    [Fact]
    public void Freigabe_begrenzt_NTFS()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u").WithShare(new ShareAce(S.AuthenticatedUsers, ShareAccess.Lesen, false));
        var f = b.Folder(@"\X", 1, Allow(u, Full));
        var grant = Assert.Single(EffectiveAccess.ForPerson(b.Scan, u));
        Assert.Equal(AccessLevel.Vollzugriff, grant.Ntfs);
        Assert.Equal(AccessLevel.Lesen, grant.ViaShare);
        Assert.Equal(AccessLevel.Lesen, grant.Effective);
        Assert.Equal(f.Path, grant.Path);
    }

    [Fact]
    public void Freigabe_hoechste_Stufe_ueber_alle_Gruppen()
    {
        var u = Sid(2001); var g = Sid(1001);
        var b = new ScanBuilder().User(u, @"T\u").Group(g, @"T\G", u)
            .WithShare(new ShareAce(S.Everyone, ShareAccess.Lesen, false), new ShareAce(g, ShareAccess.Aendern, false));
        b.Folder("", 0, Allow(u, Full));
        Assert.Equal(AccessLevel.Aendern, Assert.Single(EffectiveAccess.ForPerson(b.Scan, u)).Effective);
    }

    [Fact]
    public void Freigabe_Verweigern_sperrt_den_Netzzugriff()
    {
        var u = Sid(2001); var g = Sid(1001);
        var b = new ScanBuilder().User(u, @"T\u").Group(g, @"T\Praktikanten", u)
            .WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false), new ShareAce(g, ShareAccess.Aendern, true));
        var f = b.Folder("", 0, Allow(u, Modify));
        var grant = EffectiveAccessInternals.Grant(b.Scan, f, u);
        Assert.Equal(AccessLevel.Aendern, grant.Ntfs);
        Assert.Equal(AccessLevel.Keine, grant.ViaShare);
        Assert.Empty(EffectiveAccess.ForPerson(b.Scan, u));
        Assert.Empty(EffectiveAccess.PeopleForFolder(b.Scan, f));
    }

    [Fact]
    public void Freigabe_ohne_Treffer_bedeutet_kein_Netzzugriff()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u").WithShare(new ShareAce(S.Administrators, ShareAccess.Vollzugriff, false));
        var f = b.Folder("", 0, Allow(u, Modify));
        Assert.Equal(AccessLevel.Keine, EffectiveAccessInternals.Grant(b.Scan, f, u).ViaShare);
    }

    [Fact]
    public void Freigabe_nicht_lesbar_unbekannt_oder_keine_ergibt_null()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "Daten", PermissionsReadable = false });
        var unreadable = b.Folder("", 0, Allow(u, Modify));
        var unknownShare = b.LocalFolder(@"\\ANDERER\X", 0, Allow(u, Modify));
        unknownShare.ShareUnc = @"\\ANDERER\X";
        var local = b.LocalFolder(@"D:\Lokal", 0, Allow(u, Modify));

        foreach (var f in new[] { unreadable, unknownShare, local })
        {
            var grant = EffectiveAccessInternals.Grant(b.Scan, f, u);
            Assert.Null(grant.ViaShare);
            Assert.Equal(AccessLevel.Aendern, grant.Effective);
        }
    }

    [Fact]
    public void Freigabe_wird_ohne_Gross_Klein_und_abschliessenden_Trenner_gefunden()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u").WithShare(new ShareAce(S.Everyone, ShareAccess.Lesen, false));
        var f = b.Folder("", 0, Allow(u, Modify));
        f.ShareUnc = @"\\srv\DATEN\";
        Assert.Equal(AccessLevel.Lesen, EffectiveAccessInternals.Grant(b.Scan, f, u).ViaShare);
    }

    // ---------- ForFolder ----------

    [Fact]
    public void ForFolder_Wurzel_ohne_Ersteller_Besitzer()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.ForFolder(scan, Node(scan, ""));
        Assert.Equal([S.System, S.Administrators, S.LaborAlle], grants.Select(g => g.Sid));
        Assert.Equal([AccessLevel.Vollzugriff, AccessLevel.Vollzugriff, AccessLevel.Aendern], grants.Select(g => g.Effective));
        Assert.All(grants, g => Assert.Empty(g.Via));
        Assert.All(grants, g => Assert.Equal(g.Ntfs, g.ViaShare)); // Freigabe: Jeder Vollzugriff
    }

    [Fact]
    public void ForFolder_ohne_Konten_mit_nur_Verweigerung()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.ForFolder(scan, Node(scan, @"\Archiv"));
        Assert.Equal([S.System, S.Administrators, S.LaborAlle, S.AuthenticatedUsers], grants.Select(g => g.Sid));
        Assert.DoesNotContain(grants, g => g.Sid == S.Max);
        Assert.Equal(AccessLevel.Lesen, grants[3].Effective);
    }

    [Fact]
    public void ForFolder_Verweigerung_fuer_Jeder_trifft_auch_Gruppen()
    {
        var g = Sid(1001);
        var b = new ScanBuilder().Group(g, @"T\Team").WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));
        var f = b.Folder("", 0, Deny(S.Everyone, FileSystemRights.Delete), Allow(g, Modify));
        var grant = Assert.Single(EffectiveAccess.ForFolder(b.Scan, f));
        Assert.Equal(g, grant.Sid);
        Assert.Equal(AccessLevel.Schreiben, grant.Ntfs);
        Assert.True(grant.ReducedByDeny);
        Assert.Equal(AccessLevel.Schreiben, grant.ViaShare);
    }

    [Fact]
    public void ForFolder_mehrere_Eintraege_derselben_SID_werden_zusammengefasst()
    {
        var g = Sid(1001);
        var b = new ScanBuilder().Group(g, @"T\Team");
        var f = b.LocalFolder(@"D:\X", 0,
            Allow(g, FileSystemRights.ReadAndExecute),
            Allow(g, FileSystemRights.Write | FileSystemRights.Delete, inheritance: InheritanceFlags.None),
            Allow(g, FileSystemRights.Synchronize));
        var grant = Assert.Single(EffectiveAccess.ForFolder(b.Scan, f));
        Assert.Equal(AccessLevel.Aendern, grant.Ntfs);
        Assert.Null(grant.ViaShare);
    }

    [Fact]
    public void ForFolder_nur_Synchronisieren_ist_kein_Zugriff()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        var f = b.LocalFolder(@"D:\X", 0, Allow(u, FileSystemRights.Synchronize));
        Assert.Empty(EffectiveAccess.ForFolder(b.Scan, f));
    }

    [Fact]
    public void ForFolder_und_PeopleForFolder_bei_nicht_lesbarem_Ordner_leer()
    {
        var scan = S.Create();
        var personal = Node(scan, @"\Personal");
        Assert.Empty(EffectiveAccess.ForFolder(scan, personal));
        Assert.Empty(EffectiveAccess.PeopleForFolder(scan, personal));
    }

    // ---------- PeopleForFolder ----------

    [Fact]
    public void PeopleForFolder_Wurzel_nur_Personen_mit_Zugriff()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.PeopleForFolder(scan, Node(scan, ""));
        Assert.Equal([S.Erika, S.Max], grants.Select(g => g.Sid));
        Assert.All(grants, g => Assert.Equal(AccessLevel.Aendern, g.Effective));
        Assert.Equal([@"LAB\QC-Team", @"LAB\Labor-Alle"], grants[0].Via);
    }

    [Fact]
    public void PeopleForFolder_QC_sortiert_nach_Recht()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.PeopleForFolder(scan, Node(scan, @"\QC"));
        Assert.Equal([S.Max, S.Erika], grants.Select(g => g.Sid));
        Assert.Equal([AccessLevel.Vollzugriff, AccessLevel.Aendern], grants.Select(g => g.Effective));
    }

    [Fact]
    public void PeopleForFolder_breite_Gruppe_wird_zusaetzlich_genannt()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.PeopleForFolder(scan, Node(scan, @"\Austausch"));
        Assert.Equal([S.Everyone, S.Erika, S.Klaus, S.Max], grants.Select(g => g.Sid));
        Assert.All(grants, g => Assert.Equal(AccessLevel.Aendern, g.Effective));
        Assert.Empty(grants[0].Via);
    }

    [Fact]
    public void PeopleForFolder_Archiv_mit_Verweigerung_und_Authentifizierten()
    {
        var scan = S.Create();
        var grants = EffectiveAccess.PeopleForFolder(scan, Node(scan, @"\Archiv"));
        Assert.Equal([S.Erika, S.Klaus, S.AuthenticatedUsers], grants.Select(g => g.Sid));
        Assert.Equal([AccessLevel.Aendern, AccessLevel.Lesen, AccessLevel.Lesen], grants.Select(g => g.Effective));
    }

    [Fact]
    public void PeopleForFolder_breite_Gruppe_auch_als_Mitglied_einer_berechtigten_Gruppe()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u")
            .Known(S.AuthenticatedUsers, @"NT-AUTORITÄT\Authentifizierte Benutzer")
            .Known(SidCatalog.Interactive, @"NT-AUTORITÄT\INTERAKTIV")
            .Group(S.BuiltinUsers, @"VORDEFINIERT\Benutzer", S.AuthenticatedUsers, SidCatalog.Interactive);
        var f = b.LocalFolder(@"D:\Daten", 0, Allow(S.BuiltinUsers, Read));

        var grants = EffectiveAccess.PeopleForFolder(b.Scan, f);
        var auth = Assert.Single(grants, g => g.Sid == S.AuthenticatedUsers);
        Assert.Equal(AccessLevel.Lesen, auth.Effective);
        Assert.Equal([@"VORDEFINIERT\Benutzer"], auth.Via);
        Assert.Contains(grants, g => g.Sid == SidCatalog.Interactive); // lokal: INTERAKTIV zählt
        Assert.Contains(grants, g => g.Sid == S.BuiltinUsers);
        Assert.Contains(grants, g => g.Sid == u);

        // Über die Freigabe ist INTERAKTIV bedeutungslos
        b.WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));
        var shared = b.Folder(@"\Netz", 1, Allow(S.BuiltinUsers, Read));
        Assert.DoesNotContain(EffectiveAccess.PeopleForFolder(b.Scan, shared), g => g.Sid == SidCatalog.Interactive);
    }

    [Fact]
    public void PeopleForFolder_keine_breite_Gruppe_ohne_Bezug_zur_ACL()
    {
        var scan = S.Create();
        // Jeder ist im Lauf bekannt, steht aber an QC nirgends
        Assert.DoesNotContain(EffectiveAccess.PeopleForFolder(scan, Node(scan, @"\QC")), g => SidCatalog.IsBroad(g.Sid));
    }
}

/// <summary>Zugriff auf das Recht einer Person auf einen einzelnen Knoten (interne API).</summary>
internal static class EffectiveAccessInternals
{
    public static AccessGrant Grant(ScanResult scan, FolderNode folder, string sid)
    {
        var ctx = new AccessContext(scan);
        return EffectiveAccess.ForToken(ctx, folder, sid, AccessContext.LogonFor(folder));
    }
}
