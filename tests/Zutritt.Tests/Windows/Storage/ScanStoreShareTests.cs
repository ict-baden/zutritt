using System.Diagnostics;
using System.Security.Principal;
using Zutritt.Core.Storage;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;
using Zutritt.Tests.Windows.Scanning;

namespace Zutritt.Tests.Windows.Storage;

/// <summary>Datenbank auf einer echten SMB-Freigabe dieses Rechners (braucht Adminrechte, legt die Freigabe an und löscht sie).</summary>
public sealed class ScanStoreShareTests
{
    /// <summary>Echte SMB-Freigabe auf diesem Rechner: Datenbank über \\localhost ohne WAL, Roundtrip vollständig.</summary>
    [AdminFact]
    public void Datenbank_auf_einer_Freigabe_laeuft_ohne_WAL() => WithShare((dir, share) =>
    {
        var unc = $@"\\localhost\{share}\zutritt.db";
        var scan = StorageScans.Edge();
        using (var store = new ScanStore(unc))
        {
            Assert.Equal("delete", JournalMode(store));
            store.Save(scan);
            DeepAssert.Equal(scan, store.Load(scan.Id));
        }

        // Lokal geöffnet bleibt eine geteilte Datei im klassischen Journal – ein anderer Rechner könnte sie offen haben
        var local = Path.Combine(dir, "zutritt.db");
        using (var store = new ScanStore(local))
        {
            Assert.Equal("delete", JournalMode(store));
            DeepAssert.Equal(scan, store.Load(scan.Id));
        }
        File.Delete(local);

        // Lokal (mit WAL) angelegt und später über die Freigabe geöffnet: dauerhaft zurück auf das klassische Journal,
        // ohne dass der gemeinsame Speicher (-shm) je übers Netz benutzt wird
        var moved = Path.Combine(dir, "verschoben.db");
        using (var store = new ScanStore(moved))
        {
            Assert.Equal("wal", JournalMode(store));
            store.Save(scan);
        }
        using (var store = new ScanStore($@"\\localhost\{share}\verschoben.db"))
        {
            Assert.Equal("delete", JournalMode(store));
            Assert.False(File.Exists(moved + "-shm"), "-shm über die Freigabe angelegt");
            Assert.False(File.Exists(moved + "-wal"), "-wal über die Freigabe angelegt");
            DeepAssert.Equal(scan, store.Load(scan.Id));
        }
        using (var store = new ScanStore(moved)) Assert.Equal("delete", JournalMode(store));
        File.Delete(moved);
    });

    /// <summary>
    /// Ist die Datei lokal im WAL-Modus offen, darf ein Zugriff über die Freigabe nicht den gemeinsamen Speicher (-shm)
    /// übers Netz teilen – das kann die Datei beschädigen, über \\localhost hing es sogar. Erwartet: schnelle, deutsche
    /// Ablehnung; nach dem Schließen gelingt es und beide Seiten arbeiten im klassischen Journal auf derselben Datei.
    /// </summary>
    [AdminFact]
    public void Lokal_offene_WAL_Datenbank_wird_ueber_die_Freigabe_abgelehnt() => WithShare((dir, share) =>
    {
        var local = Path.Combine(dir, "geteilt.db");
        var unc = $@"\\localhost\{share}\geteilt.db";
        var scan = StorageScans.SampleWithFindings();

        using (var a = new ScanStore(local))
        {
            a.Save(scan);
            Assert.Equal("wal", JournalMode(a));

            var sw = Stopwatch.StartNew();
            var attempt = Task.Run(() => Record.Exception(() => new ScanStore(unc).Dispose()));
            Assert.True(attempt.Wait(TimeSpan.FromSeconds(60)), "Öffnen über die Freigabe hängt");
            var ex = Assert.IsType<ScanStoreException>(attempt.Result);
            Assert.Contains("WAL-Modus", ex.Message);
            Assert.Contains(unc, ex.Message);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"Ablehnung dauerte {sw.Elapsed}");

            // Die lokale Seite merkt davon nichts
            var second = SampleScan.Create(scan.StartedAt.AddDays(1));
            a.Save(second);
            DeepAssert.Equal(scan, a.Load(scan.Id));
        }

        using (var b = new ScanStore(unc))
        {
            Assert.Equal("delete", JournalMode(b));
            Assert.False(File.Exists(local + "-shm"), "-shm über die Freigabe angelegt");
            Assert.Equal(2, b.List().Count);

            using var a = new ScanStore(local);
            Assert.Equal("delete", JournalMode(a));
            a.Save(SampleScan.Create(scan.StartedAt.AddDays(2)));
            Assert.Equal(3, b.List().Count);
            DeepAssert.Equal(scan, b.Load(scan.Id));
        }
    });

    /// <summary>Freigabe „zt-test-…“ auf einen temporären Ordner; wird im finally immer gelöscht.</summary>
    private static void WithShare(Action<string, string> test)
    {
        using var dir = new TempFolder();
        var share = "zt-test-" + Guid.NewGuid().ToString("N")[..8];
        var me = WindowsIdentity.GetCurrent().Name;
        Net($"share {share}=\"{dir.Path}\" \"/GRANT:{me},FULL\"");
        try
        {
            test(dir.Path, share);
        }
        finally
        {
            Net($"share {share} /delete /y");
        }
    }

    private static string JournalMode(ScanStore store) => TestDb.Text(store.Connection, "PRAGMA journal_mode;");

    private static void Net(string arguments)
    {
        var psi = new ProcessStartInfo("net.exe", arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"net {arguments} → {p.ExitCode}: {output}");
    }
}
