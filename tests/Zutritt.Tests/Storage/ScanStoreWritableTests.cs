// SPDX-License-Identifier: MIT
using Microsoft.Data.Sqlite;
using Zutritt.Core.Reports;
using Zutritt.Core.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

/// <summary>
/// Vorab-Prüfungen vor einem langen Scan (Befund „Scan geht verloren, wenn das Speichern scheitert“): Datenbank und
/// Berichtsordner müssen beschreibbar sein, bevor gescannt wird.
/// </summary>
public sealed class ScanStoreWritableTests
{
    [Fact]
    public void Beschreibbare_Datenbank_besteht_die_Pruefung_ohne_Spuren()
    {
        using var db = new TestDb();
        using var store = db.Open();

        store.CheckWritable();
        store.CheckWritable();

        Assert.Empty(store.List());
        Assert.Equal(Schema.CurrentVersion, TestDb.Scalar(store.Connection, "PRAGMA user_version;"));
        Assert.True(store.Save(SampleScan.Create()) > 0);
    }

    [Fact]
    public void Schreibgeschuetzte_Datei_faellt_vor_dem_Scan_auf()
    {
        using var db = new TestDb();
        using (var first = db.Open()) first.Save(SampleScan.Create());
        MakeReadOnly(db.File);
        try
        {
            using var store = db.Open();
            Assert.Single(store.List());   // lesen geht weiter

            var ex = Assert.Throws<ScanStoreException>(store.CheckWritable);
            Assert.False(ex.IsLocked);
            Assert.IsType<SqliteException>(ex.InnerException);
            // Genau das wäre sonst erst nach dem Scan beim Speichern passiert
            Assert.Throws<ScanStoreException>(() => store.Save(SampleScan.Create()));
            Assert.Single(store.List());
        }
        finally
        {
            MakeWritable(db.File);
        }
    }

    [Fact]
    public void Schreibgeschuetzter_Ordner_faellt_vor_dem_Scan_auf()
    {
        // Unter Windows bräuchte das eigene ACLs; dort deckt die schreibgeschützte Datei den Fall ab
        if (OperatingSystem.IsWindows()) return;
        using var db = new TestDb();
        using (var first = db.Open())
        {
            first.Save(SampleScan.Create());
            // Klassisches Journal wie über eine Freigabe: jede Schreibtransaktion braucht eine neue Datei im Ordner
            TestDb.Execute(first.Connection, "PRAGMA journal_mode = DELETE;");
        }
        var mode = File.GetUnixFileMode(db.Folder);
        File.SetUnixFileMode(db.Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using var store = db.Open();
            Assert.Throws<ScanStoreException>(store.CheckWritable);
        }
        finally
        {
            File.SetUnixFileMode(db.Folder, mode);
        }
    }

    [Theory]
    [InlineData(5, true)]     // SQLITE_BUSY
    [InlineData(6, true)]     // SQLITE_LOCKED
    [InlineData(261, true)]   // SQLITE_BUSY_RECOVERY (erweiterter Code)
    [InlineData(8, false)]    // SQLITE_READONLY
    [InlineData(14, false)]   // SQLITE_CANTOPEN
    public void Nur_eine_Sperre_gilt_als_voruebergehend(int code, bool locked)
    {
        var ex = new ScanStoreException("x", new SqliteException("y", code));
        Assert.Equal(locked, ex.IsLocked);
        Assert.False(new ScanStoreException("ohne Ursache").IsLocked);
    }

    [Fact]
    public void Berichtsziel_wird_vorab_geprueft_und_der_Ordner_angelegt()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "neu", "bericht.pdf");

        Assert.Equal(path, ReportTarget.Check(path));

        Assert.True(Directory.Exists(Path.Combine(temp.Path, "neu")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(temp.Path, "neu")));   // Probedatei ist wieder weg
    }

    [Fact]
    public void Berichtsziel_unter_einer_Datei_wird_abgelehnt()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "datei.txt");
        File.WriteAllText(file, "x");

        var ex = Assert.Throws<ReportWriteException>(() => ReportTarget.Check(Path.Combine(file, "bericht.xlsx")));
        Assert.Contains("bericht.xlsx", ex.Message);
        Assert.Throws<ReportWriteException>(() => ReportTarget.Check(temp.Path));   // Ordner statt Datei
    }

    [Fact]
    public void Berichtsziel_in_schreibgeschuetztem_Ordner_wird_abgelehnt()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempFolder();
        var folder = temp.Sub("berichte");
        var mode = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Throws<ReportWriteException>(() => ReportTarget.Check(Path.Combine(folder, "bericht.xlsx")));
        }
        finally
        {
            File.SetUnixFileMode(folder, mode);
        }
    }

    private static void MakeReadOnly(string file)
    {
        if (OperatingSystem.IsWindows()) File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        else File.SetUnixFileMode(file, UnixFileMode.UserRead);
    }

    private static void MakeWritable(string file)
    {
        if (OperatingSystem.IsWindows()) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        else File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
