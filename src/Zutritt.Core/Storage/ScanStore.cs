// SPDX-License-Identifier: MIT
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

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
                throw new ScanStoreException(Texts.Format(Strings.Store_FolderNotCreated, DatabasePath, ex.Message), ex, ErrorCodes.DatabaseOpen);
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
                throw new ScanStoreException(Texts.Format(Strings.Store_CannotOpen, DatabasePath, ScanStoreException.Describe(sx)), sx, ErrorCodes.DatabaseOpen);
            throw;
        }
    }

    /// <summary>
    /// %LocalAppData%\Zutritt\zutritt.db – pro Benutzer, weil ein Lauf vertraulich ist (siehe DatabaseLocation).
    /// </summary>
    public static string DefaultPath => DatabaseLocation.Default;

    /// <summary>Version des Datenbankschemas, die diese Ausgabe schreibt (für das Diagnosepaket).</summary>
    public static int CurrentSchemaVersion => Schema.CurrentVersion;

    /// <summary>Voller Pfad der geöffneten Datenbank bzw. „:memory:“.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Speichert den Lauf in einer Transaktion als neuen Eintrag und setzt danach <see cref="ScanResult.Id"/>
    /// und <see cref="FolderNode.Id"/>. Schlägt es fehl, bleibt der Lauf unverändert und nichts ist gespeichert.
    /// </summary>
    public long Save(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return Run(Strings.Store_SaveFailed, ErrorCodes.DatabaseSave, () =>
        {
            var (id, folderIds) = ScanWriter.Insert(_db, scan);
            scan.Id = id;
            for (var i = 0; i < folderIds.Length; i++) scan.Folders[i].Id = folderIds[i];
            return id;
        });
    }

    /// <summary>
    /// Prüft vor einem langen Scan, ob sich ein Lauf überhaupt speichern ließe: schreibt probeweise in einer
    /// Transaktion und nimmt es sofort zurück. Erkennt eine schreibgeschützte Datei, einen schreibgeschützten Ordner
    /// (Journal nicht anlegbar) und eine gerade gesperrte Datei – nicht aber einen später voll werdenden Datenträger.
    /// </summary>
    /// <exception cref="ScanStoreException">Die Datenbank ist nicht beschreibbar.</exception>
    public void CheckWritable() => Run(Strings.Store_NotWritable, ErrorCodes.DatabaseNotWritable, () =>
    {
        using var tx = _db.BeginTransaction();
        // Schreibt die Kopfseite (gleicher Wert) – braucht Schreibrecht auf Datei und Journal, ändert aber nichts
        Schema.Execute(_db, $"PRAGMA user_version = {Schema.CurrentVersion};", tx);
        tx.Rollback();
        return true;
    });

    /// <summary>Lädt einen Lauf vollständig, null wenn es ihn nicht gibt.</summary>
    public ScanResult? Load(long id) =>
        Run(Texts.Format(Strings.Store_LoadFailed, id), ErrorCodes.DatabaseRead, () => ScanReader.Load(_db, id));

    /// <summary>
    /// Alle Läufe, neueste zuerst (nach Startzeit). <see cref="ScanSummary.Folders"/> zählt die gespeicherten
    /// Ordner (Wurzeln, eigene Rechte, Fehler), <see cref="ScanSummary.FoldersVisited"/> alle geprüften.
    /// </summary>
    public IReadOnlyList<ScanSummary> List() =>
        Run(Strings.Store_ListFailed, ErrorCodes.DatabaseRead, () => ScanReader.List(_db));

    /// <summary>
    /// Der letzte Lauf vor <paramref name="scan"/> mit denselben Zielen, für den Vergleich: gleiche Zielmenge
    /// (Reihenfolge, Groß/Klein und abschließendes „\“ egal), früher gestartet, nicht der Lauf selbst. Hängen
    /// die Ziele vom Rechner ab („.“, lokale Pfade, \\localhost), zählt nur ein Lauf desselben Rechners. Unter
    /// diesen hat ein Lauf mit denselben Einstellungen (Dateien, Tiefe, administrative Freigaben, Gruppen, ZT06-Ebene)
    /// Vorrang – ein zwischendurch flach gestarteter Lauf verdrängt so nicht den letzten vollständigen.
    /// </summary>
    public ScanSummary? FindPrevious(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var targets = scan.Request?.Targets;
        var key = TargetSet.Key(targets);
        var machine = TargetSet.DependsOnMachine(targets) ? scan.Machine ?? "" : null;
        return Run(Strings.Store_FindPreviousFailed, ErrorCodes.DatabaseRead,
            () => ScanReader.FindPrevious(_db, key, scan.StartedAt.UtcTicks, scan.Id, machine, scan.Request));
    }

    /// <summary>Löscht einen Lauf mit allem, was dazugehört. Unbekannte Nummern werden ignoriert.</summary>
    public void Delete(long id) => Run(Texts.Format(Strings.Store_DeleteFailed, id), ErrorCodes.DatabaseDelete, () =>
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

    private T Run<T>(string action, string errorCode, Func<T> work)
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
                throw new ScanStoreException($"{action} {ScanStoreException.Describe(ex)}", ex, errorCode);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException or InvalidCastException
                                           or OverflowException or KeyNotFoundException or NotSupportedException)
            {
                throw new ScanStoreException(Texts.Format(Strings.Store_InvalidData, action, DatabasePath, ex.Message), ex, ErrorCodes.DatabaseInvalid);
            }
        }
    }
}
