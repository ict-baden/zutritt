using System.Diagnostics;
using Zutritt.Core.Model;
using Zutritt.Core.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

public sealed class ScanStoreFileTests
{
    [Fact]
    public void Neue_Datei_bekommt_Schema_Version_Kennung_WAL_und_Fremdschluessel()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var c = store.Connection;

        Assert.Equal(Schema.CurrentVersion, TestDb.Scalar(c, "PRAGMA user_version;"));
        Assert.Equal(2, Schema.CurrentVersion);
        Assert.Equal(0x5A545254, TestDb.Scalar(c, "PRAGMA application_id;"));
        Assert.Equal("wal", TestDb.Text(c, "PRAGMA journal_mode;"));
        Assert.Equal(1, TestDb.Scalar(c, "PRAGMA foreign_keys;"));
        Assert.Equal(2, TestDb.Scalar(c, "PRAGMA auto_vacuum;"));  // INCREMENTAL
        Assert.Equal(db.File, store.DatabasePath);

        string[] tables = ["scan", "share", "share_ace", "folder", "ace", "principal", "member", "finding", "error"];
        foreach (var t in tables)
            Assert.Equal(1, TestDb.Scalar(c, $"SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = '{t}';"));
        string[] indexes = ["ix_scan_started", "ix_scan_targets", "ix_share_scan", "ix_folder_scan", "ix_folder_path"];
        foreach (var i in indexes)
            Assert.Equal(1, TestDb.Scalar(c, $"SELECT count(*) FROM sqlite_schema WHERE type = 'index' AND name = '{i}';"));
    }

    [Fact]
    public void Fremdschluessel_sind_wirksam()
    {
        using var db = new TestDb();
        using var store = db.Open();

        var ex = Assert.ThrowsAny<Exception>(() => TestDb.Execute(store.Connection, "INSERT INTO ace (folder_id, ord, sid, rights, is_deny, is_inherited, inheritance, propagation) VALUES (999, 0, 'S-1-1-0', 1, 0, 0, 0, 0);"));
        Assert.Contains("FOREIGN KEY", ex.Message);
    }

    [Fact]
    public void Laden_nutzt_die_Indizes()
    {
        using var db = new TestDb();
        using var store = db.Open();
        store.Save(SampleScan.Create());

        // Bei 100 Läufen à 20.000 Ordner wäre ein vollständiger Tabellendurchlauf je Laden spürbar
        var folderPlan = Plan(store, "SELECT id FROM folder WHERE scan_id = 1 ORDER BY ord;");
        Assert.Contains("ix_folder_scan", folderPlan);
        var acePlan = Plan(store, "SELECT a.sid FROM ace a JOIN folder f ON f.id = a.folder_id WHERE f.scan_id = 1 ORDER BY a.folder_id, a.ord;");
        Assert.Contains("ix_folder_scan", acePlan);
        Assert.DoesNotContain("SCAN a", acePlan);
        var previousPlan = Plan(store, "SELECT id FROM scan WHERE targets_key = 'X' AND started_utc < 5 ORDER BY started_utc DESC LIMIT 1;");
        Assert.Contains("ix_scan_targets", previousPlan);
    }

    [Fact]
    public void Datei_ist_nach_Dispose_loeschbar_und_ohne_WAL_Reste()
    {
        using var db = new TestDb();
        var store = db.Open();
        store.Save(StorageScans.SampleWithFindings());
        store.Load(1);
        store.List();

        store.Dispose();
        store.Dispose();  // doppelt ist harmlos

        Assert.False(File.Exists(db.File + "-wal"), "-wal-Datei nach dem Schließen übrig");
        Assert.False(File.Exists(db.File + "-shm"), "-shm-Datei nach dem Schließen übrig");
        File.Delete(db.File);
        Assert.False(File.Exists(db.File));
    }

    [Fact]
    public void Nach_Dispose_gibt_es_ObjectDisposedException()
    {
        using var db = new TestDb();
        var store = db.Open();
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.Save(SampleScan.Create()));
        Assert.Throws<ObjectDisposedException>(() => store.Load(1));
        Assert.Throws<ObjectDisposedException>(() => store.List());
        Assert.Throws<ObjectDisposedException>(() => store.FindPrevious(SampleScan.Create()));
        Assert.Throws<ObjectDisposedException>(() => store.Delete(1));
    }

    [Fact]
    public void Ordner_der_Datenbank_wird_angelegt()
    {
        using var db = new TestDb();
        var path = Path.Combine(db.Folder, "a", "b", "c", "zutritt.db");

        using (var store = new ScanStore(path)) store.Save(SampleScan.Create());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Relativer_Pfad_wird_zum_vollen_Pfad()
    {
        using var db = new TestDb();
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, db.File);

        using var store = new ScanStore(relative);

        Assert.Equal(db.File, store.DatabasePath);
    }

    [Fact]
    public void Leerer_Pfad_ist_ein_Argumentfehler()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ScanStore(""));
        Assert.ThrowsAny<ArgumentException>(() => new ScanStore("  "));
        Assert.ThrowsAny<ArgumentNullException>(() => new ScanStore(null!));
    }

    [Fact]
    public void Keine_SQLite_Datei_wird_abgelehnt_und_nicht_veraendert()
    {
        using var db = new TestDb();
        const string content = "Das ist ein Bericht, keine Datenbank. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor.";
        File.WriteAllText(db.File, content);

        var ex = Assert.Throws<ScanStoreException>(db.Open);

        Assert.Contains("keine SQLite-Datenbank", ex.Message);
        Assert.Contains(db.File, ex.Message);
        Assert.Equal(content, File.ReadAllText(db.File));
        File.Delete(db.File);  // nicht gesperrt
    }

    [Fact]
    public void Fremde_SQLite_Datenbank_wird_abgelehnt_und_nicht_veraendert()
    {
        using var db = new TestDb();
        using (var raw = TestDb.Raw(db.File))
        {
            TestDb.Execute(raw, "CREATE TABLE kunden (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO kunden (name) VALUES ('Muster');");
        }

        var ex = Assert.Throws<ScanStoreException>(db.Open);

        Assert.Contains("keine Zutritt-Datenbank", ex.Message);
        using (var raw = TestDb.Raw(db.File))
        {
            Assert.Equal(0, TestDb.Scalar(raw, "SELECT count(*) FROM sqlite_schema WHERE name = 'scan';"));
            Assert.Equal(0, TestDb.Scalar(raw, "PRAGMA user_version;"));
            Assert.Equal("delete", TestDb.Text(raw, "PRAGMA journal_mode;"));
            Assert.Equal(1, TestDb.Scalar(raw, "SELECT count(*) FROM kunden;"));
        }
        File.Delete(db.File);
    }

    [Fact]
    public void Datenbank_einer_neueren_Version_wird_abgelehnt()
    {
        using var db = new TestDb();
        using (var store = db.Open()) store.Save(SampleScan.Create());
        using (var raw = TestDb.Raw(db.File)) TestDb.Execute(raw, "PRAGMA user_version = 99;");

        var ex = Assert.Throws<ScanStoreException>(db.Open);

        Assert.Contains("neueren Zutritt-Version", ex.Message);
        Assert.Contains("99", ex.Message);
        using (var raw = TestDb.Raw(db.File)) Assert.Equal(99, TestDb.Scalar(raw, "PRAGMA user_version;"));
        File.Delete(db.File);
    }

    [Fact]
    public void Datenbank_mit_Schema_1_wird_migriert_und_alte_Laeufe_bleiben_lesbar()
    {
        using var db = new TestDb();
        var old = SampleScan.Create();
        using (var store = db.Open()) store.Save(old);
        // Stand von Schema 1 nachbauen: ohne die Spalte für „Vererbung weicht ab“
        using (var raw = TestDb.Raw(db.File))
        {
            TestDb.Execute(raw, "ALTER TABLE folder DROP COLUMN inheritance_differs; PRAGMA user_version = 1;");
            Assert.Equal(0, TestDb.Scalar(raw, "SELECT count(*) FROM pragma_table_info('folder') WHERE name = 'inheritance_differs';"));
        }

        using (var store = db.Open())
        {
            Assert.Equal(2, TestDb.Scalar(store.Connection, "PRAGMA user_version;"));
            var loaded = store.Load(old.Id)!;
            Assert.All(loaded.Folders, f => Assert.False(f.InheritanceDiffers));
            Assert.Equal(old.Folders.Count, loaded.Folders.Count);

            // Neue Läufe speichern das Kennzeichen
            var scan = SampleScan.Create();
            scan.Folders[1].InheritanceDiffers = true;
            var id = store.Save(scan);
            Assert.Equal([false, true], store.Load(id)!.Folders.Take(2).Select(f => f.InheritanceDiffers));
            Assert.Equal(2, store.List().Count);
        }
        File.Delete(db.File);
    }

    [Fact]
    public void Leere_Datei_wird_als_neue_Datenbank_eingerichtet()
    {
        using var db = new TestDb();
        File.WriteAllBytes(db.File, []);

        using var store = db.Open();

        Assert.Equal(Schema.CurrentVersion, TestDb.Scalar(store.Connection, "PRAGMA user_version;"));
        Assert.True(store.Save(SampleScan.Create()) > 0);
    }

    [Fact]
    public void Fehlgeschlagenes_Speichern_hinterlaesst_nichts_und_aendert_den_Lauf_nicht()
    {
        using var db = new TestDb();
        using var store = db.Open();
        // Fehler erst bei der letzten Tabelle: alles davor muss mit zurückgerollt werden
        TestDb.Execute(store.Connection, "CREATE TRIGGER zt_test_fehler BEFORE INSERT ON error BEGIN SELECT RAISE(ABORT, 'Testfehler'); END;");
        var scan = SampleScan.Create();

        var ex = Assert.Throws<ScanStoreException>(() => store.Save(scan));

        Assert.StartsWith("Der Lauf konnte nicht gespeichert werden.", ex.Message);
        Assert.Equal(0, scan.Id);
        Assert.All(scan.Folders, f => Assert.Equal(0, f.Id));
        foreach (var t in new[] { "scan", "share", "share_ace", "folder", "ace", "principal", "member", "finding", "error" })
            Assert.Equal(0, TestDb.Scalar(store.Connection, $"SELECT count(*) FROM {t};"));

        TestDb.Execute(store.Connection, "DROP TRIGGER zt_test_fehler;");
        Assert.True(store.Save(scan) > 0);
    }

    [Fact]
    public async Task Gleichzeitige_Zugriffe_aus_mehreren_Threads()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var tasks = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            var ids = new List<long>();
            for (var i = 0; i < 5; i++)
            {
                var scan = StorageScans.SampleWithFindings(t0.AddMinutes(w * 10 + i));
                ids.Add(store.Save(scan));
                DeepAssert.Equal(scan, store.Load(scan.Id));
                store.List();
                store.FindPrevious(scan);
            }
            return ids;
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        var all = results.SelectMany(ids => ids).ToList();
        Assert.Equal(20, all.Distinct().Count());
        Assert.Equal(20, store.List().Count);
    }

    [Fact]
    public void Zwei_Instanzen_auf_derselben_Datei_sehen_sich_gegenseitig()
    {
        using var db = new TestDb();
        using var a = db.Open();
        using var b = db.Open();

        var scan = StorageScans.Edge();
        var id = a.Save(scan);
        DeepAssert.Equal(scan, b.Load(id));

        var later = SampleScan.Create(scan.StartedAt.AddDays(1));
        later.Request = later.Request with { Targets = scan.Request.Targets };
        later.Machine = scan.Machine;
        b.Save(later);
        Assert.Equal(id, a.FindPrevious(later)?.Id);

        a.Delete(id);
        Assert.Null(b.Load(id));
        Assert.Single(b.List());
    }

    [WindowsFact("Dateisperren gelten nur unter Windows verbindlich")]
    public void Gesperrte_Datei_meldet_sich_auf_Deutsch()
    {
        using var db = new TestDb();
        using (var store = db.Open()) store.Save(SampleScan.Create());
        // Ein anderer Prozess hält die Datei exklusiv offen (z. B. Virenscanner, Sicherung)
        using var blocker = new FileStream(db.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<ScanStoreException>(db.Open);

        Assert.Contains(db.File, ex.Message);
        Assert.Contains("gesperrt", ex.Message);
        Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(ex.InnerException);
    }

    [Fact]
    public void Dispose_wartet_nicht_auf_die_Schreibsperre_eines_anderen_Prozesses()
    {
        using var db = new TestDb();
        var store = db.Open();
        // Genug Daten und Abfragen, dass PRAGMA optimize beim Schließen analysieren will und dafür schreiben muss
        store.Save(StorageScans.Large(folders: 2_000, acesPerFolder: 5));
        store.Load(1);
        store.List();
        store.FindPrevious(SampleScan.Create());

        using var other = TestDb.Raw(db.File);
        TestDb.Execute(other, "BEGIN IMMEDIATE; INSERT INTO error (scan_id, ord, path, message) VALUES (1, 999, 'x', 'y');");
        var sw = Stopwatch.StartNew();
        store.Dispose();
        sw.Stop();
        TestDb.Execute(other, "ROLLBACK;");

        // Vorher 30 s (Standard-Timeout); jetzt etwa 1 s – das Budget lässt Luft für einen ausgelasteten Rechner
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Dispose dauerte {sw.Elapsed}");
        Assert.Equal(1, TestDb.Scalar(other, "SELECT count(*) FROM scan;"));
    }

    [Fact]
    public void Netzpfade_werden_erkannt()
    {
        Assert.True(DatabaseLocation.IsNetworkPath(@"\\server\freigabe\zutritt.db"));
        Assert.True(DatabaseLocation.IsNetworkPath(@"\\?\UNC\server\freigabe\zutritt.db"));
        Assert.True(DatabaseLocation.IsNetworkPath(@"\\localhost\c$\zutritt.db"));
        if (OperatingSystem.IsWindows())
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;
            Assert.False(DatabaseLocation.IsNetworkPath(Path.Combine(systemDrive, "Daten", "zutritt.db")));
            Assert.False(DatabaseLocation.IsNetworkPath(@"\\?\" + Path.Combine(systemDrive, "Daten", "zutritt.db")));
        }
    }

    private static string JournalMode(ScanStore store) => TestDb.Text(store.Connection, "PRAGMA journal_mode;");

    private static string Plan(ScanStore store, string sql)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var r = cmd.ExecuteReader();
        var lines = new List<string>();
        while (r.Read()) lines.Add(r.GetString(3));
        return string.Join(" | ", lines);
    }
}
