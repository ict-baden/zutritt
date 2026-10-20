using System.Globalization;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt;

/// <summary>
/// Bezeichnungen für Aufzählungen und Zahlen in der Sprache der Oberfläche, Zahlen und Daten im Gebietsschema –
/// gemeinsam für Oberfläche und Befehlszeile.
/// </summary>
public static class Labels
{
    public static string Of(Severity s) => s switch
    {
        Severity.Hoch => Strings.Severity_High,
        Severity.Mittel => Strings.Severity_Medium,
        Severity.Niedrig => Strings.Severity_Low,
        _ => Strings.Severity_Info,
    };

    public static string Of(PrincipalKind k) => k switch
    {
        PrincipalKind.Benutzer => Strings.Kind_User,
        PrincipalKind.Gruppe => Strings.Kind_Group,
        PrincipalKind.Bekannt => Strings.Kind_KnownAccount,
        PrincipalKind.Computer => Strings.Kind_Computer,
        PrincipalKind.Verwaist => Strings.Kind_Orphaned,
        _ => Strings.Kind_Unknown,
    };

    public static string Of(ChangeKind k) => k switch
    {
        ChangeKind.Hinzugefuegt => Strings.Change_Added,
        ChangeKind.Entfernt => Strings.Change_Removed,
        _ => Strings.Change_Changed,
    };

    public static string Of(AccessLevel level) => Rights.Label(level);

    public static string Of(ShareAccess access) => Rights.Label(access);

    /// <summary>Recht eines Eintrags für Menschen – bei Verweigerungen, was gesperrt ist (siehe <see cref="Rights.EntryLabel"/>).</summary>
    public static string RightsText(AceEntry ace) => Rights.EntryLabel(ace.Rights, ace.IsDeny);

    /// <summary>Wofür ein Eintrag gilt – mit denselben Worten wie der Windows-Dialog „Erweiterte Sicherheitseinstellungen“.</summary>
    public static string AppliesTo(AceEntry ace, bool isFile)
    {
        if (isFile) return Strings.AppliesTo_ThisFile;
        bool ci = (ace.Inheritance & AceInheritance.ContainerInherit) != 0;
        bool oi = (ace.Inheritance & AceInheritance.ObjectInherit) != 0;
        string text = (ace.InheritOnly, ci, oi) switch
        {
            (false, true, true) => Strings.AppliesTo_FolderSubfoldersFiles,
            (false, true, false) => Strings.AppliesTo_FolderSubfolders,
            (false, false, true) => Strings.AppliesTo_FolderFiles,
            (false, false, false) => Strings.AppliesTo_ThisFolder,
            (true, true, true) => Strings.AppliesTo_SubfoldersFiles,
            (true, true, false) => Strings.AppliesTo_Subfolders,
            (true, false, true) => Strings.AppliesTo_Files,
            _ => Strings.AppliesTo_SubfoldersFiles,
        };
        return (ace.Propagation & AcePropagation.NoPropagateInherit) != 0 && (ci || oi) ? Texts.Format(Strings.AppliesTo_OneLevelShort, text) : text;
    }

    public static string Number(long n) => n.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Zeitpunkt in Ortszeit, z. B. „14.10.2026, 09:00“ (Datum und Uhrzeit kurz im Gebietsschema).</summary>
    public static string Date(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        return Texts.Format(Strings.Ui_DateTime, local.ToString("d", CultureInfo.CurrentCulture), local.ToString("t", CultureInfo.CurrentCulture));
    }

    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1
            ? Texts.Format(Strings.Ui_DurationHours, (int)d.TotalHours, d.Minutes, d.Seconds)
            : Texts.Format(Strings.Ui_DurationMinutes, (int)d.TotalMinutes, d.Seconds);
    }

    /// <summary>Ziele eines Laufs für Überschriften: „\\FS01“ oder „\\FS01\Labor und 2 weitere“.</summary>
    public static string Targets(IReadOnlyList<string> targets) => targets.Count switch
    {
        0 => Strings.Ui_NoTarget,
        1 => TargetName(targets[0]),
        2 => Texts.Format(Strings.Text_ListAnd, TargetName(targets[0]), TargetName(targets[1])),
        _ => Texts.Format(Strings.Ui_TargetAndMore, TargetName(targets[0]), targets.Count - 1),
    };

    public static string TargetName(string target) => target.Trim() == "." ? Strings.Ui_LocalShares : target.Trim();

    /// <summary>„MUSTER\QC-Team“ → „QC-Team“ (für enge Spalten; der volle Name steht dann im Tooltip).</summary>
    public static string WithoutDomain(string name) => AccountName.ShortName(name);

    /// <summary>Gruppenkette „über welche Gruppen“ kurz: „QC-Team › Labor-Alle“ statt mit Domäne vor jedem Namen.</summary>
    public static string ViaChain(IReadOnlyList<string> via) =>
        via.Count == 0 ? Strings.Pdf_DirectlyGranted : string.Join(" › ", via.Select(WithoutDomain));

    /// <summary>
    /// Pfad im eigenen Profil mit Umgebungsvariable statt ausgeschriebenem Benutzerordner, z. B.
    /// „%LocalAppData%\Zutritt\zutritt.db“ – so steht auf der Info-Seite und auf Screenshots kein Benutzername.
    /// Andere Pfade bleiben, wie sie sind.
    /// </summary>
    public static string WithVariables(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var folders = new List<(string Folder, string Variable)>
        {
            (Path.GetTempPath(), "%TEMP%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LocalAppData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%AppData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
        };
        char[] separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        // Der längste passende Ordner gewinnt (%TEMP% liegt meist unter %LocalAppData%)
        foreach (var (folder, variable) in folders.Where(f => f.Folder.Length > 0).OrderByDescending(f => f.Folder.TrimEnd(separators).Length))
        {
            var root = folder.TrimEnd(separators);
            if (path.Length >= root.Length && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && (path.Length == root.Length || separators.Contains(path[root.Length])))
                return variable + path[root.Length..];
        }
        return path;
    }

    /// <summary>Kürzt lange Pfade in der Mitte, damit Anfang (Freigabe) und Ende (Ordner) sichtbar bleiben.</summary>
    public static string ShortenPath(string path, int max = 90)
    {
        if (path.Length <= max) return path;
        // Zu wenig Platz für Anfang, „…“ und Ende (schmale Konsole): nur so viel vom Anfang, wie passt
        if (max < 5) return max <= 0 ? "" : path[..max];
        int head = max / 2 - 2, tail = max - head - 1;
        return string.Concat(path.AsSpan(0, head), "…", path.AsSpan(path.Length - tail));
    }

    /// <summary>Vorschlag für den Dateinamen eines Berichts: Zutritt-Bericht-&lt;Ziel&gt;-&lt;Datum&gt;.</summary>
    public static string ReportFileName(ScanResult scan)
    {
        var targets = scan.Request.Targets;
        string target = targets.Count == 0 ? scan.Machine : targets[0].Trim() == "." ? scan.Machine : targets[0];
        var cleaned = new string(target.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
        cleaned = cleaned.Trim('-');
        if (cleaned.Length == 0) cleaned = Strings.Ui_FileRun;
        if (cleaned.Length > 60) cleaned = cleaned[..60].TrimEnd('-');
        if (targets.Count > 1) cleaned += Texts.Format(Strings.Ui_FileAndMore, targets.Count - 1);
        return Texts.Format(Strings.Ui_ReportFileName, cleaned,
            scan.StartedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
