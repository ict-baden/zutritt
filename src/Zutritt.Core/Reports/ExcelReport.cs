using ClosedXML.Excel;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

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
        progress?.Report(Strings.Excel_ProgressSaving);
        workbook.SaveAs(stream);
    });

    /// <summary>Baut die Arbeitsmappe im Speicher; <paramref name="maxRows"/> nur für Tests kleiner.</summary>
    internal static XLWorkbook Create(ReportData data, int maxRows = MaxRows, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var workbook = new XLWorkbook();
        try
        {
            progress?.Report(Strings.Excel_ProgressFilling);
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
        var ws = workbook.Worksheets.Add(Strings.Sheet_Overview, 1);
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
        subtitle.Value = Strings.Excel_Subtitle;
        subtitle.Style.Font.FontColor = Muted;
        row++;

        Section(Strings.Excel_Run);
        Pair(Strings.Excel_Targets, string.Join("\n", h.Targets), lines: Math.Max(1, h.Targets.Count));
        Pair(Strings.Pdf_Period, ReportText.Period(h.StartedAt, h.FinishedAt));
        Pair(Strings.Pdf_Machine, h.Machine);
        Pair(Strings.Pdf_ScannedBy, h.ScannedBy);
        Pair(Strings.Excel_ToolVersionScan, h.ToolVersion);
        Pair(Strings.Pdf_ReportCreated, Texts.Format(Strings.Pdf_ReportCreatedWith, ReportText.DateTime(h.CreatedAt), h.ReportVersion).TrimEnd());
        Pair(Strings.Pdf_Scope, ReportText.Scope(h.Request));
        row++;

        Section(Strings.Excel_Numbers);
        Number(Strings.Pdf_FoldersChecked, h.FoldersVisited);
        Number(Strings.Pdf_FilesChecked, h.FilesVisited);
        Number(Strings.Pdf_SectionFolders, h.FoldersStored);
        if (h.FilesStored > 0 || h.Request.IncludeFiles) Number(Strings.Pdf_FilesWithOwn, h.FilesStored);
        Number(Strings.Pdf_SectionShares, data.Shares.Count);
        Number(Strings.Pdf_AccountsGroups, h.Principals);
        Number(Strings.Excel_PeopleUserAccounts, data.People.Count);
        Number(Strings.Excel_PeopleWithAccess, data.PeopleWithAccess);
        Number(Strings.Pdf_UnreadableFolders, data.UnreadableFolders, warn: data.UnreadableFolders > 0);
        Number(Strings.Pdf_ReadErrors, data.Errors.Count, warn: data.Errors.Count > 0);
        row++;

        Section(Strings.Pdf_FindingsBySeverity);
        foreach (var severity in new[] { Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info })
        {
            var label = ws.Cell(row, 1);
            label.Value = ReportText.Severity(severity);
            StyleSeverity(label, severity);
            SetNumber(ws.Cell(row, 2), data.CountFindings(severity));
            row++;
        }
        Number(Strings.Excel_Total, data.Findings.Count, bold: true);
        row++;

        if (data.Comparison is { } c)
        {
            Section(Texts.Format(Strings.Excel_ComparisonWithRun, ReportText.DateTime(c.OlderStartedAt)));
            if (c.ScopeNote.Length > 0) Note(NoteKind.Warnung, c.ScopeNote);
            if (!c.HasChanges) Pair(Strings.Excel_Result, Strings.Pdf_NoChanges);
            // Reihenfolge wie im Blatt „Änderungen“: Befunde, Ursachen, Folgen für Personen
            Number(Strings.Cmp_NewFindings, c.NewFindings, warn: c.NewFindings > 0);
            Number(Strings.Cmp_ResolvedFindings, c.ResolvedFindings);
            Number(Strings.Cmp_ChangedAces, c.AceChanges);
            Number(Strings.Cmp_ChangedMemberships, c.MembershipChanges);
            Number(Strings.Excel_FoldersAdded, c.FoldersAdded);
            Number(Strings.Excel_FoldersRemoved, c.FoldersRemoved);
            Number(Strings.Cmp_MoreAccess, c.MoreAccess, warn: c.MoreAccess > 0);
            Number(Strings.Cmp_LessAccess, c.LessAccess);
            row++;
        }

        // Warnungen zuerst – auch die zur Kürzung dieser Mappe –, dann die Lesehilfen
        Section(Strings.Pdf_Notes);
        foreach (var note in data.Notes.Where(n => n.Kind == NoteKind.Warnung))
            Note(note.Kind, note.Text);
        foreach (var text in truncated)
            Note(NoteKind.Warnung, text);
        foreach (var note in data.Notes.Where(n => n.Kind != NoteKind.Warnung))
            Note(note.Kind, note.Text);
        row++;

        // Wegweiser für Prüfer, die die Mappe zum ersten Mal sehen
        Section(Strings.Excel_SheetsGuide);
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
            label.Value = kind == NoteKind.Warnung ? Strings.Excel_Attention : Strings.Col_Note;
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
    private static Dictionary<string, string> SheetDescriptions => new()
    {
        [Strings.Sheet_Findings] = Strings.Excel_DescFindings,
        [Strings.Sheet_FoldersPermissions] = Strings.Excel_DescFolders,
        [Strings.Sheet_People] = Strings.Excel_DescPeople,
        [Strings.Sheet_AccessPerPerson] = Strings.Excel_DescAccessPerPerson,
        [Strings.Sheet_Groups] = Strings.Excel_DescGroups,
        [Strings.Sheet_Shares] = Strings.Excel_DescShares,
        [Strings.Sheet_Rules] = Strings.Excel_DescRules,
        [Strings.Sheet_Changes] = Strings.Excel_DescChanges,
        [Strings.Sheet_Errors] = Strings.Excel_DescErrors,
    };

    private static void SetNumber(IXLCell cell, long value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "#,##0";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
    }

    private static void AddFindings(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, Strings.Sheet_Findings, "Befunde", data.Findings, data.Findings.Count, maxRows, Strings.Excel_NoFindings, truncated,
        [
            new(Strings.Col_Severity, f => ReportText.Severity(f.Severity), Style: (cell, f) => StyleSeverity(cell, f.Severity)),
            new(Strings.Col_Rule, f => f.RuleId),
            new(Strings.Col_Title, f => Text(f.Title), MaxWidth: 40),
            new(Strings.Col_Path, f => Text(f.Path), MaxWidth: 70),
            new(Strings.Col_Account, f => Text(f.Account), MaxWidth: 40),
            new(Strings.Col_Message, f => Text(f.Message), MaxWidth: 80),
            new(Strings.Col_Recommendation, f => Text(f.Recommendation), MaxWidth: 80),
            new("SID", f => f.Sid ?? "", MaxWidth: 50),
        ]);

    private static void AddFolders(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated)
    {
        // Ordner ohne Einträge (z. B. nicht lesbar) bekommen trotzdem eine Zeile, sonst fehlten sie im Filter
        var rows = data.Folders.SelectMany(f => f.Aces.Count == 0
            ? new[] { (Folder: f, Ace: (AceRow?)null) }
            : f.Aces.Select(a => (Folder: f, Ace: (AceRow?)a)));
        int total = data.Folders.Sum(f => Math.Max(1, f.Aces.Count));

        AddTable(wb, Strings.Sheet_FoldersPermissions, "OrdnerRechte", rows, total, maxRows, Strings.Excel_NoFolders, truncated,
        [
            new(Strings.Col_Path, r => Text(r.Folder.Path), MaxWidth: 80),
            new(Strings.Col_Depth, r => r.Folder.Depth, Format: "0"),
            new(Strings.Col_ItemType, r => r.Folder.IsFile ? Strings.Pdf_File : Strings.Excel_Folder),
            new(Strings.Pdf_InheritanceBroken, r => ReportText.YesNo(r.Folder.InheritanceBroken)),
            // Verschobene Ordner, die alte geerbte Rechte mitgenommen haben (Regel ZT14)
            new(Strings.Excel_ColInheritanceDiffers, r => ReportText.YesNo(r.Folder.InheritanceDiffers)),
            new(Strings.Col_Owner, r => Text(r.Folder.Owner), MaxWidth: 40),
            new(Strings.Col_Account, r => Text(r.Ace?.Account), MaxWidth: 40),
            new(Strings.Col_AccountKind, r => r.Ace is null ? "" : ReportText.Kind(r.Ace.Kind)),
            new(Strings.Col_Right, r => r.Ace?.Right ?? "", MaxWidth: 40),
            new(Strings.Col_AllowDenyLong, r => r.Ace is null ? "" : ReportText.AllowDeny(r.Ace.IsDeny),
                Style: (cell, _) => { cell.Style.Font.FontColor = Warning; cell.Style.Font.Bold = true; }, StyleIf: r => r.Ace?.IsDeny == true),
            new(Strings.Col_InheritedExplicit, r => r.Ace is null ? "" : ReportText.Origin(r.Ace.IsInherited)),
            new(Strings.Col_AppliesTo, r => r.Ace?.AppliesTo ?? "", MaxWidth: 45),
            new(Strings.Col_Share, r => Text(r.Folder.ShareUnc), MaxWidth: 40),
            new(Strings.Col_Note, r => r.Folder.Error is null ? "" : Text(Texts.Format(Strings.Pdf_NotReadable, r.Folder.Error)), MaxWidth: 60,
                Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: r => r.Folder.Error is not null),
            new("SID", r => r.Ace?.Sid ?? "", MaxWidth: 50),
            new(Strings.Col_RightsMask, r => r.Ace is null ? "" : ReportText.Mask(r.Ace.Rights)),
        ]);
    }

    private static void AddPeople(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated)
    {
        List<Col<PersonRow>> cols =
        [
            new(Strings.Col_Person, p => Text(p.Name), MaxWidth: 40),
            new(Strings.Col_DisplayName, p => Text(p.DisplayName), MaxWidth: 40),
            new(Strings.Col_Disabled, p => ReportText.YesNo(p.IsDisabled)),
            new(Strings.Col_FoldersWithAccess, p => p.FolderCount, Format: "#,##0"),
            new(Strings.Col_HighestRight, p => Rights.Label(p.Highest)),
            new(Strings.Col_ViaGroups, p => Text(string.Join("; ", p.ViaGroups)), MaxWidth: 80),
            new(Strings.Col_DirectlyGranted, p => ReportText.YesNo(p.HasDirect)),
            new("SID", p => p.Sid, MaxWidth: 50),
        ];
        // Nur bei gekürzten Einzelzugriffen: sonst hielte man eine Person ohne Zeilen in „Zugriffe je Person“ für unberechtigt
        if (data.PersonGrantsTruncated)
            cols.Insert(4, new(Strings.Col_GrantsListed, p => p.GrantsComplete ? Strings.Text_Yes : Strings.Excel_NoOnlyTotal,
                MaxWidth: 26, Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: p => !p.GrantsComplete));
        AddTable(wb, Strings.Sheet_People, "Personen", data.People, data.People.Count, maxRows, Strings.Excel_NoUsers, truncated, cols);
    }

    private static void AddPersonGrants(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated)
    {
        var rows = data.People.SelectMany(p => p.Grants.Select(g => (Person: p, Grant: g)));
        int total = data.People.Sum(p => p.Grants.Count);
        AddTable(wb, Strings.Sheet_AccessPerPerson, "ZugriffeJePerson", rows, total, maxRows, Strings.Excel_NoGrants, truncated,
        [
            new(Strings.Col_Person, r => Text(r.Person.Name), MaxWidth: 40),
            new(Strings.Col_DisplayName, r => Text(r.Person.DisplayName), MaxWidth: 40),
            new(Strings.Col_Path, r => Text(r.Grant.Path), MaxWidth: 80),
            new(Strings.Col_EffectiveRight, r => Rights.Label(r.Grant.Effective)),
            new(Strings.Col_NtfsRight, r => Rights.Label(r.Grant.Ntfs)),
            new(Strings.Col_ViaShare, r => r.Grant.ViaShare is { } s ? Rights.Label(s) : ReportText.Dash),
            new(Strings.Col_ViaGroups, r => Text(r.Grant.Via), MaxWidth: 70),
            new(Strings.Col_ReducedByDeny, r => ReportText.YesNo(r.Grant.ReducedByDeny)),
        ]);
    }

    private static void AddGroups(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, Strings.Sheet_Groups, "Gruppen", data.Groups, data.Groups.Count, maxRows, Strings.Excel_NoGroups, truncated,
        [
            new(Strings.Col_Group, g => Text(g.Name), MaxWidth: 45),
            new(Strings.Col_MembersDirect, g => g.DirectCount, Format: "#,##0"),
            new(Strings.Col_MembersTotal, g => g.TotalPeople, Format: "#,##0"),
            new(Strings.Col_Complete, g => ReportText.YesNo(g.Complete),
                Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: g => !g.Complete),
            new(Strings.Col_InFolderAcls, g => g.UsedInFolders, Format: "#,##0"),
            new(Strings.Col_DirectMembers, g => Text(string.Join("; ", g.DirectMembers)), MaxWidth: 80),
            new(Strings.Col_Note, g => Text(g.Note), MaxWidth: 70),
            new("SID", g => g.Sid, MaxWidth: 50),
        ]);

    private static void AddShares(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, Strings.Sheet_Shares, "Freigaben", data.Shares, data.Shares.Count, maxRows,
            Strings.Excel_NoShares, truncated,
        [
            new(Strings.Col_Share, s => Text(s.Unc), MaxWidth: 50),
            new(Strings.Col_Path, s => Text(s.LocalPath), MaxWidth: 50),
            new(Strings.Col_Remark, s => Text(s.Remark), MaxWidth: 40),
            new(Strings.Col_SharePermissions, s => Text(s.PermissionsText), MaxWidth: 90,
                Style: (cell, _) => cell.Style.Font.FontColor = Warning, StyleIf: s => !s.Readable),
            new(Strings.Col_AdminShare, s => ReportText.YesNo(s.IsAdminShare)),
            new(Strings.Col_PermissionsReadable, s => ReportText.YesNo(s.Readable)),
        ]);

    private static void AddRules(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, Strings.Sheet_Rules, "Pruefregeln", data.Rules, data.Rules.Count, maxRows, Strings.Excel_NoRules, truncated,
        [
            new(Strings.Col_Rule, r => r.Id),
            new(Strings.Col_Severity, r => ReportText.Severity(r.Severity), Style: (cell, r) => StyleSeverity(cell, r.Severity)),
            new(Strings.Col_Title, r => Text(r.Title), MaxWidth: 45),
            new(Strings.Col_Explanation, r => Text(r.Explanation), MaxWidth: 90),
            new(Strings.Col_Recommendation, r => Text(r.Recommendation), MaxWidth: 90),
            new(Strings.Col_Findings, r => r.Count, Format: "#,##0"),
        ]);

    private static void AddChanges(XLWorkbook wb, ComparisonData comparison, int maxRows, List<string> truncated) =>
        AddTable(wb, Strings.Sheet_Changes, "Aenderungen", comparison.Rows, comparison.Rows.Count, maxRows,
            Strings.Pdf_NoChanges, truncated,
        [
            new(Strings.Col_Area, r => ChangeArea.Label(r.Area)),
            new(Strings.Col_Change, r => r.Change, MaxWidth: 30),
            new(Strings.Col_PathOrGroup, r => Text(r.Subject), MaxWidth: 70),
            new(Strings.Col_Account, r => Text(r.Account), MaxWidth: 40),
            new(Strings.Col_Before, r => Text(r.Before), MaxWidth: 70),
            new(Strings.Col_After, r => Text(r.After), MaxWidth: 70),
        ]);

    private static void AddErrors(XLWorkbook wb, ReportData data, int maxRows, List<string> truncated) =>
        AddTable(wb, Strings.Sheet_Errors, "Fehler", data.Errors, data.Errors.Count, maxRows, Strings.Excel_NoErrors, truncated,
        [
            new(Strings.Col_Path, e => Text(e.Path), MaxWidth: 90),
            new(Strings.Col_Message, e => Text(e.Message), MaxWidth: 90),
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
            truncated.Add(Texts.Format(Strings.Excel_SheetTruncated, ReportText.Quote(sheetName), ReportText.Number(written), ReportText.Number(total)));

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
        // „Seite {0} von {1}“ in der Reihenfolge der Sprache, die Platzhalter werden zu Feldern von Excel
        var parts = System.Text.RegularExpressions.Regex.Split(Strings.Pdf_PageOf, @"\{(\d)\}");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) { if (parts[i].Length > 0) ws.PageSetup.Footer.Right.AddText(parts[i]); }
            else ws.PageSetup.Footer.Right.AddText(parts[i] == "0" ? XLHFPredefinedText.PageNumber : XLHFPredefinedText.NumberOfPages);
        }
    }

    private static void SetProperties(XLWorkbook wb, ReportData data)
    {
        wb.Properties.Title = ReportText.Title;
        wb.Properties.Subject = Text(string.Join("; ", data.Header.Targets));
        wb.Properties.Author = ReportText.Product;
        wb.Properties.Comments = Texts.Format(Strings.Excel_CreatedWith, ReportText.Product, data.Header.ReportVersion).TrimEnd();
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
