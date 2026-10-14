using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Analysis;

/// <summary>Die Sammelabfrage muss je Person genau dasselbe liefern wie die Einzelabfrage.</summary>
public sealed class ForPeopleTests
{
    [Fact]
    public void Liefert_dasselbe_wie_ForPerson()
    {
        var scan = SampleScan.Create();
        var people = scan.Principals.Values.Where(p => p.Kind == PrincipalKind.Benutzer).Select(p => p.Sid).ToList();

        var all = EffectiveAccess.ForPeople(scan, people);

        Assert.Equal(people.Count, all.Count);
        foreach (var sid in people)
            Assert.Equal(Flat(EffectiveAccess.ForPerson(scan, sid)), Flat(all[sid]));
    }

    [Fact]
    public void Doppelte_und_leere_SIDs_stoeren_nicht()
    {
        var scan = SampleScan.Create();

        var all = EffectiveAccess.ForPeople(scan, [SampleScan.Max, "", SampleScan.Max.ToLowerInvariant(), SampleScan.Erika]);

        Assert.Equal(2, all.Count);
        Assert.Equal(Flat(EffectiveAccess.ForPerson(scan, SampleScan.Erika)), Flat(all[SampleScan.Erika]));
    }

    [Fact]
    public void Abbruch_wird_beachtet()
    {
        var scan = SampleScan.Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => EffectiveAccess.ForPeople(scan, [SampleScan.Max], cts.Token));
    }

    // AccessGrant.Via ist ein Array – Records vergleichen das nach Referenz, deshalb als Text
    private static IEnumerable<string> Flat(IEnumerable<AccessGrant> grants) =>
        grants.Select(g => $"{g.Path}|{g.Sid}|{g.Ntfs}|{g.ViaShare}|{g.ReducedByDeny}|{string.Join(">", g.Via)}");
}
