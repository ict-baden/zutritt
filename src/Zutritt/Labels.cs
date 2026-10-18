using System.Globalization;
using Zutritt.Core.Model;

namespace Zutritt;

/// <summary>Deutsche Bezeichnungen für Aufzählungen und Zahlen – gemeinsam für Oberfläche und Befehlszeile.</summary>
public static class Labels
{
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Of(Severity s) => s switch
    {
        Severity.Hoch => "Hoch",
        Severity.Mittel => "Mittel",
        Severity.Niedrig => "Niedrig",
        _ => "Info",
    };

    public static string Of(PrincipalKind k) => k switch
    {
        PrincipalKind.Benutzer => "Benutzer",
        PrincipalKind.Gruppe => "Gruppe",
        PrincipalKind.Bekannt => "Bekanntes Konto",
        PrincipalKind.Computer => "Computer",
        PrincipalKind.Verwaist => "Verwaist",
        _ => "Unbekannt",
    };

    public static string Of(ChangeKind k) => k switch
    {
        ChangeKind.Hinzugefuegt => "Hinzugefügt",
        ChangeKind.Entfernt => "Entfernt",
        _ => "Geändert",
    };

    public static string Of(AccessLevel level) => Rights.Label(level);

    public static string Of(ShareAccess access) => Rights.Label(access);

    /// <summary>Recht eines Eintrags für Menschen – bei Verweigerungen, was gesperrt ist (siehe <see cref="Rights.EntryLabel"/>).</summary>
    public static string RightsText(AceEntry ace) => Rights.EntryLabel(ace.Rights, ace.IsDeny);

    /// <summary>Wofür ein Eintrag gilt – mit denselben Worten wie der Windows-Dialog „Erweiterte Sicherheitseinstellungen“.</summary>
    public static string AppliesTo(AceEntry ace, bool isFile)
    {
        if (isFile) return "Nur diese Datei";
        bool ci = (ace.Inheritance & AceInheritance.ContainerInherit) != 0;
        bool oi = (ace.Inheritance & AceInheritance.ObjectInherit) != 0;
        string text = (ace.InheritOnly, ci, oi) switch
        {
            (false, true, true) => "Diesen Ordner, Unterordner und Dateien",
            (false, true, false) => "Diesen Ordner und Unterordner",
            (false, false, true) => "Diesen Ordner und Dateien",
            (false, false, false) => "Nur diesen Ordner",
            (true, true, true) => "Nur Unterordner und Dateien",
            (true, true, false) => "Nur Unterordner",
            (true, false, true) => "Nur Dateien",
            _ => "Nur Unterordner und Dateien",
        };
        return (ace.Propagation & AcePropagation.NoPropagateInherit) != 0 && (ci || oi) ? text + " (eine Ebene)" : text;
    }

    public static string Number(long n) => n.ToString("N0", German);

    public static string Date(DateTimeOffset t) => t.ToLocalTime().ToString("dd.MM.yyyy, HH:mm", German);

    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00} h"
            : $"{(int)d.TotalMinutes}:{d.Seconds:00} min";
    }

    /// <summary>Ziele eines Laufs für Überschriften: „\\FS01“ oder „\\FS01\Labor und 2 weitere“.</summary>
    public static string Targets(IReadOnlyList<string> targets) => targets.Count switch
    {
        0 => "ohne Ziel",
        1 => TargetName(targets[0]),
        2 => $"{TargetName(targets[0])} und {TargetName(targets[1])}",
        _ => $"{TargetName(targets[0])} und {targets.Count - 1} weitere",
    };

    public static string TargetName(string target) => target.Trim() == "." ? "Freigaben dieses Rechners" : target.Trim();

    /// <summary>„MUSTER\QC-Team“ → „QC-Team“ (für enge Spalten; der volle Name steht dann im Tooltip).</summary>
    public static string WithoutDomain(string name) => AccountName.ShortName(name);

    /// <summary>Gruppenkette „über welche Gruppen“ kurz: „QC-Team › Labor-Alle“ statt mit Domäne vor jedem Namen.</summary>
    public static string ViaChain(IReadOnlyList<string> via) =>
        via.Count == 0 ? "direkt berechtigt" : string.Join(" › ", via.Select(WithoutDomain));

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
        if (cleaned.Length == 0) cleaned = "Lauf";
        if (cleaned.Length > 60) cleaned = cleaned[..60].TrimEnd('-');
        if (targets.Count > 1) cleaned += $"-und-{targets.Count - 1}-weitere";
        return $"Zutritt-Bericht-{cleaned}-{scan.StartedAt.ToLocalTime():yyyy-MM-dd}";
    }
}
