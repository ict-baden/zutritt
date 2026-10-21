// SPDX-License-Identifier: MIT
using System.Diagnostics;
using SQLitePCL;
using Xunit.Abstractions;

namespace Zutritt.Tests.Storage;

/// <summary>Zeitmessungen laufen allein: parallele Tests mit vielen kleinen fsyncs verfälschen sie um ein Vielfaches.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Speicher-Zeitmessung";
}

[Collection(TimingCollection.Name)]
public sealed class ScanStoreLargeTests(ITestOutputHelper output)
{
    /// <summary>
    /// 20.000 Ordner × 5 Einträge = 100.000 ACEs. Gemessen allein knapp 1 s zum Speichern und 0,3 s zum Laden;
    /// das Budget ist großzügig, weil der Rechner nebenbei ausgelastet sein kann. Rückfälle wie eine Transaktion
    /// je Zeile fängt <see cref="Speichern_ist_eine_einzige_Transaktion"/> zuverlässig und ohne Uhr.
    /// </summary>
    [Fact]
    public void Grosser_Lauf_speichern_laden_loeschen_im_Zeitbudget()
    {
        var budget = TimeSpan.FromSeconds(30);
        using var db = new TestDb();
        var scan = StorageScans.Large(folders: 20_000, acesPerFolder: 5);
        Assert.Equal(100_000, scan.Folders.Sum(f => f.Aces.Count));
        var small = StorageScans.SampleWithFindings();

        long sizeWithBoth;
        using (var store = db.Open())
        {
            store.Save(small);

            var sw = Stopwatch.StartNew();
            var id = store.Save(scan);
            var save = sw.Elapsed;

            sw.Restart();
            var loaded = store.Load(id);
            var load = sw.Elapsed;

            sw.Restart();
            var list = store.List();
            var previous = store.FindPrevious(scan);
            var query = sw.Elapsed;

            DeepAssert.Equal(scan, loaded);
            Assert.Equal(2, list.Count);
            Assert.Equal(20_000, list[0].Folders);
            Assert.Null(previous);
            store.Dispose();
            sizeWithBoth = new FileInfo(db.File).Length;

            using var again = db.Open();
            sw.Restart();
            again.Delete(id);
            var delete = sw.Elapsed;

            output.WriteLine($"Speichern {save.TotalMilliseconds:F0} ms, Laden {load.TotalMilliseconds:F0} ms, Liste+Vorgänger {query.TotalMilliseconds:F0} ms, Löschen {delete.TotalMilliseconds:F0} ms, Datei {sizeWithBoth / 1024 / 1024.0:F1} MB");
            Assert.True(save < budget, $"Speichern dauerte {save}");
            Assert.True(load < budget, $"Laden dauerte {load}");
            Assert.True(query < TimeSpan.FromSeconds(5), $"Liste und Vorgänger dauerten {query}");
            Assert.True(delete < budget, $"Löschen dauerte {delete}");
            DeepAssert.Equal(small, again.Load(small.Id));
        }

        // Nach dem Löschen gibt die Datei den Platz frei (incremental_vacuum) und ist nicht mehr gesperrt
        var sizeAfterDelete = new FileInfo(db.File).Length;
        output.WriteLine($"Datei nach dem Löschen {sizeAfterDelete / 1024 / 1024.0:F2} MB");
        Assert.True(sizeAfterDelete < sizeWithBoth / 4, $"Datei schrumpft nicht: {sizeWithBoth} → {sizeAfterDelete} Bytes");
        File.Delete(db.File);
        Assert.False(File.Exists(db.File));
    }

    [Fact]
    public void Speichern_ist_eine_einzige_Transaktion()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var commits = 0;
        raw.sqlite3_commit_hook(store.Connection.Handle, _ => { commits++; return 0; }, null);
        try
        {
            store.Save(StorageScans.Large(folders: 2_000, acesPerFolder: 5));
            Assert.Equal(1, commits);

            commits = 0;
            store.Load(1);
            store.List();
            Assert.Equal(0, commits);  // Lesen schreibt nichts
        }
        finally
        {
            raw.sqlite3_commit_hook(store.Connection.Handle, null, null);
        }
    }
}
