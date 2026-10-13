using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Identity;

public class FakeDirectoryTests
{
    [Fact]
    public void Lookup_liefert_Kopien()
    {
        var fake = new FakeDirectory().Add("S-1-5-21-1-2-3-1001", @"LAB\max", PrincipalKind.Benutzer);

        var a = fake.Lookup("s-1-5-21-1-2-3-1001")!;
        a.Name = "geändert";
        a.IsDisabled = true;
        var b = fake.Lookup("S-1-5-21-1-2-3-1001")!;

        Assert.Equal(@"LAB\max", b.Name);
        Assert.Equal("max", b.DisplayName);
        Assert.False(b.IsDisabled);
        Assert.Null(fake.Lookup("S-1-5-21-1-2-3-9999"));
    }

    [Fact]
    public void Mitglieder_leer_unbekannt_oder_nicht_lesbar()
    {
        var fake = new FakeDirectory()
            .Add("S-1-5-21-1-2-3-1100", @"LAB\Leer", PrincipalKind.Gruppe)
            .Add("S-1-5-21-1-2-3-1101", @"LAB\Team", PrincipalKind.Gruppe, "S-1-5-21-1-2-3-1001")
            .AddMember("S-1-5-21-1-2-3-1101", "S-1-5-21-1-2-3-1002")
            .AddMember("S-1-5-21-1-2-3-1101", "s-1-5-21-1-2-3-1002")
            .Add("S-1-5-21-1-2-3-1001", @"LAB\max", PrincipalKind.Benutzer)
            .Add("S-1-5-21-1-2-3-1102", @"LAB\Geheim", PrincipalKind.Gruppe, "S-1-5-21-1-2-3-1001")
            .MarkMembersUnreadable("S-1-5-21-1-2-3-1102");

        Assert.Empty(fake.GetMembers("S-1-5-21-1-2-3-1100")!);
        Assert.Equal(["S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1002"], fake.GetMembers("S-1-5-21-1-2-3-1101"));
        Assert.Null(fake.GetMembers("S-1-5-21-1-2-3-1102"));
        Assert.Null(fake.GetMembers("S-1-5-21-1-2-3-1001"));
        Assert.Null(fake.GetMembers("S-1-5-21-1-2-3-4711"));
    }

    [Fact]
    public void FromScan_uebernimmt_Konten_Mitglieder_und_unlesbare_Gruppen()
    {
        var scan = SampleScan.Create();
        scan.Principals[SampleScan.QcTeam].MembersUnknown = true;

        var fake = FakeDirectory.FromScan(scan);

        Assert.Equal("Klaus Alt", fake.Lookup(SampleScan.Klaus)!.DisplayName);
        Assert.True(fake.Lookup(SampleScan.Klaus)!.IsDisabled);
        Assert.Equal([SampleScan.QcTeam, SampleScan.Max], fake.GetMembers(SampleScan.LaborAlle));
        Assert.Null(fake.GetMembers(SampleScan.QcTeam));
        Assert.Empty(fake.GetMembers(SampleScan.LeereGruppe)!);
        Assert.Equal(PrincipalKind.Verwaist, fake.Lookup(SampleScan.Verwaist)!.Kind);
    }

    [Fact]
    public void Hinzufuegen_ersetzt_vorhandene_Eintraege()
    {
        var fake = new FakeDirectory().Add("S-1-5-21-1-2-3-1001", @"LAB\alt", PrincipalKind.Benutzer);
        fake.Add(new Principal { Sid = "S-1-5-21-1-2-3-1001", Name = @"LAB\neu", Kind = PrincipalKind.Benutzer, IsDisabled = true });

        var p = fake.Lookup("S-1-5-21-1-2-3-1001")!;
        Assert.Equal(@"LAB\neu", p.Name);
        Assert.True(p.IsDisabled);
    }
}
