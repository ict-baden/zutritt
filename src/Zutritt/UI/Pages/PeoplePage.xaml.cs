// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.UI.Pages;

/// <summary>Personen: alle Benutzerkonten des Laufs, rechts „Wo darf die Person überall hin?“ und ihre Gruppen.</summary>
public partial class PeoplePage : UserControl, IPage, ISearchPage
{
    /// <summary>Eintrag der Personenliste; die Zahl der Ordner wird im Hintergrund nachgetragen.</summary>
    public sealed class PersonItem(Principal principal) : INotifyPropertyChanged
    {
        private int? _count;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Principal Principal { get; } = principal;
        public string Sid => Principal.Sid;
        public string DisplayName { get; } = principal.DisplayName.Length > 0 ? principal.DisplayName : AccountName.LastPart(principal.Label);
        public string Account => Principal.Label;
        public string Initials { get; } = Ui.Initials(principal.DisplayName, principal.Label);
        public bool IsDisabled => Principal.IsDisabled;
        internal string SearchText { get; } = $"{principal.DisplayName}\n{principal.Label}";
        public override string ToString() => $"{DisplayName}, {Account}" + (IsDisabled ? Strings.Pe_DisabledSuffix : "");

        public int? FolderCount
        {
            get => _count;
            set
            {
                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderCountText)));
            }
        }

        public string FolderCountText => _count switch
        {
            null => "…",
            { } n => Texts.Format(Texts.Plural(n, Strings.Pe_FolderCountOne, Strings.Pe_FolderCountMany), Labels.Number(n)),
        };
    }

    /// <param name="Via">Gruppenkette kurz (ohne Domäne), <paramref name="ViaFull"/> vollständig für den Tooltip.</param>
    public sealed record AccessRow(string Path, string Right, AccessLevel Level, string RightDetail, string Via, string ViaFull, string Note)
    {
        public override string ToString() => $"{Path}, {Right}, {Via}";
    }

    /// <summary>Für den Screenshot gezeigte Person des Demo-Laufs.</summary>
    internal const string ScreenshotPerson = "QC-Leitung";

    private readonly AppState _state;
    private readonly Debouncer _search = new(TimeSpan.FromMilliseconds(250));
    private ScanResult? _shown;
    private bool _loaded;
    private List<PersonItem> _all = [];
    private CancellationTokenSource? _counting;
    private Task _countTask = Task.CompletedTask;
    // Angeforderte Zählungen: Stapel (zuletzt sichtbar zuerst) und Menge gegen doppelte Anforderungen
    private readonly List<PersonItem> _waiting = [];
    private readonly HashSet<PersonItem> _queued = new(ReferenceEqualityComparer.Instance);
    private VirtualizingStackPanel? _panel;
    private Task _detailTask = Task.CompletedTask;
    private int _detailVersion;

    /// <summary>Berechnung des Details der gewählten Person (für die Messung).</summary>
    internal Task DetailTask => _detailTask;

    public PeoplePage(AppState state)
    {
        InitializeComponent();
        _state = state;
        // Blättern (und das erste Füllen der Liste) zeigt neue Personen – deren Ordner dann zählen
        PeopleList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RequestVisibleCounts()));
        ShowDetail(null);
    }

    public void OnShown()
    {
        if (!_loaded || !ReferenceEquals(_shown, _state.Scan)) Load();
    }

    public async Task PrepareScreenshotAsync()
    {
        OnShown();
        SearchBox.Text = "";
        ApplyFilter();
        var person = _all.FirstOrDefault(p => p.DisplayName == ScreenshotPerson) ?? _all.FirstOrDefault();
        PeopleList.SelectedItem = person;
        await _detailTask;
        await Ui.IdleAsync();
        if (person is not null) PeopleList.ScrollIntoView(person);
        await Ui.IdleAsync();
        // Zahlen der jetzt sichtbaren Personen abwarten
        RequestVisibleCounts();
        await _countTask;
        await Ui.IdleAsync();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ================= Liste =================

    private void Load()
    {
        _loaded = true;
        _shown = _state.Scan;
        _counting?.Cancel();
        _counting = null;
        _countTask = Task.CompletedTask;
        _waiting.Clear();
        _queued.Clear();
        PeopleList.SelectedItem = null;
        ShowDetail(null);

        if (_shown is not { } scan)
        {
            _all = [];
            ApplyFilter();
            return;
        }

        _all = scan.Principals.Values
            .Where(p => p.Kind == PrincipalKind.Benutzer)
            .Select(p => new PersonItem(p))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ApplyFilter();

        _counting = new CancellationTokenSource();
    }

    // ================= Ordner je Person =================

    /// <summary>
    /// Fordert die Zahl der Ordner für die gerade angezeigten Personen an. Gezählt wird nur, was jemand sieht:
    /// Bei 3.000 Personen und 50.000 Ordnern bräuchte das Zählen aller Personen minutenlang die ganze CPU.
    /// </summary>
    private void RequestVisibleCounts()
    {
        if (_shown is not { } scan || _counting is not { } cts) return;
        var visible = VisibleItems();
        // Von unten nach oben ablegen: abgearbeitet wird vom Ende her, also die oberste sichtbare Person zuerst
        for (int i = visible.Count - 1; i >= 0; i--)
        {
            var item = visible[i];
            if (item.FolderCount is null && _queued.Add(item)) _waiting.Add(item);
        }
        if (_waiting.Count == 0 || !_countTask.IsCompleted) return;
        var task = CountWaitingAsync(scan, cts.Token);
        _countTask = task;
        Ui.Run(_state, () => task, Strings.Pe_CountFailed);
    }

    /// <summary>Angelegte Listeneinträge – die sichtbaren plus den kleinen Vorrat der Virtualisierung.</summary>
    private List<PersonItem> VisibleItems()
    {
        // Neu suchen, wenn die Vorlage der Liste ersetzt wurde (Wechsel Hell/Dunkel in Windows) – sonst blieben
        // die Zahlen danach bei „…“ stehen
        if (_panel is null || !_panel.IsDescendantOf(PeopleList)) _panel = Ui.FindChild<VirtualizingStackPanel>(PeopleList);
        if (_panel is null) return [];
        return _panel.Children.OfType<ListBoxItem>().Select(c => c.DataContext).OfType<PersonItem>().ToList();
    }

    /// <summary>
    /// Zählt die angeforderten Personen in kleinen Paketen im Hintergrund, die zuletzt sichtbar gewordenen zuerst.
    /// Wer inzwischen weggeblättert wurde, wird übersprungen und erst wieder angefragt, wenn er erneut erscheint.
    /// </summary>
    private async Task CountWaitingAsync(ScanResult scan, CancellationToken ct)
    {
        // Je Paket entsteht der Auswertungs-Kontext nur einmal
        const int Batch = 24;
        while (!ct.IsCancellationRequested)
        {
            var visible = new HashSet<PersonItem>(VisibleItems(), ReferenceEqualityComparer.Instance);
            var batch = new List<PersonItem>();
            while (batch.Count < Batch && _waiting.Count > 0)
            {
                var item = _waiting[^1];
                _waiting.RemoveAt(_waiting.Count - 1);
                _queued.Remove(item);
                if (item.FolderCount is null && visible.Contains(item)) batch.Add(item);
            }
            if (batch.Count == 0) return;
            var counts = await Task.Run(() =>
            {
                var access = EffectiveAccess.ForPeople(scan, batch.Select(p => p.Sid), ct);
                return batch.Select(p => access.TryGetValue(p.Sid, out var g) ? g.Count : 0).ToList();
            }, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(scan, _shown)) return;
            for (int j = 0; j < batch.Count; j++) batch[j].FolderCount = counts[j];
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _search.Run(ApplyFilter);

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var selected = PeopleList.SelectedItem;
        var items = query.Length == 0 ? _all : _all.Where(p => p.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        PeopleList.ItemsSource = items;
        if (selected is not null && items.Contains(selected)) PeopleList.SelectedItem = selected;

        int disabled = _all.Count(p => p.IsDisabled);
        ListCaption.Text = _shown is null ? ""
            : Texts.Format(Texts.Plural(_all.Count, Strings.Pe_CountOne, Strings.Pe_CountMany), Labels.Number(_all.Count))
              + (disabled > 0 ? Texts.Format(Strings.Pe_OfWhichDisabled, Labels.Number(disabled)) : "");
        if (_shown is null) ShowListMessage(Strings.Fo_NoRunShort);
        else if (_all.Count == 0)
            ShowListMessage(_shown.Request.ResolveGroups
                ? Strings.Pe_NoUsers
                : Strings.Pe_NoUsersNoGroups);
        else if (items.Count == 0) ShowListMessage(Texts.Format(Strings.Pe_NoMatch, query));
        else ListEmpty.Visibility = Visibility.Collapsed;
    }

    private void ShowListMessage(string text)
    {
        ListEmpty.Text = text;
        ListEmpty.Visibility = Visibility.Visible;
    }

    private void PeopleList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetail(PeopleList.SelectedItem as PersonItem);

    // ================= Detail =================

    private void ShowDetail(PersonItem? item)
    {
        int version = ++_detailVersion;
        if (item is null || _shown is null)
        {
            DetailRoot.Visibility = Visibility.Collapsed;
            DetailEmpty.Visibility = Visibility.Visible;
            DetailEmpty.Text = _shown is null ? Strings.Fo_NoRunShort : Strings.Pe_SelectPerson;
            _detailTask = Task.CompletedTask;
            return;
        }
        DetailRoot.Visibility = Visibility.Visible;
        DetailEmpty.Visibility = Visibility.Collapsed;

        var p = item.Principal;
        DetailInitials.Text = item.Initials;
        DetailName.Text = item.DisplayName;
        DetailAccount.Text = p.Label;
        Facts.Children.Clear();
        if (p.IsDisabled) AddTag(Facts, "\uE7BA", Strings.Pe_AccountDisabled, "SevMedium", "SevMediumBg");
        AddTag(Facts, p.IsLocal ? "\uE770" : "\uE774", p.IsLocal ? Strings.Pe_LocalAccount : Strings.Pe_DomainAccount);
        AddTag(Facts, "\uE8EC", p.Sid);

        Groups.Children.Clear();
        AccessTable.ItemsSource = null;
        AccessTitle.Text = Strings.Area_Access;
        GroupsTitle.Text = Strings.Pe_MemberOf;
        AccessStatus.Text = Strings.Fo_Calculating;
        AccessStatus.Visibility = Visibility.Visible;

        var scan = _shown;
        var task = ShowAccessAsync(scan, item, version);
        _detailTask = task;
        Ui.Run(_state, () => task, Strings.Pe_RightsFailed);
    }

    private async Task ShowAccessAsync(ScanResult scan, PersonItem item, int version)
    {
        var (grants, groups) = await Task.Run(() =>
            (EffectiveAccess.ForPerson(scan, item.Sid), EffectiveAccess.GroupsOf(scan, item.Sid)));
        if (version != _detailVersion) return;

        // Gruppen: echte Gruppen alphabetisch, bekannte Prinzipale (Jeder, Authentifizierte Benutzer) dahinter
        var memberOf = groups.Where(g => !g.Equals(item.Sid, StringComparison.OrdinalIgnoreCase))
            // Implizite Identitäten (Jeder, Authentifizierte Benutzer, Diese Organisation) stehen nur im Lauf, wenn ein
            // Eintrag sie nennt – sonst der bekannte Name statt der bloßen SID
            .Select(g => scan.Principals.GetValueOrDefault(g) is { } gp ? (Name: gp.Label, Known: gp.Kind == PrincipalKind.Bekannt)
                : SidCatalog.FallbackName(g) is { } known ? (Name: known, Known: true) : (Name: g, Known: false))
            .OrderBy(g => g.Known).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var g in memberOf)
            AddTag(Groups, g.Known ? "\uE716" : "\uE902", g.Name, g.Known ? "TextFillColorTertiaryBrush" : null);
        GroupsTitle.Text = memberOf.Count == 0 ? Strings.Pe_NoGroup : Texts.Format(Strings.Pe_MemberOfCount, memberOf.Count);

        var rows = grants.Select(g => new AccessRow(
            g.Path, Labels.Of(g.Effective), g.Effective,
            g.ViaShare is { } share ? Texts.Format(Strings.Pe_NtfsViaShare, Labels.Of(g.Ntfs), Labels.Of(share)) : Texts.Format(Strings.Pe_NtfsNoShare, Labels.Of(g.Ntfs)),
            Labels.ViaChain(g.Via), g.Via.Count == 0 ? Strings.Pdf_DirectlyGranted : string.Join(" › ", g.Via),
            g.ReducedByDeny ? Strings.Fo_ReducedByDeny
                : g.ViaShare is { } capped && capped < g.Ntfs ? Strings.Pe_ShareCaps : "")).ToList();
        AccessTable.ItemsSource = rows;
        AccessTitle.Text = Texts.Format(Texts.Plural(rows.Count, Strings.Pe_AccessToOne, Strings.Pe_AccessToMany), Labels.Number(rows.Count));
        AccessStatus.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccessStatus.Text = Strings.Pe_NoAccess;
        item.FolderCount = rows.Count;
    }

    private void AddTag(Panel target, string glyph, string text, string? foreground = null, string? background = null)
    {
        var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) };
        var label = new TextBlock { Text = text, Style = (Style)FindResource("TagText"), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, foreground ?? "TextFillColorSecondaryBrush");
        if (foreground is not null) label.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(label);
        var border = new Border { Style = (Style)FindResource("TagBorder"), Child = content, Padding = new Thickness(8, 3, 9, 3) };
        if (background is not null) border.SetResourceReference(Border.BackgroundProperty, background);
        target.Children.Add(border);
    }
}
