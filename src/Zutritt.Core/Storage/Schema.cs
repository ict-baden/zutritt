using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Zutritt.Core.Storage;

/// <summary>Wo die Datenbank liegt – davon hängt das Journal ab.</summary>
internal enum StorageMedium
{
    Memory,
    /// <summary>Lokales Laufwerk: neue Dateien bekommen WAL.</summary>
    Local,
    /// <summary>UNC-Pfad oder Netzlaufwerk: nie WAL, siehe <see cref="Schema.Prepare"/>.</summary>
    Network,
}

/// <summary>
/// Aufbau und Migration der Datenbank. Die Version steht in <c>PRAGMA user_version</c>; jede Stufe in
/// <see cref="Migrations"/> hebt die Datenbank genau um eine Version an. Neue Felder kommen als neue Stufe
/// hinzu – vorhandene Stufen werden nie geändert, sonst wären ältere Dateien nicht mehr lesbar.
/// </summary>
internal static class Schema
{
    /// <summary>„ZTRT“ in <c>PRAGMA application_id</c> – erkennt fremde SQLite-Dateien, bevor wir sie verändern.</summary>
    public const int ApplicationId = 0x5A545254;

    public static int CurrentVersion => Migrations.Length;

    // Aufzählungen: Severity und ShareAccess haben feste Zahlenwerte im Vertrag und werden als Zahl
    // gespeichert (sortierbar). PrincipalKind hat keine festen Werte und wird als Name gespeichert, damit
    // ein späteres Einfügen in die Aufzählung alte Läufe nicht umdeutet. Rechte als vorzeichenlose
    // 32-Bit-Maske, damit GENERIC_READ (0x80000000) in Datenbank-Werkzeugen nicht negativ erscheint.
    private const string V1 = """
        CREATE TABLE scan (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,  -- AUTOINCREMENT: gelöschte Nummern nie wieder vergeben (Berichte nennen sie)
            started_at         TEXT    NOT NULL,                   -- ISO 8601 mit Offset (Round-Trip-Format „o“)
            started_utc        INTEGER NOT NULL,                   -- UTC-Ticks zum Sortieren und Vergleichen
            finished_at        TEXT    NOT NULL,
            machine            TEXT    NOT NULL,
            scanned_by         TEXT    NOT NULL,
            tool_version       TEXT    NOT NULL,
            request_json       TEXT    NOT NULL,
            targets            TEXT    NOT NULL,                   -- Anzeige: Ziele mit „; “ verbunden
            targets_key        TEXT    NOT NULL,                   -- normalisierte Zielmenge für den Vergleich mit früheren Läufen
            folders_visited    INTEGER NOT NULL,
            files_visited      INTEGER NOT NULL,
            folder_count       INTEGER NOT NULL,                   -- gespeicherte Ordner, Befunde: für die Liste ohne Zählabfragen
            finding_count      INTEGER NOT NULL,
            high_finding_count INTEGER NOT NULL
        );
        CREATE INDEX ix_scan_started ON scan(started_utc);
        CREATE INDEX ix_scan_targets ON scan(targets_key, started_utc);

        CREATE TABLE share (
            id                   INTEGER PRIMARY KEY,
            scan_id              INTEGER NOT NULL REFERENCES scan(id) ON DELETE CASCADE,
            ord                  INTEGER NOT NULL,
            server               TEXT    NOT NULL,
            name                 TEXT    NOT NULL,
            local_path           TEXT    NOT NULL,
            remark               TEXT    NOT NULL,
            is_admin_share       INTEGER NOT NULL,
            permissions_readable INTEGER NOT NULL
        );
        CREATE INDEX ix_share_scan ON share(scan_id, ord);

        CREATE TABLE share_ace (
            share_id INTEGER NOT NULL REFERENCES share(id) ON DELETE CASCADE,
            ord      INTEGER NOT NULL,
            sid      TEXT    NOT NULL,
            access   INTEGER NOT NULL,                             -- ShareAccess
            is_deny  INTEGER NOT NULL,
            PRIMARY KEY (share_id, ord)
        ) WITHOUT ROWID;

        CREATE TABLE folder (
            id                 INTEGER PRIMARY KEY,
            scan_id            INTEGER NOT NULL REFERENCES scan(id) ON DELETE CASCADE,
            ord                INTEGER NOT NULL,
            path               TEXT    NOT NULL,
            depth              INTEGER NOT NULL,
            is_file            INTEGER NOT NULL,
            share_unc          TEXT,
            owner_sid          TEXT    NOT NULL,
            inheritance_broken INTEGER NOT NULL,
            error              TEXT
        );
        CREATE INDEX ix_folder_scan ON folder(scan_id, ord);
        CREATE INDEX ix_folder_path ON folder(path COLLATE NOCASE, scan_id);  -- Verlauf eines Ordners über mehrere Läufe

        CREATE TABLE ace (
            folder_id    INTEGER NOT NULL REFERENCES folder(id) ON DELETE CASCADE,
            ord          INTEGER NOT NULL,
            sid          TEXT    NOT NULL,
            rights       INTEGER NOT NULL,                         -- FileSystemRights, vorzeichenlos
            is_deny      INTEGER NOT NULL,
            is_inherited INTEGER NOT NULL,
            inheritance  INTEGER NOT NULL,                         -- InheritanceFlags
            propagation  INTEGER NOT NULL,                         -- PropagationFlags
            PRIMARY KEY (folder_id, ord)
        ) WITHOUT ROWID;

        CREATE TABLE principal (
            scan_id         INTEGER NOT NULL REFERENCES scan(id) ON DELETE CASCADE,
            sid             TEXT    NOT NULL,                      -- Schlüssel in ScanResult.Principals
            ord             INTEGER NOT NULL,
            principal_sid   TEXT,                                  -- nur gesetzt, wenn Principal.Sid vom Schlüssel abweicht
            name            TEXT    NOT NULL,
            display_name    TEXT    NOT NULL,
            kind            TEXT    NOT NULL,                      -- PrincipalKind als Name
            is_local        INTEGER NOT NULL,
            is_disabled     INTEGER NOT NULL,
            members_unknown INTEGER NOT NULL,
            PRIMARY KEY (scan_id, sid)
        ) WITHOUT ROWID;

        CREATE TABLE member (
            scan_id    INTEGER NOT NULL,
            group_sid  TEXT    NOT NULL,
            ord        INTEGER NOT NULL,
            member_sid TEXT    NOT NULL,
            PRIMARY KEY (scan_id, group_sid, ord),
            FOREIGN KEY (scan_id, group_sid) REFERENCES principal(scan_id, sid) ON DELETE CASCADE
        ) WITHOUT ROWID;

        CREATE TABLE finding (
            scan_id  INTEGER NOT NULL REFERENCES scan(id) ON DELETE CASCADE,
            ord      INTEGER NOT NULL,
            rule_id  TEXT    NOT NULL,
            severity INTEGER NOT NULL,                             -- Severity: 0 Info … 3 Hoch
            path     TEXT    NOT NULL,
            sid      TEXT,
            message  TEXT    NOT NULL,
            PRIMARY KEY (scan_id, ord)
        ) WITHOUT ROWID;

        CREATE TABLE error (
            scan_id INTEGER NOT NULL REFERENCES scan(id) ON DELETE CASCADE,
            ord     INTEGER NOT NULL,
            path    TEXT    NOT NULL,
            message TEXT    NOT NULL,
            PRIMARY KEY (scan_id, ord)
        ) WITHOUT ROWID;
        """;

    // Version 2: Kennzeichen „geerbte Rechte weichen vom Elternordner ab“ (FolderNode.InheritanceDiffers, Regel ZT14).
    // Ältere Läufe kannten es nicht – 0 ist für sie richtig, ihre Befunde sind ohnehin gespeichert.
    private const string V2 = """
        ALTER TABLE folder ADD COLUMN inheritance_differs INTEGER NOT NULL DEFAULT 0;
        """;

    private static readonly string[] Migrations = [V1, V2];

    /// <summary>Verbindung einstellen, Datei prüfen und auf den aktuellen Stand bringen.</summary>
    /// <param name="path">Voller Pfad der Datei (für Meldungen und den Blick in den Dateikopf).</param>
    public static void Prepare(SqliteConnection db, string path, StorageMedium medium)
    {
        // Fremdschlüssel gelten je Verbindung und sind in SQLite standardmäßig aus
        Execute(db, "PRAGMA foreign_keys = ON;");

        // Über das Netz darf SQLite den gemeinsamen Speicher (-shm) einer WAL-Datenbank nie benutzen: über SMB ist er
        // nicht zuverlässig, die Datei kann beschädigt werden (über \\localhost hing der Zugriff sogar dauerhaft). Im
        // Sperrmodus EXCLUSIVE öffnet SQLite eine WAL-Datenbank ohne ihn – und nur, wenn niemand sonst sie offen hat.
        // Muss vor dem ersten Lesen stehen.
        var network = medium == StorageMedium.Network;
        var walOverNetwork = network && HeaderSaysWal(path);
        if (network) Execute(db, "PRAGMA locking_mode = EXCLUSIVE;");

        long appId;
        try
        {
            // Wer eine WAL-Datenbank offen hat, hält sie, bis er sie schließt: dann nicht 30 s warten
            appId = Scalar(db, "PRAGMA application_id;", timeoutSeconds: walOverNetwork ? 5 : null);
        }
        catch (SqliteException ex) when (walOverNetwork && (ex.SqliteErrorCode & 0xFF) is 5 or 6)
        {
            throw new ScanStoreException($"Die Datenbank „{path}“ ist gerade im WAL-Modus geöffnet – meist von Zutritt auf dem Rechner, auf dem sie liegt. Über eine Freigabe lässt sie sich erst öffnen, wenn sie dort geschlossen ist; Zutritt stellt sie dann dauerhaft auf das für Freigaben geeignete Journal um.", ex);
        }
        var version = Scalar(db, "PRAGMA user_version;");
        bool isNew = appId == 0 && version == 0 && Scalar(db, "SELECT count(*) FROM sqlite_schema;") == 0;

        if (!isNew && appId != ApplicationId)
            throw new ScanStoreException($"Die Datei „{path}“ ist eine SQLite-Datenbank, aber keine Zutritt-Datenbank. Sie wird nicht verändert.");
        if (version > CurrentVersion)
            throw new ScanStoreException($"Die Datenbank „{path}“ stammt von einer neueren Zutritt-Version (Schema {version}, diese Version kennt bis {CurrentVersion}). Bitte Zutritt aktualisieren.");

        // Muss vor der ersten Tabelle stehen; danach gibt Delete freie Seiten per incremental_vacuum zurück
        if (isNew) Execute(db, "PRAGMA auto_vacuum = INCREMENTAL;");
        if (isNew && medium == StorageMedium.Local)
        {
            // WAL nur für neue Dateien auf einem lokalen Laufwerk. Eine vorhandene Datei im klassischen Journal bleibt
            // so: sie wurde schon über eine Freigabe geöffnet, und wer sie dort noch offen hat, nähme nach einem Wechsel
            // zu WAL den gemeinsamen Speicher übers Netz.
            Execute(db, "PRAGMA journal_mode = WAL;");
        }
        else if (network && Text(db, "PRAGMA journal_mode;") == "wal")
        {
            // Lokal angelegt, jetzt über eine Freigabe: dauerhaft zurück auf das klassische Journal. Die Sperre gehört
            // uns; scheitert es trotzdem, lieber nicht öffnen als mit WAL übers Netz weiterarbeiten.
            Execute(db, "PRAGMA journal_mode = DELETE;");
        }
        // Eine große Speicherung darf die -wal-Datei nicht dauerhaft aufblähen
        Execute(db, "PRAGMA journal_size_limit = 33554432;");
        // 32 MB Seitencache statt 2 MB: ein Lauf mit 100.000 ACEs passt hinein, ohne mitten in der Transaktion auszulagern
        Execute(db, "PRAGMA cache_size = -32768;");

        if (version < CurrentVersion) Migrate(db);

        if (network)
        {
            // Sperre wieder freigeben, damit andere Rechner die Datei benutzen können – SQLite tut das erst beim nächsten Zugriff
            Execute(db, "PRAGMA locking_mode = NORMAL;");
            Scalar(db, "SELECT count(*) FROM sqlite_schema;");
        }
    }

    /// <summary>
    /// Steht im Dateikopf der WAL-Modus (Bytes 18 und 19 = 2)? Ohne SQLite gelesen, also ohne Sperren und ohne -shm.
    /// </summary>
    internal static bool HeaderSaysWal(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[20];
            return file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length && header[18] == 2 && header[19] == 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Migrate(SqliteConnection db)
    {
        // BEGIN IMMEDIATE: öffnen zwei Prozesse gleichzeitig eine neue Datei, migriert nur einer;
        // der andere wartet und sieht danach die neue Version.
        using var tx = db.BeginTransaction();
        var version = Scalar(db, "PRAGMA user_version;", tx);
        for (var v = (int)version; v < Migrations.Length; v++)
            Execute(db, Migrations[v], tx);
        Execute(db, $"PRAGMA user_version = {CurrentVersion}; PRAGMA application_id = {ApplicationId};", tx);
        tx.Commit();
    }

    internal static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    internal static long Scalar(SqliteConnection db, string sql, SqliteTransaction? tx = null, int? timeoutSeconds = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        if (timeoutSeconds is { } t) cmd.CommandTimeout = t;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static string Text(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }
}
