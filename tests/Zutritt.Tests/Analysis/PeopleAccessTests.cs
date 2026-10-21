// SPDX-License-Identifier: MIT
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

/// <summary>
/// <see cref="PeopleAccess"/> rechnet je Ordner-Klasse nur einmal und nur für Personen, die ein Erlauben-Eintrag trifft.
/// Geprüft wird gegen die schlichte Rechnung <see cref="EffectiveAccess.ForPerson(ScanResult, string)"/> je Person –
/// mit zufälligen Läufen, in denen viele Ordner dieselben Einträge mit anderem Besitzer tragen.
/// </summary>
public sealed class PeopleAccessTests
{
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Gleich_der_Rechnung_je_Person(int seed)
    {
        var random = new Random(seed);
        var scan = ScanComparerEquivalenceTests.RandomScan(random);
        AddCopies(scan, random);
        AssertSameAsForPerson(scan);
    }

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (int i = 1; i <= 120; i++) data.Add(i);
        return data;
    }

    [Fact]
    public void Beispiellauf_gleich_der_Rechnung_je_Person() => AssertSameAsForPerson(S.Create());

    [Fact]
    public void Besitzerrechte_haengen_am_Besitzer_auch_bei_gleichen_Eintraegen()
    {
        // Gleiche Einträge, anderer Besitzer: Mit BESITZERRECHTE dürfen die Ordner nicht zusammenfallen
        var b = new ScanBuilder();
        b.User(Sid(2001), @"T\anna").User(Sid(2002), @"T\ben");
        foreach (var (path, owner) in new[] { (Share, S.Administrators), (Share + @"\A", Sid(2001)), (Share + @"\B", Sid(2002)) })
        {
            var node = new FolderNode { Path = path, Depth = path == Share ? 0 : 1, ShareUnc = Share, OwnerSid = owner, InheritanceBroken = true };
            node.Aces.Add(new AceEntry { Sid = SidCatalog.OwnerRights, Rights = S.Modify, Inheritance = CIOI });
            b.Scan.Folders.Add(node);
        }
        b.WithShare(new ShareAce(S.Everyone, ShareAccess.Vollzugriff, false));

        var access = PeopleAccess.Compute(new AccessContext(b.Scan), [Sid(2001), Sid(2002)]);
        Assert.Equal([Share + @"\A"], access.Grants(Sid(2001)).Select(g => g.Path));
        Assert.Equal([Share + @"\B"], access.Grants(Sid(2002)).Select(g => g.Path));
        AssertSameAsForPerson(b.Scan);
    }

    [Fact]
    public void Unbekannte_Person_und_leere_Liste()
    {
        var access = PeopleAccess.Compute(new AccessContext(S.Create()), []);
        var summary = access.Summary(S.Max);
        Assert.Equal(0, summary.Folders);
        Assert.Equal(AccessLevel.Keine, summary.Highest);
        Assert.Empty(summary.Via);
        Assert.False(summary.HasDirect);
        Assert.Empty(access.Grants(S.Max));
    }

    /// <summary>Zusätzliche Ordner mit den Einträgen vorhandener, aber anderem Pfad und Besitzer – teils in tieferen Ebenen.</summary>
    private static void AddCopies(ScanResult scan, Random random)
    {
        var originals = scan.Folders.Where(f => f.Error is null).ToList();
        string[] owners = [Sid(2001), Sid(2002), Sid(1001), S.Administrators];
        for (int i = 0; i < 12 && originals.Count > 0; i++)
        {
            var source = originals[random.Next(originals.Count)];
            var copy = new FolderNode
            {
                Path = $@"{source.Path}\Kopie{i}",
                Depth = source.Depth + 1,
                ShareUnc = source.ShareUnc,
                OwnerSid = random.Next(2) == 0 ? source.OwnerSid : owners[random.Next(owners.Length)],
                InheritanceBroken = source.InheritanceBroken,
            };
            copy.Aces.AddRange(source.Aces);
            scan.Folders.Add(copy);
        }
    }

    private static void AssertSameAsForPerson(ScanResult scan)
    {
        // Alle Benutzer, dazu Gruppen und eine unbekannte SID – PeopleAccess darf sich auf keine Kontoart verlassen
        var sids = scan.Principals.Values.Select(p => p.Sid).Append("S-1-5-21-9-9-9-9").ToList();
        var access = PeopleAccess.Compute(new AccessContext(scan), sids);

        foreach (var sid in sids)
        {
            var expected = EffectiveAccess.ForPerson(scan, sid);
            var actual = access.Grants(sid);
            Assert.Equal(Describe(expected), Describe(actual));

            var summary = access.Summary(sid);
            Assert.Equal(expected.Count, summary.Folders);
            Assert.Equal(expected.Count == 0 ? AccessLevel.Keine : expected.Max(g => g.Effective), summary.Highest);
            Assert.Equal(expected.Any(g => g.Via.Count == 0), summary.HasDirect);
            Assert.Equal(expected.SelectMany(g => g.Via).Distinct().Order(StringComparer.Ordinal), summary.Via.Order(StringComparer.Ordinal));
        }
    }

    /// <summary>Alle Felder eines Zugriffs, nach Pfad sortiert (die Reihenfolge der Einzelzugriffe ist Sache des Aufrufers).</summary>
    private static List<string> Describe(IEnumerable<AccessGrant> grants) =>
        grants.Select(g => $"{g.Path}|{g.Sid}|{g.Ntfs}|{g.ViaShare}|{g.Effective}|{g.ReducedByDeny}|{string.Join(">", g.Via)}")
            .Order(StringComparer.Ordinal).ToList();
}
