// SPDX-License-Identifier: MIT
using Zutritt.Core;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Windows.App;

/// <summary>
/// Nach dem Scan in der Oberfläche (Befund der Prüfung: Ein fertiger Scan ging verloren, wenn das Speichern scheiterte;
/// ohne geöffnete Datenbank meldete die Übersicht „Scan abgeschlossen“ ohne jeden Hinweis).
/// </summary>
public sealed class AppStateTests
{
    [Fact]
    public async Task Ohne_Datenbank_wird_der_Lauf_geladen_und_als_nicht_gespeichert_gemeldet()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "zutritt.db");
        File.WriteAllText(path, "keine SQLite-Datenbank, sondern eine fremde Datei mit genug Text für einen Dateikopf");
        using var state = AppState.Open(path);
        Assert.Null(state.Store);
        Assert.NotNull(state.StartupProblem);

        var scan = SampleScan.Create();
        var notSaved = await state.AdoptAsync(scan);

        Assert.Same(scan, state.Scan);
        Assert.NotNull(notSaved);
        Assert.Contains(ErrorCodes.DatabaseOpen, notSaved);
        Assert.Contains("keine Datenbank", notSaved);
    }

    [Fact]
    public async Task Scheitert_das_Speichern_bleibt_der_Lauf_geladen()
    {
        using var db = new TestDb();
        using var state = AppState.Open(db.File);
        Assert.NotNull(state.Store);
        // Die Datei kaputt machen, während Zutritt sie offen hat: Speichern und Vergleich scheitern
        using (var raw = TestDb.Raw(db.File)) TestDb.Execute(raw, "DROP TABLE finding; DROP TABLE scan;");

        var scan = SampleScan.Create();
        var notSaved = await state.AdoptAsync(scan);

        Assert.Same(scan, state.Scan);
        Assert.Null(state.Comparison);
        Assert.NotNull(notSaved);
        Assert.Contains(ErrorCodes.DatabaseSave, notSaved);
    }

    [Fact]
    public async Task Gespeicherter_Lauf_meldet_nichts()
    {
        using var db = new TestDb();
        using var state = AppState.Open(db.File);
        var scan = SampleScan.Create();

        Assert.Null(await state.AdoptAsync(scan));
        Assert.Same(scan, state.Scan);
        Assert.True(scan.Id > 0);
    }
}
