using System.Globalization;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using Zutritt.Core.Model;

namespace Zutritt.Core.Reports;

/// <summary>Deutsche Bezeichnungen und Formatierungen, die Excel- und PDF-Bericht gemeinsam nutzen.</summary>
internal static class ReportText
{
    /// <summary>Berichte sind immer deutsch – unabhängig von der Spracheinstellung des Rechners.</summary>
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Namen sortieren wie ein Mensch: Groß/Klein egal, Umlaute an ihrem Platz.</summary>
    public static readonly StringComparer NameOrder = StringComparer.Create(German, CompareOptions.IgnoreCase);

    public const string Title = "Berechtigungsbericht";
    public const string Product = "Zutritt";
    public const string Dash = "–";

    public static string Severity(Severity severity) => severity switch
    {
        Model.Severity.Hoch => "Hoch",
        Model.Severity.Mittel => "Mittel",
        Model.Severity.Niedrig => "Niedrig",
        _ => "Info",
    };

    public static string Kind(PrincipalKind kind) => kind switch
    {
        PrincipalKind.Benutzer => "Benutzer",
        PrincipalKind.Gruppe => "Gruppe",
        PrincipalKind.Bekannt => "Integriert",
        PrincipalKind.Computer => "Computer",
        PrincipalKind.Verwaist => "Verwaist",
        _ => "Unbekannt",
    };

    public static string Change(ChangeKind kind) => kind switch
    {
        ChangeKind.Hinzugefuegt => "Hinzugefügt",
        ChangeKind.Entfernt => "Entfernt",
        _ => "Geändert",
    };

    public static string YesNo(bool value) => value ? "Ja" : "Nein";

    public static string AllowDeny(bool isDeny) => isDeny ? "Verweigern" : "Erlauben";

    public static string Origin(bool isInherited) => isInherited ? "geerbt" : "explizit";

    /// <summary>
    /// „Anwenden auf“ wie im Windows-Dialog „Erweiterte Sicherheitseinstellungen“, damit Prüfer die
    /// Angaben ohne Übersetzung wiederfinden.
    /// </summary>
    public static string AppliesTo(InheritanceFlags inheritance, PropagationFlags propagation, bool isFile)
    {
        if (isFile) return "Nur diese Datei";
        bool containers = (inheritance & InheritanceFlags.ContainerInherit) != 0;
        bool objects = (inheritance & InheritanceFlags.ObjectInherit) != 0;
        bool inheritOnly = (propagation & PropagationFlags.InheritOnly) != 0;
        bool oneLevel = (propagation & PropagationFlags.NoPropagateInherit) != 0;

        string text = (containers, objects, inheritOnly) switch
        {
            (false, false, _) => "Nur diesen Ordner",
            (true, true, false) => "Diesen Ordner, Unterordner und Dateien",
            (true, false, false) => "Diesen Ordner und Unterordner",
            (false, true, false) => "Diesen Ordner und Dateien",
            (true, true, true) => "Nur Unterordner und Dateien",
            (true, false, true) => "Nur Unterordner",
            (false, true, true) => "Nur Dateien",
        };
        // Ohne Vererbungs-Flags spielt „nur eine Ebene“ keine Rolle
        return oneLevel && (containers || objects) ? text + " (nur eine Ebene)" : text;
    }

    /// <summary>Kurzbeschreibung eines Eintrags, z. B. „Ändern · Erlauben · explizit · Diesen Ordner, Unterordner und Dateien“.</summary>
    public static string Ace(AceEntry ace, bool isFile, bool withMask = false)
    {
        var text = $"{Rights.EntryLabel(ace.Rights, ace.IsDeny)} · {AllowDeny(ace.IsDeny)} · {Origin(ace.IsInherited)} · {AppliesTo(ace.Inheritance, ace.Propagation, isFile)}";
        return withMask ? $"{text} ({Mask(ace.Rights)})" : text;
    }

    public static string Mask(FileSystemRights rights) => "0x" + ((uint)rights).ToString("X8", CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString("N0", German);

    public static string Quote(string text) => $"„{text}“";

    /// <summary>Zeitpunkt in Ortszeit dieses Rechners, z. B. „14.10.2026 09:00“.</summary>
    public static string DateTime(DateTimeOffset time) =>
        time == default ? Dash : time.ToLocalTime().ToString("dd.MM.yyyy HH:mm", German);

    /// <summary>Zeitraum eines Laufs; am selben Tag ohne zweites Datum.</summary>
    public static string Period(DateTimeOffset started, DateTimeOffset finished)
    {
        if (started == default) return Dash;
        if (finished == default || finished < started) return DateTime(started) + " Uhr";
        var a = started.ToLocalTime();
        var b = finished.ToLocalTime();
        var end = a.Date == b.Date ? b.ToString("HH:mm", German) : DateTime(finished);
        return $"{DateTime(started)} {Dash} {end} Uhr";
    }

    /// <summary>Was gescannt wurde, in einer Zeile – damit der Prüfumfang im Bericht nachvollziehbar ist.</summary>
    public static string Scope(ScanRequest request)
    {
        var parts = new List<string>
        {
            request.IncludeFiles ? "Ordner und Dateien" : "nur Ordner (Dateien nicht einzeln)",
            request.MaxDepth is { } depth ? $"bis Tiefe {depth}" : "alle Ebenen",
            request.ResolveGroups ? "Gruppen bis zur Person aufgelöst" : "Gruppen nicht aufgelöst",
            request.SkipAdminShares ? "administrative Freigaben übersprungen" : "administrative Freigaben einbezogen",
        };
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Version für die Anzeige: ein angehängter Commit-Hash („1.0.0+f733926704a0…“) wird auf sieben Zeichen
    /// gekürzt – nachvollziehbar genug, ohne dass 40 Hex-Zeichen das Deckblatt füllen.
    /// </summary>
    public static string Version(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var plus = version.IndexOf('+');
        if (plus < 0) return version;
        var build = version[(plus + 1)..];
        return build.Length > 7 && build.All(Uri.IsHexDigit) ? version[..(plus + 8)] : version;
    }

    /// <summary>Version dieses Zutritt (ohne angehängten Commit-Hash).</summary>
    public static string ProductVersion { get; } = ReadProductVersion();

    private static string ReadProductVersion()
    {
        var asm = typeof(ReportText).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString() ?? "";
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>
    /// Entfernt Zeichen, die in XML (xlsx) nicht erlaubt sind oder die Schrift nicht darstellen kann:
    /// Steuerzeichen und einzelne Surrogat-Hälften (NTFS erlaubt beliebiges UTF-16 in Namen).
    /// Ersetzt sie durch „�“, damit sichtbar bleibt, dass dort etwas stand.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int i = 0;
        while (i < text.Length && IsValidAt(text, i, out int step)) i += step;
        if (i == text.Length) return text;

        var sb = new StringBuilder(text.Length);
        sb.Append(text, 0, i);
        while (i < text.Length)
        {
            if (IsValidAt(text, i, out int length))
            {
                sb.Append(text, i, length);
                i += length;
            }
            else
            {
                sb.Append('�');
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsValidAt(string text, int i, out int length)
    {
        char c = text[i];
        length = 1;
        if (char.IsHighSurrogate(c))
        {
            if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { length = 2; return true; }
            return false;
        }
        if (char.IsLowSurrogate(c)) return false;
        return c is '\t' or '\n' or '\r' || (c >= 0x20 && c != 0xFFFE && c != 0xFFFF);
    }
}

/// <summary>Farben der Berichte: Schwere-Ampel und die dezente Produkt-Akzentfarbe (wie Vitrine).</summary>
internal static class ReportColors
{
    public const string AccentBlue = "#5B8CFF";
    public const string AccentViolet = "#7A3FD1";
    public const string AccentLight = "#EEF2FF";
    public const string Text = "#1F2937";
    public const string Muted = "#6B7280";
    public const string Line = "#D1D5DB";
    public const string Warning = "#C62828";

    /// <summary>Hintergrund und Schriftfarbe der Schwere-Zelle; Gelb und Grau brauchen dunkle Schrift.</summary>
    public static (string Fill, string Font) For(Severity severity) => severity switch
    {
        Severity.Hoch => ("#C62828", "#FFFFFF"),
        Severity.Mittel => ("#EF6C00", "#FFFFFF"),
        Severity.Niedrig => ("#FBC02D", "#212121"),
        _ => ("#BDBDBD", "#212121"),
    };
}
