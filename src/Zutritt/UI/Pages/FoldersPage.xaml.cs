using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;

namespace Zutritt.UI.Pages;

/// <summary>Ordner: Baum der gespeicherten Ordner, rechts Einträge, „Wer darf hier rein“ und Befunde.</summary>
public partial class FoldersPage : UserControl, IPage, ISearchPage
{
    /// <param name="AccountName">Kurzform für die enge Spalte (Anzeigename oder Name ohne Domäne), <paramref name="Account"/> voll im Tooltip.</param>
    public sealed record AceRow(string Account, string AccountName, string Kind, string Right, AccessLevel Level, bool IsDeny, string Origin, string AppliesTo)
    {
        public override string ToString() => $"{Account}, {Right}, {Origin}, {AppliesTo}";
    }
    /// <param name="Via">Gruppenkette kurz (ohne Domäne), <paramref name="ViaFull"/> vollständig für den Tooltip.</param>
    public sealed record PersonRow(string Person, string Account, string ViaShare, AccessLevel EffectiveLevel, string Ntfs, AccessLevel NtfsLevel,
        string Via, string ViaFull, string Note)
    {
        public override string ToString() => $"{Person}, Freigabe {ViaShare}, NTFS {Ntfs}, {Via}";
    }
    public sealed record FindingRow(Severity Severity, string RuleId, string Message)
    {
        public override string ToString() => $"{Labels.Of(Severity)}, {RuleId}, {Message}";
    }
    public sealed record SearchRow(string Name, string Path)
    {
        public override string ToString() => Path;
    }

    /// <summary>Für den Screenshot gezeigter Ordner des Demo-Laufs.</summary>
    internal const string ScreenshotFolder = @"\\FS01\Labor\QC";

    private readonly AppState _state;
    private readonly Debouncer _search = new(TimeSpan.FromMilliseconds(250));
    private ScanResult? _shown;
    private bool _loaded;
    private FolderTree? _tree;
    private Task _treeTask = Task.CompletedTask;
    private Task _peopleTask = Task.CompletedTask;
    private int _peopleVersion;
    private FolderTreeNode? _current;

    /// <summary>Berechnung von „Wer darf hier rein“ für den gewählten Ordner (für die Messung).</summary>
    internal Task PeopleTask => _peopleTask;

    public FoldersPage(AppState state)
    {
        InitializeComponent();
        _state = state;
        Tree.ItemContainerStyle = BuildContainerStyle();
    }

    /// <summary>
    /// Baum-Einträge mit Bindung an Auf-/Zugeklappt und Auswahl des Modells, Kinder ebenfalls virtualisiert.
    /// Im Code, weil der Stil auf dem Fluent-Stil aufbauen muss, der erst zur Laufzeit geladen wird.
    /// </summary>
    private static Style BuildContainerStyle()
    {
        var style = new Style(typeof(TreeViewItem), Application.Current.TryFindResource(typeof(TreeViewItem)) as Style);
        style.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(FolderTreeNode.IsExpanded)) { Mode = BindingMode.TwoWay }));
        style.Setters.Add(new Setter(TreeViewItem.IsSelectedProperty, new Binding(nameof(FolderTreeNode.IsSelected)) { Mode = BindingMode.TwoWay }));
        style.Setters.Add(new Setter(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)))));
        if (Application.Current.TryFindResource("TreeItemTemplate") is ControlTemplate template)
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    public void OnShown()
    {
        if (!_loaded || !ReferenceEquals(_shown, _state.Scan)) Load();
    }

    public async Task PrepareScreenshotAsync()
    {
        OnShown();
        await _treeTask;
        SearchBox.Text = "";
        // Fremde Läufe (z. B. die große Messung) haben den Demo-Ordner nicht – dann der erste gespeicherte Unterordner
        var path = _tree is null || _tree.FindNearest(ScreenshotFolder) is not null ? ScreenshotFolder
            : _tree.Roots.FirstOrDefault()?.Children.FirstOrDefault()?.Path ?? ScreenshotFolder;
        await SelectPathAsync(path);
        Tabs.SelectedItem = TabPeople;
        await _peopleTask;
        await Ui.IdleAsync();
        if (_current is not null) await RevealAsync(_current);
        await Ui.IdleAsync();
    }

    /// <summary>
    /// Für das zusätzliche Bild „ordner-verschoben“: den ersten Ordner mit abweichender Vererbung (ZT14) mit seinen – allesamt
    /// geerbten – Einträgen zeigen.
    /// false, wenn der Lauf keinen hat.
    /// </summary>
    internal async Task<bool> PrepareMovedScreenshotAsync()
    {
        OnShown();
        await _treeTask;
        if (_shown?.Folders.FirstOrDefault(f => f is { InheritanceDiffers: true, InheritanceBroken: false, Error: null }) is not { } moved)
            return false;
        SearchBox.Text = "";
        await SelectPathAsync(moved.Path);
        Tabs.SelectedItem = TabAces;
        await Ui.IdleAsync();
        if (_current is not null) await RevealAsync(_current);
        await Ui.IdleAsync();
        return true;
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ================= Laden =================

    private void Load()
    {
        _loaded = true;
        _shown = _state.Scan;
        _tree = null;
        _current = null;
        Tree.ItemsSource = null;
        SearchResults.ItemsSource = null;
        SearchResults.Visibility = Visibility.Collapsed;
        Tree.Visibility = Visibility.Visible;
        ShowDetail(null);

        if (_shown is not { } scan)
        {
            ShowTreeMessage("Kein Lauf geladen. Starten Sie auf der Übersicht einen Scan oder öffnen Sie einen gespeicherten Lauf.");
            _treeTask = Task.CompletedTask;
            return;
        }
        ShowTreeMessage("Ordner werden geladen …");
        var task = BuildTreeAsync(scan);
        _treeTask = task;
        Ui.Run(_state, () => task, "Der Ordnerbaum ließ sich nicht aufbauen");
    }

    private async Task BuildTreeAsync(ScanResult scan)
    {
        var tree = await Task.Run(() => FolderTree.Build(scan));
        if (!ReferenceEquals(scan, _shown)) return;
        _tree = tree;
        Tree.ItemsSource = tree.Roots;
        // Wenige Freigaben: gleich aufgeklappt, damit die erste Ebene zu sehen ist
        if (tree.Roots.Count <= 6)
            foreach (var root in tree.Roots) root.IsExpanded = true;
        if (tree.Roots.Count == 0) ShowTreeMessage("In diesem Lauf sind keine Ordner gespeichert.");
        else TreeEmpty.Visibility = Visibility.Collapsed;
        if (SearchBox.Text.Trim().Length > 0) ApplySearch();
    }

    private void ShowTreeMessage(string text)
    {
        TreeEmpty.Text = text;
        TreeEmpty.Visibility = Visibility.Visible;
    }

    // ================= Auswahl =================

    /// <summary>Ordner anzeigen (z. B. aus den Befunden): Baum bis dorthin aufklappen und auswählen.</summary>
    public void SelectPath(string path) => Ui.Run(_state, () => SelectPathAsync(path), "Der Ordner ließ sich nicht anzeigen");

    internal async Task SelectPathAsync(string path)
    {
        OnShown();
        await _treeTask;
        if (_tree is null) return;
        ClearSearch();
        var node = _tree.FindNearest(path);
        if (node is null)
        {
            _state.Report(NoticeKind.Info, $"„{path}“ ist in diesem Lauf nicht als Ordner gespeichert.");
            return;
        }
        Select(node);
        await RevealAsync(node);
    }

    private void Select(FolderTreeNode node)
    {
        foreach (var ancestor in node.Chain().SkipLast(1)) ancestor.IsExpanded = true;
        if (_current is not null && !ReferenceEquals(_current, node)) _current.IsSelected = false;
        node.IsSelected = true;
        ShowDetail(node);
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FolderTreeNode node || ReferenceEquals(node, _current)) return;
        // Der alte Eintrag kann wegvirtualisiert sein – dann setzt der Baum seine Auswahl nicht selbst zurück
        if (_current is not null) _current.IsSelected = false;
        ShowDetail(node);
    }

    /// <summary>
    /// Bringt einen Knoten im virtualisierten Baum in Sicht. Container gibt es nur für sichtbare Einträge,
    /// deshalb Ebene für Ebene erzeugen lassen (Vorgehen aus der WPF-Dokumentation „TreeViewItem finden“).
    /// </summary>
    private async Task RevealAsync(FolderTreeNode node)
    {
        ItemsControl parent = Tree;
        foreach (var step in node.Chain())
        {
            var container = await ContainerForAsync(parent, step);
            if (container is null) return;
            if (ReferenceEquals(step, node))
            {
                container.BringIntoView();
                return;
            }
            container.IsExpanded = true;
            parent = container;
        }
    }

    private static async Task<TreeViewItem?> ContainerForAsync(ItemsControl parent, object item)
    {
        parent.ApplyTemplate();
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem ready) return ready;

        int index = parent.Items.IndexOf(item);
        if (index < 0) return null;
        var presenter = Ui.FindChild<ItemsPresenter>(parent);
        if (presenter is null) return null;
        presenter.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(presenter) > 0 && VisualTreeHelper.GetChild(presenter, 0) is VirtualizingStackPanel panel)
            panel.BringIndexIntoViewPublic(index);
        await Ui.IdleAsync();
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
    }

    private void Parent_Click(object sender, RoutedEventArgs e)
    {
        if (_current?.NearestStoredAncestor() is not { } ancestor) return;
        Select(ancestor);
        Ui.Run(_state, () => RevealAsync(ancestor), "Der Ordner ließ sich nicht anzeigen");
    }

    // ================= Suche =================

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _search.Run(ApplySearch);

    private void ApplySearch()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0 || _tree is null)
        {
            bool wasSearching = SearchResults.Visibility == Visibility.Visible;
            SearchResults.ItemsSource = null;
            SearchResults.Visibility = Visibility.Collapsed;
            Tree.Visibility = Visibility.Visible;
            if (_tree is not null && _tree.Roots.Count > 0) TreeEmpty.Visibility = Visibility.Collapsed;
            if (wasSearching && _current is { } current) Ui.Run(_state, () => RevealAsync(current), "Der Ordner ließ sich nicht anzeigen");
            return;
        }

        var matches = _tree.Stored
            .Where(f => f.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new SearchRow(LastSegment(f.Path), f.Path))
            .ToList();
        SearchResults.ItemsSource = matches;
        SearchResults.Visibility = Visibility.Visible;
        Tree.Visibility = Visibility.Collapsed;
        if (matches.Count == 0) ShowTreeMessage($"Kein gespeicherter Ordner passt zu „{query}“.");
        else TreeEmpty.Visibility = Visibility.Collapsed;
    }

    private void ClearSearch()
    {
        if (SearchBox.Text.Length == 0) return;
        SearchBox.Text = "";
        ApplySearch();
    }

    private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is SearchRow row && _tree?.Find(row.Path) is { } node) Select(node);
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd(WindowsPath.Separator);
        int cut = trimmed.LastIndexOf(WindowsPath.Separator);
        return cut >= 0 && cut < trimmed.Length - 1 ? trimmed[(cut + 1)..] : trimmed;
    }

    // ================= Detail =================

    private void ShowDetail(FolderTreeNode? node)
    {
        _current = node;
        _peopleVersion++;
        if (node is null)
        {
            DetailRoot.Visibility = Visibility.Collapsed;
            DetailEmpty.Visibility = Visibility.Visible;
            DetailEmpty.Text = _shown is null ? "Kein Lauf geladen." : "Ordner links auswählen, um seine Berechtigungen zu sehen.";
            return;
        }
        DetailRoot.Visibility = Visibility.Visible;
        DetailEmpty.Visibility = Visibility.Collapsed;
        DetailName.Text = node.IsTop ? node.Path : node.Name;
        DetailPath.Text = node.Path;
        Facts.Children.Clear();

        if (node.Folder is not { } folder)
        {
            Tabs.Visibility = Visibility.Collapsed;
            InheritedPanel.Visibility = Visibility.Visible;
            var ancestor = node.NearestStoredAncestor();
            AddFact("\uE8B7", "Ohne eigene Rechte");
            InheritedText.Text = ancestor is null
                ? $"„{node.Name}“ hat keine eigenen Berechtigungen. Zutritt speichert nur Ordner mit eigenen Rechten und die Scan-Wurzeln."
                : $"„{node.Name}“ hat keine eigenen Berechtigungen – es gilt, was an „{ancestor.Path}“ eingestellt ist. Zutritt speichert nur Ordner mit eigenen Rechten.";
            ParentButton.Visibility = ancestor is null ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        Tabs.Visibility = Visibility.Visible;
        InheritedPanel.Visibility = Visibility.Collapsed;
        var scan = _shown!;

        if (folder.ShareUnc is { } unc)
        {
            var share = scan.Shares.FirstOrDefault(s => s.UncPath.Equals(unc, StringComparison.OrdinalIgnoreCase));
            var tip = share is null ? null
                : !share.PermissionsReadable ? "Freigabe-Berechtigungen nicht lesbar"
                : "Freigabe-Berechtigungen:\n" + string.Join("\n", share.Permissions.Select(p =>
                    $"{_state.NameOf(p.Sid)}: {Rights.Label(p)}"));
            AddFact("\uE8CE", $"Freigabe {unc}", tip);
        }
        AddFact("\uE77B", folder.OwnerSid.Length > 0 ? $"Besitzer: {_state.NameOf(folder.OwnerSid)}" : "Besitzer unbekannt");
        if (folder.InheritanceBroken) AddFact("\uE72E", "Vererbung unterbrochen", "Rechte von oben gelten hier nicht – nur die eigenen Einträge.");
        else if (folder.InheritanceDiffers)
            AddFact("\uE8DE", "Vererbung weicht ab", "Die geerbten Einträge passen nicht zum übergeordneten Ordner – vermutlich verschoben. "
                + "Hier gelten womöglich noch Rechte vom früheren Ort (Befund ZT14).", caution: true);
        else AddFact("\uE71B", "Erbt vom übergeordneten Ordner");
        AddFact("\uE81E", folder.IsRoot ? "Scan-Wurzel" : $"Ebene {folder.Depth}");
        if (folder.IsFile) AddFact("\uE7C3", "Datei");
        if (folder.Error is { } error) AddFact("\uE783", $"Nicht lesbar: {error}", critical: true);

        AcesGrid.ItemsSource = folder.Aces.Select(a =>
        {
            var p = _state.PrincipalOf(a.Sid);
            return new AceRow(_state.NameOf(a.Sid), _state.ShortNameOf(a.Sid), p is null ? "Unbekannt" : Labels.Of(p.Kind),
                (a.IsDeny ? "Verweigern: " : "") + Labels.RightsText(a), a.Level, a.IsDeny,
                a.IsInherited ? "geerbt" : "explizit", Labels.AppliesTo(a, folder.IsFile));
        }).ToList();

        var findings = scan.Findings.Where(f => FolderTree.Key(f.Path).Equals(FolderTree.Key(folder.Path), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .Select(f => new FindingRow(f.Severity, f.RuleId, f.Message)).ToList();
        FindingsGrid.ItemsSource = findings;
        FindingsGrid.Visibility = findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FindingsEmpty.Visibility = findings.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        TabFindings.Header = findings.Count > 0 ? $"Befunde ({Labels.Number(findings.Count)})" : "Befunde";
        TabAces.Header = $"Einträge ({Labels.Number(folder.Aces.Count)})";

        LoadPeople(scan, folder);
    }

    /// <param name="caution">Auffällig, aber kein Fehler (z. B. abweichende Vererbung): Symbol in Warnfarbe.</param>
    private void AddFact(string glyph, string text, string? tooltip = null, bool critical = false, bool caution = false)
    {
        var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
        var label = new TextBlock { Text = text, Style = (Style)FindResource("TagText"), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty,
            critical ? "SystemFillColorCriticalBrush" : caution ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
        if (critical) label.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(label);
        var border = new Border { Style = (Style)FindResource("TagBorder"), Child = content, Padding = new Thickness(8, 3, 9, 3) };
        if (tooltip is not null) border.ToolTip = tooltip;
        if (critical) border.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCriticalBackgroundBrush");
        Facts.Children.Add(border);
    }

    private void LoadPeople(ScanResult scan, FolderNode folder)
    {
        int version = ++_peopleVersion;
        PeopleTable.ItemsSource = null;
        BroadNotice.Visibility = Visibility.Collapsed;
        TabPeople.Header = "Wer darf hier rein";
        PeopleStatus.Visibility = Visibility.Visible;
        if (folder.Error is not null)
        {
            PeopleStatus.Text = "Der Ordner ist nicht lesbar – wer hinein darf, lässt sich nicht bestimmen.";
            _peopleTask = Task.CompletedTask;
            return;
        }
        PeopleStatus.Text = "Wird berechnet …";
        var task = ShowPeopleAsync(scan, folder, version);
        _peopleTask = task;
        Ui.Run(_state, () => task, "„Wer darf hier rein“ ließ sich nicht berechnen");
    }

    private async Task ShowPeopleAsync(ScanResult scan, FolderNode folder, int version)
    {
        var grants = await Task.Run(() => EffectiveAccess.PeopleForFolder(scan, folder));
        if (version != _peopleVersion) return;

        Principal? P(string sid) => scan.Principals.GetValueOrDefault(sid);
        var broad = grants.Where(g => P(g.Sid)?.Kind == PrincipalKind.Bekannt).ToList();
        var people = grants.Where(g => P(g.Sid)?.Kind != PrincipalKind.Bekannt)
            .Select(g =>
            {
                var p = P(g.Sid);
                var person = p is null ? g.Sid : p.DisplayName.Length > 0 ? p.DisplayName : p.Label;
                var share = g.ViaShare is { } v ? Labels.Of(v) : folder.ShareUnc is null ? "keine Freigabe" : "nicht lesbar";
                var note = g.ReducedByDeny ? "Durch einen Verweigern-Eintrag eingeschränkt" : p?.IsDisabled == true ? "Das Konto ist deaktiviert" : "";
                return new PersonRow(person, p?.Label ?? g.Sid, share, g.Effective, Labels.Of(g.Ntfs), g.Ntfs,
                    Labels.ViaChain(g.Via), g.Via.Count == 0 ? "direkt berechtigt" : string.Join(" › ", g.Via), note);
            }).ToList();

        if (broad.Count > 0)
        {
            var list = string.Join(", ", broad.Select(g => $"„{AppState.NameIn(scan, g.Sid)}“ ({Labels.Of(g.Effective)})"));
            BroadText.Text = $"Außerdem berechtigt: {list}. Damit darf hier praktisch jede angemeldete Person hinein – auch wer unten nicht einzeln steht.";
            BroadNotice.Visibility = Visibility.Visible;
        }
        PeopleTable.ItemsSource = people;
        TabPeople.Header = $"Wer darf hier rein ({Labels.Number(people.Count)})";
        PeopleStatus.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PeopleStatus.Text = scan.Request.ResolveGroups
            ? "Keine einzelne Person hat hier Zugriff."
            : "Keine einzelne Person gefunden – der Lauf wurde ohne Auflösung der Gruppen erstellt.";
    }
}
