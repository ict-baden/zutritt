using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zutritt.Core.Model;

namespace Zutritt.Core.Storage;

/// <summary>
/// Speichert Läufe in einer SQLite-Datei (Standard: %LocalAppData%\Zutritt\zutritt.db).
/// Gespeicherte Läufe sind unveränderlich: <see cref="Save"/> legt immer einen neuen Lauf an, damit ein
/// abgezeichneter Stand später genau so wieder geladen werden kann. Threadsicher; Datenbankprobleme kommen
/// als <see cref="ScanStoreException"/> mit deutscher Meldung.
/// </summary>
public sealed class ScanStore : IDisposable
{
    private const string InMemory = ":memory:";
    private readonly SqliteConnection _db;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <param name="databasePath">Datenbankdatei (der Ordner wird bei Bedarf angelegt) oder „:memory:“ für einen flüchtigen Speicher.</param>
    /// <exception cref="ScanStoreException">
    /// Datei nicht anlegbar, keine Zutritt-Datenbank, von einer neueren Version oder – beim Öffnen über eine Freigabe –
    /// gerade anderswo im WAL-Modus geöffnet.
    /// </exception>
    public ScanStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var inMemory = databasePath == InMemory;
        DatabasePath = inMemory ? InMemory : Path.GetFullPath(databasePath);

        if (!inMemory)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ScanStoreException($"Der Ordner für die Datenbank „{DatabasePath}“ kann nicht angelegt werden: {ex.Message}", ex);
            }
        }

        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Ohne Pool schließt Dispose die Datei wirklich; ein gepoolter Handle hielte sie gesperrt
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString());
        try
        {
            _db.Open();
            var medium = inMemory ? StorageMedium.Memory
                : DatabaseLocation.IsNetworkPath(DatabasePath) ? StorageMedium.Network : StorageMedium.Local;
            Schema.Prepare(_db, DatabasePath, medium);
        }
        catch (Exception ex)
        {
            _db.Dispose();
            if (ex is SqliteException sx)
                throw new ScanStoreException($"Die Datenbank „{DatabasePath}“ kann nicht geöffnet werden. {ScanStoreException.Describe(sx)}", sx);
            throw;
        }
    }

    /// <summary>
    /// %LocalAppData%\Zutritt\zutritt.db – pro Benutzer, weil ein Lauf vertraulich ist (siehe DatabaseLocation).
    /// </summary>
    public static string DefaultPath => DatabaseLocation.Default;

    /// <summary>Voller Pfad der geöffneten Datenbank bzw. „:memory:“.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Speichert den Lauf in einer Transaktion als neuen Eintrag und setzt danach <see cref="ScanResult.Id"/>
    /// und <see cref="FolderNode.Id"/>. Schlägt es fehl, bleibt der Lauf unverändert und nichts ist gespeichert.
    /// </summary>
    public long Save(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return Run("Der Lauf konnte nicht gespeichert werden.", () =>
        {
            var (id, folderIds) = ScanWriter.Insert(_db, scan);
            scan.Id = id;
            for (var i = 0; i < folderIds.Length; i++) scan.Folders[i].Id = folderIds[i];
            return id;
        });
    }

    /// <summary>Lädt einen Lauf vollständig, null wenn es ihn nicht gibt.</summary>
    public ScanResult? Load(long id) =>
        Run($"Lauf {id} konnte nicht geladen werden.", () => ScanReader.Load(_db, id));

    /// <summary>
    /// Alle Läufe, neueste zuerst (nach Startzeit). <see cref="ScanSummary.Folders"/> zählt die gespeicherten
    /// Ordner (Wurzeln, eigene Rechte, Fehler), nicht alle besuchten.
    /// </summary>
    public IReadOnlyList<ScanSummary> List() =>
        Run("Die Liste der Läufe konnte nicht gelesen werden.", () => ScanReader.List(_db));

    /// <summary>
    /// Der letzte Lauf vor <paramref name="scan"/> mit denselben Zielen, für den Vergleich: gleiche Zielmenge
    /// (Reihenfolge, Groß/Klein und abschließendes „\“ egal), früher gestartet, nicht der Lauf selbst. Hängen
    /// die Ziele vom Rechner ab („.“, lokale Pfade, \\localhost), zählt nur ein Lauf desselben Rechners.
    /// </summary>
    public ScanSummary? FindPrevious(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var targets = scan.Request?.Targets;
        var key = TargetSet.Key(targets);
        var machine = TargetSet.DependsOnMachine(targets) ? scan.Machine ?? "" : null;
        return Run("Der vorige Lauf konnte nicht gesucht werden.",
            () => ScanReader.FindPrevious(_db, key, scan.StartedAt.UtcTicks, scan.Id, machine));
    }

    /// <summary>Löscht einen Lauf mit allem, was dazugehört. Unbekannte Nummern werden ignoriert.</summary>
    public void Delete(long id) => Run($"Lauf {id} konnte nicht gelöscht werden.", () =>
    {
        using (var tx = _db.BeginTransaction())
        {
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            // Kinder zuerst und ausdrücklich: schneller als die Kaskade Zeile für Zeile
            cmd.CommandText = """
                DELETE FROM ace WHERE folder_id IN (SELECT id FROM folder WHERE scan_id = $id);
                DELETE FROM folder WHERE scan_id = $id;
                DELETE FROM share_ace WHERE share_id IN (SELECT id FROM share WHERE scan_id = $id);
                DELETE FROM share WHERE scan_id = $id;
                DELETE FROM member WHERE scan_id = $id;
                DELETE FROM principal WHERE scan_id = $id;
                DELETE FROM finding WHERE scan_id = $id;
                DELETE FROM error WHERE scan_id = $id;
                DELETE FROM scan WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        // Freie Seiten zurückgeben, sonst schrumpft die Datei nie. Gelöscht ist schon – Fehler hier sind egal.
        try { Schema.Execute(_db, "PRAGMA incremental_vacuum;"); }
        catch (SqliteException) { }
        return true;
    });

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                // Nur Pflege: hält ein anderer Prozess gerade die Schreibsperre (z. B. die Befehlszeile speichert
                // einen großen Lauf), nicht bis zum Standard-Timeout von 30 s warten – das Fenster hinge beim Schließen.
                using var cmd = _db.CreateCommand();
                cmd.CommandTimeout = 1;
                cmd.CommandText = "PRAGMA optimize;";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException) { }
            // Beim Schließen der letzten Verbindung überträgt SQLite die -wal-Datei und löscht sie
            SqliteConnection.ClearPool(_db);
            _db.Dispose();
        }
    }

    /// <summary>Nur für Tests: Pragmas und Zeilen direkt prüfen.</summary>
    internal SqliteConnection Connection => _db;

    private T Run<T>(string action, Func<T> work)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                return work();
            }
            catch (SqliteException ex)
            {
                throw new ScanStoreException($"{action} {ScanStoreException.Describe(ex)}", ex);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException or InvalidCastException
                                           or OverflowException or KeyNotFoundException or NotSupportedException)
            {
                throw new ScanStoreException($"{action} Die Datenbank „{DatabasePath}“ enthält ungültige Daten ({ex.Message}).", ex);
            }
        }
    }
}
