// SPDX-License-Identifier: MIT
using Zutritt.Cli;
using Zutritt.Core.Storage;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Windows.App;

/// <summary>
/// Befehlszeile nach dem Scan: Scheitert das Speichern, gehen die Berichte trotzdem raus (Exitcode 3); was der Scan am
/// Ende braucht, wird vorher geprüft. In der Sammlung „Protokoll“: läuft allein, weil Konsole und Protokoll global sind.
/// </summary>
[Collection("Protokoll")]
public sealed class CommandLineScanTests : IDisposable
{
    private readonly TextWriter _out = Console.Out;
    private readonly TextWriter _err = Console.Error;
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public CommandLineScanTests()
    {
        Console.SetOut(_stdout);
        Console.SetError(_stderr);
    }

    public void Dispose()
    {
        Console.SetOut(_out);
        Console.SetError(_err);
    }

    [Fact]
    public void Speichern_scheitert_Berichte_werden_trotzdem_geschrieben()
    {
        using var db = new TestDb();
        using var store = db.Open();
        // Fehler erst beim Speichern, nicht bei der Vorab-Prüfung (die schreibt keine Zeilen)
        TestDb.Execute(store.Connection, "CREATE TRIGGER zt_test_fehler BEFORE INSERT ON scan BEGIN SELECT RAISE(ABORT, 'Testfehler'); END;");
        store.CheckWritable();
        var report = Path.Combine(db.Folder, "bericht.xlsx");

        int code = CommandLine.FinishScan(store, SampleScan.Create(), compare: true, [report]);

        Assert.Equal(CommandLine.ExitError, code);
        Assert.True(File.Exists(report));
        Assert.Contains("nicht gespeichert", _stderr.ToString());
        Assert.Contains("Exitcode 3", _stdout.ToString());
        Assert.Empty(store.List());
    }

    [Fact]
    public void Gespeicherter_Lauf_ergibt_den_Exitcode_der_Befunde()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = SampleScan.Create();

        int code = CommandLine.FinishScan(store, scan, compare: false, []);

        Assert.Equal(CommandLine.ExitCodeFor(scan), code);
        Assert.Single(store.List());
        Assert.Contains($"Lauf {scan.Id} gespeichert", _stdout.ToString());
    }

    [Fact]
    public void Datenbank_unter_einer_Datei_bricht_vor_dem_Scan_ab()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "datei.txt");
        File.WriteAllText(file, "x");

        var ex = Assert.Throws<CommandLine.PreconditionException>(() => CommandLine.OpenStoreForScan(Path.Combine(file, "zutritt.db")));
        Assert.Contains("Es wurde nicht gescannt", ex.Message);
    }

    [Fact]
    public void Schreibgeschuetzte_Datenbank_bricht_vor_dem_Scan_ab()
    {
        using var db = new TestDb();
        using (var first = db.Open()) first.Save(SampleScan.Create());
        File.SetAttributes(db.File, File.GetAttributes(db.File) | FileAttributes.ReadOnly);
        try
        {
            var ex = Assert.Throws<CommandLine.PreconditionException>(() => CommandLine.OpenStoreForScan(db.File));
            Assert.Contains("Es wurde nicht gescannt", ex.Message);
        }
        finally
        {
            File.SetAttributes(db.File, File.GetAttributes(db.File) & ~FileAttributes.ReadOnly);
        }
    }
}
