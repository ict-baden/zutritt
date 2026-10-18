using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Platform;
using Zutritt.Core.Scanning;

namespace Zutritt.Tests.Scanning;

/// <summary>
/// Der Scanner über die Plattform-Schnittstelle – mit einer Attrappe statt Windows, deshalb auf jedem System.
/// Die echten Ordner, Freigaben und Konten prüfen die Tests unter Windows/Scanning.
/// </summary>
public sealed class FakePlatformTests
{
    private const string User = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    [Fact]
    public async Task Ohne_Plattform_kein_Scan_mit_deutscher_Meldung()
    {
        var scanner = new Scanner(platform: null);
        Assert.False(scanner.IsSupported);
        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => scanner.RunAsync(new ScanRequest { Targets = [@"C:\Daten"] }));
        Assert.Contains("nur unter Windows", ex.Message);
    }

    [Fact]
    public async Task Baum_wird_ueber_die_Plattform_abgelaufen()
    {
        var platform = new FakeScanPlatform()
            .Folder(@"D:\Daten", explicitAce: true)
            .Folder(@"D:\Daten\Labor")
            .Folder(@"D:\Daten\Labor\QC", explicitAce: true)
            .Folder(@"D:\Daten\Kaputt", listError: "Zugriff verweigert");
        var scanner = new Scanner(platform, (_, _) => { }, _ => []);

        var scan = await scanner.RunAsync(new ScanRequest { Targets = [@"D:\Daten"] });

        Assert.Equal(@"TEST\scanner", scan.ScannedBy);
        Assert.Equal(4, scan.FoldersVisited);
        // Gespeichert: Wurzel, eigene Einträge, Fehler – nicht der reine Erbe „Labor“
        Assert.Equal([@"D:\Daten", @"D:\Daten\Kaputt", @"D:\Daten\Labor\QC"], scan.Folders.Select(f => f.Path).Order(StringComparer.Ordinal));
        var broken = Assert.Single(scan.Errors);
        Assert.Equal(@"D:\Daten\Kaputt", broken.Path);
        Assert.Equal("Inhalt nicht auflistbar: Zugriff verweigert", broken.Message);
    }

    [Fact]
    public async Task Nicht_erreichbares_Ziel_ist_ein_Fehler_kein_Abbruch()
    {
        var platform = new FakeScanPlatform();
        var scan = await new Scanner(platform, (_, _) => { }, _ => []).RunAsync(new ScanRequest { Targets = [@"\\FS01\Weg"] });

        var node = Assert.Single(scan.Folders);
        Assert.Equal("Ziel nicht erreichbar: Netzwerkpfad nicht gefunden", node.Error);
        Assert.Contains(scan.Errors, e => e.Path == @"\\FS01\Weg");
    }

    [Fact]
    public async Task Konten_kommen_vom_Resolver_der_Plattform()
    {
        var platform = new FakeScanPlatform().Folder(@"D:\Daten", explicitAce: true);
        platform.Directory.Add(User, @"TEST\max", PrincipalKind.Benutzer);

        var scan = await new Scanner(platform).RunAsync(new ScanRequest { Targets = [@"D:\Daten"] });

        Assert.Equal(@"TEST\max", scan.Principals[User].Name);
        Assert.Equal(1, platform.ResolversCreated);
    }

    [Fact]
    public async Task Server_ohne_Antwort_haelt_den_Lauf_nicht_an()
    {
        using var never = new ManualResetEventSlim(false);
        var platform = new FakeScanPlatform { HangOnShares = never };
        var scanner = new Scanner(platform, (_, _) => { }, _ => []) { ShareTimeout = TimeSpan.FromMilliseconds(300) };
        try
        {
            var scan = await scanner.RunAsync(new ScanRequest { Targets = [WindowsPath.Server("FS01")] });
            var error = Assert.Single(scan.Errors);
            Assert.Contains("keine Antwort innerhalb von 300 Millisekunden", error.Message);
        }
        finally
        {
            never.Set();
        }
    }

    /// <summary>Plattform im Speicher: Ordner mit Rechten und Fehlern, Freigaben gibt es keine.</summary>
    private sealed class FakeScanPlatform : IScanPlatform
    {
        private readonly Dictionary<string, (bool ExplicitAce, string? ListError)> _folders = new(StringComparer.OrdinalIgnoreCase);

        public FakeDirectory Directory { get; } = new();
        public ManualResetEventSlim? HangOnShares { get; init; }
        public int ResolversCreated { get; private set; }

        public FakeScanPlatform Folder(string path, bool explicitAce = false, string? listError = null)
        {
            _folders[path] = (explicitAce, listError);
            return this;
        }

        public string CurrentUserName => @"TEST\scanner";

        public IdentityResolver CreateIdentityResolver()
        {
            ResolversCreated++;
            return new IdentityResolver([Directory]);
        }

        public ScanTarget? ParseTarget(string? raw, out string? error)
        {
            error = null;
            var t = WindowsPath.TrimEnd(raw!.Trim());
            if (!WindowsPath.IsUnc(t)) return new ScanTarget(TargetKind.LocalPath, t);
            var parts = WindowsPath.Segments(t);
            return parts.Length == 1 ? new ScanTarget(TargetKind.Server, t, parts[0]) : new ScanTarget(TargetKind.UncPath, t, parts[0], parts[1]);
        }

        public string? FullLocalPath(string path) => path;

        public ShareListing EnumerateShares(string server)
        {
            HangOnShares?.Wait();
            return new([], "Kein Serverdienst", ServerServiceStopped: true);
        }

        public ShareLookup LookupShare(string server, string share) => new(null, "Netzwerkpfad nicht gefunden", NotFound: false);

        public TargetProbe Probe(string path)
            => _folders.ContainsKey(path) ? TargetProbe.Directory : TargetProbe.Unreachable("Netzwerkpfad nicht gefunden");

        public FolderNode ReadSecurity(string path, int depth, bool isFile, bool followLinks, byte[]? parentDescriptor, bool readParent,
            bool wantDescriptor, out byte[]? descriptor)
        {
            descriptor = null;
            var node = new FolderNode { Path = path, Depth = depth, IsFile = isFile, OwnerSid = "S-1-5-32-544" };
            var inherited = new AceEntry
            {
                Sid = "S-1-5-32-544", Rights = FileRights.FullControl, IsInherited = depth > 0,
                Inheritance = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit,
            };
            node.Aces.Add(inherited);
            // Eigener Eintrag hier; vererbt kommt er bei allen Unterordnern an
            var modify = new AceEntry { Sid = User, Rights = FileRights.Modify, Inheritance = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit };
            for (var above = WindowsPath.Parent(path); above is not null; above = WindowsPath.Parent(above))
            {
                if (_folders.TryGetValue(above, out var a) && a.ExplicitAce)
                {
                    node.Aces.Add(modify with { IsInherited = true });
                    break;
                }
            }
            if (_folders.TryGetValue(path, out var f) && f.ExplicitAce) node.Aces.Add(modify);
            return node;
        }

        public string? ListDirectory(string directory, List<DirEntry> entries)
        {
            foreach (var path in _folders.Keys)
                if (WindowsPath.Parent(path) is { } parent && parent.Equals(directory, StringComparison.OrdinalIgnoreCase))
                    entries.Add(new DirEntry(WindowsPath.LastSegment(path), IsDirectory: true, IsLink: false));
            return _folders.TryGetValue(directory, out var f) ? f.ListError : null;
        }
    }
}
