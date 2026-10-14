using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using static Zutritt.Tests.Reports.ReportSamples;
using static Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Reports;

public sealed class ReportDataBuilderTests
{
    [Fact]
    public void Header_UebernimmtLaufangabenUndZaehler()
    {
        var scan = Scan();
        var data = Data(scan);

        var h = data.Header;
        Assert.Equal([Root], h.Targets);
        Assert.Equal("ADMIN-PC", h.Machine);
        Assert.Equal(@"LAB\admin.max", h.ScannedBy);
        Assert.Equal("1.0.0", h.ToolVersion);
        Assert.Equal(CreatedAt, h.CreatedAt);
        Assert.Equal(scan.StartedAt, h.StartedAt);
        Assert.Equal(scan.FinishedAt, h.FinishedAt);
        Assert.Equal(1234, h.FoldersVisited);
        Assert.Equal(6, h.FoldersStored);
        Assert.Equal(0, h.FilesStored);
        Assert.Equal(scan.Principals.Count, h.Principals);
        Assert.False(string.IsNullOrWhiteSpace(h.ReportVersion));
        Assert.DoesNotContain('+', h.ReportVersion);
    }

    [Fact]
    public void Header_OhneZiele_NimmtDieWurzeln()
    {
        var scan = Scan();
        scan.Request = new ScanRequest { Targets = [] };
        Assert.Equal([Root], Data(scan).Header.Targets);
    }

    [Fact]
    public void Befunde_NachSchwereSortiert_MitRegeltextenUndNamen()
    {
        var data = Data();

        Assert.Equal(13, data.Findings.Count);
        Assert.Equal(2, data.CountFindings(Severity.Hoch));
        Assert.Equal(5, data.CountFindings(Severity.Mittel));
        Assert.Equal(3, data.CountFindings(Severity.Niedrig));
        Assert.Equal(3, data.CountFindings(Severity.Info));
        // Absteigend nach Schwere
        Assert.True(data.Findings.Zip(data.Findings.Skip(1)).All(p => p.First.Severity >= p.Second.Severity));

        var first = data.Findings[0];
        Assert.Equal("ZT01", first.RuleId);
        Assert.Equal("Breite Gruppe darf schreiben", first.Title);
        Assert.Equal("Schreibrecht nur an Fachgruppen vergeben.", first.Recommendation);
        Assert.Equal(Austausch, first.Path);
        Assert.Equal("Jeder", first.Account);

        var zt05 = data.Findings.Single(f => f.RuleId == "ZT05");
        Assert.Equal(@"LAB\max.muster", zt05.Account);
        Assert.Equal(Max, zt05.Sid);

        // Befund ohne Konto
        Assert.Equal("", data.Findings.Single(f => f.RuleId == "ZT06").Account);
        // Innerhalb gleicher Regel nach Pfad: QC kommt nach Projekte\…
        var zt03 = data.Findings.Where(f => f.RuleId == "ZT03").Select(f => f.Path).ToList();
        Assert.Equal([Rohdaten, Qc], zt03);
    }

    [Fact]
    public void Pruefregeln_AlleRegelnMitAnzahl_UnbekannteRegelWirdErgaenzt()
    {
        var scan = Scan();
        scan.Findings.Add(F("ZT99", Severity.Hoch, Root, null, "Neue Regel aus einer neueren Version."));
        var data = Data(scan);

        Assert.Equal(14, data.Rules.Count);
        Assert.Equal(2, data.Rules.Single(r => r.Id == "ZT03").Count);
        Assert.Equal(0, data.Rules.Single(r => r.Id == "ZT13").Count);
        var unknown = data.Rules.Single(r => r.Id == "ZT99");
        Assert.Equal(1, unknown.Count);
        Assert.Equal(Severity.Hoch, unknown.Severity);
        Assert.Equal("ZT99", unknown.Title);
        Assert.Equal("ZT99", data.Findings.Single(f => f.RuleId == "ZT99").Title);
        // Sortiert nach ID
        Assert.Equal(data.Rules.Select(r => r.Id).Order(StringComparer.Ordinal), data.Rules.Select(r => r.Id));
    }

    [Fact]
    public void Ordner_InBaumReihenfolge_MitNamenStattSids()
    {
        var data = Data();

        Assert.Equal([Root, Archiv, Austausch, Personal, Rohdaten, Qc], data.Folders.Select(f => f.Path));

        var austausch = data.Folders.Single(f => f.Path == Austausch);
        Assert.Equal(@"LAB\max.muster", austausch.Owner);
        Assert.False(austausch.InheritanceBroken);
        var everyone = austausch.Aces[0];
        Assert.Equal("Jeder", everyone.Account);
        Assert.Equal(PrincipalKind.Bekannt, everyone.Kind);
        Assert.Equal(AccessLevel.Aendern, everyone.Level);
        Assert.False(everyone.IsInherited);
        Assert.False(everyone.IsDeny);
        Assert.Equal("Diesen Ordner, Unterordner und Dateien", everyone.AppliesTo);
        // ACL-Reihenfolge bleibt erhalten
        Assert.Equal([Everyone, TestSupport.SampleScan.System, Administrators, LaborAlle], austausch.Aces.Select(a => a.Sid));

        var rohdaten = data.Folders.Single(f => f.Path == Rohdaten);
        var orphan = rohdaten.Aces.Single(a => a.Sid == Verwaist);
        Assert.Equal(Verwaist, orphan.Account);
        Assert.Equal(PrincipalKind.Verwaist, orphan.Kind);
        Assert.Equal(4, rohdaten.Depth);
        Assert.True(rohdaten.InheritanceBroken);

        var archiv = data.Folders.Single(f => f.Path == Archiv);
        Assert.True(archiv.Aces.Single(a => a.Sid == Max).IsDeny);

        var personal = data.Folders.Single(f => f.Path == Personal);
        Assert.Equal("Zugriff verweigert", personal.Error);
        Assert.Empty(personal.Aces);
        Assert.Equal("", personal.Owner);
        Assert.Equal(1, data.UnreadableFolders);
    }

    [Fact]
    public void Ordner_UnbekannteSid_ZeigtSidUndArtUnbekannt()
    {
        var scan = Scan();
        const string stranger = "S-1-5-21-9-9-9-1234";
        scan.Folders[0].Aces.Add(Ace(stranger, Read));
        scan.Folders[0].OwnerSid = stranger;

        var root = Data(scan).Folders.Single(f => f.Path == Root);
        var ace = root.Aces.Single(a => a.Sid == stranger);
        Assert.Equal(stranger, ace.Account);
        Assert.Equal(PrincipalKind.Unbekannt, ace.Kind);
        Assert.Equal(stranger, root.Owner);
    }

    [Fact]
    public void Ordner_PrinzipalOhneNamen_FaelltAufSidZurueck()
    {
        var scan = Scan();
        scan.Principals[Max].Name = "";
        var data = Data(scan);

        Assert.Equal(Max, data.Folders.Single(f => f.Path == Qc).Aces.Single(a => a.Sid == Max).Account);
        Assert.Contains(data.People, p => p.Name == Max);
    }

    [Fact]
    public void Personen_SummenUndEinzelzugriffe()
    {
        var data = Data();

        Assert.Equal([@"LAB\erika.beispiel", @"LAB\klaus.alt", @"LAB\max.muster"], data.People.Select(p => p.Name));
        Assert.Equal(3, data.PeopleWithAccess);

        var max = data.People.Single(p => p.Sid == Max);
        Assert.Equal("Max Muster", max.DisplayName);
        Assert.Equal(3, max.FolderCount);
        Assert.Equal(AccessLevel.Vollzugriff, max.Highest);
        Assert.True(max.HasDirect);
        Assert.False(max.IsDisabled);
        Assert.Equal(["Jeder", @"LAB\Labor-Alle"], max.ViaGroups);
        Assert.True(max.GrantsComplete);
        // Nach Pfad sortiert, „direkt“ statt leerer Kette
        Assert.Equal([Root, Austausch, Qc], max.Grants.Select(g => g.Path));
        Assert.Equal("direkt", max.Grants.Single(g => g.Path == Qc).Via);

        var erika = data.People.Single(p => p.Sid == Erika);
        Assert.Equal(@"LAB\QC-Team → LAB\Labor-Alle", erika.Grants.Single(g => g.Path == Root).Via);
        Assert.False(erika.HasDirect);
        Assert.Equal(["Jeder", @"LAB\Labor-Alle", @"LAB\QC-Team"], erika.ViaGroups);

        Assert.True(data.People.Single(p => p.Sid == Klaus).IsDisabled);
        Assert.False(data.PersonGrantsTruncated);
    }

    [Fact]
    public void Personen_EinzelzugriffeInBaumReihenfolge_AuchPfadeAusserhalbDerOrdner()
    {
        const string fremdB = @"\\FS02\Daten\B", fremdA = @"\\FS02\Daten\A";
        var data = ReportDataBuilder.Build(Scan(), null, Rules(), sid => sid == Max
            ? [G(fremdB, Max, AccessLevel.Lesen), G(Qc, Max, AccessLevel.Lesen), G(fremdA, Max, AccessLevel.Lesen),
               G(Rohdaten, Max, AccessLevel.Lesen), G(Root, Max, AccessLevel.Lesen), G(Archiv, Max, AccessLevel.Lesen)]
            : [], CreatedAt);

        // Gespeicherte Ordner in Baum-Reihenfolge (wie im Blatt „Ordner & Rechte“), unbekannte Pfade danach sortiert
        Assert.Equal([Root, Archiv, Rohdaten, Qc, fremdA, fremdB], data.People.Single(p => p.Sid == Max).Grants.Select(g => g.Path));
    }

    [Fact]
    public void Personen_VieleZugriffe_ImZeitbudget()
    {
        // Je Person neu nach Pfad (deutsch, abschnittsweise) sortiert dauerte das rund 20 s – jetzt um 1 s
        var scan = Large(folders: 5_000, acesPerFolder: 2, users: 1_000);
        var grants = scan.Folders.AsEnumerable().Reverse().Select(f => G(f.Path, "x", AccessLevel.Lesen, @"LAB\Gruppe-01")).ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = ReportDataBuilder.Build(scan, null, Rules(), _ => grants, CreatedAt);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Aufbereiten brauchte {sw.Elapsed}");
        var first = data.People.First(p => p.FolderCount == 5_000);
        Assert.Equal(data.Folders.Select(f => f.Path), first.Grants.Select(g => g.Path));
    }

    [Fact]
    public void Personen_KeinZugriffZaehltNicht()
    {
        var data = ReportDataBuilder.Build(Scan(), null, Rules(),
            sid => sid == Max ? [G(Root, Max, AccessLevel.Keine), G(Qc, Max, AccessLevel.Lesen)] : [], CreatedAt);

        var max = data.People.Single(p => p.Sid == Max);
        Assert.Equal(1, max.FolderCount);
        Assert.Equal(AccessLevel.Lesen, max.Highest);
        var erika = data.People.Single(p => p.Sid == Erika);
        Assert.Equal(0, erika.FolderCount);
        Assert.Equal(AccessLevel.Keine, erika.Highest);
        Assert.Equal(1, data.PeopleWithAccess);
    }

    [Fact]
    public void Personen_EinzelzugriffeBegrenzt_NurGanzePersonen_SummenBleiben()
    {
        // Budget 4: Erika (4) passt genau, Klaus und Max (je 3) nicht mehr
        var data = Data(maxPersonGrants: 4);

        var erika = data.People.Single(p => p.Sid == Erika);
        Assert.True(erika.GrantsComplete);
        Assert.Equal(4, erika.Grants.Count);
        foreach (var sid in new[] { Klaus, Max })
        {
            var p = data.People.Single(x => x.Sid == sid);
            Assert.False(p.GrantsComplete);
            Assert.Empty(p.Grants);
            Assert.Equal(3, p.FolderCount);
        }
        Assert.True(data.PersonGrantsTruncated);
        Assert.Contains(data.Notes, n => n.Kind == NoteKind.Warnung && n.Text.Contains("für 2 Personen sind nur die Summen"));
    }

    [Fact]
    public void Gruppen_TransitiveMitglieder_NutzungUndHinweise()
    {
        var data = Data();
        GroupRow Group(string sid) => data.Groups.Single(g => g.Sid == sid);

        // Nur Kind=Gruppe, alphabetisch
        Assert.Equal(data.Groups.Select(g => g.Name).Order(ReportText.NameOrder), data.Groups.Select(g => g.Name));
        Assert.DoesNotContain(data.Groups, g => g.Sid == Everyone);

        var labor = Group(LaborAlle);
        Assert.Equal(2, labor.DirectCount);
        Assert.Equal(2, labor.TotalPeople); // Max direkt, Erika über QC-Team
        Assert.True(labor.Complete);
        Assert.Equal(["LAB\\max.muster", @"LAB\QC-Team"], labor.DirectMembers);
        Assert.Equal(3, labor.UsedInFolders); // Wurzel, Austausch (geerbt), Archiv (geerbt)
        Assert.Equal("", labor.Note);

        var builtinUsers = Group(BuiltinUsers);
        Assert.Equal(3, builtinUsers.TotalPeople); // über Domänen-Benutzer
        Assert.Contains("Authentifizierte Benutzer", builtinUsers.Note);
        Assert.Contains("praktisch alle Benutzer", builtinUsers.Note);

        Assert.Equal(5, Group(Administrators).UsedInFolders);
        var empty = Group(LeereGruppe);
        Assert.Equal(0, empty.DirectCount);
        Assert.Equal(0, empty.TotalPeople);
    }

    [Fact]
    public void Gruppen_ZyklusEndet_UndNichtLesbareGruppenWerdenGenannt()
    {
        var scan = Scan();
        const string a = Domain + "-7001", b = Domain + "-7002", c = Domain + "-7003";
        scan.Principals[a] = new Principal { Sid = a, Name = @"LAB\Zyklus-A", Kind = PrincipalKind.Gruppe };
        scan.Principals[b] = new Principal { Sid = b, Name = @"LAB\Zyklus-B", Kind = PrincipalKind.Gruppe };
        scan.Principals[c] = new Principal { Sid = c, Name = @"LAB\Extern", Kind = PrincipalKind.Gruppe, MembersUnknown = true };
        scan.Principals[a].Members.AddRange([b, Max]);
        scan.Principals[b].Members.AddRange([a, Erika, c]);

        var data = Data(scan);
        var groupA = data.Groups.Single(g => g.Sid == a);
        Assert.Equal(2, groupA.TotalPeople);
        Assert.False(groupA.Complete);
        Assert.Contains("„LAB\\Extern“", groupA.Note);
        Assert.Contains("Anzahl unvollständig", groupA.Note);

        var external = data.Groups.Single(g => g.Sid == c);
        Assert.False(external.Complete);
        Assert.Contains("Mitglieder nicht lesbar", external.Note);
        Assert.Contains(data.Notes, n => n.Kind == NoteKind.Warnung && n.Text.Contains("Bei 1 Gruppe konnten die Mitglieder nicht gelesen werden"));
    }

    [Fact]
    public void Freigaben_RechteAlsText_NichtLesbarUndLeer()
    {
        var scan = Scan();
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Geheim", PermissionsReadable = false });
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Leer", Permissions = [] });
        scan.Shares.Add(new ShareInfo
        {
            Server = "FS01", Name = "C$", IsAdminShare = true,
            Permissions = [new ShareAce(Administrators, ShareAccess.Vollzugriff, false), new ShareAce(Max, ShareAccess.Aendern, true)],
        });

        var data = Data(scan);
        Assert.Equal([@"\\FS01\C$", @"\\FS01\Geheim", @"\\FS01\Labor", @"\\FS01\Leer"], data.Shares.Select(s => s.Unc));
        Assert.Equal("Jeder: Vollzugriff", data.Shares.Single(s => s.Unc == Root).PermissionsText);
        Assert.Equal("nicht lesbar", data.Shares.Single(s => s.Unc == @"\\FS01\Geheim").PermissionsText);
        Assert.False(data.Shares.Single(s => s.Unc == @"\\FS01\Geheim").Readable);
        Assert.StartsWith("keine Einträge", data.Shares.Single(s => s.Unc == @"\\FS01\Leer").PermissionsText);
        var admin = data.Shares.Single(s => s.Unc == @"\\FS01\C$");
        Assert.True(admin.IsAdminShare);
        // Verweigern „ab Ändern“: Lesen bleibt möglich – ausgeschrieben, was gesperrt ist
        Assert.Equal(@"VORDEFINIERT\Administratoren: Vollzugriff; LAB\max.muster: Verweigert: Ändern und Vollzugriff (ab Ändern)", admin.PermissionsText);
        Assert.Contains(data.Notes, n => n.Kind == NoteKind.Warnung && n.Text.Contains("Bei 1 Freigabe waren die Freigabe-Berechtigungen nicht lesbar"));
    }

    [Fact]
    public void Hinweise_NennenNichtLesbaresUndPruefumfang()
    {
        var scan = Scan();
        scan.Errors.Add(new ScanError(@"\\FS02", "Server nicht erreichbar"));
        scan.Request = scan.Request with { MaxDepth = 2, ResolveGroups = false };

        var notes = Data(scan).Notes;
        var warnings = notes.Where(n => n.Kind == NoteKind.Warnung).Select(n => n.Text).ToList();
        Assert.Contains("1 Ordner nicht lesbar – dort konnte nichts geprüft werden.", warnings);
        Assert.Contains(warnings, w => w.StartsWith("1 weiterer Fehler beim Lesen"));
        Assert.Contains(warnings, w => w.Contains("auf 2 Ebenen begrenzt"));
        Assert.Contains(warnings, w => w.Contains("nicht bis zur Person aufgelöst"));
        // Warnungen zuerst, dann die Lesehilfen
        Assert.True(notes.SkipWhile(n => n.Kind == NoteKind.Warnung).All(n => n.Kind == NoteKind.Hinweis));
        Assert.Contains(notes, n => n.Kind == NoteKind.Hinweis && n.Text.Contains("strengere von Freigabe- und NTFS-Berechtigung"));
    }

    [Fact]
    public void Hinweise_FehlerOhneNichtLesbareOrdner_HeissenNichtWeitere()
    {
        var scan = Scan();
        scan.Folders.RemoveAll(f => f.Error is not null);
        scan.Errors.Clear();
        scan.Errors.Add(new ScanError(@"\\FS02", "Server nicht erreichbar"));
        scan.Errors.Add(new ScanError(@"\\FS03", "Server nicht erreichbar"));

        var warnings = Data(scan).Notes.Where(n => n.Kind == NoteKind.Warnung).Select(n => n.Text).ToList();
        Assert.Equal(["2 Fehler beim Lesen (z. B. Server oder Freigaben nicht erreichbar)."], warnings);
    }

    [Fact]
    public void Hinweise_SauberZielOhneFreigabe_KeineWarnungen()
    {
        var scan = Scan();
        scan.Folders.RemoveAll(f => f.Error is not null);
        scan.Errors.Clear();
        scan.Shares.Clear();
        foreach (var f in scan.Folders) f.ShareUnc = null;

        var notes = Data(scan).Notes;
        Assert.DoesNotContain(notes, n => n.Kind == NoteKind.Warnung);
        Assert.DoesNotContain(notes, n => n.Text.Contains("Freigabe"));
    }

    [Fact]
    public void Vergleich_AlleArtenAlsZeilen_NamenAuchAusAltemLauf()
    {
        var data = DataWithComparison();
        var c = Assert.IsType<ComparisonData>(data.Comparison);

        Assert.Equal(1, c.NewFindings);
        Assert.Equal(1, c.ResolvedFindings);
        Assert.Equal(1, c.MoreAccess);
        Assert.Equal(1, c.LessAccess);
        Assert.Equal(3, c.AceChanges);
        Assert.Equal(1, c.FoldersAdded);
        Assert.Equal(1, c.FoldersRemoved);
        Assert.Equal(2, c.MembershipChanges);
        Assert.Equal(11, c.Rows.Count);
        Assert.True(c.HasChanges);

        // Reihenfolge der Bereiche: Befunde, dann Ursachen, zuletzt die (oft zahlreichen) Folgen für Personen
        Assert.Equal(["Befund", "Befund", "Berechtigung", "Berechtigung", "Berechtigung", "Mitgliedschaft", "Mitgliedschaft",
            "Ordner", "Ordner", "Zugriff", "Zugriff"], c.Rows.Select(r => r.Area));
        Assert.Equal(["Mehr Zugriff", "Weniger Zugriff"], c.Rows.Where(r => r.Area == "Zugriff").Select(r => r.Change));

        var neu = c.Rows[0];
        Assert.Equal("Neu", neu.Change);
        Assert.Equal(Austausch, neu.Subject);
        Assert.Equal("Jeder", neu.Account);
        Assert.StartsWith("ZT01 Breite Gruppe darf schreiben (Hoch)", neu.After);

        var behoben = c.Rows[1];
        Assert.Equal("Behoben", behoben.Change);
        Assert.Equal(@"LAB\ehemalig", behoben.Account); // nur im älteren Lauf bekannt

        var mehr = c.Rows.Single(r => r.Area == "Zugriff" && r.Account == @"LAB\max.muster");
        Assert.Equal(("Mehr Zugriff", "Ändern", "Vollzugriff"), (mehr.Change, mehr.Before, mehr.After));
        var weniger = c.Rows.Single(r => r.Area == "Zugriff" && r.Account == @"LAB\klaus.alt");
        Assert.Equal("Weniger Zugriff", weniger.Change);

        var added = c.Rows.Single(r => r.Area == "Berechtigung" && r.Change == "Hinzugefügt");
        Assert.Equal("", added.Before);
        Assert.Equal("Ändern · Erlauben · explizit · Diesen Ordner, Unterordner und Dateien", added.After);
        // Gleiche Stufe, andere Maske: Maske wird gezeigt
        var mask = c.Rows.Single(r => r.Area == "Berechtigung" && r.Subject == Archiv);
        Assert.Contains("(0x", mask.Before);
        Assert.NotEqual(mask.Before, mask.After);

        var removedMember = c.Rows.Single(r => r.Area == "Mitgliedschaft" && r.Change == "Entfernt");
        Assert.Equal(@"LAB\QC-Team", removedMember.Subject);
        Assert.Equal(@"LAB\ehemalig", removedMember.Account);
        Assert.Contains(c.Rows, r => r.Area == "Mitgliedschaft" && r.Change == "Aufgenommen" && r.Account == @"LAB\max.muster");

        // Einheitliche Schreibweise der Spalte „Änderung“: am Zellanfang groß, wie „Hinzugefügt“ und „Geändert“
        Assert.Equal(["Aufgenommen", "Behoben", "Entfernt", "Geändert", "Hinzugefügt", "Keine eigenen Rechte mehr", "Mehr Zugriff", "Neu",
            "Neu mit eigenen Rechten", "Weniger Zugriff"], c.Rows.Select(r => r.Change).Distinct().Order(StringComparer.Ordinal));
        Assert.All(c.Rows, r => Assert.True(char.IsUpper(r.Change[0]), r.Change));
    }

    [Fact]
    public void Vergleich_OhneAenderungen()
    {
        var newer = Scan();
        var c = new ComparisonResult { Older = TestSupport.SampleScan.Create(newer.StartedAt.AddDays(-1)), Newer = newer };
        var data = Data(newer, c);
        Assert.NotNull(data.Comparison);
        Assert.False(data.Comparison!.HasChanges);
        Assert.Empty(data.Comparison.Rows);
    }

    [Fact]
    public void Leerer_Lauf_ErgibtLeereListen()
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [@"C:\Leer"] } };
        var data = Data(scan);

        Assert.Empty(data.Findings);
        Assert.Empty(data.Folders);
        Assert.Empty(data.People);
        Assert.Empty(data.Groups);
        Assert.Empty(data.Shares);
        Assert.Equal(13, data.Rules.Count);
        Assert.All(data.Rules, r => Assert.Equal(0, r.Count));
        Assert.Null(data.Comparison);
    }
}
