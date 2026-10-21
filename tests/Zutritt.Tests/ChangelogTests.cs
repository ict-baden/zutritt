// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Zutritt.Tests;

/// <summary>
/// Richtlinie 04: Jede Version steht im CHANGELOG, und ihr Abschnitt endet mit „### English“ – dem Text des Releases
/// (.github/scripts/release-notes.ps1). Fehlte er, scheiterte erst der Release-Lauf nach dem Tag; so schon im Pull Request.
/// </summary>
public sealed partial class ChangelogTests
{
    [GeneratedRegex(@"^## \[(?<version>\d+\.\d+\.\d+)\]")]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"^### English\s*$")]
    private static partial Regex EnglishHeading();

    [GeneratedRegex(@"(?s)<!--.*?-->")]
    private static partial Regex HtmlComment();

    [Fact]
    public void Jede_Version_hat_einen_englischen_Release_Text()
    {
        var sections = Sections();
        Assert.NotEmpty(sections);
        foreach (var (version, lines) in sections)
        {
            int english = lines.FindIndex(l => EnglishHeading().IsMatch(l));
            Assert.True(english >= 0, $"CHANGELOG {version}: „### English“ fehlt");
            // Wie release-notes.ps1: bis zur nächsten „### “ – steht danach noch ein deutscher Abschnitt, fehlte er im Release
            var rest = lines.Skip(english + 1).ToList();
            Assert.False(rest.Any(l => l.StartsWith("### ", StringComparison.Ordinal)),
                $"CHANGELOG {version}: „### English“ muss der letzte Unterabschnitt sein");
            Assert.True(HtmlComment().Replace(string.Join("\n", rest), "").Trim().Length > 0,
                $"CHANGELOG {version}: „### English“ ist leer");
        }
    }

    [Fact]
    public void Version_der_App_liegt_nicht_hinter_dem_CHANGELOG()
    {
        // Nach dem Tag gleich weiterzählen (0.1.1 → 0.1.2), sonst hießen die CI-Builds älter als das Release
        var newest = Sections().Select(s => Version.Parse(s.Version)).Max()!;
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Zutritt", "Zutritt.csproj"));
        var text = project.Descendants("Version").First().Value.Trim();
        Assert.True(Version.TryParse(text.Split('-', '+')[0], out var app), $"Zutritt.csproj: Version „{text}“ nicht lesbar");
        Assert.True(app >= newest, $"Zutritt.csproj hat {text}, der CHANGELOG schon {newest}");
    }

    private static List<(string Version, List<string> Lines)> Sections()
    {
        var sections = new List<(string, List<string>)>();
        List<string>? current = null;
        foreach (var line in File.ReadLines(Path.Combine(RepositoryRoot(), "CHANGELOG.md")))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var match = VersionHeading().Match(line);
                current = match.Success ? [] : null; // „Unveröffentlicht“ zählt nicht
                if (current is not null) sections.Add((match.Groups["version"].Value, current));
            }
            else current?.Add(line);
        }
        return sections;
    }

    /// <summary>Vom Testordner aus nach oben bis zur Zutritt.sln.</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Zutritt.sln"))) return dir.FullName;
        throw new InvalidOperationException("Zutritt.sln nicht gefunden – der Test läuft außerhalb des Repos.");
    }
}
