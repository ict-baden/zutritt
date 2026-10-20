// SPDX-License-Identifier: MIT
namespace Zutritt.Tests;

/// <summary>Richtlinie 06: Jede Quelldatei beginnt mit der SPDX-Zeile – auch neue.</summary>
public sealed class SourceHeaderTests
{
    [Fact]
    public void Jede_CSharp_Datei_beginnt_mit_der_SPDX_Zeile()
    {
        var root = RepositoryRoot();
        var missing = new[] { "src", "tests" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(Path.GetRelativePath(root, f)))
            .Where(f => File.ReadLines(f).FirstOrDefault()?.TrimStart('﻿') != "// SPDX-License-Identifier: MIT")
            .Select(f => Path.GetRelativePath(root, f))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, "Ohne „// SPDX-License-Identifier: MIT“ in der ersten Zeile:\n  " + string.Join("\n  ", missing));
    }

    private static bool IsBuildOutput(string relative) =>
        relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    /// <summary>Vom Testordner aus nach oben bis zur Zutritt.sln.</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Zutritt.sln"))) return dir.FullName;
        throw new InvalidOperationException("Zutritt.sln nicht gefunden – der Test läuft außerhalb des Repos.");
    }
}
