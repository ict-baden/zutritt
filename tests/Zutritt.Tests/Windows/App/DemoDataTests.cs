using Zutritt.Core.Model;
using Zutritt.Demo;

namespace Zutritt.Tests.Windows.App;

/// <summary>
/// Demo-Daten nennen keine Personen (Regel des Inhabers, CHECKLISTE „Testdaten und Screenshots ohne echte
/// Personendaten“): nur Rollen- und Dienstkonten, keine „Vorname Nachname“ und keine „vorname.nachname“.
/// </summary>
public sealed class DemoDataTests
{
    // Häufige Vornamen (deutsch und international) – ein Treffer am Anfang eines Anzeige- oder Kontonamens ist verdächtig
    private static readonly HashSet<string> FirstNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Anna", "Ben", "Clara", "David", "Emma", "Felix", "Greta", "Hannah", "Jonas", "Julia", "Karl", "Lea", "Lena", "Leon", "Lukas",
        "Maria", "Marie", "Max", "Mia", "Moritz", "Noah", "Paul", "Sophie", "Tim", "Tom", "Laura", "Elias", "Emil", "Ida", "Jakob", "Johanna",
        "Lina", "Luis", "Mila", "Nele", "Niklas", "Ole", "Paula", "Peter", "Petra", "Ralf", "Sabine", "Sandra", "Stefan", "Susanne", "Thomas",
        "Ute", "Werner", "Yvonne", "Zoe", "Frank", "Heike", "Jürgen", "Katrin", "Michael", "Monika", "Andreas", "Birgit", "Christian",
        "Daniela", "Finn", "Erika", "Klaus", "Hans", "Uwe", "Jan", "Jo", "Till", "Tobias", "Markus", "Martin", "Sarah", "Lisa", "Anja",
        "John", "Jane", "Mike", "Alex", "Chris", "Sven", "Kai", "Jens", "Dirk", "Bernd", "Gerd", "Helga", "Inge", "Ingrid", "Ursula",
    };

    [Fact]
    public void Demo_Daten_nennen_keine_Personen()
    {
        var (older, newer) = DemoData.Create();
        AssertNoPersons(older);
        AssertNoPersons(newer);
    }

    [Fact]
    public void Grosser_Demo_Lauf_nennt_keine_Personen()
    {
        var (older, newer) = LargeDemo.Create(folders: 1_000, people: 60);
        AssertNoPersons(older);
        AssertNoPersons(newer);
        Assert.All(newer.Folders, f => Assert.DoesNotContain(FirstNames, n => f.Path.Contains(n + " ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Demo_behaelt_deaktivierte_und_verwaiste_Konten()
    {
        var (_, newer) = DemoData.Create();
        Assert.Contains(newer.Principals.Values, p => p.Kind == PrincipalKind.Benutzer && p.IsDisabled);
        Assert.Contains(newer.Principals.Values, p => p.Kind == PrincipalKind.Verwaist);
        Assert.Contains(newer.Findings, f => f.RuleId == "ZT12");
    }

    private static void AssertNoPersons(ScanResult scan)
    {
        foreach (var p in scan.Principals.Values.Where(p => p.Kind == PrincipalKind.Benutzer))
        {
            var display = p.DisplayName.Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries);
            var account = AccountName.LastPart(p.Name).Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
            Assert.False(display.Length > 0 && FirstNames.Contains(display[0]), $"Anzeigename sieht nach einer Person aus: „{p.DisplayName}“");
            Assert.False(account.Length > 0 && FirstNames.Contains(account[0]), $"Kontoname sieht nach einer Person aus: „{p.Name}“");
        }
        foreach (var f in scan.Findings)
            Assert.DoesNotContain(FirstNames, n => f.Message.Contains("\\" + n.ToLowerInvariant() + ".", StringComparison.Ordinal));
    }
}
