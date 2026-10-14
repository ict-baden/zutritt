using ClosedXML.Excel;
using Zutritt.Core.Model;

namespace Zutritt.Core.Reports;

/// <summary>
/// Excel-Bericht (xlsx, ClosedXML): Übersicht, Befunde, Ordner &amp; Rechte, Personen, Zugriffe je Person,
/// Gruppen, Freigaben, Prüfregeln, Änderungen (nur mit Vergleich) und Fehler. Jedes Blatt außer der
/// Übersicht ist eine Excel-Tabelle mit Autofilter und fixierter Kopfzeile – zum Filtern beim Audit.
/// </summary>
public static class ExcelReport
{
    /// <summary>
    /// Höchstens so viele Datenzeilen je Blatt. Excel könnte 1.048.575, aber ClosedXML hält die ganze Mappe
    /// im Speicher (grob 250–550 Byte je Zelle) – bei 15 Spalten wären das mehrere GB. Darüber wird gekürzt
    /// und in der Übersicht genannt.
    /// </summary>
    internal const int MaxRows = 500_000;

    /// <summary>Mehr Zeichen fasst eine Excel-Zelle nicht.</summary>
    private const int MaxCellText = 32_767;

    /// <summary>Für die Spaltenbreite genügt ein Blick auf die ersten Zeilen.</summary>
    private const int WidthSampleRows = 2_000;

    private static readonly XLColor Accent = XLColor.FromHtml(ReportColors.AccentBlue);
    private static readonly XLColor AccentViolet = XLColor.FromHtml(ReportColors.AccentViolet);
    private static readonly XLColor Muted = XLColor.FromHtml(ReportColors.Muted);
    private static readonly XLColor Warning = XLColor.FromHtml(ReportColors.Warning);

    /// <summary>Schreibt den Excel-Bericht für <paramref name="scan"/>; mit <paramref name="comparison"/> zusätzlich das Blatt „Änderungen“.</summary>
    /// <exception cref="ReportWriteException">Die Datei konnte nicht geschrieben werden (z. B. in Excel geöffnet).</exception>
    public static void Write(ScanResult scan, ComparisonResult? comparison, string path) => Write(scan, comparison, path, progress: null);

    /// <summary>Wie <see cref="Write(ScanResult, ComparisonResult?, string)"/>, meldet dabei den jeweiligen Arbeitsschritt.</summary>
    /// <param name="progress">Erhält kurze deutsche Sätze wie „Arbeitsmappe wird gespeichert …“ (aus dem Hintergrund-Thread).</param>
    /// <exception cref="ReportWriteException">Die Datei konnte nicht geschrieben werden (z. B. in Excel geöffnet).</exception>
    public static void Write(ScanResult scan, ComparisonResult? comparison, string path, IProgress<string>? progress)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ReportFile.FullPath(path); // Ziel prüfen, bevor die Auswertung läuft
        Write(ReportDataBuilder.Build(scan, comparison, progress), path, progress);
    }

    internal static void Write(ReportData data, string path, IProgress<string>? progress = null) => ReportFile.Write(path, stream =>
    {
        using var workbook = Create(data, MaxRows, progress);
        progress?.Report("Arbeitsmappe wird gespeichert …");
        workbook.SaveAs(stream);
    });

    /// <summary>Baut die Arbeitsmappe im Speicher; <paramref name="maxRows"/> nur für Tests kleiner.</summary>
    internal static XLWorkbook Create(ReportData data, int maxRows = MaxRows, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var workbook = new XLWorkbook();
        try
        {
            progress?.Report("Arbeitsmappe wird gefüllt …");
            var truncated = new List<string>();
            AddFindings(workbook, data, maxRows, truncated);
            AddFolders(workbook, data, maxRows, truncated);
            AddPeople(workbook, data, maxRows, truncated);
            AddPersonGrants(workbook, data, maxRows, truncated);
            AddGroups(workbook, data, maxRows, truncated);
            AddShares(workbook, data, maxRows, truncated);
            AddRules(workbook, data, maxRows, truncated);
            if (data.Comparison is not null) AddChanges(workbook, data.Comparison, maxRows, truncated);
            AddErrors(workbook, data, maxRows, truncated);
            // Die Übersicht zuletzt bauen (sie nennt gekürzte Blätter), aber vorne einsortieren
            AddOverview(workbook, data, truncated);
            SetProperties(workbook, data);
            return workbook;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    // ---- Blätter ----------------------------------------------------------------------------------

    private static void AddOverview(XLWorkbook workbook, ReportData data, List<string> truncated)
    {
        var ws = workbook.Worksheets.Add("Übersicht", 1);
        ws.TabColor = AccentViolet;
        ws.Column(1).Width = 36;
        ws.Column(2).Width = 95;
        ws.Column(2).Style.Alignment.WrapText = true;
        ws.Column(2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Column(1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.Column(2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        var h = data.Header;
        int row = 1;

        var title = ws.Cell(row++, 1);
        title.Value = ReportText.Title;
        title.Style.Font.FontSize = 18;
        title.Style.Font.Bold = true;
        title.Style.Font.FontColor = AccentViolet;
        var subtitle = ws.Cell(row++, 1);
        subtitle.Value = "Zutritt · Freigaben und Ordnerrechte";
        subtitle.Style.Font.FontColor = Muted;
        row++;

        Section("Lauf");
        Pair("Ziele", string.Join("\n", h.Targets), lines: Math.Max(1, h.Targets.Count));
        Pair("Zeitraum", ReportText.Period(h.StartedAt, h.FinishedAt));
        Pair("Rechner", h.Machine);
        Pair("Gescannt von", h.ScannedBy);
        Pair("Zutritt-Version (Scan)", h.ToolVersion);
        Pair("Bericht erstellt", $"{ReportText.DateTime(h.CreatedAt)} mit Zutritt {h.ReportVersion}".TrimEnd());
        Pair("Umfang", ReportText.Scope(h.Request));
        row++;

        Section("Zahlen");
        Number("Ordner geprüft", h.FoldersVisited);
        Number("Dateien geprüft", h.FilesVisited);
        Number("Ordner mit eigenen Rechten", h.FoldersStored);
        if (h.FilesStored > 0 || h.Request.IncludeFiles) Number("Dateien mit eigenen Rechten", h.FilesStored);
        Number("Freigaben", data.Shares.Count);
        Number("Konten und Gruppen", h.Principals);
        Number("Personen (Benutzerkonten)", data.People.Count);
        Number("Personen mit Zugriff", data.PeopleWithAccess);
        Number("Nicht lesbare Ordner", data.UnreadableFolders, warn: data.UnreadableFolders > 0);
        Number("Fehler beim Lesen", data.Errors.Count, warn: data.Errors.Count > 0);
        row++;

        Section("Befunde je Schwere");
        foreach (var severity in new[] { Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info })
        {
            var label = ws.Cell(row, 1);
            label.Value = ReportText.Severity(severity);
            StyleSeverity(label, severity);
            SetNumber(ws.Cell(row, 2), data.CountFindings(severity));
            row++;
        }
        Number("Gesamt", data.Findings.Count, bold: true);
        row++;

        if (data.Comparison is { } c)
        {
            Section($"Vergleich mit dem Lauf vom {ReportText.DateTime(c.OlderStartedAt)}");
            if (!c.HasChanges) Pair("Ergebnis", "Keine Änderungen seit dem letzten Lauf.");
            // Reihenfolge wie im Blatt „Änderungen“: Befunde, Ursachen, Folgen für Personen
            Number("Neue Befunde", c.NewFindings, warn: c.NewFindings > 0);
            Number("Behobene Befunde", c.ResolvedFindings);
            Number("Geänderte Berechtigungseinträge", c.AceChanges);
            Number("Geänderte Gruppenmitgliedschaften", c.MembershipChanges);
            Number("Ordner neu mit eigenen Rechten", c.FoldersAdded);
            Number("Ordner ohne eigene Rechte mehr", c.FoldersRemoved);
            Number("Personen-Zugriffe erweitert", c.MoreAccess, warn: c.MoreAccess > 0);
            Number("Personen-Zugriffe eingeschränkt", c.LessAccess);
            row++;
        }

        // Warnungen zuerst – auch die zur Kürzung dieser Mappe –, dann die Lesehilfen
        Section("Hinweise");
        foreach (var note in data.Notes.Where(n => n.Kind == NoteKind.Warnung))
            Note(note.Kind, note.Text);
        foreach (var text in truncated)
            Note(NoteKind.Warnung, text);
        foreach (var note in data.Notes.Where(n => n.Kind != NoteKind.Warnung))
            Note(note.Kind, note.Text);
        row++;

        // Wegweiser für Prüfer, die die Mappe zum ersten Mal sehen
        Section("Blätter dieser Mappe");
        foreach (var sheet in workbook.Worksheets.Where(w => w != ws))
            if (SheetDescriptions.TryGetValue(sheet.Name, out var description))
                Pair(ReportText.Quote(sheet.Name), description);

        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        Footer(ws);
        foreach (var sheet in workbook.Worksheets)
        {
            sheet.TabSelected = false;
            sheet.TabActive = false;
        }
        ws.TabSelected = true;
        ws.TabActive = true;
        return;

        void Section(string text)
        {
            var cell = ws.Cell(row, 1);
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 12;
            cell.Style.Font.FontColor = AccentViolet;
            ws.Range(row, 1, row, 2).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 2).Style.Border.BottomBorderColor = Accent;
            row++;
        }

        void Pair(string label, string value, int lines = 1)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 2).Value = Text(value);
            if (lines > 1) ws.Row(row).Height = 15 * Math.Min(lines, 25);
            row++;
        }

        void Number(string label, long value, bool warn = false, bool bold = false)
        {
            ws.Cell(row, 1).Value = label;
            var cell = ws.Cell(row, 2);
            SetNumber(cell, value);
            if (warn) { cell.Style.Font.FontColor = Warning; cell.Style.Font.Bold = true; }
            if (bold) { cell.Style.Font.Bold = true; ws.Cell(row, 1).Style.Font.Bold = true; }
            row++;
        }

        void Note(NoteKind kind, string text)
        {
            var label = ws.Cell(row, 1);
            label.Value = kind == NoteKind.Warnung ? "Achtung" : "Hinweis";
            var cell = ws.Cell(row, 2);
            cell.Value = Text(text);
            if (kind == NoteKind.Warnung)
            {
                label.Style.Font.FontColor = Warning;
                label.Style.Font.Bold = true;
                cell.Style.Font.FontColor = Warning;
            }
            // Zeilenhöhe selbst setzen – Excel passt sie für umbrochenen Text nicht zuverlässig an
            int lines = (int)Math.Ceiling(text.Length / 110.0);
            if (lines > 1) ws.Row(row).Height = 15 * lines;
            row++;
        }
    }

    /// <summary>Kurzbeschreibung je Blatt für den Wegweiser in der Übersicht.</summary>
    private static readonly Dictionary<string, string> SheetDescriptions = new()
    {
        ["Befunde"] = "Alle Befunde nach Schwere – mit Regel, Pfad, Konto, Meldung und Empfehlung.",
        ["Ordner & Rechte"] = "Jeder aufgeführte Ordner mit jedem Berechtigungseintrag (eine Zeile je Eintrag, auch geerbte); "
            + "„Vererbung weicht ab“ kennzeichnet z. B. verschobene Ordner mit Rechten vom früheren Ort.",
        ["Personen"] = "Je Benutzerkonto: Anzahl Ordner mit Zugriff, höchstes Recht und über welche Gruppen.",
        ["Zugriffe je Person"] = "Jede Person je Ordner mit effektivem Recht, NTFS- und Freigabe-Recht und der Gruppenkette.",
        ["Gruppen"] = "Mitglieder direkt und gesamt (auch verschachtelt) und in wie vielen Ordner-ACLs die Gruppe steht.",
        ["Freigaben"] = "Freigaben mit Pfad, Bemerkung und Freigabe-Berechtigungen.",
        ["Prüfregeln"] = "Prüfumfang: alle Regeln mit Erklärung, Empfehlung und Anzahl der Befunde.",
        ["Änderungen"] = "Unterschiede zum vorherigen Lauf: Befunde, Einträge, Mitgliedschaften, Ordner, Zugriffe von Personen.",
        ["Fehler"] = "Pfade, die nicht gelesen werden konnten – dort ist nichts geprüft.",
    };

    private static void SetNumber(IXLCell cell, long value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "#,##0";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
    }

    private static void AddFindings(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, "Befunde", "Befunde", data.Findings, data.Findings.Count, maxRows, "Keine Befunde.", truncated,
        [
            new("Schwere", f => ReportText.Severity(f.Severity), Style: (cell, f) => StyleSeverity(cell, f.Severity)),
            new("Regel", f => f.RuleId),
            new("Titel", f => Text(f.Title), MaxWidth: 40),
            new("Pfad", f => Text(f.Path), MaxWidth: 70),
            new("Konto", f => Text(f.Account), MaxWidth: 40),
            new("Meldung", f => Text(f.Message), MaxWidth: 80),
            new("Empfehlung", f => Text(f.Recommendation), MaxWidth: 80),
            new("SID", f => f.Sid ?? "", MaxWidth: 50),
        ]);

    private static void AddFolders(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated)
    {
        // Ordner ohne Einträge (z. B. nicht lesbar) bekommen trotzdem eine Zeile, sonst fehlten sie im Filter
        var rows = data.Folders.SelectMany(f => f.Aces.Count == 0
            ? new[] { (Folder: f, Ace: (AceRow?)null) }
            : f.Aces.Select(a => (Folder: f, Ace: (AceRow?)a)));
        int total = data.Folders.Sum(f => Math.Max(1, f.Aces.Count));

        AddTable(wb, "Ordner & Rechte", "OrdnerRechte", rows, total, maxRows, "Keine Ordner gespeichert.", truncated,
        [
            new("Pfad", r => Text(r.Folder.Path), MaxWidth: 80),
            new("Tiefe", r => r.Folder.Depth, Format: "0"),
            new("Typ", r => r.Folder.IsFile ? "Datei" : "Ordner"),
            new("Vererbung unterbrochen", r => ReportText.YesNo(r.Folder.InheritanceBroken)),
            // Verschobene Ordner, die alte geerbte Rechte mitgenommen haben (Regel ZT14)
            new("Vererbung weicht ab", r => ReportText.YesNo(r.Folder.InheritanceDiffers)),
            new("Besitzer", r => Text(r.Folder.Owner), MaxWidth: 40),
            new("Konto", r => Text(r.Ace?.Account), MaxWidth: 40),
            new("Art", r => r.Ace is null ? "" : ReportText.Kind(r.Ace.Kind)),
            new("Recht", r => r.Ace?.Right ?? "", MaxWidth: 40),
            new("Erlauben/Verweigern", r => r.Ace is null ? "" : ReportText.AllowDeny(r.Ace.IsDeny),
                Style: (cell, _) => { cell.Style.Font.FontColor = Warning; cell.Style.Font.Bold = true; }, StyleIf: r => r.Ace?.IsDeny == true),
            new("Geerbt/explizit", r => r.Ace is null ? "" : ReportText.Origin(r.Ace.IsInherited)),
            new("Gilt für", r => r.Ace?.AppliesTo ?? "", MaxWidth: 45),
            new("Freigabe", r => Text(r.Folder.ShareUnc), MaxWidth: 40),
            new("Hinweis", r => r.Folder.Error is null ? "" : Text("Nicht lesbar: " + r.Folder.Error), MaxWidth: 60,
                Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: r => r.Folder.Error is not null),
            new("SID", r => r.Ace?.Sid ?? "", MaxWidth: 50),
            new("Rechte-Maske", r => r.Ace is null ? "" : ReportText.Mask(r.Ace.Rights)),
        ]);
    }

    private static void AddPeople(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated)
    {
        List<Col<PersonRow>> cols =
        [
            new("Person", p => Text(p.Name), MaxWidth: 40),
            new("Anzeigename", p => Text(p.DisplayName), MaxWidth: 40),
            new("Deaktiviert", p => ReportText.YesNo(p.IsDisabled)),
            new("Ordner mit Zugriff", p => p.FolderCount, Format: "#,##0"),
            new("Höchstes Recht", p => Rights.Label(p.Highest)),
            new("Über Gruppen", p => Text(string.Join("; ", p.ViaGroups)), MaxWidth: 80),
            new("Direkt berechtigt", p => ReportText.YesNo(p.HasDirect)),
            new("SID", p => p.Sid, MaxWidth: 50),
        ];
        // Nur bei gekürzten Einzelzugriffen: sonst hielte man eine Person ohne Zeilen in „Zugriffe je Person“ für unberechtigt
        if (data.PersonGrantsTruncated)
            cols.Insert(4, new("Einzelzugriffe aufgeführt", p => p.GrantsComplete ? "Ja" : "Nein – nur die Summe",
                MaxWidth: 26, Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: p => !p.GrantsComplete));
        AddTable(wb, "Personen", "Personen", data.People, data.People.Count, maxRows, "Keine Benutzerkonten im Lauf.", truncated, cols);
    }

    private static void AddPersonGrants(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated)
    {
        var rows = data.People.SelectMany(p => p.Grants.Select(g => (Person: p, Grant: g)));
        int total = data.People.Sum(p => p.Grants.Count);
        AddTable(wb, "Zugriffe je Person", "ZugriffeJePerson", rows, total, maxRows, "Keine Zugriffe ermittelt.", truncated,
        [
            new("Person", r => Text(r.Person.Name), MaxWidth: 40),
            new("Anzeigename", r => Text(r.Person.DisplayName), MaxWidth: 40),
            new("Pfad", r => Text(r.Grant.Path), MaxWidth: 80),
            new("Effektives Recht", r => Rights.Label(r.Grant.Effective)),
            new("NTFS-Recht", r => Rights.Label(r.Grant.Ntfs)),
            new("Über Freigabe", r => r.Grant.ViaShare is { } s ? Rights.Label(s) : ReportText.Dash),
            new("Über Gruppen", r => Text(r.Grant.Via), MaxWidth: 70),
            new("Durch Verweigern eingeschränkt", r => ReportText.YesNo(r.Grant.ReducedByDeny)),
        ]);
    }

    private static void AddGroups(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, "Gruppen", "Gruppen", data.Groups, data.Groups.Count, maxRows, "Keine Gruppen im Lauf.", truncated,
        [
            new("Gruppe", g => Text(g.Name), MaxWidth: 45),
            new("Mitglieder direkt", g => g.DirectCount, Format: "#,##0"),
            new("Mitglieder gesamt (Personen)", g => g.TotalPeople, Format: "#,##0"),
            new("Vollständig", g => ReportText.YesNo(g.Complete),
                Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: g => !g.Complete),
            new("In Ordner-ACLs", g => g.UsedInFolders, Format: "#,##0"),
            new("Direkte Mitglieder", g => Text(string.Join("; ", g.DirectMembers)), MaxWidth: 80),
            new("Hinweis", g => Text(g.Note), MaxWidth: 70),
            new("SID", g => g.Sid, MaxWidth: 50),
        ]);

    private static void AddShares(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, "Freigaben", "Freigaben", data.Shares, data.Shares.Count, maxRows,
            "Keine Freigaben im Lauf (nur lokale Pfade gescannt).", truncated,
        [
            new("Freigabe", s => Text(s.Unc), MaxWidth: 50),
            new("Pfad", s => Text(s.LocalPath), MaxWidth: 50),
            new("Bemerkung", s => Text(s.Remark), MaxWidth: 40),
            new("Freigabe-Rechte", s => Text(s.PermissionsText), MaxWidth: 90,
                Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: s => !s.Readable),
            new("Admin-Freigabe", s => ReportText.YesNo(s.IsAdminShare)),
            new("Rechte lesbar", s => ReportText.YesNo(s.Readable)),
        ]);

    private static void AddRules(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, "Prüfregeln", "Pruefregeln", data.Rules, data.Rules.Count, maxRows, "Keine Prüfregeln.", truncated,
        [
            new("Regel", r => r.Id),
            new("Schwere", r => ReportText.Severity(r.Severity), Style: (cell, r) => StyleSeverity(cell, r.Severity)),
            new("Titel", r => Text(r.Title), MaxWidth: 45),
            new("Erklärung", r => Text(r.Explanation), MaxWidth: 90),
            new("Empfehlung", r => Text(r.Recommendation), MaxWidth: 90),
            new("Befunde", r => r.Count, Format: "#,##0"),
        ]);

    private static void AddChanges(XLWorkbook wb, ComparisonData comparison, int maxRows, List<string> truncated) =>
        AddTable(wb, "Änderungen", "Aenderungen", comparison.Rows, comparison.Rows.Count, maxRows,
            "Keine Änderungen seit dem letzten Lauf.", truncated,
        [
            new("Bereich", r => r.Area),
            new("Änderung", r => r.Change, MaxWidth: 30),
            new("Pfad bzw. Gruppe", r => Text(r.Subject), MaxWidth: 70),
            new("Konto", r => Text(r.Account), MaxWidth: 40),
            new("Vorher", r => Text(r.Before), MaxWidth: 70),
            new("Nachher", r => Text(r.After), MaxWidth: 70),
        ]);

    private static void AddErrors(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, "Fehler", "Fehler", data.Errors, data.Errors.Count, maxRows, "Keine Fehler beim Lesen.", truncated,
        [
            new("Pfad", e => Text(e.Path), MaxWidth: 90),
            new("Meldung", e => Text(e.Message), MaxWidth: 90),
        ]);

    // ---- Tabellen-Helfer --------------------------------------------------------------------------

    /// <param name="Style">Hervorhebung einer Zelle – nur für Zeilen, auf die <paramref name="StyleIf"/> zutrifft (ohne: alle).</param>
    private sealed record Col<T>(
        string Header, Func<T, XLCellValue> Value, double MaxWidth = 30, string? Format = null, Action<IXLCell, T>? Style = null,
        Func<T, bool>? StyleIf = null);

    private static void AddTable<T>(XLWorkbook wb, string sheetName, string tableName, IEnumerable<T> rows, int total, int maxRows,
        string emptyText, List<string> truncated, IReadOnlyList<Col<T>> cols)
    {
        var ws = wb.Worksheets.Add(sheetName);
        var widths = new double[cols.Count];
        bool wrapped = false;
        for (int c = 0; c < cols.Count; c++)
        {
            ws.Cell(1, c + 1).Value = cols[c].Header;
            widths[c] = HeaderWidth(cols[c].Header, out bool twoLines);
            wrapped |= twoLines;
        }

        var items = rows.Take(Math.Max(0, maxRows)).ToList();
        foreach (var item in items.Take(WidthSampleRows))
            for (int c = 0; c < cols.Count; c++)
                widths[c] = Math.Max(widths[c], DisplayLength(cols[c].Value(item)));

        // Alle Werte auf einmal einfügen: Zelle für Zelle legt ClosedXML je Zelle ein Objekt an – bei Hunderttausenden
        // Zeilen mal 16 Spalten der größte Teil der Zeit. Hervorhebungen danach nur dort, wo sie nötig sind.
        if (items.Count > 0)
        {
            ws.Cell(2, 1).InsertData(items.Select(item =>
            {
                var values = new object[cols.Count];
                for (int c = 0; c < cols.Count; c++) values[c] = Raw(cols[c].Value(item));
                return values;
            }));
            for (int c = 0; c < cols.Count; c++)
            {
                if (cols[c].Style is not { } style) continue;
                var applies = cols[c].StyleIf;
                for (int i = 0; i < items.Count; i++)
                    if (applies is null || applies(items[i])) style(ws.Cell(i + 2, c + 1), items[i]);
            }
        }
        int row = items.Count + 1;
        int written = row - 1;
        if (written == 0)
        {
            row = 2;
            ws.Cell(2, 1).Value = emptyText;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontColor = Muted;
        }
        if (written < total)
            truncated.Add($"Blatt {ReportText.Quote(sheetName)} gekürzt: {ReportText.Number(written)} von {ReportText.Number(total)} Zeilen "
                + "(Größenbegrenzung des Excel-Berichts).");

        var table = ws.Range(1, 1, row, cols.Count).CreateTable(tableName);
        table.Theme = XLTableTheme.TableStyleLight1;
        table.ShowRowStripes = true;
        table.ShowAutoFilter = true;

        var header = ws.Range(1, 1, 1, cols.Count);
        header.Style.Fill.BackgroundColor = Accent;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        // Lange Überschriften zweizeilig statt breiter Spalten für „Ja“/„Nein“
        header.Style.Alignment.WrapText = true;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        if (wrapped) ws.Row(1).Height = 30;
        ws.SheetView.FreezeRows(1);

        for (int c = 0; c < cols.Count; c++)
        {
            ws.Column(c + 1).Width = Math.Clamp(widths[c] + 1, 8, Math.Max(8, cols[c].MaxWidth));
            if (cols[c].Format is { } format && row >= 2)
                ws.Range(2, c + 1, row, c + 1).Style.NumberFormat.Format = format;
        }

        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(1, 1);
        Footer(ws);
    }

    /// <summary>
    /// Breite, die eine Spaltenüberschrift braucht (in Zeichen, plus Platz für den Filter-Knopf). Ab zwei Wörtern
    /// und 14 Zeichen wird sie auf zwei Zeilen verteilt – möglichst gleich lang.
    /// </summary>
    internal static double HeaderWidth(string header, out bool twoLines)
    {
        const int filterButton = 3;
        twoLines = false;
        var words = header.Split(' ');
        if (header.Length <= 14 || words.Length < 2) return header.Length + filterButton;
        int best = header.Length;
        for (int i = 1; i < words.Length; i++)
        {
            int first = string.Join(' ', words[..i]).Length;
            int second = string.Join(' ', words[i..]).Length;
            best = Math.Min(best, Math.Max(first, second));
        }
        twoLines = true;
        return best + filterButton;
    }

    /// <summary>Wert für <c>InsertData</c>: der nackte Text bzw. die Zahl (eine verpackte <see cref="XLCellValue"/> würde dort zu Text).</summary>
    private static object Raw(XLCellValue value) =>
        value.IsText ? value.GetText() : value.IsNumber ? value.GetNumber() : value.IsBoolean ? value.GetBoolean() : value.IsBlank ? Blank.Value : value.ToString();

    private static double DisplayLength(XLCellValue value) =>
        value.IsText ? value.GetText().Length : value.IsBlank ? 0 : 8;

    private static void StyleSeverity(IXLCell cell, Severity severity)
    {
        var (fill, font) = ReportColors.For(severity);
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
        cell.Style.Font.FontColor = XLColor.FromHtml(font);
        cell.Style.Font.Bold = true;
    }

    private static void Footer(IXLWorksheet ws)
    {
        ws.PageSetup.Footer.Left.AddText($"{ReportText.Product} · {ReportText.Title}");
        ws.PageSetup.Footer.Right.AddText("Seite ");
        ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
        ws.PageSetup.Footer.Right.AddText(" von ");
        ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages);
    }

    private static void SetProperties(XLWorkbook wb, ReportData data)
    {
        wb.Properties.Title = ReportText.Title;
        wb.Properties.Subject = Text(string.Join("; ", data.Header.Targets));
        wb.Properties.Author = ReportText.Product;
        wb.Properties.Comments = $"Erstellt mit {ReportText.Product} {data.Header.ReportVersion}".TrimEnd();
        wb.Properties.Created = data.Header.CreatedAt == default ? DateTime.Now : data.Header.CreatedAt.LocalDateTime;
    }

    /// <summary>
    /// Text für eine Zelle: ungültige XML-Zeichen ersetzt, auf die Excel-Grenze gekürzt. Ein Apostroph am Anfang
    /// (NTFS erlaubt „'Alt“ als Ordnernamen) nimmt ClosedXML als Textkennzeichen und entfernt ihn – deshalb verdoppelt.
    /// </summary>
    private static string Text(string? text)
    {
        var clean = ReportText.Clean(text);
        if (clean.StartsWith('\'')) clean = "'" + clean;
        if (clean.Length <= MaxCellText) return clean;
        int cut = MaxCellText - 1;
        if (char.IsHighSurrogate(clean[cut - 1])) cut--; // kein halbes Zeichen stehen lassen
        return clean[..cut] + "…";
    }
}
