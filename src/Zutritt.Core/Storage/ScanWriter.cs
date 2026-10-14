using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zutritt.Core.Model;

namespace Zutritt.Core.Storage;

/// <summary>
/// Schreibt einen Lauf in einer einzigen Transaktion. Je Tabelle ein vorbereiteter Befehl, der für alle
/// Zeilen wiederverwendet wird – so brauchen 100.000 ACEs etwa eine Sekunde statt Minuten.
/// </summary>
internal static class ScanWriter
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Gibt die neue Lauf-Nummer und die Ordner-Nummern (in der Reihenfolge von <c>scan.Folders</c>) zurück.</summary>
    public static (long ScanId, long[] FolderIds) Insert(SqliteConnection db, ScanResult scan)
    {
        using var tx = db.BeginTransaction();
        var scanId = InsertScan(db, tx, scan);
        InsertShares(db, tx, scanId, scan.Shares);
        var folderIds = InsertFolders(db, tx, scanId, scan.Folders);
        InsertPrincipals(db, tx, scanId, scan.Principals);
        InsertFindings(db, tx, scanId, scan.Findings);
        InsertErrors(db, tx, scanId, scan.Errors);
        tx.Commit();
        return (scanId, folderIds);
    }

    private static long InsertScan(SqliteConnection db, SqliteTransaction tx, ScanResult scan)
    {
        var targets = scan.Request?.Targets ?? [];
        using var cmd = Command(db, tx, """
            INSERT INTO scan (started_at, started_utc, finished_at, machine, scanned_by, tool_version, request_json,
                              targets, targets_key, folders_visited, files_visited, folder_count, finding_count, high_finding_count)
            VALUES ($started_at, $started_utc, $finished_at, $machine, $scanned_by, $tool_version, $request_json,
                    $targets, $targets_key, $folders_visited, $files_visited, $folder_count, $finding_count, $high_finding_count)
            RETURNING id;
            """);
        cmd.Parameters.AddWithValue("$started_at", scan.StartedAt.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$started_utc", scan.StartedAt.UtcTicks);
        cmd.Parameters.AddWithValue("$finished_at", scan.FinishedAt.ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$machine", Text(scan.Machine));
        cmd.Parameters.AddWithValue("$scanned_by", Text(scan.ScannedBy));
        cmd.Parameters.AddWithValue("$tool_version", Text(scan.ToolVersion));
        cmd.Parameters.AddWithValue("$request_json", JsonSerializer.Serialize(scan.Request ?? new ScanRequest { Targets = [] }, Json));
        cmd.Parameters.AddWithValue("$targets", TargetSet.Display(targets));
        cmd.Parameters.AddWithValue("$targets_key", TargetSet.Key(targets));
        cmd.Parameters.AddWithValue("$folders_visited", scan.FoldersVisited);
        cmd.Parameters.AddWithValue("$files_visited", scan.FilesVisited);
        cmd.Parameters.AddWithValue("$folder_count", scan.Folders.Count);
        cmd.Parameters.AddWithValue("$finding_count", scan.Findings.Count);
        cmd.Parameters.AddWithValue("$high_finding_count", scan.Findings.Count(f => f.Severity == Severity.Hoch));
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertShares(SqliteConnection db, SqliteTransaction tx, long scanId, List<ShareInfo> shares)
    {
        if (shares.Count == 0) return;
        using var share = Command(db, tx, """
            INSERT INTO share (scan_id, ord, server, name, local_path, remark, is_admin_share, permissions_readable)
            VALUES ($scan_id, $ord, $server, $name, $local_path, $remark, $is_admin_share, $permissions_readable)
            RETURNING id;
            """);
        Param(share, "$scan_id", SqliteType.Integer, scanId);
        var sOrd = Param(share, "$ord", SqliteType.Integer);
        var sServer = Param(share, "$server", SqliteType.Text);
        var sName = Param(share, "$name", SqliteType.Text);
        var sLocal = Param(share, "$local_path", SqliteType.Text);
        var sRemark = Param(share, "$remark", SqliteType.Text);
        var sAdmin = Param(share, "$is_admin_share", SqliteType.Integer);
        var sReadable = Param(share, "$permissions_readable", SqliteType.Integer);
        share.Prepare();

        using var ace = Command(db, tx, "INSERT INTO share_ace (share_id, ord, sid, access, is_deny) VALUES ($share_id, $ord, $sid, $access, $is_deny);");
        var aShare = Param(ace, "$share_id", SqliteType.Integer);
        var aOrd = Param(ace, "$ord", SqliteType.Integer);
        var aSid = Param(ace, "$sid", SqliteType.Text);
        var aAccess = Param(ace, "$access", SqliteType.Integer);
        var aDeny = Param(ace, "$is_deny", SqliteType.Integer);
        ace.Prepare();

        for (var i = 0; i < shares.Count; i++)
        {
            var s = shares[i];
            sOrd.Value = i;
            sServer.Value = Text(s.Server);
            sName.Value = Text(s.Name);
            sLocal.Value = Text(s.LocalPath);
            sRemark.Value = Text(s.Remark);
            sAdmin.Value = Bit(s.IsAdminShare);
            sReadable.Value = Bit(s.PermissionsReadable);
            var shareId = (long)share.ExecuteScalar()!;

            var perms = s.Permissions ?? [];
            for (var j = 0; j < perms.Count; j++)
            {
                var p = perms[j];
                aShare.Value = shareId;
                aOrd.Value = j;
                aSid.Value = Text(p.Sid);
                aAccess.Value = (long)p.Access;
                aDeny.Value = Bit(p.IsDeny);
                ace.ExecuteNonQuery();
            }
        }
    }

    private static long[] InsertFolders(SqliteConnection db, SqliteTransaction tx, long scanId, List<FolderNode> folders)
    {
        var ids = new long[folders.Count];
        if (folders.Count == 0) return ids;

        using var folder = Command(db, tx, """
            INSERT INTO folder (scan_id, ord, path, depth, is_file, share_unc, owner_sid, inheritance_broken, inheritance_differs, error)
            VALUES ($scan_id, $ord, $path, $depth, $is_file, $share_unc, $owner_sid, $inheritance_broken, $inheritance_differs, $error)
            RETURNING id;
            """);
        Param(folder, "$scan_id", SqliteType.Integer, scanId);
        var fOrd = Param(folder, "$ord", SqliteType.Integer);
        var fPath = Param(folder, "$path", SqliteType.Text);
        var fDepth = Param(folder, "$depth", SqliteType.Integer);
        var fFile = Param(folder, "$is_file", SqliteType.Integer);
        var fShare = Param(folder, "$share_unc", SqliteType.Text);
        var fOwner = Param(folder, "$owner_sid", SqliteType.Text);
        var fBroken = Param(folder, "$inheritance_broken", SqliteType.Integer);
        var fDiffers = Param(folder, "$inheritance_differs", SqliteType.Integer);
        var fError = Param(folder, "$error", SqliteType.Text);
        folder.Prepare();

        using var ace = Command(db, tx, """
            INSERT INTO ace (folder_id, ord, sid, rights, is_deny, is_inherited, inheritance, propagation)
            VALUES ($folder_id, $ord, $sid, $rights, $is_deny, $is_inherited, $inheritance, $propagation);
            """);
        var aFolder = Param(ace, "$folder_id", SqliteType.Integer);
        var aOrd = Param(ace, "$ord", SqliteType.Integer);
        var aSid = Param(ace, "$sid", SqliteType.Text);
        var aRights = Param(ace, "$rights", SqliteType.Integer);
        var aDeny = Param(ace, "$is_deny", SqliteType.Integer);
        var aInherited = Param(ace, "$is_inherited", SqliteType.Integer);
        var aInheritance = Param(ace, "$inheritance", SqliteType.Integer);
        var aPropagation = Param(ace, "$propagation", SqliteType.Integer);
        ace.Prepare();

        for (var i = 0; i < folders.Count; i++)
        {
            var f = folders[i];
            fOrd.Value = i;
            fPath.Value = Text(f.Path);
            fDepth.Value = f.Depth;
            fFile.Value = Bit(f.IsFile);
            fShare.Value = Nullable(f.ShareUnc);
            fOwner.Value = Text(f.OwnerSid);
            fBroken.Value = Bit(f.InheritanceBroken);
            fDiffers.Value = Bit(f.InheritanceDiffers);
            fError.Value = Nullable(f.Error);
            var folderId = (long)folder.ExecuteScalar()!;
            ids[i] = folderId;

            for (var j = 0; j < f.Aces.Count; j++)
            {
                var a = f.Aces[j];
                aFolder.Value = folderId;
                aOrd.Value = j;
                aSid.Value = Text(a.Sid);
                aRights.Value = (long)unchecked((uint)a.Rights);
                aDeny.Value = Bit(a.IsDeny);
                aInherited.Value = Bit(a.IsInherited);
                aInheritance.Value = (long)a.Inheritance;
                aPropagation.Value = (long)a.Propagation;
                ace.ExecuteNonQuery();
            }
        }
        return ids;
    }

    private static void InsertPrincipals(SqliteConnection db, SqliteTransaction tx, long scanId, Dictionary<string, Principal> principals)
    {
        if (principals.Count == 0) return;
        using var principal = Command(db, tx, """
            INSERT INTO principal (scan_id, sid, ord, principal_sid, name, display_name, kind, is_local, is_disabled, members_unknown)
            VALUES ($scan_id, $sid, $ord, $principal_sid, $name, $display_name, $kind, $is_local, $is_disabled, $members_unknown);
            """);
        Param(principal, "$scan_id", SqliteType.Integer, scanId);
        var pSid = Param(principal, "$sid", SqliteType.Text);
        var pOrd = Param(principal, "$ord", SqliteType.Integer);
        var pOwnSid = Param(principal, "$principal_sid", SqliteType.Text);
        var pName = Param(principal, "$name", SqliteType.Text);
        var pDisplay = Param(principal, "$display_name", SqliteType.Text);
        var pKind = Param(principal, "$kind", SqliteType.Text);
        var pLocal = Param(principal, "$is_local", SqliteType.Integer);
        var pDisabled = Param(principal, "$is_disabled", SqliteType.Integer);
        var pUnknown = Param(principal, "$members_unknown", SqliteType.Integer);
        principal.Prepare();

        using var member = Command(db, tx, "INSERT INTO member (scan_id, group_sid, ord, member_sid) VALUES ($scan_id, $group_sid, $ord, $member_sid);");
        Param(member, "$scan_id", SqliteType.Integer, scanId);
        var mGroup = Param(member, "$group_sid", SqliteType.Text);
        var mOrd = Param(member, "$ord", SqliteType.Integer);
        var mSid = Param(member, "$member_sid", SqliteType.Text);
        member.Prepare();

        var ord = 0;
        // Reihenfolge des Wörterbuchs bleibt erhalten – Oberfläche und Berichte zählen Konten darin auf
        foreach (var (key, p) in principals)
        {
            pSid.Value = key;
            pOrd.Value = ord++;
            pOwnSid.Value = string.Equals(p.Sid, key, StringComparison.Ordinal) ? DBNull.Value : Nullable(p.Sid);
            pName.Value = Text(p.Name);
            pDisplay.Value = Text(p.DisplayName);
            pKind.Value = p.Kind.ToString();
            pLocal.Value = Bit(p.IsLocal);
            pDisabled.Value = Bit(p.IsDisabled);
            pUnknown.Value = Bit(p.MembersUnknown);
            principal.ExecuteNonQuery();

            for (var j = 0; j < p.Members.Count; j++)
            {
                mGroup.Value = key;
                mOrd.Value = j;
                mSid.Value = Text(p.Members[j]);
                member.ExecuteNonQuery();
            }
        }
    }

    private static void InsertFindings(SqliteConnection db, SqliteTransaction tx, long scanId, List<Finding> findings)
    {
        if (findings.Count == 0) return;
        using var cmd = Command(db, tx, """
            INSERT INTO finding (scan_id, ord, rule_id, severity, path, sid, message)
            VALUES ($scan_id, $ord, $rule_id, $severity, $path, $sid, $message);
            """);
        Param(cmd, "$scan_id", SqliteType.Integer, scanId);
        var ord = Param(cmd, "$ord", SqliteType.Integer);
        var rule = Param(cmd, "$rule_id", SqliteType.Text);
        var severity = Param(cmd, "$severity", SqliteType.Integer);
        var path = Param(cmd, "$path", SqliteType.Text);
        var sid = Param(cmd, "$sid", SqliteType.Text);
        var message = Param(cmd, "$message", SqliteType.Text);
        cmd.Prepare();

        for (var i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            ord.Value = i;
            rule.Value = Text(f.RuleId);
            severity.Value = (long)f.Severity;
            path.Value = Text(f.Path);
            sid.Value = Nullable(f.Sid);
            message.Value = Text(f.Message);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertErrors(SqliteConnection db, SqliteTransaction tx, long scanId, List<ScanError> errors)
    {
        if (errors.Count == 0) return;
        using var cmd = Command(db, tx, "INSERT INTO error (scan_id, ord, path, message) VALUES ($scan_id, $ord, $path, $message);");
        Param(cmd, "$scan_id", SqliteType.Integer, scanId);
        var ord = Param(cmd, "$ord", SqliteType.Integer);
        var path = Param(cmd, "$path", SqliteType.Text);
        var message = Param(cmd, "$message", SqliteType.Text);
        cmd.Prepare();

        for (var i = 0; i < errors.Count; i++)
        {
            ord.Value = i;
            path.Value = Text(errors[i].Path);
            message.Value = Text(errors[i].Message);
            cmd.ExecuteNonQuery();
        }
    }

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction tx, string sql)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static SqliteParameter Param(SqliteCommand cmd, string name, SqliteType type, object? value = null)
    {
        var p = cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
        return p;
    }

    // Pflichtfelder sind im Modell nicht-null; ein null von außen wird zu "" statt die ganze Speicherung abzubrechen
    private static string Text(string? s) => s ?? "";
    private static object Nullable(string? s) => s is null ? DBNull.Value : s;
    private static long Bit(bool b) => b ? 1L : 0L;
}
