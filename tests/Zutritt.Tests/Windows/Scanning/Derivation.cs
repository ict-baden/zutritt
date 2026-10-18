using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Platform.Windows.Scanning;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

/// <summary>
/// Prüft die Zusage des Scanners an die Auswertung: Jeder nicht gespeicherte Ordner hat genau die Rechte, die er
/// vom nächsthöheren gespeicherten Ordner erbt – so leitet die Auswertung (EffectiveAccess) ihn ab: Einträge mit
/// „Container erben“, „nicht weitergeben“ nur eine Ebene weit, ERSTELLER-BESITZER entfällt.
/// Nur für Bäume ohne ERSTELLER-BESITZER (dessen Ersatz durch den Ersteller leitet die Auswertung bewusst nicht ab).
/// </summary>
internal static class Derivation
{
    private static readonly HashSet<string> CreatorPlaceholders = new(StringComparer.OrdinalIgnoreCase) { "S-1-3-0", "S-1-3-1", "S-1-3-2", "S-1-3-3" };

    public static void AssertDerivable(ScanResult result, string root)
    {
        var stored = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in result.Folders.Where(f => !f.IsFile)) stored.TryAdd(f.Path, f);
        int checkedFolders = 0;
        foreach (var dir in Walk(root))
        {
            if (stored.ContainsKey(dir)) continue;
            var (ancestor, distance) = NearestStored(stored, dir);
            Assert.True(ancestor is not null, $"Kein gespeicherter Vorfahr für „{dir}“");
            Assert.True(ancestor!.Error is null, $"„{dir}“ ist nicht gespeichert, aber sein Vorfahr „{ancestor.Path}“ hat einen Fehler");
            var expected = NodeSignature.Effective(Inherit(ancestor, distance));
            var actual = NodeSignature.Effective(AclReader.Read(dir, 0, isFile: false));
            Assert.True(expected == actual, $"„{dir}“ (nicht gespeichert) lässt sich nicht von „{ancestor.Path}“ ableiten:\nabgeleitet {expected}\ntatsächlich {actual}");
            checkedFolders++;
        }
        Assert.True(checkedFolders > 0, "Kein nicht gespeicherter Ordner – der Test prüft nichts");
    }

    private static FolderNode Inherit(FolderNode ancestor, int distance)
    {
        var node = new FolderNode { Path = ancestor.Path + "\\…", Depth = ancestor.Depth + distance };
        foreach (var ace in ancestor.Aces)
        {
            if (CreatorPlaceholders.Contains(ace.Sid)) continue;
            bool noPropagate = (ace.Propagation & AcePropagation.NoPropagateInherit) != 0;
            if ((ace.Inheritance & AceInheritance.ContainerInherit) == 0 || (noPropagate && distance > 1)) continue;
            node.Aces.Add(ace with { Rights = Rights.MapGeneric(ace.Rights), IsInherited = true, Propagation = AcePropagation.None });
        }
        return node;
    }

    private static (FolderNode? Node, int Distance) NearestStored(Dictionary<string, FolderNode> stored, string dir)
    {
        int distance = 0;
        for (var p = Path.GetDirectoryName(dir); p is not null; p = Path.GetDirectoryName(p))
        {
            distance++;
            if (stored.TryGetValue(p, out var node)) return (node, distance);
        }
        return (null, 0);
    }

    /// <summary>Alle Ordner unter <paramref name="root"/> (mit ihm), Verknüpfungen nicht verfolgt.</summary>
    private static IEnumerable<string> Walk(string root)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            yield return dir;
            foreach (var child in Directory.EnumerateDirectories(dir))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) stack.Push(child);
        }
    }
}
