// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;
using Zutritt.Core.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

public sealed class ScanStoreHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(2));

    private static ScanResult Run(DateTimeOffset started, string machine = "ADMIN-PC", params string[] targets)
    {
        var scan = SampleScan.Create(started);
        scan.Machine = machine;
        if (targets.Length > 0) scan.Request = scan.Request with { Targets = targets };
        return scan;
    }

    [Fact]
    public void Lauf_mit_altem_Schluessel_fuer_D_wird_weiter_gefunden()
    {
        // Ältere Ausgaben speicherten „scan D:“ unter dem Schlüssel „D:“, heute heißt er „D:\“ – ein geplanter
        // „scan D: --vergleich“ soll nach dem Update trotzdem mit dem letzten Lauf vergleichen
        using var db = new TestDb();
        using var store = db.Open();
        var old = store.Save(Run(T0, "ADMIN-PC", "D:"));
        TestDb.Execute(store.Connection, $"UPDATE scan SET targets_key = 'D:' WHERE id = {old};");

        Assert.Equal(old, store.FindPrevious(Run(T0.AddDays(7), "ADMIN-PC", "D:"))?.Id);
        Assert.Equal(old, store.FindPrevious(Run(T0.AddDays(7), "ADMIN-PC", @"d:\"))?.Id);
        Assert.Null(store.FindPrevious(Run(T0.AddDays(7), "ADMIN-PC", @"E:")));

        // Neuere Läufe mit neuem Schlüssel gehen vor
        var newer = store.Save(Run(T0.AddDays(1), "ADMIN-PC", "D:"));
        Assert.Equal(newer, store.FindPrevious(Run(T0.AddDays(7), "ADMIN-PC", "D:"))?.Id);
    }

    [Fact]
    public void Liste_ist_nach_Startzeit_absteigend_sortiert_nicht_nach_Speicherreihenfolge()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var mid = store.Save(Run(T0.AddDays(1)));
        var old = store.Save(Run(T0));
        var newest = store.Save(Run(T0.AddDays(2)));
        // gleicher Zeitpunkt in anderem Offset: die höhere Nummer gilt als neuer
        var sameInstant = store.Save(Run(T0.AddDays(1).ToOffset(TimeSpan.Zero)));

        Assert.Equal([newest, sameInstant, mid, old], store.List().Select(s => s.Id));
    }

    [Fact]
    public void Zusammenfassung_enthaelt_Zaehler_Ziele_und_Person()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = StorageScans.SampleWithFindings(T0);
        scan.Request = scan.Request with { Targets = [@"\\FS01\Labor", @"D:\Daten; alt"] };
        var id = store.Save(scan);

        var s = Assert.Single(store.List());

        Assert.Equal(id, s.Id);
        Assert.True(T0.EqualsExact(s.StartedAt));
        Assert.Equal(@"\\FS01\Labor; D:\Daten; alt", s.Targets);
        Assert.Equal(6, s.Folders);          // gespeicherte Ordner
        Assert.Equal(scan.FoldersVisited, s.FoldersVisited);  // geprüfte Ordner
        Assert.True(s.FoldersVisited > s.Folders);
        Assert.Equal(5, s.Findings);
        Assert.Equal(2, s.HighFindings);
        Assert.Equal(@"LAB\admin.max", s.ScannedBy);
    }

    [Fact]
    public void Leere_Datenbank_hat_leere_Liste_und_keinen_Vorgaenger()
    {
        using var db = new TestDb();
        using var store = db.Open();

        Assert.Empty(store.List());
        Assert.Null(store.FindPrevious(SampleScan.Create()));
    }

    [Fact]
    public void Vorgaenger_ist_der_neueste_fruehere_Lauf_mit_denselben_Zielen()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var r1 = Run(T0);
        var r2 = Run(T0.AddDays(1));
        var r3 = Run(T0.AddDays(2));
        var other = Run(T0.AddDays(1).AddHours(12), targets: [@"\\FS01\Andere"]);
        // in falscher Reihenfolge speichern: maßgeblich ist die Startzeit
        store.Save(r3);
        store.Save(other);
        store.Save(r1);
        store.Save(r2);

        Assert.Equal(r2.Id, store.FindPrevious(r3)?.Id);
        Assert.Equal(r1.Id, store.FindPrevious(r2)?.Id);
        Assert.Null(store.FindPrevious(r1));
        Assert.Null(store.FindPrevious(other));

        // ungespeicherter neuer Lauf (Id 0) findet den neuesten
        var fresh = Run(T0.AddDays(3));
        var prev = store.FindPrevious(fresh);
        Assert.NotNull(prev);
        Assert.Equal(r3.Id, prev.Id);
        Assert.Equal(@"\\FS01\Labor", prev.Targets);
    }

    [Fact]
    public void Zielmenge_vergleicht_ohne_Reihenfolge_Gross_Klein_Dubletten_und_Schraegstrich_am_Ende()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var stored = Run(T0, targets: [@"\\FS01\Labor", @"\\FS01\Archiv"]);
        store.Save(stored);

        Assert.Equal(stored.Id, store.FindPrevious(Run(T0.AddDays(1), targets: [@"\\fs01\archiv\", @"\\FS01\LABOR", @"//FS01/Labor"]))?.Id);
        Assert.Null(store.FindPrevious(Run(T0.AddDays(1), targets: [@"\\FS01\Labor"])));                                  // Teilmenge
        Assert.Null(store.FindPrevious(Run(T0.AddDays(1), targets: [@"\\FS01\Labor", @"\\FS01\Archiv", @"\\FS01\Neu"]))); // Obermenge
        Assert.Null(store.FindPrevious(Run(T0.AddDays(1), targets: [@"\\FS01\Labor\Unterordner", @"\\FS01\Archiv"])));
    }

    [Fact]
    public void Vorgaenger_muss_echt_frueher_beginnen_und_ist_nie_der_Lauf_selbst()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var a = Run(T0);
        store.Save(a);
        var sameStart = Run(T0.ToOffset(TimeSpan.FromHours(-5)));  // gleicher Zeitpunkt, anderer Offset
        store.Save(sameStart);

        Assert.Null(store.FindPrevious(a));
        Assert.Null(store.FindPrevious(sameStart));
        // ein Tick früher genügt
        var b = Run(T0.AddTicks(1));
        Assert.Equal(sameStart.Id, store.FindPrevious(b)?.Id);
    }

    [Fact]
    public void Zeitzonen_werden_als_Zeitpunkt_verglichen()
    {
        using var db = new TestDb();
        using var store = db.Open();
        // 08:00+02:00 = 06:00 UTC liegt vor 07:00+00:00, obwohl die Uhrzeit-Zahl größer ist
        var early = Run(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(2)));
        var late = Run(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero));
        store.Save(late);
        store.Save(early);

        Assert.Equal(early.Id, store.FindPrevious(late)?.Id);
        Assert.Null(store.FindPrevious(early));
        Assert.Equal([late.Id, early.Id], store.List().Select(s => s.Id));
    }

    [Fact]
    public void Lokale_Ziele_zaehlen_nur_auf_demselben_Rechner()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var localA = Run(T0, "PC-A", @"C:\Daten");
        var localB = Run(T0.AddDays(1), "PC-B", @"C:\Daten");
        var uncA = Run(T0, "PC-A", @"\\FS01\Labor");
        store.Save(localA);
        store.Save(localB);
        store.Save(uncA);

        // C:\Daten auf PC-B ist ein anderer Ordner als auf PC-A
        Assert.Null(store.FindPrevious(localB));
        Assert.Equal(localA.Id, store.FindPrevious(Run(T0.AddDays(2), "pc-a", @"c:\daten\"))?.Id);
        Assert.Equal(localB.Id, store.FindPrevious(Run(T0.AddDays(2), "PC-B", @"C:\Daten"))?.Id);
        // \\FS01\Labor ist von überall dasselbe
        Assert.Equal(uncA.Id, store.FindPrevious(Run(T0.AddDays(1), "PC-B", @"\\FS01\Labor"))?.Id);
    }

    [Fact]
    public void Rechnername_vergleicht_Gross_Klein_auch_bei_Umlauten()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var older = Run(T0, "PRÜF-PC", @"C:\Daten");
        var foreign = Run(T0.AddHours(1), "PRÜF-PC-2", @"C:\Daten");  // neuer, aber anderer Rechner
        store.Save(older);
        store.Save(foreign);

        // SQLite-NOCASE faltet nur ASCII – „Ü“ und „ü“ müssen trotzdem derselbe Rechner sein
        Assert.Equal(older.Id, store.FindPrevious(Run(T0.AddDays(1), "prüf-pc", @"C:\Daten"))?.Id);
        Assert.Equal(foreign.Id, store.FindPrevious(Run(T0.AddDays(1), "Prüf-PC-2", @"C:\Daten"))?.Id);
        Assert.Null(store.FindPrevious(Run(T0.AddDays(1), "PRUEF-PC", @"C:\Daten")));
    }

    [Fact]
    public void Delete_entfernt_den_Lauf_mit_allen_Kindzeilen_und_laesst_andere_unberuehrt()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var keep = StorageScans.Edge();
        var gone = StorageScans.SampleWithFindings();
        store.Save(keep);
        store.Save(gone);
        Assert.True(CountRows(store, gone.Id).Values.All(n => n > 0), "Vorbedingung: alle Tabellen haben Zeilen");

        store.Delete(gone.Id);

        Assert.Null(store.Load(gone.Id));
        Assert.All(CountRows(store, gone.Id), kv => Assert.True(kv.Value == 0, $"{kv.Key}: {kv.Value} Zeilen übrig"));
        Assert.Equal([keep.Id], store.List().Select(s => s.Id));
        DeepAssert.Equal(keep, store.Load(keep.Id));
        Assert.Empty(Rows(store, "PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void Delete_unbekannter_Nummer_ist_harmlos()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = SampleScan.Create();
        store.Save(scan);

        store.Delete(424242);
        store.Delete(0);

        Assert.NotNull(store.Load(scan.Id));
    }

    [Fact]
    public void Geloeschte_Nummern_werden_nicht_wieder_vergeben()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var a = store.Save(SampleScan.Create());
        var b = store.Save(SampleScan.Create());

        store.Delete(b);
        var c = store.Save(SampleScan.Create());

        // Ein abgezeichneter Bericht nennt „Lauf 2“ – diese Nummer darf nie einen anderen Lauf meinen
        Assert.True(c > b, $"neue Nummer {c} muss größer als die gelöschte {b} sein");
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Vorgaenger_ueberspringt_geloeschte_Laeufe()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var r1 = Run(T0);
        var r2 = Run(T0.AddDays(1));
        store.Save(r1);
        store.Save(r2);

        store.Delete(r2.Id);

        Assert.Equal(r1.Id, store.FindPrevious(Run(T0.AddDays(2)))?.Id);
    }

    /// <summary>Zeilen eines Laufs je Tabelle.</summary>
    private static Dictionary<string, long> CountRows(ScanStore store, long id) => new()
    {
        ["scan"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM scan WHERE id = {id};"),
        ["share"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM share WHERE scan_id = {id};"),
        ["share_ace"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM share_ace WHERE share_id IN (SELECT id FROM share WHERE scan_id = {id}) OR share_id NOT IN (SELECT id FROM share);"),
        ["folder"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM folder WHERE scan_id = {id};"),
        ["ace"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM ace WHERE folder_id IN (SELECT id FROM folder WHERE scan_id = {id}) OR folder_id NOT IN (SELECT id FROM folder);"),
        ["principal"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM principal WHERE scan_id = {id};"),
        ["member"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM member WHERE scan_id = {id};"),
        ["finding"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM finding WHERE scan_id = {id};"),
        ["error"] = TestDb.Scalar(store.Connection, $"SELECT count(*) FROM error WHERE scan_id = {id};"),
    };

    private static List<object[]> Rows(ScanStore store, string sql)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<object[]>();
        while (r.Read())
        {
            var row = new object[r.FieldCount];
            r.GetValues(row);
            rows.Add(row);
        }
        return rows;
    }
}
