using System.Security.AccessControl;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

public sealed class FindingRulesTests
{
    private const FileSystemRights Read = S.Read;
    private const FileSystemRights Modify = S.Modify;
    private const FileSystemRights Full = S.Full;
    private const string Root = @"\\FS01\Labor";

    private static string Key(string rule, string relative, string? sid) => $"{rule}|{Root}{relative}|{sid}".ToUpperInvariant();

    [Fact]
    public void Beispiel_ergibt_genau_die_erwarteten_Befunde()
    {
        var findings = FindingRules.Evaluate(S.Create());
        var expected = new[]
        {
            Key("ZT01", @"\Austausch", S.Everyone),
            Key("ZT02", @"\Archiv", S.AuthenticatedUsers),
            Key("ZT03", @"\QC", S.Max),
            Key("ZT03", @"\Projekte\2019\Alt\Rohdaten", S.Klaus),
            Key("ZT04", @"\Projekte\2019\Alt\Rohdaten", S.Verwaist),
            Key("ZT05", @"\QC", S.Max),
            Key("ZT06", @"\Projekte\2019\Alt\Rohdaten", null),
            Key("ZT07", @"\Archiv", S.Max),
            Key("ZT08", "", S.Everyone),
            Key("ZT09", @"\Personal", null),
            Key("ZT10", @"\Austausch", S.Max),
            Key("ZT11", @"\Projekte\2019\Alt\Rohdaten", S.LeereGruppe),
            Key("ZT12", @"\Projekte\2019\Alt\Rohdaten", S.Klaus),
        };
        Assert.Equal(expected.Order(), findings.Select(f => f.Key).Order());
    }

    [Fact]
    public void Befunde_haben_Schwere_der_Regel_und_sind_sortiert()
    {
        var findings = FindingRules.Evaluate(S.Create());
        Assert.All(findings, f => Assert.Equal(FindingRules.Get(f.RuleId)!.Severity, f.Severity));
        Assert.Equal(findings.OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId, StringComparer.Ordinal).Select(f => f.Key),
            findings.Select(f => f.Key));
        Assert.Equal(Severity.Hoch, findings[0].Severity);
        Assert.Equal(Severity.Info, findings[^1].Severity);
    }

    [Fact]
    public void Meldungen_nennen_Namen_auf_Deutsch()
    {
        var findings = FindingRules.Evaluate(S.Create());
        string Msg(string rule, string? sid = null) => findings.Single(f => f.RuleId == rule && (sid is null || f.Sid == sid)).Message;

        Assert.Equal("„Jeder“ hat „Ändern“ – praktisch jede angemeldete Person darf hier schreiben.", Msg("ZT01"));
        Assert.Contains(@"„NT-AUTORITÄT\Authentifizierte Benutzer“ hat „Lesen“", Msg("ZT02"));
        Assert.Contains(@"„LAB\max.muster“ hat „Vollzugriff“ direkt als Person", Msg("ZT03", S.Max));
        Assert.Contains(S.Verwaist, Msg("ZT04"));
        Assert.Contains("Tiefe 4", Msg("ZT06"));
        Assert.Contains("„Ändern“ verweigert", Msg("ZT07"));
        Assert.Contains("Zugriff verweigert", Msg("ZT09"));
        Assert.Equal(@"Besitzer ist das Benutzerkonto „LAB\max.muster“.", Msg("ZT10"));
        Assert.Contains(@"„LAB\Alt-Projekt“ hat keine Mitglieder", Msg("ZT11"));
        Assert.Contains(@"deaktivierte Konto „LAB\klaus.alt“", Msg("ZT12"));
        Assert.All(findings, f => Assert.DoesNotContain("\"", f.Message));
    }

    [Fact]
    public void Regelkatalog_vollstaendig()
    {
        var all = FindingRules.All;
        Assert.Equal(Enumerable.Range(1, 14).Select(i => $"ZT{i:00}"), all.Select(r => r.Id));
        Assert.Equal(
            [Severity.Hoch, Severity.Mittel, Severity.Mittel, Severity.Mittel, Severity.Hoch, Severity.Niedrig, Severity.Info,
             Severity.Info, Severity.Info, Severity.Niedrig, Severity.Niedrig, Severity.Mittel, Severity.Info, Severity.Niedrig],
            all.Select(r => r.Severity));
        Assert.All(all, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.True(r.Explanation.Length > 40, r.Id);
            Assert.True(r.Recommendation.Length > 20, r.Id);
        });
        Assert.Equal("Breite Gruppe darf schreiben", FindingRules.Get("zt01")!.Title);
        Assert.Null(FindingRules.Get("ZT99"));
    }

    [Fact]
    public void Geerbte_Eintraege_erzeugen_keine_ACL_Befunde()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        b.Folder(@"\Unten", 1,
            Allow(S.Everyone, Modify, inherited: true),
            Allow(u, Full, inherited: true),
            Deny(u, FileSystemRights.Delete, inherited: true));
        Assert.Empty(FindingRules.Evaluate(b.Scan));
    }

    [Fact]
    public void Eindeutig_je_Regel_Pfad_und_SID()
    {
        var b = new ScanBuilder();
        b.Folder("", 0,
            Allow(S.Everyone, Modify),
            Allow(S.Everyone, FileSystemRights.Write, inheritance: InheritanceFlags.None),
            Allow(S.Everyone, Read, inheritance: InheritanceFlags.ObjectInherit));
        var finding = Assert.Single(FindingRules.Evaluate(b.Scan));
        Assert.Equal("ZT01", finding.RuleId);
    }

    [Fact]
    public void Breite_Gruppe_nur_fuer_Inhalte_wird_erkannt()
    {
        var b = new ScanBuilder();
        b.Folder("", 0,
            Allow(S.AuthenticatedUsers, Read, inheritance: InheritanceFlags.None),
            Allow(S.AuthenticatedUsers, Modify, inheritance: InheritanceFlags.ObjectInherit, propagation: PropagationFlags.InheritOnly));
        var finding = Assert.Single(FindingRules.Evaluate(b.Scan));
        Assert.Equal("ZT01", finding.RuleId);
        Assert.Contains("(gilt für Unterordner bzw. Dateien)", finding.Message);
    }

    [Fact]
    public void Bereiche_verschmelzen_nicht_zu_einem_hoeheren_Recht()
    {
        // Schreiben nur am Ordner, Löschen nur an Dateien: nirgends „Ändern“
        var g = Sid(1001);
        var b = new ScanBuilder().Group(g, @"T\Team", Sid(2001));
        b.Folder("", 0,
            Allow(S.Everyone, FileSystemRights.ReadData | FileSystemRights.WriteData, inheritance: InheritanceFlags.None),
            Allow(S.Everyone, FileSystemRights.Delete, inheritance: InheritanceFlags.ObjectInherit, propagation: PropagationFlags.InheritOnly));
        var finding = Assert.Single(FindingRules.Evaluate(b.Scan));
        Assert.Contains("„Schreiben“", finding.Message);
    }

    [Fact]
    public void Explizite_Verweigerung_mindert_breite_Gruppe_auf_Lesen()
    {
        var b = new ScanBuilder();
        b.Folder("", 0, Deny(S.Everyone, FileSystemRights.Write), Allow(S.AuthenticatedUsers, Modify));
        var findings = FindingRules.Evaluate(b.Scan);
        Assert.Equal(["ZT02", "ZT07"], findings.Select(f => f.RuleId).Order());
        Assert.Equal(S.AuthenticatedUsers, findings.Single(f => f.RuleId == "ZT02").Sid);
        Assert.Equal(S.Everyone, findings.Single(f => f.RuleId == "ZT07").Sid);
    }

    [Fact]
    public void Breite_Gruppen_inklusive_Domaenen_Benutzer_und_Gaeste()
    {
        var b = new ScanBuilder().Group(Sid(513), @"T\Domänen-Benutzer").Group(Sid(514), @"T\Domänen-Gäste")
            .Group(S.BuiltinUsers, @"VORDEFINIERT\Benutzer", S.AuthenticatedUsers);
        b.Folder("", 0, Allow(Sid(513), Modify), Allow(Sid(514), Read), Allow(S.BuiltinUsers, FileSystemRights.ListDirectory));
        b.Folder(@"\X", 1, Allow(SidCatalog.Interactive, FileSystemRights.AppendData), Allow(SidCatalog.Network, Read));
        var keys = FindingRules.Evaluate(b.Scan).Select(f => (f.RuleId, f.Sid)).ToList();
        Assert.Contains(("ZT01", Sid(513)), keys);
        Assert.Contains(("ZT02", Sid(514)), keys);
        Assert.Contains(("ZT02", S.BuiltinUsers), keys);
        Assert.Contains(("ZT01", SidCatalog.Interactive), keys);
        Assert.Contains(("ZT02", SidCatalog.Network), keys);
        Assert.DoesNotContain(keys, k => k.RuleId == "ZT05"); // Domänen-Benutzer ändern ist ZT01, kein Vollzugriff
    }

    [Fact]
    public void Breite_Gruppe_mit_Vollzugriff_ist_ZT01_nicht_ZT05()
    {
        var b = new ScanBuilder();
        b.Folder("", 0, Allow(S.Everyone, Full));
        var finding = Assert.Single(FindingRules.Evaluate(b.Scan));
        Assert.Equal("ZT01", finding.RuleId);
        Assert.Contains("„Vollzugriff“", finding.Message);
    }

    [Fact]
    public void Administrator_Konto_RID_500_ist_keine_Person_im_Sinne_der_Regeln()
    {
        var admin = Sid(500);
        var b = new ScanBuilder().User(admin, @"T\Administrator");
        var f = b.Folder("", 0, Allow(admin, Full));
        f.OwnerSid = admin;
        Assert.Empty(FindingRules.Evaluate(b.Scan));
    }

    [Fact]
    public void Vollzugriff_nur_fuer_Nicht_Admins()
    {
        var team = Sid(1001);
        var b = new ScanBuilder()
            .Group(Sid(512), @"T\Domänen-Admins", Sid(2001))
            .Group(Sid(519), @"T\Organisations-Admins", Sid(2001))
            .Group(team, @"T\Team", Sid(2001))
            .Group(S.Administrators, @"VORDEFINIERT\Administratoren", Sid(512))
            .Group(SidCatalog.BackupOperators, @"VORDEFINIERT\Sicherungs-Operatoren", Sid(2001))
            .Known(SidCatalog.TrustedInstaller, @"NT SERVICE\TrustedInstaller")
            .Known(S.System, @"NT-AUTORITÄT\SYSTEM")
            .Known(SidCatalog.LocalService, @"NT-AUTORITÄT\LOKALER DIENST");
        b.Folder("", 0,
            Allow(Sid(512), Full), Allow(Sid(519), Full), Allow(S.Administrators, Full), Allow(SidCatalog.BackupOperators, Full),
            Allow(SidCatalog.TrustedInstaller, Full), Allow(S.System, Full), Allow(S.CreatorOwner, Full, propagation: PropagationFlags.InheritOnly),
            Allow(SidCatalog.LocalService, Full), Allow(team, Full));
        var finding = Assert.Single(FindingRules.Evaluate(b.Scan));
        Assert.Equal(("ZT05", team), (finding.RuleId, finding.Sid));
        Assert.Equal(Severity.Hoch, finding.Severity);
    }

    [Fact]
    public void Vollzugriff_durch_Recht_Berechtigungen_aendern_und_fuer_unbekannte_Konten()
    {
        var fremd = "S-1-5-21-7-7-7-1234"; // nicht aufgelöst (fremde Domäne)
        var b = new ScanBuilder();
        b.Scan.Principals[fremd] = new Principal { Sid = fremd, Name = fremd, Kind = PrincipalKind.Unbekannt };
        b.Folder("", 0, Allow(fremd, FileSystemRights.ReadAndExecute | FileSystemRights.ChangePermissions));
        Assert.Equal(("ZT05", fremd), FindingRules.Evaluate(b.Scan).Select(f => (f.RuleId, f.Sid)).Single());
    }

    [Fact]
    public void Tief_versteckte_Ausnahme_nach_Schwelle()
    {
        var b = new ScanBuilder();
        b.Scan.Request = new ScanRequest { Targets = [Share], DeepInheritanceBreakDepth = 2 };
        b.Scan.Folders.Add(new FolderNode { Path = Share, Depth = 0, InheritanceBroken = true, ShareUnc = Share });
        b.Scan.Folders.Add(new FolderNode { Path = Share + @"\A", Depth = 1, InheritanceBroken = true, ShareUnc = Share });
        b.Scan.Folders.Add(new FolderNode { Path = Share + @"\A\B", Depth = 2, InheritanceBroken = true, ShareUnc = Share });
        b.Scan.Folders.Add(new FolderNode { Path = Share + @"\A\C", Depth = 2, InheritanceBroken = false, ShareUnc = Share });
        // Die Ausnahmen lassen eine fachliche Gruppe zu (nur Administratoren wäre keine versteckte Freigabe)
        foreach (var f in b.Scan.Folders) f.Aces.Add(Allow(Sid(1001), Read));
        Assert.Equal([Share + @"\A\B"], FindingRules.Evaluate(b.Scan).Where(f => f.RuleId == "ZT06").Select(f => f.Path));

        // Auch bei Schwelle 0 gilt die Wurzel nie als „versteckt“
        b.Scan.Request = b.Scan.Request with { DeepInheritanceBreakDepth = 0 };
        Assert.Equal([Share + @"\A", Share + @"\A\B"], FindingRules.Evaluate(b.Scan).Where(f => f.RuleId == "ZT06").Select(f => f.Path));
    }

    [Fact]
    public void Verweigern_Eintrag_auch_wenn_nur_an_Inhalte_vererbt()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        b.Folder("", 0, Deny(u, FileSystemRights.Delete, propagation: PropagationFlags.InheritOnly));
        var finding = Assert.Single(FindingRules.Evaluate(b.Scan));
        Assert.Equal(("ZT07", u), (finding.RuleId, finding.Sid));
    }

    [Fact]
    public void Verweigerte_Teilrechte_werden_benannt_statt_als_Stufe()
    {
        // ToLevel hieße hier „Kein Zugriff verweigert“ bzw. „Vollzugriff verweigert“ – beides falsch
        var u = Sid(2001); var weg = Sid(2999);
        var b = new ScanBuilder().User(u, @"T\u").Orphan(weg);
        b.Folder("", 0, Deny(S.Everyone, FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles), Allow(S.Administrators, Full));
        b.Folder(@"\A", 1, Deny(u, FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership), Allow(weg, FileSystemRights.Delete));
        b.Folder(@"\B", 1, Deny(u, Full), Deny(S.AuthenticatedUsers, FileSystemRights.Write));
        var findings = FindingRules.Evaluate(b.Scan);
        string Msg(string rule, string path, string sid) => findings.Single(f => f.RuleId == rule && f.Path == Share + path && f.Sid == sid).Message;

        Assert.Equal("Verweigern-Eintrag für „Jeder“ („Löschen“ und „Unterordner und Dateien löschen“ verweigert).", Msg("ZT07", "", S.Everyone));
        Assert.Equal("Verweigern-Eintrag für „T\\u“ („Berechtigungen ändern“ und „Besitz übernehmen“ verweigert).", Msg("ZT07", @"\A", u));
        Assert.Equal("Verweigern-Eintrag für „T\\u“ („Vollzugriff“ verweigert).", Msg("ZT07", @"\B", u));
        Assert.Contains("(„Schreiben“ verweigert)", Msg("ZT07", @"\B", S.AuthenticatedUsers));
        Assert.Contains("hat einen Eintrag „Löschen“", Msg("ZT04", @"\A", weg));
    }

    [Theory]
    [InlineData(FileSystemRights.Modify | FileSystemRights.Synchronize, "„Ändern“")]
    [InlineData(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, "„Lesen“")]
    [InlineData(FileSystemRights.Write | FileSystemRights.Delete, "„Schreiben“ und „Löschen“")]
    [InlineData(FileSystemRights.ReadData | FileSystemRights.WriteData | FileSystemRights.ChangePermissions, "„Lesen“, „Schreiben“ und „Berechtigungen ändern“")]
    [InlineData(FileSystemRights.Traverse, "„Ordner durchsuchen bzw. Datei ausführen“")]
    [InlineData(FileSystemRights.Synchronize, "„Synchronisieren“")]
    [InlineData((FileSystemRights)0x10000000, "„Vollzugriff“")]
    public void Rechte_als_Text(FileSystemRights rights, string text) => Assert.Equal(text, FindingRules.Describe(rights));

    [Fact]
    public void Leere_Gruppe_nur_wenn_Mitglieder_wirklich_bekannt()
    {
        var leer = Sid(1001); var unbekannt = Sid(1002);
        var b = new ScanBuilder().Group(leer, @"T\Leer").Group(unbekannt, @"T\Fremd")
            .Known(S.AuthenticatedUsers, @"NT-AUTORITÄT\Authentifizierte Benutzer");
        b.Scan.Principals[unbekannt].MembersUnknown = true;
        b.Folder("", 0, Allow(leer, Read), Allow(unbekannt, Read), Deny(leer, FileSystemRights.Delete, inheritance: InheritanceFlags.None),
            Allow(S.AuthenticatedUsers, FileSystemRights.Traverse, inheritance: InheritanceFlags.None));

        var zt11 = FindingRules.Evaluate(b.Scan).Where(f => f.RuleId == "ZT11").ToList();
        Assert.Equal(leer, Assert.Single(zt11).Sid);

        // Ohne Gruppenauflösung weiß der Lauf nichts über Mitglieder
        b.Scan.Request = b.Scan.Request with { ResolveGroups = false };
        Assert.DoesNotContain(FindingRules.Evaluate(b.Scan), f => f.RuleId == "ZT11");
    }

    [Fact]
    public void Leere_Gruppe_nur_mit_Verweigerung_ist_nicht_berechtigt()
    {
        var leer = Sid(1001);
        var b = new ScanBuilder().Group(leer, @"T\Leer");
        b.Folder("", 0, Deny(leer, Full));
        Assert.Equal(["ZT07"], FindingRules.Evaluate(b.Scan).Select(f => f.RuleId));
    }

    [Fact]
    public void Deaktiviertes_Konto_nur_mit_Erlauben_Eintrag()
    {
        var aus = Sid(2001); var an = Sid(2002);
        var b = new ScanBuilder().User(aus, @"T\aus", disabled: true).User(an, @"T\an");
        b.Folder("", 0, Allow(an, Read));
        b.Folder(@"\Gesperrt", 1, Deny(aus, Full));
        var findings = FindingRules.Evaluate(b.Scan);
        Assert.DoesNotContain(findings, f => f.RuleId == "ZT12");
        Assert.DoesNotContain(findings, f => f.RuleId == "ZT03" && f.Sid == aus);
        Assert.Contains(findings, f => f.RuleId == "ZT03" && f.Sid == an);
    }

    [Fact]
    public void Verwaiste_SID_als_Besitzer_Eintrag_und_Freigabe()
    {
        var weg = Sid(2999); var auchWeg = Sid(2998);
        var b = new ScanBuilder().Orphan(weg).Orphan(auchWeg)
            .WithShare(new ShareAce(auchWeg, ShareAccess.Lesen, false), new ShareAce(S.Everyone, ShareAccess.Lesen, false));
        var beides = b.Folder("", 0, Allow(weg, Read));
        beides.OwnerSid = weg;
        var nurBesitzer = b.Folder(@"\B", 1);
        nurBesitzer.OwnerSid = auchWeg;

        var zt04 = FindingRules.Evaluate(b.Scan).Where(f => f.RuleId == "ZT04").ToList();
        Assert.Equal(3, zt04.Count);
        Assert.Contains("ist Besitzer und hat einen Eintrag „Lesen“", zt04.Single(f => f.Path == Share && f.Sid == weg).Message);
        Assert.StartsWith("Besitzer ist die verwaiste SID", zt04.Single(f => f.Path == Share + @"\B").Message);
        Assert.Contains("Freigabe-Berechtigung", zt04.Single(f => f.Path == Share && f.Sid == auchWeg).Message);
        Assert.DoesNotContain(FindingRules.Evaluate(b.Scan), f => f.RuleId == "ZT08"); // Jeder nur Lesen
    }

    [Fact]
    public void Freigabe_Jeder_oder_Authentifizierte_ab_Aendern()
    {
        var b = new ScanBuilder();
        b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "A", Permissions = [new ShareAce(S.AuthenticatedUsers, ShareAccess.Aendern, false)] });
        b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "B", Permissions = [new ShareAce(S.Everyone, ShareAccess.Vollzugriff, true)] });
        b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "C", Permissions = [new ShareAce(S.Everyone, ShareAccess.Lesen, false)] });
        b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "D", PermissionsReadable = false });
        var findings = FindingRules.Evaluate(b.Scan);
        Assert.Equal(
            [("ZT08", @"\\SRV\A", (string?)S.AuthenticatedUsers), ("ZT13", @"\\SRV\D", null)],
            findings.Select(f => (f.RuleId, f.Path, f.Sid)).OrderBy(x => x.RuleId));
        Assert.Contains("„Ändern“", findings.Single(f => f.RuleId == "ZT08").Message);
    }

    [Fact]
    public void Fehler_ohne_Knoten_werden_gemeldet_aber_nicht_doppelt()
    {
        var b = new ScanBuilder();
        b.Scan.Folders.Add(new FolderNode { Path = Share + @"\Gesperrt", Depth = 1, ShareUnc = Share, Error = "Zugriff verweigert" });
        b.Scan.Errors.Add(new ScanError(Share + @"\Gesperrt", "Zugriff verweigert"));
        b.Scan.Errors.Add(new ScanError(@"\\FS99", "Server nicht erreichbar."));
        b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "Daten", PermissionsReadable = false });
        b.Scan.Errors.Add(new ScanError(Share, "Freigabe-Berechtigungen nicht lesbar"));

        var findings = FindingRules.Evaluate(b.Scan);
        Assert.Equal([@"\\FS99", Share + @"\Gesperrt"], findings.Where(f => f.RuleId == "ZT09").Select(f => f.Path).Order());
        Assert.Equal("Nicht geprüft: Server nicht erreichbar.", findings.Single(f => f.Path == @"\\FS99").Message);
        Assert.Single(findings, f => f.RuleId == "ZT13");
    }

    [Fact]
    public void Datei_mit_eigenem_Recht_und_Besitzer()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\u");
        var file = new FolderNode { Path = Share + @"\x.txt", Depth = 1, IsFile = true, ShareUnc = Share, OwnerSid = u };
        file.Aces.Add(Allow(u, Modify, inheritance: InheritanceFlags.None));
        b.Scan.Folders.Add(file);
        Assert.Equal(["ZT03", "ZT10"], FindingRules.Evaluate(b.Scan).Select(f => f.RuleId).Order());
    }

    [Fact]
    public void Leerer_Lauf_ohne_Befunde()
    {
        Assert.Empty(FindingRules.Evaluate(new ScanResult()));
    }
}
