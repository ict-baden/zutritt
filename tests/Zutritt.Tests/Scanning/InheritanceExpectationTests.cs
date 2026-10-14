using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Tests.Scanning;

/// <summary>
/// Vererbungsregeln von Windows im Kleinen (SDDL statt echter Ordner): was ein Unterordner bzw. eine Datei erben
/// muss, und welche Unterschiede trotzdem normal sind.
/// </summary>
public sealed class InheritanceExpectationTests
{
    private const string U = "S-1-5-21-1-2-3-1001";
    private const string U2 = "S-1-5-21-1-2-3-1002";

    private static FolderNode Node(string sddl, bool isFile = false) =>
        AclReader.FromDescriptor(@"C:\X", 1, isFile, new RawSecurityDescriptor(sddl));

    private static bool SubfolderMatches(string parent, string child) =>
        InheritanceExpectation.ForSubfolders(Node(parent)).Matches(Node(child));

    [Fact]
    public void Generische_Rechte_werden_beim_Erben_aufgeteilt()
        => Assert.True(SubfolderMatches("O:BAD:(A;OICI;GA;;;LS)(A;OICI;FA;;;SY)",
            "O:BAD:AI(A;ID;FA;;;LS)(A;OICIIOID;GA;;;LS)(A;OICIID;FA;;;SY)"));

    [Theory]
    // „Eine Ebene“: beim Kind nur wirksam, nicht weiter vererbbar
    [InlineData("O:BAD:(A;CINP;0x1301bf;;;BO)", "O:BAD:AI(A;ID;0x1301bf;;;BO)", true)]
    [InlineData("O:BAD:(A;CINP;0x1301bf;;;BO)", "O:BAD:AI(A;CIID;0x1301bf;;;BO)", false)]
    // „Nur Dateien“: Der Unterordner gibt den Eintrag als „nur vererben“ an seine Dateien weiter
    [InlineData("O:BAD:(A;OI;0x1301bf;;;PU)", "O:BAD:AI(A;OIIOID;0x1301bf;;;PU)", true)]
    [InlineData("O:BAD:(A;OI;0x1301bf;;;PU)", "O:BAD:AI", false)]
    // „Nur dieser Ordner“ kommt beim Kind nicht an – hat das Kind ihn geerbt, stammt er von woanders
    [InlineData("O:BAD:(A;;0x1200a9;;;AU)(A;OICI;FA;;;SY)", "O:BAD:AI(A;OICIID;FA;;;SY)", true)]
    [InlineData("O:BAD:(A;;0x1200a9;;;AU)(A;OICI;FA;;;SY)", "O:BAD:AI(A;OICIID;FA;;;SY)(A;ID;0x1200a9;;;AU)", false)]
    // Verweigerungen zählen getrennt von Erlaubnissen
    [InlineData("O:BAD:(D;OICI;WD;;;NS)(A;OICI;FA;;;NS)", "O:BAD:AI(D;OICIID;WD;;;NS)(A;OICIID;FA;;;NS)", true)]
    [InlineData("O:BAD:(D;OICI;WD;;;NS)(A;OICI;FA;;;NS)", "O:BAD:AI(A;OICIID;FA;;;NS)", false)]
    // Andere Rechte als vererbt
    [InlineData("O:BAD:(A;OICI;0x1200a9;;;NS)", "O:BAD:AI(A;OICIID;0x1301bf;;;NS)", false)]
    // SYNCHRONIZE ergänzt Windows teils beim Vererben – zählt nicht
    [InlineData("O:BAD:(A;OICI;0x1301bf;;;NS)", "O:BAD:AI(A;OICIID;0x0301bf;;;NS)", true)]
    public void Vererbungsarten(string parent, string child, bool matches)
        => Assert.Equal(matches, SubfolderMatches(parent, child));

    [Fact]
    public void Explizite_Eintraege_des_Kindes_spielen_keine_Rolle()
        => Assert.True(SubfolderMatches("O:BAD:(A;OICI;FA;;;SY)", "O:BAD:AI(A;;FA;;;NS)(A;OICIID;FA;;;SY)"));

    [Theory]
    // Ersteller = Besitzer
    [InlineData($"O:{U}D:AI(A;ID;FA;;;{U})(A;OICIIOID;GA;;;CO)(A;OICIID;FA;;;SY)", true)]
    // Besitz später übernommen: der Eintrag des früheren Besitzers hat genau die Rechte von ERSTELLER-BESITZER
    [InlineData($"O:BAD:AI(A;ID;FA;;;{U})(A;OICIIOID;GA;;;CO)(A;OICIID;FA;;;SY)", true)]
    // … aber nur ein Konto
    [InlineData($"O:BAD:AI(A;ID;FA;;;{U})(A;ID;FA;;;{U2})(A;OICIIOID;GA;;;CO)(A;OICIID;FA;;;SY)", false)]
    // … und nur mit genau diesen Rechten (ein fremdes „Ändern“ ist kein Ersteller-Eintrag)
    [InlineData($"O:BAD:AI(A;ID;0x1301bf;;;{U})(A;OICIIOID;GA;;;CO)(A;OICIID;FA;;;SY)", false)]
    public void Ersteller_Besitzer(string child, bool matches)
        => Assert.Equal(matches, SubfolderMatches("O:BAD:(A;OICIIO;GA;;;CO)(A;OICI;FA;;;SY)", child));

    [Theory]
    [InlineData($"O:{U}D:AI(A;ID;FR;;;{U2})(A;OICIIOID;GR;;;CG)", true)]
    [InlineData($"O:{U}D:AI(A;ID;FR;;;{U2})(A;ID;FR;;;BU)(A;OICIIOID;GR;;;CG)", false)]
    public void Erstellergruppe_erklaert_hoechstens_ein_Konto(string child, bool matches)
        => Assert.Equal(matches, SubfolderMatches("O:BAD:(A;OICIIO;GR;;;CG)", child));

    [Theory]
    [InlineData($"O:{U}D:AI(A;ID;FA;;;SY)(A;ID;0x1301bf;;;PU)(A;ID;FA;;;{U})", true)]
    // Netzwerkdienst bekommt nur Unterordner („nur Container“) – bei einer Datei ist er fremd
    [InlineData($"O:{U}D:AI(A;ID;FA;;;SY)(A;ID;0x1301bf;;;PU)(A;ID;FA;;;{U})(A;ID;0x1200a9;;;NS)", false)]
    [InlineData($"O:{U}D:AI(A;ID;FA;;;SY)(A;ID;FA;;;{U})", false)]
    public void Dateien_erben_alle_Eintraege_mit_Objekte_erben(string file, bool matches)
    {
        var folder = Node($"O:BAD:(A;OICI;FA;;;SY)(A;CI;0x1200a9;;;NS)(A;OI;0x1301bf;;;PU)(A;OICIIO;GA;;;CO)");
        Assert.Equal(matches, InheritanceExpectation.ForFiles(folder).Matches(Node(file, isFile: true)));
    }

    [Fact]
    public void Ohne_Aussage_bei_unterbrochener_Vererbung_und_Fehlern()
    {
        var expectation = InheritanceExpectation.ForSubfolders(Node("O:BAD:(A;OICI;FA;;;SY)"));
        Assert.True(expectation.Matches(Node("O:BAD:P(A;OICI;FA;;;NS)")));
        Assert.True(expectation.Matches(new FolderNode { Path = @"C:\X", Error = "Zugriff verweigert" }));
    }
}
