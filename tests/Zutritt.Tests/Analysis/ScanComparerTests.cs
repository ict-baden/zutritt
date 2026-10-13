using System.Diagnostics;
using System.Security.AccessControl;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

public sealed class ScanComparerTests
{
    private const string Root = @"\\FS01\Labor";

    /// <summary>Lauf mit gespeicherten Befunden – wie ihn der Scanner liefert.</summary>
    private static ScanResult Evaluated(Action<ScanResult>? change = null, DateTimeOffset? started = null)
    {
        var scan = S.Create(started);
        change?.Invoke(scan);
        scan.Findings.AddRange(FindingRules.Evaluate(scan));
        return scan;
    }

    private static ScanResult Newer(Action<ScanResult> change) =>
        Evaluated(change, new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.FromHours(2)));

    private static FolderNode Node(ScanResult scan, string relative) =>
        scan.Folders.Single(f => f.Path.Equals(Root + relative, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ersetzt die Einträge eines Ordners (Pfad und Flags sind unveränderlich).</summary>
    private static void SetAces(ScanResult scan, string relative, params AceEntry[] aces)
    {
        var node = Node(scan, relative);
        node.Aces.Clear();
        node.Aces.AddRange(aces);
    }

    private static FolderNode AddFolder(ScanResult scan, string relative, int depth, bool broken, params AceEntry[] aces)
    {
        var f = new FolderNode { Path = Root + relative, Depth = depth, InheritanceBroken = broken, ShareUnc = Root, OwnerSid = S.Administrators };
        f.Aces.AddRange(aces);
        scan.Folders.Add(f);
        return f;
    }

    private static AceEntry Inherited(string sid, FileSystemRights rights) => Allow(sid, rights, inherited: true);

    [Fact]
    public void Gleiche_Laeufe_ohne_Aenderungen()
    {
        var result = ScanComparer.Compare(Evaluated(), Newer(_ => { }));
        Assert.False(result.HasChanges);
        Assert.Empty(result.AceChanges);
        Assert.Empty(result.AccessChanges);
        Assert.Empty(result.NewFindings);
        Assert.Empty(result.ResolvedFindings);
    }

    [Fact]
    public void Ergebnis_verweist_auf_beide_Laeufe()
    {
        var older = Evaluated();
        var newer = Newer(_ => { });
        var result = ScanComparer.Compare(older, newer);
        Assert.Same(older, result.Older);
        Assert.Same(newer, result.Newer);
    }

    [Fact]
    public void Eintraege_hinzugefuegt_entfernt_geaendert_mit_Folgen()
    {
        var older = Evaluated();
        var newer = Newer(s =>
        {
            SetAces(s, @"\QC", Allow(S.System, S.Full), Allow(S.Administrators, S.Full), Allow(S.QcTeam, S.Modify), Allow(S.Max, S.Modify));
            SetAces(s, @"\Austausch", Allow(S.AuthenticatedUsers, S.Read),
                Inherited(S.System, S.Full), Inherited(S.Administrators, S.Full), Inherited(S.LaborAlle, S.Modify));
        });

        var result = ScanComparer.Compare(older, newer);

        Assert.Equal(
            [(Root + @"\Austausch", ChangeKind.Entfernt, S.Everyone), (Root + @"\Austausch", ChangeKind.Hinzugefuegt, S.AuthenticatedUsers),
             (Root + @"\QC", ChangeKind.Geaendert, S.Max)],
            result.AceChanges.Select(c => (c.Path, c.Kind, c.Sid)));
        var changed = result.AceChanges[2];
        Assert.Equal(S.Full, changed.Before!.Rights);
        Assert.Equal(S.Modify, changed.After!.Rights);
        Assert.Null(result.AceChanges[0].After);
        Assert.Null(result.AceChanges[1].Before);

        Assert.Equal(
            [new AccessChange(Root + @"\Austausch", S.Klaus, AccessLevel.Aendern, AccessLevel.Lesen),
             new AccessChange(Root + @"\QC", S.Max, AccessLevel.Vollzugriff, AccessLevel.Aendern)],
            result.AccessChanges);

        Assert.Equal([("ZT02", Root + @"\Austausch", S.AuthenticatedUsers)], result.NewFindings.Select(f => (f.RuleId, f.Path, f.Sid!)));
        Assert.Equal([("ZT01", S.Everyone), ("ZT05", S.Max)], result.ResolvedFindings.Select(f => (f.RuleId, f.Sid!)).Order());
        Assert.Empty(result.FoldersAdded);
        Assert.Empty(result.FoldersRemoved);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Neue_Ausnahme_mit_intakter_Vererbung_aendert_keine_Zugriffe()
    {
        // Der Ordner gab es schon, er erbte nur alles; jetzt hat er zusätzlich einen eigenen Eintrag
        var newer = Newer(s => AddFolder(s, @"\Neu", 1, false,
            Allow(S.Erika, S.Read), Inherited(S.System, S.Full), Inherited(S.Administrators, S.Full), Inherited(S.LaborAlle, S.Modify)));

        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Equal([Root + @"\Neu"], result.FoldersAdded);
        Assert.Empty(result.AceChanges);
        Assert.Empty(result.AccessChanges);
        Assert.Equal([("ZT03", S.Erika)], result.NewFindings.Select(f => (f.RuleId, f.Sid!)));
    }

    [Fact]
    public void Neu_unterbrochene_Vererbung_nimmt_Zugriff_weg()
    {
        var newer = Newer(s => AddFolder(s, @"\Geheim", 1, true, Allow(S.Administrators, S.Full), Allow(S.QcTeam, S.Read)));
        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Equal(
            [new AccessChange(Root + @"\Geheim", S.Erika, AccessLevel.Aendern, AccessLevel.Lesen),
             new AccessChange(Root + @"\Geheim", S.Max, AccessLevel.Aendern, AccessLevel.Keine)],
            result.AccessChanges);
    }

    [Fact]
    public void Aufgehobene_Ausnahme_erbt_wieder_vom_naechsten_gespeicherten_Ordner()
    {
        var newer = Newer(s => s.Folders.Remove(Node(s, @"\Projekte\2019\Alt\Rohdaten")));
        var result = ScanComparer.Compare(Evaluated(), newer);
        var rohdaten = Root + @"\Projekte\2019\Alt\Rohdaten";
        Assert.Equal([rohdaten], result.FoldersRemoved);
        Assert.Equal(
            [new AccessChange(rohdaten, S.Erika, AccessLevel.Keine, AccessLevel.Aendern),
             new AccessChange(rohdaten, S.Klaus, AccessLevel.Lesen, AccessLevel.Keine),
             new AccessChange(rohdaten, S.Max, AccessLevel.Keine, AccessLevel.Aendern)],
            result.AccessChanges);
        Assert.Contains(result.ResolvedFindings, f => f.RuleId == "ZT06");
    }

    [Fact]
    public void Mitgliedschaften_und_ihre_Folgen()
    {
        var newer = Newer(s =>
        {
            s.Principals[S.QcTeam].Members.Add(S.Klaus);
            s.Principals[S.LaborAlle].Members.Remove(S.Max);
        });
        var result = ScanComparer.Compare(Evaluated(), newer);

        Assert.Equal(
            [new MembershipChange(S.LaborAlle, S.Max, ChangeKind.Entfernt), new MembershipChange(S.QcTeam, S.Klaus, ChangeKind.Hinzugefuegt)],
            result.MembershipChanges);
        Assert.Equal(
            [new AccessChange(Root, S.Klaus, AccessLevel.Keine, AccessLevel.Aendern),
             new AccessChange(Root, S.Max, AccessLevel.Aendern, AccessLevel.Keine),
             new AccessChange(Root + @"\Archiv", S.Klaus, AccessLevel.Lesen, AccessLevel.Aendern),
             new AccessChange(Root + @"\QC", S.Klaus, AccessLevel.Keine, AccessLevel.Aendern)],
            result.AccessChanges);
        Assert.Empty(result.AceChanges);
    }

    [Fact]
    public void Neues_Konto_nur_im_neueren_Lauf()
    {
        var neu = S.Domain + "-2004";
        var newer = Newer(s =>
        {
            s.Principals[neu] = new Principal { Sid = neu, Name = @"LAB\neu.kollege", Kind = PrincipalKind.Benutzer };
            s.Principals[S.LaborAlle].Members.Add(neu);
        });
        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Equal([new MembershipChange(S.LaborAlle, neu, ChangeKind.Hinzugefuegt)], result.MembershipChanges);
        // Vorher galt für das (unbekannte) Konto nur Jeder/Authentifizierte Benutzer
        Assert.Equal(
            [new AccessChange(Root, neu, AccessLevel.Keine, AccessLevel.Aendern),
             new AccessChange(Root + @"\Archiv", neu, AccessLevel.Lesen, AccessLevel.Aendern)],
            result.AccessChanges);
    }

    [Fact]
    public void Ohne_Gruppenaufloesung_keine_Mitgliedschaftsaenderungen()
    {
        var newer = Newer(s =>
        {
            s.Request = s.Request with { ResolveGroups = false };
            foreach (var p in s.Principals.Values) p.Members.Clear();
        });
        Assert.Empty(ScanComparer.Compare(Evaluated(), newer).MembershipChanges);
    }

    [Fact]
    public void Lauf_ohne_Gruppenaufloesung_erzeugt_keine_Schein_Aenderungen()
    {
        // Älterer Lauf ohne Gruppen: dort fehlen alle Mitgliedschaften. Ohne Ausgleich sähe es so aus, als hätten
        // Erika und Max ihr Gruppenrecht gerade erst bekommen.
        var older = Evaluated(s =>
        {
            s.Request = s.Request with { ResolveGroups = false };
            foreach (var p in s.Principals.Values) p.Members.Clear();
            s.Principals.Remove(S.Erika);
        });
        var unchanged = ScanComparer.Compare(older, Newer(_ => { }));
        Assert.Empty(unchanged.AccessChanges);
        Assert.Empty(unchanged.MembershipChanges);

        // Echte Änderungen an direkten Einträgen bleiben sichtbar
        var changed = ScanComparer.Compare(older, Newer(s => SetAces(s, @"\QC",
            Allow(S.System, S.Full), Allow(S.Administrators, S.Full), Allow(S.QcTeam, S.Modify), Allow(S.Max, S.Read))));
        Assert.Equal([new AccessChange(Root + @"\QC", S.Max, AccessLevel.Vollzugriff, AccessLevel.Lesen)], changed.AccessChanges);
    }

    [Fact]
    public void Besitzerrechte_folgen_der_Mitgliedschaft_in_der_Besitzergruppe()
    {
        // Einträge unverändert, nur Erika ist jetzt in der Gruppe, die Besitzer ist – BESITZERRECHTE gelten nun auch für sie
        const string owners = S.Domain + "-1200";
        static void Setup(ScanResult s)
        {
            s.Principals[owners] = new Principal { Sid = owners, Name = @"LAB\Projekt-Besitzer", Kind = PrincipalKind.Gruppe };
            var f = AddFolder(s, @"\Projekt", 1, true, Allow(S.Administrators, S.Full), Allow(SidCatalog.OwnerRights, S.Modify));
            f.OwnerSid = owners;
        }
        var older = Evaluated(Setup);
        var newer = Newer(s =>
        {
            Setup(s);
            s.Principals[owners].Members.Add(S.Erika);
        });
        var result = ScanComparer.Compare(older, newer);
        Assert.Equal([new AccessChange(Root + @"\Projekt", S.Erika, AccessLevel.Keine, AccessLevel.Aendern)], result.AccessChanges);
        Assert.Empty(result.AceChanges);
    }

    [Fact]
    public void Grosser_Lauf_mit_wenigen_Aenderungen_schnell()
    {
        // 20.000 gespeicherte Ordner, 2.000 Personen in 200 Gruppen; an jedem 50. Ordner wechselt die Gruppe.
        // Vorher rechnete der Vergleich jede Person auf jedem Ordner (gut 100 s), jetzt nur, was sich ändern kann.
        static ScanResult Big(bool changed)
        {
            var b = new ScanBuilder().WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));
            var users = Enumerable.Range(0, 2000).Select(i => Sid(100_000 + i)).ToList();
            foreach (var u in users) b.User(u, $@"T\u{u[^6..]}");
            for (int g = 0; g < 200; g++) b.Group(Sid(1000 + g), $@"T\G{g}", users.Where((_, i) => i % 200 == g).ToArray());
            b.Folder("", 0, Allow(S.Administrators, S.Full));
            for (int i = 0; i < 20_000; i++)
            {
                var g = Sid(1000 + (i * 7 + (changed && i % 50 == 0 ? 1 : 0)) % 200);
                b.Folder($@"\P{i / 100}\O{i}", 2, Allow(S.System, S.Full), Allow(S.Administrators, S.Full), Allow(g, S.Modify),
                    Allow(Sid(1000 + i * 3 % 200), S.Read), Allow(S.Administrators, S.Full, inherited: true));
            }
            return b.Scan;
        }

        var older = Big(changed: false);
        var newer = Big(changed: true);
        var watch = Stopwatch.StartNew();
        var result = ScanComparer.Compare(older, newer);
        watch.Stop();

        // 400 Ordner × (10 Personen verlieren „Ändern“ + 10 gewinnen es); die Gruppen sind disjunkt
        Assert.Equal(400 * 2, result.AceChanges.Count);
        Assert.Equal(400 * 20, result.AccessChanges.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Vergleich dauerte {watch.Elapsed}");
    }

    [Fact]
    public void Unbekannte_Mitglieder_werden_nicht_verglichen()
    {
        var newer = Newer(s =>
        {
            s.Principals[S.LaborAlle].Members.Clear();
            s.Principals[S.LaborAlle].MembersUnknown = true;
        });
        Assert.Empty(ScanComparer.Compare(Evaluated(), newer).MembershipChanges);
    }

    [Fact]
    public void Pfade_ohne_Gross_Klein()
    {
        var newer = Newer(s =>
        {
            var copies = s.Folders.Select(f =>
            {
                var c = new FolderNode
                {
                    Path = f.Path.ToUpperInvariant(), Depth = f.Depth, IsFile = f.IsFile, InheritanceBroken = f.InheritanceBroken,
                    ShareUnc = f.ShareUnc?.ToLowerInvariant(), OwnerSid = f.OwnerSid, Error = f.Error,
                };
                c.Aces.AddRange(f.Aces);
                return c;
            }).ToList();
            s.Folders.Clear();
            s.Folders.AddRange(copies);
        });
        Assert.False(ScanComparer.Compare(Evaluated(), newer).HasChanges);
    }

    [Fact]
    public void Gleicher_Schluessel_mehrfach_und_generische_Rechte_sind_keine_Aenderung()
    {
        var older = Evaluated(s => SetAces(s, @"\QC",
            Allow(S.System, S.Full), Allow(S.Administrators, S.Full), Allow(S.QcTeam, FileSystemRights.ReadAndExecute),
            Allow(S.QcTeam, FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.Synchronize), Allow(S.Max, S.Full)));
        var newer = Newer(s => SetAces(s, @"\QC",
            Allow(S.System, (FileSystemRights)0x10000000), Allow(S.Administrators, S.Full),
            Allow(S.QcTeam, FileSystemRights.ReadAndExecute | FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.Synchronize),
            Allow(S.Max, S.Full)));
        var result = ScanComparer.Compare(older, newer);
        Assert.Empty(result.AceChanges);
        Assert.Empty(result.AccessChanges);
    }

    [Fact]
    public void Vererbungsflags_gehoeren_zum_Schluessel()
    {
        var newer = Newer(s =>
        {
            var qc = Node(s, @"\QC");
            var max = qc.Aces.Single(a => a.Sid == S.Max);
            qc.Aces[qc.Aces.IndexOf(max)] = max with { Inheritance = InheritanceFlags.None };
        });
        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Equal([ChangeKind.Entfernt, ChangeKind.Hinzugefuegt], result.AceChanges.Select(c => c.Kind));
        Assert.Empty(result.AccessChanges); // am Ordner selbst bleibt es Vollzugriff
    }

    [Fact]
    public void Nicht_lesbare_Ordner_werden_nicht_verglichen()
    {
        var newer = Newer(s =>
        {
            // Personal ist jetzt lesbar, QC dafür nicht mehr
            s.Folders.Remove(Node(s, @"\Personal"));
            AddFolder(s, @"\Personal", 1, true, Allow(S.Administrators, S.Full), Allow(S.Erika, S.Modify));
            s.Folders.Remove(Node(s, @"\QC"));
            s.Folders.Add(new FolderNode { Path = Root + @"\QC", Depth = 1, ShareUnc = Root, Error = "Zugriff verweigert" });
            s.Folders.Add(new FolderNode { Path = Root + @"\Neu-Gesperrt", Depth = 1, ShareUnc = Root, Error = "Zugriff verweigert" });
        });
        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Empty(result.AceChanges);
        Assert.Empty(result.FoldersAdded);
        Assert.Empty(result.FoldersRemoved);
        Assert.DoesNotContain(result.AccessChanges, c => c.Path.EndsWith(@"\QC") || c.Path.EndsWith(@"\Personal") || c.Path.EndsWith("Gesperrt"));
        Assert.Contains(result.ResolvedFindings, f => f.RuleId == "ZT09" && f.Path.EndsWith(@"\Personal"));
        Assert.Contains(result.NewFindings, f => f.RuleId == "ZT09" && f.Path.EndsWith(@"\QC"));
    }

    [Fact]
    public void Unter_nicht_lesbarem_Ordner_wird_nichts_nachgebildet()
    {
        // Älterer Lauf kannte \Personal\Akten mit eigenen Rechten nicht, weil \Personal nicht lesbar war
        var newer = Newer(s => AddFolder(s, @"\Personal\Akten", 2, true, Allow(S.Erika, S.Modify)));
        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Equal([Root + @"\Personal\Akten"], result.FoldersAdded);
        Assert.Empty(result.AccessChanges);
    }

    [Fact]
    public void Freigabe_Aenderung_wirkt_auf_effektive_Rechte()
    {
        var newer = Newer(s =>
        {
            var share = s.Shares[0];
            s.Shares[0] = share with { Permissions = [new ShareAce(S.Everyone, ShareAccess.Lesen, false)] };
        });
        var result = ScanComparer.Compare(Evaluated(), newer);
        Assert.Contains(new AccessChange(Root + @"\QC", S.Max, AccessLevel.Vollzugriff, AccessLevel.Lesen), result.AccessChanges);
        Assert.Contains(new AccessChange(Root, S.Erika, AccessLevel.Aendern, AccessLevel.Lesen), result.AccessChanges);
        Assert.DoesNotContain(result.AccessChanges, c => c.Sid == S.Klaus && c.Path.EndsWith("Rohdaten")); // Lesen bleibt Lesen
        Assert.Contains(result.ResolvedFindings, f => f.RuleId == "ZT08");
    }

    [Fact]
    public void Grosser_Vergleich_im_Zeitbudget()
    {
        static ScanResult Big(int variant)
        {
            var b = new ScanBuilder().WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));
            var users = Enumerable.Range(0, 300).Select(i => Sid(10_000 + i)).ToList();
            foreach (var u in users) b.User(u, $@"T\u{u[^5..]}");
            for (int g = 0; g < 30; g++) b.Group(Sid(1000 + g), $@"T\G{g}", users.Where((_, i) => i % 30 == g).ToArray());
            b.Group(Sid(999), @"T\Alle", Enumerable.Range(0, 30).Select(g => Sid(1000 + g)).ToArray());
            b.Folder("", 0, Allow(S.Administrators, S.Full), Allow(Sid(999), S.Read));
            for (int i = 0; i < 5000; i++)
            {
                var g = Sid(1000 + (i + variant) % 30);
                b.Folder($@"\P{i / 100}\O{i}", 2, Allow(S.Administrators, S.Full), Allow(g, S.Modify), Allow(Sid(999), S.Read, inherited: true));
            }
            return b.Scan;
        }

        var older = Big(0);
        var newer = Big(1);
        var watch = Stopwatch.StartNew();
        var result = ScanComparer.Compare(older, newer);
        watch.Stop();

        // Jeder Ordner wechselt die Gruppe: 10 Personen verlieren „Ändern“, 10 gewinnen es
        Assert.Equal(5000 * 20, result.AccessChanges.Count);
        Assert.Equal(5000 * 2, result.AceChanges.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"Vergleich dauerte {watch.Elapsed}");
    }
}
