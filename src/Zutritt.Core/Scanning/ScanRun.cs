using System.Diagnostics;
using Zutritt.Core.Model;
using Zutritt.Core.Platform;
using Zutritt.Core.Resources;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Zustand eines einzelnen Laufs: Ziele auflösen, Freigaben sammeln, Bäume ablaufen. Ohne Konten und Befunde –
/// die hängt <see cref="Scanner"/> danach an.
/// </summary>
internal sealed class ScanRun
{
    public static string PhaseTargets => Strings.Phase_Targets;
    public static string PhaseFolders => Strings.Phase_Folders;
    public static string PhaseAccounts => Strings.Phase_Accounts;
    public static string PhaseFindings => Strings.Phase_Findings;
    public static string PhaseDone => Strings.Phase_Done;

    private sealed record Root(string Path, bool IsLocal, ScanTarget? Unc, ShareLookup? Lookup);

    /// <summary>
    /// Noch zu prüfender Ordner. <see cref="ParentSignature"/> null = Rechte des Elternordners unbekannt;
    /// <see cref="ParentDescriptor"/> = rohe Beschreibung des Elternordners (für Zugriffslisten im NT4-Stil);
    /// <see cref="Expected"/> = was der Ordner vom Elternordner erben müsste, null = unbekannt (Wurzel, Fehler).
    /// </summary>
    private readonly record struct Pending(string Path, int Depth, string? ParentSignature, byte[]? ParentDescriptor, string? ShareUnc, bool IsLink,
        InheritanceExpectation? Expected);

    private readonly IScanPlatform _platform;
    private readonly ScanResult _result;
    private readonly ScanRequest _request;
    private readonly IProgress<ScanProgress>? _progress;
    private readonly CancellationToken _ct;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly long _progressIntervalMs;

    /// <summary>Zeitlimit je Freigabeliste bzw. einzelner Freigabe eines Servers.</summary>
    public TimeSpan ShareTimeout { get; init; } = TimeBudget.Shares;
    private long _lastReport;
    private string _phase = PhaseTargets;

    private readonly List<Root> _roots = [];
    private readonly HashSet<string> _rootPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shareUncs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShareLookup> _lookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DirEntry> _entries = [];
    private ShareListing? _localListing;
    private LocalShareMap? _localMap;
    private bool _walkingLocal;
    private bool _localMapChecked;
    private FolderNode? _fileExpectationFolder;
    private string? _fileExpectationOwner;
    private string? _fileExpectation;
    private FolderNode? _fileInheritanceFolder;
    private InheritanceExpectation? _fileInheritance;

    /// <param name="progressIntervalMs">Mindestabstand zwischen zwei Fortschrittsmeldungen innerhalb einer Phase.</param>
    public ScanRun(IScanPlatform platform, ScanResult result, IProgress<ScanProgress>? progress, long progressIntervalMs, CancellationToken ct)
    {
        _platform = platform;
        _result = result;
        _request = result.Request;
        _progress = progress;
        _progressIntervalMs = progressIntervalMs;
        _lastReport = -progressIntervalMs;
        _ct = ct;
    }

    /// <summary>Ziele auflösen und alle Bäume ablaufen.</summary>
    public void Execute()
    {
        SetPhase(PhaseTargets, "");
        foreach (var raw in _request.Targets)
        {
            _ct.ThrowIfCancellationRequested();
            ResolveTarget(raw);
        }

        SetPhase(PhaseFolders, "");
        foreach (var root in _roots)
        {
            _ct.ThrowIfCancellationRequested();
            WalkRoot(root);
        }
    }

    public void SetPhase(string phase, string path)
    {
        _phase = phase;
        _lastReport = _clock.ElapsedMilliseconds;
        _progress?.Report(Snapshot(path));
    }

    private ScanProgress Snapshot(string path)
        => new(_phase, path, _result.FoldersVisited, _result.Folders.Count, _result.Errors.Count);

    private void Report(string path)
    {
        if (_progress is null) return;
        long now = _clock.ElapsedMilliseconds;
        if (now - _lastReport < _progressIntervalMs) return;
        _lastReport = now;
        _progress.Report(Snapshot(path));
    }

    // ---------- Ziele ----------

    private void ResolveTarget(string raw)
    {
        var target = _platform.ParseTarget(raw, out var error);
        if (target is null)
        {
            AddError(raw ?? "", Texts.Format(Strings.Scan_InvalidTarget, error));
            return;
        }

        switch (target.Kind)
        {
            case TargetKind.LocalPath:
                AddRoot(new Root(target.Path, IsLocal: true, null, null));
                break;

            case TargetKind.UncPath:
                var lookup = LookupShare(target.Server!, target.Share!);
                if (lookup.Read is not null) AddShare(lookup.Read);
                AddRoot(new Root(target.Path, IsLocal: false, target, lookup));
                break;

            case TargetKind.Server:
                var listing = EnumerateShares(target.Server!);
                if (listing.Error is not null) { AddError(target.Path, listing.Error); break; }
                var shares = listing.Shares.Where(s => !(_request.SkipAdminShares && s.Share.IsAdminShare)).ToList();
                if (shares.Count == 0) { AddError(target.Path, Strings.Scan_NoDiskShares); break; }
                foreach (var read in shares)
                {
                    AddShare(read);
                    var unc = new ScanTarget(TargetKind.UncPath, read.Share.UncPath, read.Share.Server, read.Share.Name);
                    AddRoot(new Root(read.Share.UncPath, IsLocal: false, unc, new ShareLookup(read, null, false)));
                }
                break;

            case TargetKind.LocalShares:
                ResolveLocalShares();
                break;
        }
    }

    private void ResolveLocalShares()
    {
        var listing = LocalListing();
        if (listing.Error is not null) { AddError(".", listing.Error); return; }
        var shares = listing.Shares.Where(s => !(_request.SkipAdminShares && s.Share.IsAdminShare)).ToList();
        if (shares.Count == 0)
        {
            AddError(".", Strings.Scan_NoLocalDiskShares);
            return;
        }
        foreach (var read in shares)
        {
            // Jede Freigabe gehört ins Ergebnis, auch wenn ihr Ordner schon über eine andere Freigabe auf demselben
            // Pfad geprüft wird – sonst fehlten deren Freigabe-Rechte (z. B. „Jeder: Vollzugriff“) im Bericht.
            AddShare(read);
            if (read.Share.LocalPath.Length == 0)
            {
                // Ohne lokalen Pfad (Rechte fehlen) lässt sich die Freigabe hier nicht prüfen – wenigstens nennen.
                AddError(read.Share.UncPath, Strings.Scan_ShareLocalPathUnreadable);
                continue;
            }
            if (_platform.FullLocalPath(read.Share.LocalPath) is { } full)
                AddRoot(new Root(WindowsPath.TrimEnd(full), IsLocal: true, null, null));
            else
                AddError(read.Share.UncPath, Texts.Format(Strings.Scan_ShareLocalPathInvalid, read.Share.LocalPath));
        }
    }

    /// <summary>Freigaben dieses Rechners, einmal je Lauf gelesen (für „.“ und für die Zuordnung lokaler Pfade).</summary>
    private ShareListing LocalListing()
    {
        if (_localListing is not null) return _localListing;
        _localListing = EnumerateShares(".");
        _localMap = new LocalShareMap(_localListing.Shares, includeAdminShares: !_request.SkipAdminShares, _platform.FullLocalPath);
        return _localListing;
    }

    private ShareLookup LookupShare(string server, string share)
    {
        var key = WindowsPath.Unc(server, share);
        if (!_lookups.TryGetValue(key, out var lookup))
        {
            _lookups[key] = lookup = TimeBudget.TryRun(() => _platform.LookupShare(server, share), ShareTimeout, out var read)
                ? read
                : new ShareLookup(null, Texts.Format(Strings.Scan_ShareTimeout, key, TimeBudget.Describe(ShareTimeout)), NotFound: false);
        }
        return lookup;
    }

    /// <summary>Freigaben eines Servers mit Zeitlimit – ein Server, der nicht antwortet, hält den Lauf nicht an.</summary>
    private ShareListing EnumerateShares(string server)
        => TimeBudget.TryRun(() => _platform.EnumerateShares(server), ShareTimeout, out var listing)
            ? listing
            : new ShareListing([], Texts.Format(Strings.Scan_SharesTimeout, server, TimeBudget.Describe(ShareTimeout)));

    private void AddRoot(Root root)
    {
        // Doppelte Ziele nur einmal prüfen; liegt ein Ziel in einem anderen, prüft es nur sein eigener Durchlauf.
        if (_rootPaths.Add(root.Path)) _roots.Add(root);
    }

    private void AddShare(ShareRead read)
    {
        if (!_shareUncs.Add(read.Share.UncPath)) return;
        _result.Shares.Add(read.Share);
        if (read.Note is not null) AddError(read.Share.UncPath, read.Note);
    }

    private void AddError(string path, string message) => _result.Errors.Add(new ScanError(path, message));

    // ---------- Bäume ----------

    private void WalkRoot(Root root)
    {
        _walkingLocal = root.IsLocal;
        if (root.IsLocal && !_localMapChecked)
        {
            _localMapChecked = true;
            var listing = LocalListing();
            // Ohne Serverdienst gibt es keine Freigaben – das ist kein Fehler, sondern das Ergebnis.
            if (listing.Error is not null && !listing.ServerServiceStopped)
                AddError(Environment.MachineName, Texts.Format(Strings.Scan_LocalWithoutShares, listing.Error));
        }

        var probe = _platform.Probe(root.Path);
        if (probe.Error is not null)
        {
            var failed = new FolderNode { Path = root.Path, Depth = 0, Error = Texts.Format(Strings.Scan_TargetUnreachable, probe.Error) };
            failed.ShareUnc = root.IsLocal ? RootShareUnc(root) : root.Lookup?.Read?.Share.UncPath;
            _result.FoldersVisited++;
            _result.Folders.Add(failed);
            AddError(failed.Path, failed.Error);
            Report(failed.Path);
            return;
        }

        // Freigabe existiert (Pfad erreichbar), aber ihre Daten waren nicht lesbar: als „Rechte nicht lesbar“ aufnehmen,
        // damit Ordner und Bericht trotzdem wissen, über welche Freigabe der Zugriff läuft.
        if (root is { IsLocal: false, Unc: not null, Lookup: { Read: null } lookup })
        {
            var placeholder = new ShareInfo { Server = root.Unc.Server!, Name = root.Unc.Share!, PermissionsReadable = false };
            AddShare(new ShareRead(placeholder, lookup.Error ?? Strings.Scan_SharePermissionsUnreadable));
        }

        var shareUnc = RootShareUnc(root);
        if (!probe.IsDirectory)
        {
            // Eine Datei als Ziel: nur sie selbst prüfen.
            var file = _platform.ReadSecurity(root.Path, 0, isFile: true, followLinks: true, parentDescriptor: null, readParent: true,
                wantDescriptor: false, out _);
            file.ShareUnc = shareUnc;
            _result.FilesVisited++;
            _result.Folders.Add(file);
            if (file.Error is not null) AddError(file.Path, file.Error);
            Report(file.Path);
            return;
        }

        // Tiefensuche mit eigenem Stapel statt Rekursion: Bäume mit Hunderten Ebenen sprengen so keinen Aufrufstapel.
        var stack = new Stack<Pending>();
        stack.Push(new Pending(root.Path, 0, null, null, shareUnc, IsLink: false, Expected: null));
        while (stack.Count > 0)
        {
            _ct.ThrowIfCancellationRequested();
            VisitDirectory(stack.Pop(), stack);
        }
    }

    private string? RootShareUnc(Root root)
    {
        // Schreibweise der gelesenen Freigabe übernehmen, damit Ordner und Freigabeliste genau zusammenpassen.
        if (!root.IsLocal) return root.Lookup?.Read?.Share.UncPath ?? root.Unc?.ShareUnc;
        var read = _localMap?.Longest(root.Path);
        if (read is null) return null;
        AddShare(read);
        return read.Share.UncPath;
    }

    private void VisitDirectory(Pending item, Stack<Pending> stack)
    {
        // Verknüpfungen (Junctions, symbolische Links) nicht verfolgen: Schleifen und doppelt gezählte Bäume vermeiden.
        // Die Wurzel aber schon, falls sie selbst eine ist – sie wurde ausdrücklich als Ziel genannt.
        bool isRoot = item.Depth == 0;
        bool descend = (isRoot || !item.IsLink) && (_request.MaxDepth is not int max || item.Depth < max);

        // Die Wurzel liest ihren Elternordner bei Bedarf selbst, alle anderen bekommen ihn mit.
        var node = _platform.ReadSecurity(item.Path, item.Depth, isFile: false, followLinks: isRoot, item.ParentDescriptor,
            readParent: isRoot, wantDescriptor: descend, out var descriptor);
        _result.FoldersVisited++;
        node.ShareUnc = item.ShareUnc;
        var signature = NodeSignature.Of(node);
        // Geerbte Einträge, die der Elternordner so nicht vergibt: verschobener Ordner (Befund ZT14)
        node.InheritanceDiffers = item.Expected is not null && !item.Expected.Matches(node);

        // Gespeichert wird, was die Auswertung nicht vom nächsthöheren gespeicherten Ordner ableiten kann.
        bool store = item.Depth == 0 || node.Error is not null || node.InheritanceBroken || node.HasExplicitAces
                     || item.ParentSignature is null || signature != item.ParentSignature || node.InheritanceDiffers;
        if (store) _result.Folders.Add(node);

        if (descend)
        {
            _entries.Clear();
            var error = _platform.ListDirectory(item.Path, _entries);
            if (error is not null)
            {
                node.Error = Join(node.Error, Texts.Format(Strings.Scan_ListingFailed, error));
                if (!store) { _result.Folders.Add(node); store = true; }
            }

            // Einheitliche Reihenfolge, damit zwei Läufe über denselben Baum gleich aussehen.
            _entries.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            if (_request.IncludeFiles)
            {
                // Rechte des Ordners nur als Maßstab, wenn sie lesbar waren (signature ist dann gesetzt).
                var folder = signature is null ? null : node;
                foreach (var entry in _entries)
                    if (!entry.IsDirectory) VisitFile(WindowsPath.Join(item.Path, entry.Name), item.Depth + 1, descriptor, folder, node.ShareUnc);
            }
            // Von einem Ordner mit Fehler (auch nur teilweise auflistbar) leitet die Auswertung nichts ab – seine
            // Unterordner müssen deshalb selbst gespeichert werden. Ob sie richtig erben, lässt sich dann auch nicht sagen.
            var childSignature = node.Error is null ? signature : null;
            InheritanceExpectation? expected = null;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!entry.IsDirectory) continue;
                var child = WindowsPath.Join(item.Path, entry.Name);
                if (_rootPaths.Contains(child)) continue; // eigenes Ziel, wird mit eigener Wurzel geprüft
                // Einmal je Ordner und nur, wenn er Unterordner hat
                if (childSignature is not null) expected ??= InheritanceExpectation.ForSubfolders(node);
                stack.Push(new Pending(child, item.Depth + 1, childSignature, descriptor, ChildShareUnc(child, item.ShareUnc), entry.IsLink,
                    childSignature is null ? null : expected));
            }
        }

        if (node.Error is not null) AddError(node.Path, node.Error);
        Report(node.Path);
    }

    /// <param name="folder">Ordner der Datei, wenn seine Rechte lesbar waren – sonst zählen nur eigene Einträge.</param>
    private void VisitFile(string path, int depth, byte[]? parentDescriptor, FolderNode? folder, string? shareUnc)
    {
        _ct.ThrowIfCancellationRequested();
        var node = _platform.ReadSecurity(path, depth, isFile: true, followLinks: false, parentDescriptor, readParent: false,
            wantDescriptor: false, out _);
        _result.FilesVisited++;
        node.ShareUnc = shareUnc;
        if (folder is not null)
        {
            // Die Erwartung gilt für alle Dateien des Ordners – einmal je Ordner bilden.
            if (!ReferenceEquals(folder, _fileInheritanceFolder))
            {
                _fileInheritanceFolder = folder;
                _fileInheritance = InheritanceExpectation.ForFiles(folder);
            }
            node.InheritanceDiffers = !_fileInheritance!.Matches(node);
        }
        if (node.Error is not null || node.InheritanceBroken || node.HasExplicitAces || node.InheritanceDiffers || DiffersFromFolder(node, folder))
        {
            _result.Folders.Add(node);
            if (node.Error is not null) AddError(node.Path, node.Error);
        }
        Report(path);
    }

    /// <summary>
    /// Hat die Datei andere Rechte, als sie vom Ordner erben würde? Typisch für verschobene Dateien: Sie behalten ihre
    /// alten geerbten Einträge – eine Datei aus „Öffentlich“ bleibt in „Personal“ für „Jeder“ lesbar.
    /// </summary>
    private bool DiffersFromFolder(FolderNode file, FolderNode? folder)
    {
        if (folder is null) return false;
        // Die Erwartung hängt nur vom Besitzer ab (ERSTELLER-BESITZER) – meist für alle Dateien eines Ordners derselbe.
        if (!ReferenceEquals(folder, _fileExpectationFolder) || file.OwnerSid != _fileExpectationOwner)
        {
            _fileExpectationFolder = folder;
            _fileExpectationOwner = file.OwnerSid;
            _fileExpectation = NodeSignature.InheritedByFile(folder, file.OwnerSid);
        }
        return NodeSignature.Effective(file) != _fileExpectation;
    }

    /// <summary>
    /// Freigabe eines Unterordners: Liegt genau hier eine (lokale) Freigabe, gilt sie – sonst die des Elternordners.
    /// Das ergibt das längste passende Präfix, ohne für jeden Ordner alle Freigaben zu vergleichen.
    /// </summary>
    private string? ChildShareUnc(string child, string? parentShareUnc)
    {
        if (!_walkingLocal || _localMap is null || _localMap.IsEmpty) return parentShareUnc;
        var read = _localMap.At(child);
        if (read is null) return parentShareUnc;
        AddShare(read);
        return read.Share.UncPath;
    }

    private static string Join(string? first, string second) => first is null ? second : first + "; " + second;
}
