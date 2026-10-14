using System.Diagnostics;
using System.Security.AccessControl;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Scanning.FsTestHelper;

namespace Zutritt.Tests.Scanning;

/// <summary>
/// <see cref="FolderNode.InheritanceDiffers"/> auf echten Ordnerbäumen: gesetzt bei verschobenen Ordnern und Dateien,
/// die ihre alten geerbten Rechte mitgenommen haben – nie bei normaler Vererbung (auch nicht bei den Fällen, in denen
/// sich die Rechte des Kindes ganz regulär von denen des Elternordners unterscheiden) und nie, wenn der Elternordner
/// nicht lesbar war. Darauf baut Regel ZT14.
/// </summary>
public sealed class InheritanceDiffersTests
{
    private static Scanner Plain() => new((_, _) => { }, _ => []);

    private static Task<ScanResult> Scan(string target, bool files = false) =>
        Plain().RunAsync(new ScanRequest { Targets = [target], IncludeFiles = files });

    private static string P(TempFolder tmp, params string[] parts) => Path.Combine([tmp.Path, .. parts]);

    private static IEnumerable<string> Differing(ScanResult result) => result.Folders.Where(f => f.InheritanceDiffers).Select(f => f.Path);

    [Fact]
    public async Task Mit_Move_Item_verschobener_Ordner_behaelt_alte_Rechte_und_ergibt_ZT14()
    {
        // „A“ gibt Netzwerkdienst „Ändern“, „B“ nicht. Move-Item innerhalb eines Laufwerks nimmt die geerbten Einträge
        // mit – in „B\Projekt“ darf Netzwerkdienst weiter ändern. Die Unterordner sind mitgewandert und erben von
        // „Projekt“ ganz normal: Abweichend ist nur der verschobene Ordner selbst.
        using var tmp = new TempFolder();
        tmp.Sub("A", "Projekt", "Unterordner");
        tmp.Sub("B");
        Grant(P(tmp, "A"), NetworkService, FileSystemRights.Modify);
        MoveItem(P(tmp, "A", "Projekt"), P(tmp, "B", "Projekt"));

        var result = await Scan(tmp.Path);

        Assert.Equal([P(tmp, "B", "Projekt")], Differing(result));
        var moved = result.Folders.Single(f => f.InheritanceDiffers);
        Assert.False(moved.HasExplicitAces);
        Assert.Contains(moved.Aces, a => a.Sid == NetworkService && a.IsInherited);

        var finding = Assert.Single(FindingRules.Evaluate(result), f => f.RuleId == "ZT14");
        Assert.Equal(Severity.Niedrig, finding.Severity);
        Assert.Equal(P(tmp, "B", "Projekt"), finding.Path);
        Assert.Null(finding.Sid);
        Assert.Contains("vermutlich verschoben", finding.Message);
        Assert.Contains(@"„NT-AUTORITÄT\NETZWERKDIENST“ kommt nicht von dort", finding.Message);
    }

    [Fact]
    public async Task Verschobener_Ordner_mit_gleichen_Rechten_aber_anderer_Weitergabe()
    {
        using var tmp = new TempFolder();
        tmp.Sub("A");
        tmp.Sub("X", "m", "g");
        Grant(P(tmp, "A"), NetworkService, FileSystemRights.Modify, InheritanceFlags.ContainerInherit, PropagationFlags.NoPropagateInherit);
        Grant(P(tmp, "X"), NetworkService, FileSystemRights.Modify, InheritanceFlags.ContainerInherit);
        Directory.Move(P(tmp, "X", "m"), P(tmp, "A", "m"));

        var result = await Scan(tmp.Path);

        Assert.Equal([P(tmp, "A", "m")], Differing(result));
    }

    [Fact]
    public async Task Verschobener_Ordner_dem_am_neuen_Ort_ein_Recht_fehlt()
    {
        // Umgekehrt: „B“ vergibt Hauptbenutzer „Lesen“, der aus „A“ gekommene Ordner hat es nicht. Erst die Wurzel auf
        // „automatisch geerbt“ bringen (wie auf Dateiservern üblich) – unter %TEMP% hätte „m“ sonst eine Liste im NT4-Stil,
        // die Windows beim Lesen als „Vererbung unterbrochen“ deutet.
        using var tmp = new TempFolder();
        Grant(tmp.Path, SystemSid, FileSystemRights.FullControl);
        tmp.Sub("A", "m");
        tmp.Sub("B");
        Grant(P(tmp, "B"), PowerUsers, FileSystemRights.ReadAndExecute);
        Directory.Move(P(tmp, "A", "m"), P(tmp, "B", "m"));

        var result = await Scan(tmp.Path);

        Assert.Equal([P(tmp, "B", "m")], Differing(result));
        // Kein fremdes Konto – dann die allgemeine Meldung
        Assert.Contains("es gelten womöglich noch Rechte vom früheren Ort", FindingRules.Evaluate(result).Single(f => f.RuleId == "ZT14").Message);
    }

    [Fact]
    public async Task Regulaere_Unterschiede_zum_Elternordner_sind_keine_Abweichung()
    {
        // Wie Windows vererbt: generische Rechte werden aufgeteilt, „nur Unterordner“, „nur Dateien“, „eine Ebene“,
        // „nur vererben“ und „nur dieser Ordner“ – die Kinder werden gespeichert, weichen aber nicht ab.
        using var tmp = new TempFolder();
        var top = tmp.Sub("P");
        SetExplicitAces(top, $"D:(A;OICI;FA;;;{CurrentUserSid})(A;CI;0x1200a9;;;{NetworkService})(A;OI;0x1301bf;;;{PowerUsers})"
            + $"(A;CINP;0x1301bf;;;{BackupOperators})(A;OICI;GA;;;LS)(A;CIIO;0x1301bf;;;IU)(A;;0x1200a9;;;AU)");
        tmp.Sub("P", "c1", "c2", "c3");

        var result = await Scan(tmp.Path);

        Assert.Contains(result.Folders, f => f.Path == P(tmp, "P", "c1"));
        Assert.Empty(Differing(result));
        Assert.DoesNotContain(FindingRules.Evaluate(result), f => f.RuleId == "ZT14");
    }

    [Fact]
    public async Task Ersteller_Besitzer_ist_keine_Abweichung()
    {
        using var tmp = new TempFolder();
        var parent = tmp.Sub("P");
        SetRawDacl(parent, $"D:P(A;OICI;FA;;;SY)(A;OICIIO;GA;;;CO)(A;OICI;0x1301bf;;;{CurrentUserSid})");
        tmp.Sub("P", "c", "g");
        WriteFile(Path.Combine(parent, "c", "neu.txt"));

        var result = await Scan(tmp.Path, files: true);

        Assert.Contains(result.Folders, f => f.Path == P(tmp, "P", "c"));
        Assert.Empty(Differing(result));
    }

    [Fact]
    public async Task Kinder_eines_Ordners_mit_unlesbaren_Rechten_gelten_nicht_als_abweichend()
    {
        using var tmp = new TempFolder();
        var locked = tmp.Sub("ohne-leserecht");
        tmp.Sub("ohne-leserecht", "kind");
        try
        {
            SetRawDacl(locked, $"D:P(A;;WD;;;{OwnerRights})(A;;0x1000a1;;;{CurrentUserSid})");

            var result = await PrivilegeScope.WithoutBackupPrivilegeAsync(() => Scan(tmp.Path));

            // Gespeichert (nicht ableitbar), aber ohne Aussage über die Vererbung
            Assert.Contains(result.Folders, f => f.Path == P(tmp, "ohne-leserecht", "kind"));
            Assert.Empty(Differing(result));
            Assert.DoesNotContain(FindingRules.Evaluate(result), f => f.RuleId == "ZT14");
        }
        finally { RestoreAccess(locked); }
    }

    [Fact]
    public async Task Unterbrochene_Vererbung_und_Wurzel_sind_nie_abweichend()
    {
        using var tmp = new TempFolder();
        tmp.Sub("A", "m");
        tmp.Sub("B");
        Grant(P(tmp, "A"), NetworkService, FileSystemRights.Modify);
        Directory.Move(P(tmp, "A", "m"), P(tmp, "B", "m"));
        TempFolder.BreakInheritance(P(tmp, "B", "m"));

        var result = await Scan(tmp.Path);
        Assert.Empty(Differing(result));

        // Als Wurzel ist der Elternordner nicht Teil des Laufs – keine Aussage
        var asRoot = await Scan(P(tmp, "A"));
        Assert.Empty(Differing(asRoot));
    }

    [Fact]
    public async Task Verschobene_Datei_ist_abweichend_normale_Dateien_nicht()
    {
        using var tmp = new TempFolder();
        var open = tmp.Sub("Oeffentlich");
        var hr = tmp.Sub("Personal");
        Grant(open, Everyone, FileSystemRights.ReadAndExecute);
        WriteFile(Path.Combine(open, "gehalt.xlsx"));
        File.Move(Path.Combine(open, "gehalt.xlsx"), Path.Combine(hr, "gehalt.xlsx"));
        WriteFile(Path.Combine(hr, "normal.txt"));
        WriteFile(Path.Combine(open, "normal.txt"));
        WriteFile(Path.Combine(hr, "eigen.txt"));
        GrantFile(Path.Combine(hr, "eigen.txt"), NetworkService, FileSystemRights.Read);

        var result = await Scan(tmp.Path, files: true);

        Assert.Equal([Path.Combine(hr, "gehalt.xlsx")], Differing(result));
        // Eigener Eintrag, aber richtig geerbt: gespeichert, nicht abweichend
        Assert.False(result.Folders.Single(f => f.Path == Path.Combine(hr, "eigen.txt")).InheritanceDiffers);
        var finding = Assert.Single(FindingRules.Evaluate(result), f => f.RuleId == "ZT14");
        Assert.Contains("„Jeder“ kommt nicht von dort", finding.Message);
    }

    [Fact]
    public async Task NT4_Baum_ist_nicht_abweichend()
    {
        using var tmp = new TempFolder();
        var top = tmp.Sub("nt4");
        SetRawDacl(top, $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1301bf;;;{NetworkService})");
        tmp.Sub("nt4", "a", "b");

        var result = await Scan(top);

        Assert.Empty(Differing(result));
    }

    /// <summary>Wie ein Administrator es tut: PowerShell „Move-Item“ (auf demselben Laufwerk ein MoveFileEx).</summary>
    private static void MoveItem(string from, string to)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Pfade über Umgebungsvariablen: „-Command“ fügt weitere Argumente sonst ungeschützt in den Befehl ein
        psi.Environment["ZT_FROM"] = from;
        psi.Environment["ZT_TO"] = to;
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", "Move-Item -LiteralPath $env:ZT_FROM -Destination $env:ZT_TO -ErrorAction Stop" })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000)) { p.Kill(); throw new TimeoutException("Move-Item hängt"); }
        Assert.True(p.ExitCode == 0 && Directory.Exists(to), $"Move-Item fehlgeschlagen: {output.Result}{error.Result}");
    }
}
