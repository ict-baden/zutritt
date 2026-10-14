using Zutritt.Core.Analysis;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Tests.Integration;

/// <summary>
/// Echte Windows-Ordner, nur lesend und klein gehalten: Windows-eigene Konten, SIDs und Ordner dürfen keine
/// Fehlalarme erzeugen (gefunden im Härtetest auf C:\Program Files, C:\ProgramData, C:\Users, C:\Windows\System32).
/// Fehlt ein Ordner auf diesem Rechner, gibt es nichts zu prüfen.
/// </summary>
public sealed class SystemFolderFindingsTests
{
    private static async Task<ScanResult?> ScanAsync(string path, int? maxDepth)
    {
        if (!Directory.Exists(path)) return null;
        using var resolver = IdentityResolver.CreateDefault();
        return await new Scanner(resolver).RunAsync(new ScanRequest { Targets = [path], MaxDepth = maxDepth });
    }

    [Fact]
    public async Task Kompatibilitaets_Verknuepfungen_im_Standardprofil_sind_kein_Befund()
    {
        // „C:\Users\Default\Application Data“ usw.: „Jeder“ darf lesen, „Ordner auflisten“ ist verweigert
        var result = await ScanAsync(@"C:\Users\Default", maxDepth: 1);
        if (result is null) return;

        var links = result.Folders.Where(f => f.Aces.Any(a => a.IsDeny && a.Sid == SidCatalog.Everyone)).Select(f => f.Path).ToList();
        Assert.NotEmpty(links);
        Assert.DoesNotContain(result.Findings, f => links.Contains(f.Path) && f.RuleId is "ZT02" or "ZT07");
    }

    [Fact]
    public async Task Windows_Komponentenordner_ohne_Ordnungsbefunde_und_ohne_unbekannte_Konten()
    {
        // Besitzer TrustedInstaller, „Benutzer“ und „ALLE ANWENDUNGSPAKETE“ dürfen lesen, Vererbung tief unterbrochen
        var result = await ScanAsync(@"C:\Program Files\WindowsPowerShell", maxDepth: null);
        if (result is null) return;

        Assert.DoesNotContain(result.Findings, f => f.RuleId is "ZT02" or "ZT06" or "ZT04" or "ZT14");
        // App-Pakete, Fähigkeiten und Dienste sind bekannt – weder verwaist noch unbekannt
        Assert.DoesNotContain(result.Principals.Values, p => p.Kind is PrincipalKind.Verwaist or PrincipalKind.Unbekannt);
    }

    [Fact]
    public async Task System32_ohne_falsche_verwaiste_Dienste_und_Faehigkeitsgruppen()
    {
        // Oberste Ebene von System32: Dienst-SIDs nicht installierter Dienste (z. B. in SleepStudy) sind nicht „verwaist“
        var result = await ScanAsync(@"C:\Windows\System32", maxDepth: 1);
        if (result is null) return;

        Assert.DoesNotContain(result.Principals.Values, p => p.Kind == PrincipalKind.Verwaist
            && (p.Sid.StartsWith("S-1-5-80-", StringComparison.Ordinal) || p.Sid.StartsWith("S-1-15-", StringComparison.Ordinal)
                || SidCatalog.IsCapabilityGroup(p.Sid)));
        Assert.DoesNotContain(result.Principals.Values, p => p.Kind == PrincipalKind.Benutzer && SidCatalog.IsSpecialIdentity(p.Sid));
        Assert.DoesNotContain(result.Findings, f => f.RuleId == "ZT11" && f.Sid is { } s && SidCatalog.IsBuiltinGroup(s));
    }
}
