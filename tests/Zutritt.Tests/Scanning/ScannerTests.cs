using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Scanning.FsTestHelper;

namespace Zutritt.Tests.Scanning;

/// <summary>Scanner auf echten Ordnerbäumen (ohne Freigaben, ohne Adminrechte).</summary>
public sealed class ScannerTests
{
    /// <summary>Scanner ohne Kontenauflösung und Befunde – die bauen andere Bausteine.</summary>
    private static Scanner Plain() => new((_, _) => { }, _ => []);

    private static Task<ScanResult> Scan(params string[] targets) => Plain().RunAsync(new ScanRequest { Targets = targets });

    private static Task<ScanResult> Scan(ScanRequest request) => Plain().RunAsync(request);

    /// <summary>
    /// root
    /// ├─ a            (explizit: Netzwerkdienst Ändern)
    /// │  ├─ a1        (nur geerbt, wie a)
    /// │  └─ explizit.txt (eigenes Recht)
    /// ├─ b            (Vererbung unterbrochen)
    /// ├─ c\d\e        (e explizit: Hauptbenutzer Lesen)
    /// ├─ plain        (nur geerbt)
    /// │  └─ f.txt
    /// └─ wurzel.txt
    /// </summary>
    private static void BuildTree(TempFolder tmp)
    {
        tmp.Sub("a", "a1");
        tmp.Sub("b");
        tmp.Sub("c", "d", "e");
        tmp.Sub("plain");
        Grant(Path.Combine(tmp.Path, "a"), NetworkService, FileSystemRights.Modify);
        TempFolder.BreakInheritance(Path.Combine(tmp.Path, "b"));
        Grant(Path.Combine(tmp.Path, "c", "d", "e"), PowerUsers, FileSystemRights.ReadAndExecute);
        WriteFile(Path.Combine(tmp.Path, "a", "explizit.txt"));
        GrantFile(Path.Combine(tmp.Path, "a", "explizit.txt"), NetworkService, FileSystemRights.Read);
        WriteFile(Path.Combine(tmp.Path, "plain", "f.txt"));
        WriteFile(Path.Combine(tmp.Path, "wurzel.txt"));
    }

    private static string P(TempFolder tmp, params string[] parts) => Path.Combine([tmp.Path, .. parts]);

    [Fact]
    public async Task Speichert_nur_Knoten_mit_eigenen_Rechten()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);

        var result = await Scan(tmp.Path);

        Assert.Equal([tmp.Path, P(tmp, "a"), P(tmp, "b"), P(tmp, "c", "d", "e")], result.Folders.Select(f => f.Path));
        var root = result.Folders[0];
        Assert.True(root.IsRoot);
        Assert.False(root.IsFile);
        Assert.True(result.Folders[1].HasExplicitAces);
        Assert.True(result.Folders[2].InheritanceBroken);
        Assert.Equal(3, result.Folders[3].Depth);
        Assert.Contains(result.Folders[3].Aces, a => a.Sid == PowerUsers && !a.IsInherited);
        Assert.Equal(8, result.FoldersVisited); // root, a, a1, b, c, d, e, plain
        Assert.Equal(0, result.FilesVisited);
        Assert.Empty(result.Errors);
        Assert.All(result.Folders, f => Assert.Null(f.Error));
        Assert.All(result.Folders, f => Assert.NotEqual("", f.OwnerSid));
    }

    [Fact]
    public async Task Dateien_nur_auf_Wunsch()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);

        var result = await Scan(new ScanRequest { Targets = [tmp.Path], IncludeFiles = true });

        var file = Assert.Single(result.Folders, f => f.IsFile);
        Assert.Equal(P(tmp, "a", "explizit.txt"), file.Path);
        Assert.Equal(2, file.Depth);
        Assert.Contains(file.Aces, a => a.Sid == NetworkService && !a.IsInherited);
        Assert.Equal(3, result.FilesVisited);
        Assert.Equal(8, result.FoldersVisited);
        // Dateien stehen direkt hinter ihrem Ordner.
        Assert.Equal(P(tmp, "a"), result.Folders[result.Folders.IndexOf(file) - 1].Path);
    }

    [Fact]
    public async Task MaxDepth_begrenzt_Ordner_und_Dateien()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);

        var one = await Scan(new ScanRequest { Targets = [tmp.Path], MaxDepth = 1, IncludeFiles = true });
        Assert.Equal(5, one.FoldersVisited); // root, a, b, c, plain
        Assert.Equal(1, one.FilesVisited);   // wurzel.txt
        Assert.Equal([tmp.Path, P(tmp, "a"), P(tmp, "b")], one.Folders.Select(f => f.Path));

        var zero = await Scan(new ScanRequest { Targets = [tmp.Path], MaxDepth = 0, IncludeFiles = true });
        Assert.Equal(1, zero.FoldersVisited);
        Assert.Equal(0, zero.FilesVisited);
        Assert.Equal([tmp.Path], zero.Folders.Select(f => f.Path));
    }

    [Fact]
    public async Task Junction_wird_erfasst_aber_nicht_verfolgt()
    {
        using var tmp = new TempFolder();
        using var outside = new TempFolder();
        var secret = outside.Sub("geheim");
        Grant(secret, NetworkService, FileSystemRights.FullControl);
        Grant(outside.Path, BackupOperators, FileSystemRights.Modify);
        var link = P(tmp, "verweis");
        using var junction = Junction(link, outside.Path);
        // Eigene Rechte am Verweis selbst, damit er gespeichert wird.
        SetRawDaclNoFollow(link, $"D:P(A;;FA;;;{CurrentUserSid})(A;;0x1200a9;;;{PowerUsers})");

        var result = await Scan(tmp.Path);

        var node = Assert.Single(result.Folders, f => f.Path == link);
        Assert.Contains(node.Aces, a => a.Sid == PowerUsers);
        Assert.DoesNotContain(node.Aces, a => a.Sid == BackupOperators); // Rechte des Ziels
        Assert.DoesNotContain(result.Folders, f => f.Path.StartsWith(link + "\\", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, result.FoldersVisited); // root und Verweis, nichts dahinter
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Ziel_als_Junction_wird_verfolgt()
    {
        using var tmp = new TempFolder();
        using var outside = new TempFolder();
        outside.Sub("x");
        Grant(P(outside, "x"), NetworkService, FileSystemRights.Modify);
        var link = P(tmp, "verweis");
        using var junction = Junction(link, outside.Path);

        var result = await Scan(link);

        Assert.Equal([link, Path.Combine(link, "x")], result.Folders.Select(f => f.Path));
        Assert.Equal(2, result.FoldersVisited);
    }

    [Fact]
    public async Task Nicht_auflistbarer_Ordner_wird_Fehlerknoten()
    {
        using var tmp = new TempFolder();
        var locked = tmp.Sub("gesperrt");
        tmp.Sub("gesperrt", "innen");
        try
        {
            // Nur SYSTEM darf hinein; als Besitzer bleiben Rechte lesen und ändern (zum Aufräumen).
            SetRawDacl(locked, "D:P(A;OICI;FA;;;SY)");

            var result = await PrivilegeScope.WithoutBackupPrivilegeAsync(() => Scan(tmp.Path));

            var node = Assert.Single(result.Folders, f => f.Path == locked);
            Assert.Equal("Inhalt nicht auflistbar: Zugriff verweigert", node.Error);
            Assert.Equal([SystemSid], node.Aces.Select(a => a.Sid));
            Assert.True(node.InheritanceBroken);
            Assert.Contains(new ScanError(locked, "Inhalt nicht auflistbar: Zugriff verweigert"), result.Errors);
            Assert.Equal(2, result.FoldersVisited); // „innen“ ist nicht erreichbar
        }
        finally { RestoreAccess(locked); }
    }

    [Fact]
    public async Task Nicht_lesbare_Rechte_aber_auflistbar_Kinder_werden_gespeichert()
    {
        using var tmp = new TempFolder();
        var locked = tmp.Sub("ohne-leserecht");
        tmp.Sub("ohne-leserecht", "kind", "enkel");
        try
        {
            // Auflisten erlaubt, Rechte lesen nicht (BESITZERRECHTE nehmen dem Besitzer das implizite Leserecht).
            SetRawDacl(locked, $"D:P(A;;WD;;;{OwnerRights})(A;;0x1000a1;;;{CurrentUserSid})");

            var result = await PrivilegeScope.WithoutBackupPrivilegeAsync(() => Scan(tmp.Path));

            var node = Assert.Single(result.Folders, f => f.Path == locked);
            Assert.Equal("Berechtigungen nicht lesbar: Zugriff verweigert", node.Error);
            Assert.Empty(node.Aces);
            // Die Rechte des Kindes lassen sich nicht vom (unbekannten) Elternordner ableiten – also speichern.
            var child = Assert.Single(result.Folders, f => f.Path == P(tmp, "ohne-leserecht", "kind"));
            Assert.Null(child.Error);
            Assert.NotEmpty(child.Aces);
            Assert.DoesNotContain(result.Folders, f => f.Path == P(tmp, "ohne-leserecht", "kind", "enkel"));
            Assert.Equal(4, result.FoldersVisited);
            Assert.Single(result.Errors);
        }
        finally { RestoreAccess(locked); }
    }

    [AdminFact]
    public async Task Mit_Sicherungsrecht_wird_auch_ein_gesperrter_Ordner_geprueft()
    {
        using var tmp = new TempFolder();
        var locked = tmp.Sub("gesperrt");
        tmp.Sub("gesperrt", "innen");
        Grant(P(tmp, "gesperrt", "innen"), NetworkService, FileSystemRights.Modify);
        try
        {
            SetRawDacl(locked, "D:P(A;OICI;FA;;;SY)");

            var result = await PrivilegeScope.WithBackupPrivilegeAsync(() => Scan(tmp.Path));

            Assert.Empty(result.Errors);
            Assert.Contains(result.Folders, f => f.Path == P(tmp, "gesperrt", "innen") && f.Aces.Any(a => a.Sid == NetworkService));
            Assert.Equal(3, result.FoldersVisited);
        }
        finally { RestoreAccess(locked); }
    }

    [Fact]
    public async Task NT4_Baum_wird_nicht_komplett_gespeichert()
    {
        // Unter einem Elternordner ohne „automatisch geerbt“ (wie %TEMP%) tragen neue Ordner keine Geerbt-Kennzeichen.
        // Ohne Umrechnung sähe jeder Ordner explizit aus – alles würde gespeichert, Befunde je Unterordner wiederholt.
        using var tmp = new TempFolder();
        var top = tmp.Sub("nt4");
        SetRawDacl(top, $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1301bf;;;{NetworkService})");
        tmp.Sub("nt4", "a", "b", "c");
        tmp.Sub("nt4", "d");

        var result = await Scan(top);

        Assert.Equal([top], result.Folders.Select(f => f.Path));
        Assert.Equal(5, result.FoldersVisited);
    }

    [Fact]
    public async Task Verschobener_Ordner_mit_alter_Vererbung_wird_gespeichert()
    {
        // Verschieben auf demselben Laufwerk behält die alten geerbten Einträge – Netzwerkdienst darf in „B\m“
        // weiter ändern, obwohl „B“ das nicht vergibt. Ohne Speichern würde die Auswertung das übersehen.
        using var tmp = new TempFolder();
        tmp.Sub("A", "m");
        tmp.Sub("B");
        Grant(P(tmp, "A"), NetworkService, FileSystemRights.Modify);
        Directory.Move(P(tmp, "A", "m"), P(tmp, "B", "m"));

        var result = await Scan(tmp.Path);

        var moved = Assert.Single(result.Folders, f => f.Path == P(tmp, "B", "m"));
        Assert.False(moved.HasExplicitAces);
        Assert.False(moved.InheritanceBroken);
        Assert.Contains(moved.Aces, a => a.Sid == NetworkService && a.IsInherited);
        Assert.DoesNotContain(result.Folders, f => f.Path == P(tmp, "B"));
    }

    [Fact]
    public async Task Eintrag_nur_dieser_Ordner_Unterordner_werden_gespeichert()
    {
        using var tmp = new TempFolder();
        tmp.Sub("P", "c", "g");
        Grant(P(tmp, "P"), NetworkService, FileSystemRights.ReadAndExecute, InheritanceFlags.None);

        var result = await Scan(tmp.Path);

        Assert.Equal([tmp.Path, P(tmp, "P"), P(tmp, "P", "c")], result.Folders.Select(f => f.Path));
        Assert.DoesNotContain(result.Folders[2].Aces, a => a.Sid == NetworkService);
    }

    [Fact]
    public async Task Ersteller_Besitzer_ergibt_eigenen_Eintrag_und_wird_gespeichert()
    {
        using var tmp = new TempFolder();
        var parent = tmp.Sub("P");
        SetRawDacl(parent, $"D:P(A;OICI;FA;;;SY)(A;OICIIO;GA;;;CO)(A;OICI;0x1301bf;;;{CurrentUserSid})");
        var child = tmp.Sub("P", "c");
        tmp.Sub("P", "c", "g");

        var result = await Scan(tmp.Path);

        var node = Assert.Single(result.Folders, f => f.Path == child);
        Assert.False(node.HasExplicitAces);
        // Windows ersetzt ERSTELLER-BESITZER durch den Besitzer des neuen Ordners.
        Assert.Contains(node.Aces, a => a.Sid == node.OwnerSid && a.IsInherited && a.Level == AccessLevel.Vollzugriff);
        Assert.DoesNotContain(result.Folders, f => f.Path == Path.Combine(child, "g"));
    }

    [Fact]
    public async Task Verschachtelte_und_doppelte_Ziele_nur_einmal()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);

        var result = await Scan(tmp.Path, P(tmp, "a"), tmp.Path + "\\", P(tmp, "c").ToUpperInvariant());

        var paths = result.Folders.Select(f => f.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(8, result.FoldersVisited);
        Assert.Equal(0, result.Folders.Single(f => f.Path == P(tmp, "a")).Depth);
        // „c“ ist ein eigenes Ziel: Wurzel mit Tiefe 0, „e“ zählt von dort aus.
        Assert.Equal(0, result.Folders.Single(f => f.Path.Equals(P(tmp, "c"), StringComparison.OrdinalIgnoreCase)).Depth);
        Assert.Equal(2, result.Folders.Single(f => f.Path.EndsWith(@"\e", StringComparison.OrdinalIgnoreCase)).Depth);
    }

    [Fact]
    public async Task Ungueltige_und_fehlende_Ziele_werden_Fehler()
    {
        using var tmp = new TempFolder();
        var missing = P(tmp, "gibt-es-nicht");

        var result = await Scan("  ", missing, tmp.Path);

        Assert.Contains(new ScanError("  ", "Ungültiges Ziel: Leeres Ziel"), result.Errors);
        var node = Assert.Single(result.Folders, f => f.Path == missing);
        Assert.Equal("Ziel nicht erreichbar: Pfad nicht gefunden", node.Error);
        Assert.Contains(new ScanError(missing, node.Error!), result.Errors);
        Assert.Contains(result.Folders, f => f.Path == tmp.Path && f.Error is null);
    }

    [Fact]
    public async Task Datei_als_Ziel()
    {
        using var tmp = new TempFolder();
        var file = P(tmp, "einzeln.txt");
        WriteFile(file);

        var result = await Scan(file);

        var node = Assert.Single(result.Folders);
        Assert.True(node.IsFile);
        Assert.Equal(0, node.Depth);
        Assert.Equal(1, result.FilesVisited);
        Assert.Equal(0, result.FoldersVisited);
    }

    [Fact]
    public async Task Nicht_erreichbarer_Server_bricht_nicht_ab()
    {
        var sw = Stopwatch.StartNew();
        var result = await Scan(@"\\zt-test-gibt-es-nicht.invalid\freigabe", @"\\zt-test-gibt-es-nicht.invalid");

        var root = Assert.Single(result.Folders);
        Assert.Equal(@"\\zt-test-gibt-es-nicht.invalid\freigabe", root.Path);
        Assert.StartsWith("Ziel nicht erreichbar: ", root.Error);
        Assert.Null(root.ShareUnc);
        Assert.Empty(result.Shares);
        Assert.Contains(result.Errors, e => e.Path == @"\\zt-test-gibt-es-nicht.invalid" && e.Message.StartsWith("Freigaben von „zt-test-gibt-es-nicht.invalid“ nicht lesbar: "));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"Dauer {sw.Elapsed}");
    }

    [Fact]
    public async Task Fehlbedienung_wirft_ArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Plain().RunAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => Plain().RunAsync(new ScanRequest { Targets = [] }));
        await Assert.ThrowsAsync<ArgumentException>(() => Plain().RunAsync(new ScanRequest { Targets = ["C:\\"], MaxDepth = -1 }));
        Assert.Throws<ArgumentNullException>(() => new Scanner((Zutritt.Core.Identity.IdentityResolver)null!));
    }

    [Fact]
    public async Task Abbruch_vor_dem_Start()
    {
        using var tmp = new TempFolder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Plain().RunAsync(new ScanRequest { Targets = [tmp.Path] }, ct: cts.Token));
    }

    [Fact]
    public async Task Abbruch_waehrend_des_Laufs()
    {
        using var tmp = new TempFolder();
        for (int i = 0; i < 30; i++) tmp.Sub($"o{i:00}", "x");
        using var cts = new CancellationTokenSource();
        long visitedAtCancel = -1;
        var progress = new SyncProgress(p =>
        {
            if (p.Phase == "Ordner" && p.FoldersVisited >= 3 && visitedAtCancel < 0)
            {
                visitedAtCancel = p.FoldersVisited;
                cts.Cancel();
            }
        });
        // Jede Meldung durchlassen: Gedrosselt käme bei einem kleinen Baum evtl. keine mitten im Lauf.
        bool resolved = false;
        var scanner = new Scanner((_, _) => resolved = true, _ => []) { ProgressIntervalMs = 0 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.RunAsync(new ScanRequest { Targets = [tmp.Path] }, progress, cts.Token));
        Assert.Equal(3, visitedAtCancel);
        Assert.False(resolved);
        // Nach dem Abbruch keine weiteren Ordner mehr.
        Assert.Equal(3, progress.Items.Max(i => i.Progress.FoldersVisited));
    }

    [Fact]
    public async Task Fortschritt_hoechstens_zehnmal_pro_Sekunde_und_Phasen_in_Reihenfolge()
    {
        using var tmp = new TempFolder();
        for (int i = 0; i < 40; i++)
            for (int j = 0; j < 40; j++)
                Directory.CreateDirectory(Path.Combine(tmp.Path, $"o{i:00}", $"u{j:00}"));
        var progress = new SyncProgress();

        var result = await Plain().RunAsync(new ScanRequest { Targets = [tmp.Path] }, progress);

        var phases = progress.Items.Select(i => i.Progress.Phase).Distinct().ToList();
        Assert.Equal(["Ziele", "Ordner", "Konten", "Befunde", "Fertig"], phases);
        var folderReports = progress.Items.Where(i => i.Progress.Phase == "Ordner").ToList();
        for (int k = 1; k < folderReports.Count; k++)
            Assert.True(folderReports[k].Ms - folderReports[k - 1].Ms >= 90, $"Abstand {folderReports[k].Ms - folderReports[k - 1].Ms} ms");
        var last = progress.Items[^1].Progress;
        Assert.Equal("Fertig", last.Phase);
        Assert.Equal(result.FoldersVisited, last.FoldersVisited);
        Assert.Equal(result.Folders.Count, last.FoldersStored);
        Assert.Equal(1 + 40 + 40 * 40, result.FoldersVisited);
    }

    [Fact]
    public async Task Kopfdaten_und_Reihenfolge_der_Schritte()
    {
        using var tmp = new TempFolder();
        BuildTree(tmp);
        var calls = new List<string>();
        var finding = new Finding { RuleId = "ZT99", Severity = Severity.Info, Path = tmp.Path, Message = "Test" };
        var scanner = new Scanner(
            (r, ct) => { calls.Add($"resolve:{r.Folders.Count}"); r.Principals["S-1-5-18"] = new Principal { Sid = "S-1-5-18" }; },
            r => { calls.Add($"evaluate:{r.Principals.Count}"); return [finding]; });
        var request = new ScanRequest { Targets = [tmp.Path] };
        var before = DateTimeOffset.Now;

        var result = await scanner.RunAsync(request);

        Assert.Equal(["resolve:4", "evaluate:1"], calls);
        Assert.Equal([finding], result.Findings);
        Assert.Same(request, result.Request);
        Assert.Equal(Environment.MachineName, result.Machine);
        Assert.Equal(WindowsIdentity.GetCurrent().Name, result.ScannedBy);
        Assert.False(string.IsNullOrWhiteSpace(result.ToolVersion));
        Assert.True(result.StartedAt >= before.AddSeconds(-1));
        Assert.True(result.FinishedAt >= result.StartedAt);
        Assert.Equal(0, result.Id);
    }

    [Fact]
    public async Task Tiefer_Baum_ohne_Rekursion_und_mit_langen_Pfaden()
    {
        using var tmp = new TempFolder();
        var parts = Enumerable.Repeat("ebene", 120).ToArray();
        var deepest = tmp.Sub(parts);
        Assert.True(deepest.Length > 700);
        SetRawDacl(deepest, $"D:P(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1301bf;;;{NetworkService})");

        var result = await Scan(tmp.Path);

        var node = Assert.Single(result.Folders, f => f.Depth == 120);
        Assert.Equal(deepest, node.Path);
        Assert.Null(node.Error);
        Assert.Contains(node.Aces, a => a.Sid == NetworkService && !a.IsInherited);
        Assert.Equal(121, result.FoldersVisited);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Leerzeichen_am_Namensende_im_Baum()
    {
        using var tmp = new TempFolder();
        tmp.Sub("x");
        var spaced = P(tmp, "x ");
        Directory.CreateDirectory(@"\\?\" + spaced);
        try
        {
            SetRawDacl(spaced, $"D:P(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1200a9;;;{NetworkService})");

            var result = await Scan(tmp.Path);

            Assert.Equal([tmp.Path, spaced], result.Folders.Select(f => f.Path));
            Assert.Contains(result.Folders[1].Aces, a => a.Sid == NetworkService);
            Assert.Equal(3, result.FoldersVisited);
        }
        finally { Directory.Delete(@"\\?\" + spaced); }
    }

    [Fact]
    public async Task Lokale_Pfade_bekommen_die_Freigabe_mit_laengstem_Praefix()
    {
        // Rechnerabhängig: Erwartung wird aus den vorhandenen Freigaben berechnet (z. B. „c“ auf C:\).
        using var tmp = new TempFolder();
        tmp.Sub("a");
        var shares = ShareEnumerator.GetShares(".").Where(s => !s.IsAdminShare && s.LocalPath.Length > 0).ToList();
        var expected = shares
            .Where(s => LongPath.IsSameOrBelow(tmp.Path, LongPath.TrimEndSeparator(s.LocalPath)))
            .OrderByDescending(s => LongPath.TrimEndSeparator(s.LocalPath).Length)
            .FirstOrDefault();

        var result = await Scan(tmp.Path);

        Assert.Equal(expected?.UncPath, result.Folders[0].ShareUnc);
        if (expected is null) Assert.Empty(result.Shares);
        else Assert.Contains(result.Shares, s => s.UncPath == expected.UncPath);
    }

    /// <summary>Meldet synchron im Scan-Thread (Progress&lt;T&gt; würde über den Thread-Pool posten).</summary>
    private sealed class SyncProgress(Action<ScanProgress>? onReport = null) : IProgress<ScanProgress>
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        public List<(long Ms, ScanProgress Progress)> Items { get; } = [];

        public void Report(ScanProgress value)
        {
            Items.Add((_clock.ElapsedMilliseconds, value));
            onReport?.Invoke(value);
        }
    }
}
