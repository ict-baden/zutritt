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
    public static void Write(ScanResult scan, ComparisonResult? comparison, string path)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ReportFile.FullPath(path); // Ziel prüfen, bevor die Auswertung läuft
        Write(ReportDataBuilder.Build(scan, comparison), path);
    }

    internal static void Write(ReportData data, string path) => ReportFile.Write(path, stream =>
    {
        using var workbook = Create(data);
        workbook.SaveAs(stream);
    });

    /// <summary>Baut die Arbeitsmappe im Speicher; <paramref name="maxRows"/> nur für Tests kleiner.</summary>
    internal static XLWorkbook Create(ReportData data, int maxRows = MaxRows)
    {
        ArgumentNullException.ThrowIfNull(data);
        var workbook = new XLWorkbook();
        try
        {
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
            Number("Neue Befunde", c.NewFindings, warn: c.NewFindings > 0);
            Number("Behobene Befunde", c.ResolvedFindings);
            Number("Personen-Zugriffe erweitert", c.MoreAccess);
            Number("Personen-Zugriffe eingeschränkt", c.LessAccess);
            Number("Geänderte Berechtigungseinträge", c.AceChanges);
            Number("Ordner neu mit eigenen Rechten", c.FoldersAdded);
            Number("Ordner ohne eigene Rechte mehr", c.FoldersRemoved);
            Number("Geänderte Gruppenmitgliedschaften", c.MembershipChanges);
            row++;
        }

        Section("Hinweise");
        foreach (var note in data.Notes)
            Note(note.Kind, note.Text);
        foreach (var text in truncated)
            Note(NoteKind.Warnung, text);

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
            new("Besitzer", r => Text(r.Folder.Owner), MaxWidth: 40),
            new("Konto", r => Text(r.Ace?.Account), MaxWidth: 40),
            new("Art", r => r.Ace is null ? "" : ReportText.Kind(r.Ace.Kind)),
            new("Recht", r => r.Ace is null ? "" : Rights.Label(r.Ace.Level)),
            new("Erlauben/Verweigern", r => r.Ace is null ? "" : ReportText.AllowDeny(r.Ace.IsDeny),
                Style: (cell, r) => { if (r.Ace?.IsDeny == true) { cell.Style.Font.FontColor = Warning; cell.Style.Font.Bold = true; } }),
            new("Geerbt/explizit", r => r.Ace is null ? "" : ReportText.Origin(r.Ace.IsInherited)),
            new("Gilt für", r => r.Ace?.AppliesTo ?? "", MaxWidth: 45),
            new("Freigabe", r => Text(r.Folder.ShareUnc), MaxWidth: 40),
            new("Hinweis", r => r.Folder.Error is null ? "" : Text("Nicht lesbar: " + r.Folder.Error), MaxWidth: 60,
                Style: (cell, r) => { if (r.Folder.Error is not null) cell.Style.Font.FontColor = Warning; }),
            new("SID", r => r.Ace?.Sid ?? "", MaxWidth: 50),
            new("Rechte-Maske", r => r.Ace is null ? "" : ReportText.Mask(r.Ace.Rights)),
        ]);
    }

    private static void AddPeople(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, "Personen", "Personen", data.People, data.People.Count, maxRows, "Keine Benutzerkonten im Lauf.", truncated,
        [
            new("Person", p => Text(p.Name), MaxWidth: 40),
            new("Anzeigename", p => Text(p.DisplayName), MaxWidth: 40),
            new("Deaktiviert", p => ReportText.YesNo(p.IsDisabled)),
            new("Ordner mit Zugriff", p => p.FolderCount, Format: "#,##0"),
            new("Höchstes Recht", p => Rights.Label(p.Highest)),
            new("Über Gruppen", p => Text(string.Join("; ", p.ViaGroups)), MaxWidth: 80),
            new("Direkt berechtigt", p => ReportText.YesNo(p.HasDirect)),
            new("SID", p => p.Sid, MaxWidth: 50),
        ]);

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
                Style: (cell, g) => { if (!g.Complete) cell.Style.Font.FontColor = Warning; }),
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
                Style: (cell, s) => { if (!s.Readable) cell.Style.Font.FontColor = Warning; }),
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

    private sealed record Col<T>(
        string Header, Func<T, XLCellValue> Value, double MaxWidth = 30, string? Format = null, Action<IXLCell, T>? Style = null);

    private static void AddTable<T>(XLWorkbook wb, string sheetName, string tableName, IEnumerable<T> rows, int total, int maxRows,
        string emptyText, List<string> truncated, IReadOnlyList<Col<T>> cols)
    {
        var ws = wb.Worksheets.Add(sheetName);
        var widths = new double[cols.Count];
        for (int c = 0; c < cols.Count; c++)
        {
            ws.Cell(1, c + 1).Value = cols[c].Header;
            widths[c] = cols[c].Header.Length + 3; // Platz für den Filter-Knopf
        }

        int row = 1;
        foreach (var item in rows)
        {
            if (row > maxRows) break;
            row++;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                var value = col.Value(item);
                var cell = ws.Cell(row, c + 1);
                cell.Value = value;
                col.Style?.Invoke(cell, item);
                if (row <= WidthSampleRows) widths[c] = Math.Max(widths[c], DisplayLength(value));
            }
        }
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

    /// <summary>Text für eine Zelle: ungültige XML-Zeichen ersetzt, auf die Excel-Grenze gekürzt.</summary>
    private static string Text(string? text)
    {
        var clean = ReportText.Clean(text);
        if (clean.Length <= MaxCellText) return clean;
        int cut = MaxCellText - 1;
        if (char.IsHighSurrogate(clean[cut - 1])) cut--; // kein halbes Zeichen stehen lassen
        return clean[..cut] + "…";
    }
}
