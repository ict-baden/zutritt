// SPDX-License-Identifier: MIT
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Core.Storage;
using Zutritt.Tests.Storage;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

/// <summary>
/// Vergleich von Läufen mit anderem Umfang (Befund „Automatischer Vergleich ignoriert Umfang und Optionen“): Was nur ein
/// Lauf erfassen konnte, darf weder als „entfallen“ noch als „behoben“ erscheinen – und der Unterschied wird genannt.
/// </summary>
public sealed class ScanScopeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset T1 = T0.AddDays(7);

    /// <summary>Beispiel-Lauf mit anderen Einstellungen; <paramref name="keep"/> bildet nach, was der Scanner damit erfasst hätte.</summary>
    private static ScanResult Run(DateTimeOffset started, Func<ScanRequest, ScanRequest> options, Func<FolderNode, bool>? keep = null,
        Action<ScanResult>? change = null)
    {
        var scan = S.Create(started);
        scan.Request = options(scan.Request);
        if (keep is not null) scan.Folders.RemoveAll(f => !keep(f));
        change?.Invoke(scan);
        scan.Findings.AddRange(FindingRules.Evaluate(scan));
        return scan;
    }

    private static void AssertNoChanges(ComparisonResult result)
    {
        Assert.Empty(result.FoldersRemoved);
        Assert.Empty(result.FoldersAdded);
        Assert.Empty(result.ResolvedFindings);
        Assert.Empty(result.NewFindings);
        Assert.Empty(result.AccessChanges);
        Assert.Empty(result.AceChanges);
    }

    [Fact]
    public void Gleiche_Einstellungen_ohne_Hinweis()
    {
        var result = ScanComparer.Compare(Run(T0, r => r), Run(T1, r => r));
        Assert.False(result.ScopeDiffers);
        Assert.Equal("", ScanScope.Describe(result));
    }

    [Fact]
    public void Flacher_Lauf_nach_vollem_meldet_nichts_als_behoben()
    {
        var full = Run(T0, r => r);
        var shallow = Run(T1, r => r with { MaxDepth = 1 }, f => f.Depth <= 1);
        Assert.Contains(full.Findings, f => f.Path.EndsWith("Rohdaten", StringComparison.Ordinal));   // nur der volle Lauf sieht Tiefe 4

        var result = ScanComparer.Compare(full, shallow);

        AssertNoChanges(result);
        Assert.Equal([ScopeDifference.Depth], result.ScopeDifferences);
        Assert.Contains("alle Ebenen", ScanScope.Describe(result));
        Assert.Contains("bis Ebene 1", ScanScope.Describe(result));
        Assert.Same(full, result.Older);     // die Originale bleiben im Ergebnis
        Assert.Same(shallow, result.Newer);
    }

    [Fact]
    public void Voller_Lauf_nach_flachem_meldet_nichts_als_neu()
    {
        var result = ScanComparer.Compare(Run(T0, r => r with { MaxDepth = 1 }, f => f.Depth <= 1), Run(T1, r => r));
        AssertNoChanges(result);
        Assert.True(result.ScopeDiffers);
    }

    [Fact]
    public void Echte_Aenderung_im_gemeinsamen_Umfang_bleibt_sichtbar()
    {
        var full = Run(T0, r => r);
        var shallow = Run(T1, r => r with { MaxDepth = 1 }, f => f.Depth <= 1, s =>
        {
            var qc = s.Folders.Single(f => f.Path.EndsWith(@"\QC", StringComparison.Ordinal));
            qc.Aces.Add(Allow(S.Everyone, S.Modify));
        });

        var result = ScanComparer.Compare(full, shallow);

        Assert.Contains(result.AceChanges, c => c.Path.EndsWith(@"\QC", StringComparison.Ordinal) && c.Sid == S.Everyone);
        Assert.Contains(result.NewFindings, f => f.RuleId == "ZT01");
        Assert.Empty(result.ResolvedFindings);
        Assert.Empty(result.FoldersRemoved);
    }

    [Fact]
    public void Dateien_nur_im_aelteren_Lauf()
    {
        static void WithFile(ScanResult s)
        {
            var file = new FolderNode { Path = @"\\FS01\Labor\QC\liste.xlsx", Depth = 2, IsFile = true, ShareUnc = @"\\FS01\Labor", OwnerSid = S.Administrators };
            file.Aces.Add(Allow(S.Everyone, S.Modify, inheritance: AceInheritance.None));
            s.Folders.Add(file);
        }
        var older = Run(T0, r => r with { IncludeFiles = true }, change: WithFile);
        var newer = Run(T1, r => r);
        Assert.Contains(older.Findings, f => f.Path.EndsWith("liste.xlsx", StringComparison.Ordinal));

        var result = ScanComparer.Compare(older, newer);

        AssertNoChanges(result);
        Assert.Equal([ScopeDifference.Files], result.ScopeDifferences);
        Assert.Contains("nur der ältere Lauf hat Dateien erfasst", ScanScope.Describe(result));
    }

    [Fact]
    public void Administrative_Freigaben_nur_in_einem_Lauf()
    {
        static void WithAdminShare(ScanResult s)
        {
            s.Shares.Add(new ShareInfo
            {
                Server = "FS01", Name = "C$", LocalPath = @"C:\", IsAdminShare = true,
                Permissions = [new ShareAce(S.Administrators, ShareAccess.Vollzugriff, false)],
            });
            foreach (var (path, depth) in new[] { (@"\\FS01\C$", 0), (@"\\FS01\C$\Temp", 1) })
            {
                var f = new FolderNode { Path = path, Depth = depth, ShareUnc = @"\\FS01\C$", OwnerSid = S.Administrators, InheritanceBroken = depth == 0 };
                f.Aces.Add(Allow(S.Administrators, S.Full));
                f.Aces.Add(Allow(S.Everyone, S.Modify));
                s.Folders.Add(f);
            }
        }
        var server = new ScanRequest { Targets = [@"\\FS01"] };
        var older = Run(T0, _ => server with { SkipAdminShares = false }, change: WithAdminShare);
        var newer = Run(T1, _ => server);
        Assert.Contains(older.Findings, f => f.Path.StartsWith(@"\\FS01\C$", StringComparison.Ordinal));

        var forward = ScanComparer.Compare(older, newer);
        var backward = ScanComparer.Compare(newer.Also(s => s.StartedAt = T0), older.Also(s => s.StartedAt = T1));

        AssertNoChanges(forward);
        AssertNoChanges(backward);
        Assert.Equal([ScopeDifference.AdminShares], forward.ScopeDifferences);
    }

    [Fact]
    public void Ausdruecklich_genannte_administrative_Freigabe_bleibt_im_Vergleich()
    {
        static ScanResult AdminShareRun(DateTimeOffset t, bool skip, FileRights everyone)
        {
            var b = new ScanBuilder();
            b.Scan.StartedAt = t;
            b.Scan.Request = new ScanRequest { Targets = [@"\\SRV\C$"], SkipAdminShares = skip };
            b.Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "C$", LocalPath = @"C:\", IsAdminShare = true,
                Permissions = [new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false)] });
            var root = new FolderNode { Path = @"\\SRV\C$", Depth = 0, ShareUnc = @"\\SRV\C$", OwnerSid = S.Administrators, InheritanceBroken = true };
            root.Aces.Add(Allow(S.Administrators, S.Full));
            root.Aces.Add(Allow(S.Everyone, everyone));
            b.Scan.Folders.Add(root);
            b.Scan.Findings.AddRange(FindingRules.Evaluate(b.Scan));
            return b.Scan;
        }

        var result = ScanComparer.Compare(AdminShareRun(T0, skip: false, S.Read), AdminShareRun(T1, skip: true, S.Modify));

        Assert.True(result.ScopeDiffers);
        Assert.Contains(result.AceChanges, c => c.Path == @"\\SRV\C$" && c.Sid == S.Everyone);
    }

    [Fact]
    public void Lokaler_Ordner_ueber_administrative_Freigabe_zeigt_keinen_geaenderten_Netzzugriff()
    {
        static ScanResult Local(DateTimeOffset t, bool viaAdminShare)
        {
            var b = new ScanBuilder().User(Sid(1), @"T\person").Group(Sid(2), @"T\Team", Sid(1));
            b.Scan.StartedAt = t;
            b.Scan.Request = new ScanRequest { Targets = [@"D:\Daten"], SkipAdminShares = !viaAdminShare };
            if (viaAdminShare)
                b.Scan.Shares.Add(new ShareInfo { Server = "PC", Name = "D$", LocalPath = @"D:\", IsAdminShare = true,
                    Permissions = [new ShareAce("S-1-5-32-544", ShareAccess.Vollzugriff, false)] });
            var root = b.LocalFolder(@"D:\Daten", 0, Allow("S-1-5-32-544", S.Full), Allow(Sid(2), S.Modify));
            root.ShareUnc = viaAdminShare ? @"\\PC\D$" : null;
            b.Scan.Findings.AddRange(FindingRules.Evaluate(b.Scan));
            return b.Scan;
        }

        var result = ScanComparer.Compare(Local(T0, viaAdminShare: true), Local(T1, viaAdminShare: false));

        Assert.Empty(result.AccessChanges);
        Assert.Equal([ScopeDifference.AdminShares], result.ScopeDifferences);
    }

    [Fact]
    public void Andere_Ebene_fuer_versteckte_Ausnahmen()
    {
        var older = Run(T0, r => r);                                            // ab Ebene 3: Rohdaten (Tiefe 4) ist ZT06
        var newer = Run(T1, r => r with { DeepInheritanceBreakDepth = 5 });   // ab Ebene 5: nicht mehr
        Assert.Contains(older.Findings, f => f.RuleId == "ZT06");
        Assert.DoesNotContain(newer.Findings, f => f.RuleId == "ZT06");

        var result = ScanComparer.Compare(older, newer);

        AssertNoChanges(result);
        Assert.Equal([ScopeDifference.DeepBreakLevel], result.ScopeDifferences);
    }

    [Fact]
    public void Nur_Gruppen_verschieden_wird_genannt_und_wie_bisher_ausgeglichen()
    {
        var withGroups = Run(T0, r => r);
        Assert.Contains(withGroups.Findings, f => f.RuleId == "ZT11");   // nur mit aufgelösten Gruppen zu finden

        var result = ScanComparer.Compare(withGroups, Run(T1, r => r with { ResolveGroups = false }));
        Assert.Equal([ScopeDifference.Groups], result.ScopeDifferences);
        Assert.Empty(result.MembershipChanges);
        AssertNoChanges(result);
        Assert.Contains("Gruppen", ScanScope.Describe(result));

        // Und umgekehrt: ZT11 erscheint nicht als neu, nur weil der neue Lauf wieder Gruppen auflöst
        var back = ScanComparer.Compare(Run(T0, r => r with { ResolveGroups = false }), Run(T1, r => r));
        AssertNoChanges(back);
    }

    [Fact]
    public void Gruppen_und_Dateien_verschieden_meldet_ZT11_nicht_als_behoben()
    {
        var result = ScanComparer.Compare(Run(T0, r => r with { IncludeFiles = true }), Run(T1, r => r with { ResolveGroups = false }));
        Assert.Equal([ScopeDifference.Files, ScopeDifference.Groups], result.ScopeDifferences);
        Assert.DoesNotContain(result.ResolvedFindings, f => f.RuleId == "ZT11");
        Assert.DoesNotContain(result.NewFindings, f => f.RuleId == "ZT11");
    }

    [Fact]
    public void Ziele_werden_wie_beim_automatischen_Vergleich_normalisiert()
    {
        Assert.True(ScanScope.SameTargets([@"\\fs01\labor\"], [@"\\FS01\Labor"]));
        Assert.True(ScanScope.SameTargets(["D:"], [@"d:\"]));
        Assert.True(ScanScope.SameTargets([@"//fs01/labor", @"\\FS01\Archiv"], [@"\\FS01\ARCHIV", @"\\FS01\Labor"]));
        Assert.False(ScanScope.SameTargets([@"\\FS01\Labor"], [@"\\FS01\Labor", @"\\FS01\Archiv"]));
    }

    [Fact]
    public void Speicher_bevorzugt_den_letzten_Lauf_mit_demselben_Umfang()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var full = Run(T0, r => r);
        var shallow = Run(T0.AddDays(1), r => r with { MaxDepth = 1 }, f => f.Depth <= 1);
        store.Save(full);
        store.Save(shallow);

        // Der nächste volle Lauf vergleicht mit dem letzten vollen, nicht mit dem flachen dazwischen
        Assert.Equal(full.Id, store.FindPrevious(Run(T1, r => r))!.Id);
        Assert.Equal(shallow.Id, store.FindPrevious(Run(T1, r => r with { MaxDepth = 1 }, f => f.Depth <= 1))!.Id);
        // Ohne passenden Umfang: wie bisher der letzte mit denselben Zielen
        Assert.Equal(shallow.Id, store.FindPrevious(Run(T1, r => r with { IncludeFiles = true }))!.Id);
    }

    [Fact]
    public void Berichte_nennen_den_abweichenden_Umfang()
    {
        var older = Run(T0, r => r);
        var newer = Run(T1, r => r with { MaxDepth = 1 }, f => f.Depth <= 1);
        var comparison = ScanComparer.Compare(older, newer);

        var data = ReportDataBuilder.Build(newer, comparison);

        Assert.StartsWith("Umfang weicht ab", data.Comparison!.ScopeNote);
        Assert.Equal(NoteKind.Warnung, data.Notes[0].Kind);
        Assert.Equal(data.Comparison.ScopeNote, data.Notes[0].Text);
        Assert.Equal(0, data.Comparison.ResolvedFindings);
    }
}

internal static class ScanResultTestExtensions
{
    /// <summary>Kleine Änderung am Lauf im Ausdruck, z. B. <c>scan.Also(s => s.StartedAt = t)</c>.</summary>
    public static ScanResult Also(this ScanResult scan, Action<ScanResult> change)
    {
        change(scan);
        return scan;
    }
}
