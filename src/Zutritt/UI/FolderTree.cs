using System.ComponentModel;
using Zutritt.Core.Model;

namespace Zutritt.UI;

/// <summary>
/// Baum der gespeicherten Ordner, aus den Pfaden aufgebaut. Gespeichert sind nur Ordner mit eigenen Rechten;
/// die Ebenen dazwischen erscheinen grau. Knoten entstehen erst, wenn ihr Elternknoten aufgeklappt wird –
/// so bleiben auch Läufe mit 50.000 Ordnern flüssig.
/// </summary>
public sealed class FolderTree
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, FolderNode> _stored = new(Paths);
    private readonly Dictionary<string, string> _parent = new(Paths);
    private readonly Dictionary<string, List<string>> _children = new(Paths);
    private readonly Dictionary<string, Severity> _worst = new(Paths);
    private readonly List<FolderTreeNode> _roots = [];

    private FolderTree() { }

    public IReadOnlyList<FolderTreeNode> Roots => _roots;

    /// <summary>Alle gespeicherten Ordner (für die Suche).</summary>
    public IEnumerable<FolderNode> Stored => _stored.Values;

    /// <summary>Baut die Pfad-Indizes auf; darf im Hintergrund laufen.</summary>
    public static FolderTree Build(ScanResult scan)
    {
        var tree = new FolderTree();
        foreach (var f in scan.Folders) tree._stored[Key(f.Path)] = f;
        foreach (var finding in scan.Findings)
        {
            var key = Key(finding.Path);
            if (!tree._worst.TryGetValue(key, out var s) || finding.Severity > s) tree._worst[key] = finding.Severity;
        }

        var roots = scan.Folders.Where(f => f.IsRoot).Select(f => Key(f.Path)).Distinct(Paths).ToList();
        var longestFirst = roots.OrderByDescending(r => r.Length).ToList();
        var top = new HashSet<string>(roots, Paths);

        foreach (var folder in scan.Folders)
        {
            if (folder.IsRoot) continue;
            var key = Key(folder.Path);
            var root = longestFirst.FirstOrDefault(r => key.StartsWith(r.EndsWith('\\') ? r : r + "\\", StringComparison.OrdinalIgnoreCase));
            if (root is null)
            {
                // Ohne passende Wurzel (sollte nicht vorkommen): als eigener Eintrag oben zeigen
                top.Add(key);
                continue;
            }
            // Kette bis zur Wurzel eintragen; endet, sobald ein Teil schon bekannt ist (keine Rekursion)
            var current = key;
            while (!Paths.Equals(current, root) && !tree._parent.ContainsKey(current))
            {
                int cut = current.LastIndexOf('\\');
                var parent = cut > root.Length ? current[..cut] : root;
                tree._parent[current] = parent;
                if (!tree._children.TryGetValue(parent, out var list)) tree._children[parent] = list = [];
                list.Add(current);
                current = parent;
            }
        }

        foreach (var key in top.Order(StringComparer.CurrentCultureIgnoreCase))
            tree._roots.Add(tree.CreateNode(key, null, displayName: tree._stored.TryGetValue(key, out var f) ? f.Path : key));
        return tree;
    }

    internal static string Key(string path) => path.Length > 3 ? path.TrimEnd('\\') : path;

    internal List<FolderTreeNode> CreateChildren(FolderTreeNode parent)
    {
        if (!_children.TryGetValue(parent.Key, out var keys)) return [];
        return keys
            .Select(k => CreateNode(k, parent, displayName: k[(k.LastIndexOf('\\') + 1)..]))
            .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private FolderTreeNode CreateNode(string key, FolderTreeNode? parent, string displayName)
    {
        _stored.TryGetValue(key, out var folder);
        string tone = _worst.TryGetValue(key, out var s) ? Labels.Of(s) : "";
        return new FolderTreeNode(this, key, folder?.Path ?? key, displayName, parent, folder, tone, _children.ContainsKey(key));
    }

    /// <summary>Knoten zu einem Pfad suchen (und die Kette dorthin anlegen), null wenn nicht im Baum.</summary>
    public FolderTreeNode? Find(string path)
    {
        var chain = new Stack<string>();
        var current = Key(path);
        chain.Push(current);
        while (_parent.TryGetValue(current, out var parent))
        {
            current = parent;
            chain.Push(current);
        }

        // Einmal vom Stapel nehmen – im Prädikat würde für jede geprüfte Wurzel ein weiterer Teil verbraucht
        var top = chain.Pop();
        var node = _roots.FirstOrDefault(r => Paths.Equals(r.Key, top));
        while (node is not null && chain.Count > 0)
        {
            var next = chain.Pop();
            node = node.Children.FirstOrDefault(c => Paths.Equals(c.Key, next));
        }
        return node;
    }

    /// <summary>Wie <see cref="Find"/>, aber auch für Freigaben (UNC eines lokal gescannten Wurzelordners) und
    /// für Pfade ohne eigenen Knoten: dann der nächste gespeicherte Ordner darüber.</summary>
    public FolderTreeNode? FindNearest(string path)
    {
        if (Find(path) is { } exact) return exact;
        if (_stored.Values.FirstOrDefault(f => f.IsRoot && f.ShareUnc is { } unc && Paths.Equals(Key(unc), Key(path))) is { } shareRoot)
            return Find(shareRoot.Path);
        var current = Key(path);
        for (int cut = current.LastIndexOf('\\'); cut > 2; cut = current.LastIndexOf('\\'))
        {
            current = current[..cut];
            if (Find(current) is { } ancestor) return ancestor;
        }
        return null;
    }
}

/// <summary>Knoten im Ordnerbaum: gespeicherter Ordner oder graue Zwischenebene ohne eigene Rechte.</summary>
public sealed class FolderTreeNode : INotifyPropertyChanged
{
    private readonly FolderTree _tree;
    private List<FolderTreeNode>? _children;
    private bool _expanded;
    private bool _selected;

    internal FolderTreeNode(FolderTree tree, string key, string path, string name, FolderTreeNode? parent, FolderNode? folder,
        string findingTone, bool hasChildren)
    {
        _tree = tree;
        Key = key;
        Path = path;
        Name = name;
        Parent = parent;
        Folder = folder;
        FindingTone = findingTone;
        HasChildren = hasChildren;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal string Key { get; }
    public string Path { get; }
    public string Name { get; }
    public FolderTreeNode? Parent { get; }
    /// <summary>Gespeicherter Ordner, null bei Zwischenebenen.</summary>
    public FolderNode? Folder { get; }
    public bool IsIntermediate => Folder is null;
    public bool IsTop => Parent is null;
    public bool InheritanceBroken => Folder?.InheritanceBroken == true;
    /// <summary>Geerbte Einträge passen nicht zum Elternordner – meist ein verschobener Ordner (Regel ZT14).</summary>
    public bool InheritanceDiffers => Folder is { InheritanceDiffers: true, InheritanceBroken: false };
    public bool HasError => Folder?.Error is not null;
    public bool IsFile => Folder?.IsFile == true;
    /// <summary>Schwerster eigener Befund ("Hoch" … "Info") oder leer.</summary>
    public string FindingTone { get; }
    public bool HasChildren { get; }

    /// <summary>Kinder, erst beim ersten Zugriff angelegt (der Baum fragt sie beim Aufklappen ab).</summary>
    public IReadOnlyList<FolderTreeNode> Children => _children ??= _tree.CreateChildren(this);

    public bool IsExpanded
    {
        get => _expanded;
        set { if (_expanded != value) { _expanded = value; Changed(nameof(IsExpanded)); } }
    }

    public bool IsSelected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Changed(nameof(IsSelected)); } }
    }

    /// <summary>Vom obersten Knoten bis zu diesem (einschließlich).</summary>
    public List<FolderTreeNode> Chain()
    {
        var chain = new List<FolderTreeNode>();
        for (var n = this; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        return chain;
    }

    /// <summary>Nächster gespeicherter Ordner darüber (für Zwischenebenen: von wem geerbt wird).</summary>
    public FolderTreeNode? NearestStoredAncestor()
    {
        for (var n = Parent; n is not null; n = n.Parent)
            if (n.Folder is not null) return n;
        return null;
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
