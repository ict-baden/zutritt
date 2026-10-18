using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zutritt.Core.Model;

namespace Zutritt.Core.Storage;

/// <summary>Baut gespeicherte Läufe wieder auf. Beschädigte Daten führen zu einem Fehler, nie zu stillen Ersatzwerten.</summary>
internal static class ScanReader
{
    private const string SummaryColumns = "id, started_at, targets, folder_count, finding_count, high_finding_count, scanned_by, folders_visited";
    private const int SummaryColumnCount = 8;

    public static ScanResult? Load(SqliteConnection db, long id)
    {
        // Lesetransaktion: alle Abfragen sehen denselben Stand, auch wenn ein anderer Prozess gerade löscht
        using var tx = db.BeginTransaction(deferred: true);
        var scan = LoadScan(db, tx, id);
        if (scan is null) return null;
        LoadShares(db, tx, scan);
        LoadFolders(db, tx, scan);
        LoadPrincipals(db, tx, scan);
        LoadFindings(db, tx, scan);
        LoadErrors(db, tx, scan);
        tx.Commit();
        return scan;
    }

    public static List<ScanSummary> List(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {SummaryColumns} FROM scan ORDER BY started_utc DESC, id DESC;";
        using var r = cmd.ExecuteReader();
        var list = new List<ScanSummary>();
        while (r.Read()) list.Add(ReadSummary(r));
        return list;
    }

    /// <param name="machine">Nur Läufe dieses Rechners (Groß/Klein egal), null = egal.</param>
    public static ScanSummary? FindPrevious(SqliteConnection db, string targetsKey, long startedUtc, long excludeId, string? machine)
    {
        // Der Rechnername wird hier verglichen, nicht per COLLATE NOCASE: SQLite faltet nur ASCII,
        // „PRÜF-PC“ und „prüf-pc“ wären dort verschiedene Rechner. Der Index liefert die Läufe schon
        // absteigend; gelesen wird nur bis zum ersten passenden.
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SummaryColumns}, machine FROM scan
            WHERE targets_key = $key AND started_utc < $started AND id <> $id
            ORDER BY started_utc DESC, id DESC;
            """;
        cmd.Parameters.AddWithValue("$key", targetsKey);
        cmd.Parameters.AddWithValue("$started", startedUtc);
        cmd.Parameters.AddWithValue("$id", excludeId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (machine is null || string.Equals(r.GetString(SummaryColumnCount), machine, StringComparison.OrdinalIgnoreCase))
                return ReadSummary(r);
        }
        return null;
    }

    private static ScanSummary ReadSummary(SqliteDataReader r) => new(
        Id: r.GetInt64(0),
        StartedAt: ParseTime(r.GetString(1)),
        Targets: r.GetString(2),
        Folders: r.GetInt32(3),
        Findings: r.GetInt32(4),
        HighFindings: r.GetInt32(5),
        ScannedBy: r.GetString(6),
        FoldersVisited: r.GetInt64(7));

    private static ScanResult? LoadScan(SqliteConnection db, SqliteTransaction tx, long id)
    {
        using var cmd = Command(db, tx, """
            SELECT started_at, finished_at, machine, scanned_by, tool_version, request_json, folders_visited, files_visited
            FROM scan WHERE id = $id;
            """, id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ScanResult
        {
            Id = id,
            StartedAt = ParseTime(r.GetString(0)),
            FinishedAt = ParseTime(r.GetString(1)),
            Machine = r.GetString(2),
            ScannedBy = r.GetString(3),
            ToolVersion = r.GetString(4),
            Request = ParseRequest(r.GetString(5)),
            FoldersVisited = r.GetInt64(6),
            FilesVisited = r.GetInt64(7),
        };
    }

    private static void LoadShares(SqliteConnection db, SqliteTransaction tx, ScanResult scan)
    {
        // ShareInfo.Permissions ist init-only: erst die Einträge sammeln, dann die Freigaben bauen
        var aces = new Dictionary<long, List<ShareAce>>();
        using (var cmd = Command(db, tx, """
            SELECT a.share_id, a.sid, a.access, a.is_deny
            FROM share_ace a JOIN share s ON s.id = a.share_id
            WHERE s.scan_id = $id ORDER BY a.share_id, a.ord;
            """, scan.Id))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var shareId = r.GetInt64(0);
                if (!aces.TryGetValue(shareId, out var list)) aces[shareId] = list = [];
                list.Add(new ShareAce(r.GetString(1), (ShareAccess)r.GetInt32(2), r.GetBoolean(3)));
            }
        }

        using (var cmd = Command(db, tx, """
            SELECT id, server, name, local_path, remark, is_admin_share, permissions_readable
            FROM share WHERE scan_id = $id ORDER BY ord;
            """, scan.Id))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                scan.Shares.Add(new ShareInfo
                {
                    Server = r.GetString(1),
                    Name = r.GetString(2),
                    LocalPath = r.GetString(3),
                    Remark = r.GetString(4),
                    IsAdminShare = r.GetBoolean(5),
                    PermissionsReadable = r.GetBoolean(6),
                    Permissions = aces.TryGetValue(r.GetInt64(0), out var list) ? list : [],
                });
            }
        }
    }

    private static void LoadFolders(SqliteConnection db, SqliteTransaction tx, ScanResult scan)
    {
        var byId = new Dictionary<long, FolderNode>();
        using (var cmd = Command(db, tx, """
            SELECT id, path, depth, is_file, share_unc, owner_sid, inheritance_broken, error, inheritance_differs
            FROM folder WHERE scan_id = $id ORDER BY ord;
            """, scan.Id))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var node = new FolderNode
                {
                    Id = r.GetInt64(0),
                    Path = r.GetString(1),
                    Depth = r.GetInt32(2),
                    IsFile = r.GetBoolean(3),
                    ShareUnc = NullableText(r, 4),
                    OwnerSid = r.GetString(5),
                    InheritanceBroken = r.GetBoolean(6),
                    Error = NullableText(r, 7),
                    InheritanceDiffers = r.GetBoolean(8),
                };
                scan.Folders.Add(node);
                byId[node.Id] = node;
            }
        }
        if (byId.Count == 0) return;

        using (var cmd = Command(db, tx, """
            SELECT a.folder_id, a.sid, a.rights, a.is_deny, a.is_inherited, a.inheritance, a.propagation
            FROM ace a JOIN folder f ON f.id = a.folder_id
            WHERE f.scan_id = $id ORDER BY a.folder_id, a.ord;
            """, scan.Id))
        using (var r = cmd.ExecuteReader())
        {
            FolderNode? current = null;
            while (r.Read())
            {
                var folderId = r.GetInt64(0);
                if (current is null || current.Id != folderId) current = byId[folderId];
                current.Aces.Add(new AceEntry
                {
                    Sid = r.GetString(1),
                    Rights = (FileRights)unchecked((uint)r.GetInt64(2)),
                    IsDeny = r.GetBoolean(3),
                    IsInherited = r.GetBoolean(4),
                    Inheritance = (AceInheritance)r.GetInt32(5),
                    Propagation = (AcePropagation)r.GetInt32(6),
                });
            }
        }
    }

    private static void LoadPrincipals(SqliteConnection db, SqliteTransaction tx, ScanResult scan)
    {
        using (var cmd = Command(db, tx, """
            SELECT sid, principal_sid, name, display_name, kind, is_local, is_disabled, members_unknown
            FROM principal WHERE scan_id = $id ORDER BY ord;
            """, scan.Id))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var key = r.GetString(0);
                scan.Principals[key] = new Principal
                {
                    Sid = NullableText(r, 1) ?? key,
                    Name = r.GetString(2),
                    DisplayName = r.GetString(3),
                    Kind = ParseKind(r.GetString(4)),
                    IsLocal = r.GetBoolean(5),
                    IsDisabled = r.GetBoolean(6),
                    MembersUnknown = r.GetBoolean(7),
                };
            }
        }
        if (scan.Principals.Count == 0) return;

        using (var cmd = Command(db, tx, "SELECT group_sid, member_sid FROM member WHERE scan_id = $id ORDER BY group_sid, ord;", scan.Id))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                scan.Principals[r.GetString(0)].Members.Add(r.GetString(1));
        }
    }

    private static void LoadFindings(SqliteConnection db, SqliteTransaction tx, ScanResult scan)
    {
        using var cmd = Command(db, tx, "SELECT rule_id, severity, path, sid, message FROM finding WHERE scan_id = $id ORDER BY ord;", scan.Id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            scan.Findings.Add(new Finding
            {
                RuleId = r.GetString(0),
                Severity = (Severity)r.GetInt32(1),
                Path = r.GetString(2),
                Sid = NullableText(r, 3),
                Message = r.GetString(4),
            });
        }
    }

    private static void LoadErrors(SqliteConnection db, SqliteTransaction tx, ScanResult scan)
    {
        using var cmd = Command(db, tx, "SELECT path, message FROM error WHERE scan_id = $id ORDER BY ord;", scan.Id);
        using var r = cmd.ExecuteReader();
        while (r.Read()) scan.Errors.Add(new ScanError(r.GetString(0), r.GetString(1)));
    }

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction tx, string sql, long scanId)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", scanId);
        return cmd;
    }

    private static string? NullableText(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static DateTimeOffset ParseTime(string s) =>
        DateTimeOffset.ParseExact(s, "o", CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static ScanRequest ParseRequest(string json) =>
        JsonSerializer.Deserialize<ScanRequest>(json, ScanWriter.Json)
        ?? throw new InvalidDataException("Die Scan-Einstellungen fehlen.");

    // Eine spätere Version kann neue Kontoarten speichern; für diese Version sind sie „unbekannt“, nicht falsch
    private static PrincipalKind ParseKind(string s) =>
        Enum.TryParse<PrincipalKind>(s, ignoreCase: false, out var kind) && Enum.IsDefined(kind) && !int.TryParse(s, out _)
            ? kind
            : PrincipalKind.Unbekannt;
}
