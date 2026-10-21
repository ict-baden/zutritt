// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zutritt.Tests.Windows.App;

/// <summary>
/// THIRD-PARTY-NOTICES.txt nennt jede mitgelieferte Bibliothek mit ihrer Version (Richtlinien 02 und 06). Die Liste
/// kommt aus Zutritt.deps.json, also genau aus dem, was die App mitbringt: alle NuGet-Pakete von Zutritt.Core,
/// Zutritt.Platform.Windows und der App, ohne das, was die Windows Desktop Runtime schon enthält, und ohne Testpakete.
/// Bringt Dependabot ein neues Paket oder eine neue Version, schlägt der Test fehl, bis die Datei nachgezogen ist.
/// </summary>
public sealed partial class ThirdPartyNoticesTests
{
    [Fact]
    public void Jede_mitgelieferte_Bibliothek_steht_mit_Version_in_den_Hinweisen()
    {
        var notices = File.ReadAllText(FindNotices());
        var entries = Entries(notices);
        var packages = ShippedPackages();
        Assert.NotEmpty(packages);

        var missing = new List<string>();
        foreach (var (id, version) in packages)
        {
            var entry = entries.FirstOrDefault(e => MentionsId(e, id));
            if (entry is null) missing.Add($"{id} {version} fehlt");
            else if (!entry.Contains(version, StringComparison.Ordinal)) missing.Add($"{id}: Version {version} fehlt in seinem Eintrag");
        }
        Assert.True(missing.Count == 0, "THIRD-PARTY-NOTICES.txt ist nicht aktuell:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Nicht_ausgelieferte_Bestandteile_fehlen_in_der_deps_json()
    {
        // Windows-SDK-Projektion (Lizenz des Windows SDK) und PDFsharp-Zusatzbibliotheken mit den Segoe-WP-Schriften
        // (Directory.Build.targets) – die CI prüft zusätzlich die veröffentlichten Pakete
        var deps = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Zutritt.deps.json"));
        foreach (var file in new[] { "Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll", "PdfSharp.WPFonts.dll", "PdfSharp.Snippets.dll", "PdfSharp.Quality.dll" })
            Assert.DoesNotContain(file, deps, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PdfSharp.dll", deps, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pakete (Id, Version) aus Zutritt.deps.json – die Abhängigkeiten der App, wie sie ausgeliefert werden.</summary>
    internal static IReadOnlyList<(string Id, string Version)> ShippedPackages()
    {
        var depsFile = Path.Combine(AppContext.BaseDirectory, "Zutritt.deps.json");
        Assert.True(File.Exists(depsFile), "Zutritt.deps.json fehlt neben den Tests – ist die App referenziert?");
        using var doc = JsonDocument.Parse(File.ReadAllText(depsFile));
        return doc.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(lib => lib.Value.GetProperty("type").GetString() == "package")
            .Select(lib => (Id: lib.Name.Split('/')[0], Version: lib.Name.Split('/')[1]))
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Die Datei im Repo (vom Testordner aus nach oben bis zur Zutritt.sln).</summary>
    private static string FindNotices()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Zutritt.sln"))) continue;
            var path = Path.Combine(dir.FullName, "THIRD-PARTY-NOTICES.txt");
            Assert.True(File.Exists(path), $"THIRD-PARTY-NOTICES.txt fehlt im Repo ({dir.FullName}).");
            return path;
        }
        throw new InvalidOperationException("Zutritt.sln nicht gefunden – der Test läuft außerhalb des Repos.");
    }

    /// <summary>Nummerierte Einträge („1. Name Version – …“) bis zum ersten Lizenztext (Trennlinie aus Bindestrichen).</summary>
    private static List<string> Entries(string notices)
    {
        var head = notices.Split(["\n---"], StringSplitOptions.None)[0].Replace("\r\n", "\n");
        var starts = EntryStart().Matches(head).Select(m => m.Index).Append(head.Length).ToList();
        var list = new List<string>();
        for (int i = 0; i + 1 < starts.Count; i++) list.Add(head[starts[i]..starts[i + 1]]);
        return list;
    }

    /// <summary>Paket-Id als ganzes Wort (nicht „PDFsharp“ in „PDFsharp-MigraDoc“).</summary>
    private static bool MentionsId(string entry, string id)
        => Regex.IsMatch(entry, $@"(?<![\w.-]){Regex.Escape(id)}(?![\w-]|\.\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"^\s*\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex EntryStart();
}
