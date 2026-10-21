// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.Core.Reports;

/// <summary>
/// PDF-Bericht (A4, MigraDoc) zum Abzeichnen bei Audits: Deckblatt, Zusammenfassung, Befunde, Freigaben,
/// Ordner mit eigenen Rechten, Personen-Übersicht, Änderungen seit dem letzten Lauf, nicht geprüfte
/// Bereiche und ein Block „Prüfung und Freigabe“ mit Linien für Name, Datum, Unterschrift und Bemerkungen.
/// Sehr lange Listen kürzt das PDF mit deutlichem Hinweis – vollständig ist der Excel-Bericht.
/// </summary>
public static class PdfReport
{
    /// <summary>Schreibt den PDF-Bericht; mit <paramref name="comparison"/> zusätzlich „Änderungen seit dem letzten Lauf“.</summary>
    /// <exception cref="ReportWriteException">Die Datei konnte nicht geschrieben werden (z. B. im PDF-Betrachter geöffnet).</exception>
    public static void Write(ScanResult scan, ComparisonResult? comparison, string path) => Write(scan, comparison, path, progress: null);

    /// <summary>Wie <see cref="Write(ScanResult, ComparisonResult?, string)"/>, meldet dabei den jeweiligen Arbeitsschritt.</summary>
    /// <param name="progress">Erhält kurze deutsche Sätze wie „Seiten werden gesetzt …“ (aus dem Hintergrund-Thread).</param>
    /// <exception cref="ReportWriteException">Die Datei konnte nicht geschrieben werden (z. B. im PDF-Betrachter geöffnet).</exception>
    public static void Write(ScanResult scan, ComparisonResult? comparison, string path, IProgress<string>? progress)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ReportFile.FullPath(path); // Ziel prüfen, bevor die Auswertung läuft
        Write(ReportDataBuilder.Build(scan, comparison, progress), path, progress: progress);
    }

    internal static void Write(ReportData data, string path, PdfLimits? limits = null, IProgress<string>? progress = null)
    {
        ReportFile.FullPath(path);
        progress?.Report(Strings.Pdf_ProgressBuilding);
        var document = CreateDocument(data, limits);
        lock (RenderGate)
        {
            // Erst vollständig rendern, dann schreiben: scheitert das Rendern, bleibt die Zieldatei unberührt
            progress?.Report(Strings.Pdf_ProgressLayout);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            progress?.Report(Strings.Pdf_ProgressSaving);
            var pdf = renderer.PdfDocument;
            pdf.Info.Title = ReportText.Title;
            pdf.Info.Author = ReportText.Product;
            pdf.Info.Subject = ReportText.Clean(string.Join("; ", data.Header.Targets));
            pdf.Info.Creator = $"{ReportText.Product} {data.Header.ReportVersion}".TrimEnd();
            ReportFile.Write(path, stream => pdf.Save(stream, false));
        }
    }

    /// <summary>
    /// Nur ein MigraDoc-Satz zur Zeit im Prozess. MigraDoc 6.2.4 merkt sich die zuletzt benutzte Schrift in
    /// statischen Feldern ohne Sperre (FontHandler): setzen zwei Threads gleichzeitig, misst einer mit der Schrift
    /// des anderen – Umbrüche verschieben sich, Text kann in falscher Größe erscheinen. Gilt für jedes Rendern
    /// (auch die Umbruchprüfung in den Tests).
    /// </summary>
    internal static readonly Lock RenderGate = new();

    /// <summary>Baut das MigraDoc-Dokument (ohne zu rendern) – für Tests und Vorschau.</summary>
    internal static Document CreateDocument(ReportData data, PdfLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var family = PdfFonts.Family;
        using var text = new PdfText(family, PdfFonts.CjkFamily);
        return new Layout(data, limits ?? PdfLimits.Default, text, family).Build();
    }

    /// <summary>Stil der Einleitungstexte unter Überschriften – sie bleiben mit dem Folgenden auf einer Seite.</summary>
    internal const string IntroStyle = "Einleitung";

    /// <summary>Setzt das Dokument Abschnitt für Abschnitt.</summary>
    private sealed class Layout(ReportData data, PdfLimits limits, PdfText text, string family)
    {
        private const double ContentWidthCm = 17.0;
        private const double NormalSize = 9.5;
        private const double TableSize = 8.5;
        private const double SmallSize = 8.0;
        private const double PaddingPt = 4.0;
        private const string TableStyle = "Tabelle";
        private const string SmallStyle = "Klein";

        /// <summary>
        /// So hoch darf ein Block höchstens werden, den das PDF zusammenhält (Tabellenkopf mit den ersten Zeilen,
        /// kleine Tabellen ganz). Deutlich unter einer Seite: MigraDoc ließe die Zeilen eines Blocks, der nicht
        /// auf eine Seite passt, stillschweigend weg.
        /// </summary>
        private const double KeepBudgetPt = 260;

        /// <summary>Zeilenhöhe im Verhältnis zur Schriftgröße (Segoe UI ≈ 1,33) – mit etwas Reserve.</summary>
        private const double LineFactor = 1.35;

        private static readonly Color TextColor = Rgb(ReportColors.Text);
        private static readonly Color MutedColor = Rgb(ReportColors.Muted);
        private static readonly Color LineColor = Rgb(ReportColors.Line);
        private static readonly Color AccentColor = Rgb(ReportColors.AccentBlue);
        private static readonly Color VioletColor = Rgb(ReportColors.AccentViolet);
        private static readonly Color AccentLightColor = Rgb(ReportColors.AccentLight);
        private static readonly Color WarningColor = Rgb(ReportColors.Warning);

        private static double ContentWidth => Unit.FromCentimeter(ContentWidthCm).Point;

        /// <summary>So viele Ziele zeigt das Deckblatt höchstens – es muss eine Seite bleiben (alle stehen im Excel-Bericht).</summary>
        private const int CoverTargets = 5;

        private readonly Document _doc = new();
        /// <summary>Abschnitte fürs Inhaltsverzeichnis in Reihenfolge, je mit Sprungmarke.</summary>
        private readonly List<(string Title, string Bookmark)> _contents = [];
        private Section _section = null!;

        public Document Build()
        {
            _doc.Info.Title = ReportText.Title;
            _doc.Info.Author = ReportText.Product;
            _doc.Info.Subject = ReportText.Clean(string.Join("; ", data.Header.Targets));
            DefineStyles();
            _section = _doc.AddSection();
            SetupPage();

            // Inhaltsverzeichnis: Abschnitte, die es in diesem Bericht gibt (Seitenzahlen setzt MigraDoc beim Rendern)
            var sections = new List<string> { Strings.Pdf_SectionSummary, Strings.Pdf_SectionFindings, Strings.Pdf_SectionRules, Strings.Pdf_SectionShares,
                Strings.Pdf_SectionFolders, Strings.Pdf_SectionPeople };
            if (data.Comparison is not null) sections.Add(Strings.Pdf_SectionChanges);
            if (data.Errors.Count > 0) sections.Add(Strings.Pdf_SectionErrors);
            sections.Add(Strings.Pdf_SectionSignOff);
            foreach (var title in sections) _contents.Add((title, $"abschnitt{_contents.Count + 1}"));

            Cover();
            _section.AddPageBreak();
            Summary();
            Findings();
            Shares();
            Folders();
            People();
            if (data.Comparison is { } comparison) Changes(comparison);
            if (data.Errors.Count > 0) Errors();
            SignOff();
            return _doc;
        }

        // ---- Aufbau ---------------------------------------------------------------------------------

        private void DefineStyles()
        {
            var normal = _doc.Styles[StyleNames.Normal]!;
            normal.Font.Name = family;
            normal.Font.Size = NormalSize;
            normal.Font.Color = TextColor;
            normal.ParagraphFormat.SpaceAfter = 3;

            var h1 = _doc.Styles[StyleNames.Heading1]!;
            h1.Font.Size = 15;
            h1.Font.Bold = true;
            h1.Font.Color = VioletColor;
            h1.ParagraphFormat.SpaceBefore = 16;
            h1.ParagraphFormat.SpaceAfter = 6;
            h1.ParagraphFormat.KeepWithNext = true;

            var h2 = _doc.Styles[StyleNames.Heading2]!;
            h2.Font.Size = 11;
            h2.Font.Bold = true;
            h2.Font.Color = TextColor;
            h2.ParagraphFormat.SpaceBefore = 10;
            h2.ParagraphFormat.SpaceAfter = 4;
            h2.ParagraphFormat.KeepWithNext = true;

            foreach (var name in new[] { StyleNames.Header, StyleNames.Footer })
            {
                var style = _doc.Styles[name]!;
                style.Font.Size = SmallSize;
                style.Font.Color = MutedColor;
            }

            var table = _doc.Styles.AddStyle(TableStyle, StyleNames.Normal);
            table.Font.Size = TableSize;
            table.ParagraphFormat.SpaceAfter = 0;

            var small = _doc.Styles.AddStyle(SmallStyle, StyleNames.Normal);
            small.Font.Size = SmallSize;
            small.Font.Color = MutedColor;

            // Einleitung unter einer Überschrift: gehört zum Folgenden, sonst stünde sie allein am Seitenende
            var intro = _doc.Styles.AddStyle(IntroStyle, SmallStyle);
            intro.ParagraphFormat.SpaceAfter = 4;
            intro.ParagraphFormat.KeepWithNext = true;
        }

        private void SetupPage()
        {
            var page = _doc.DefaultPageSetup.Clone();
            page.PageFormat = PageFormat.A4;
            page.Orientation = Orientation.Portrait;
            page.TopMargin = Unit.FromCentimeter(2.4);
            page.BottomMargin = Unit.FromCentimeter(2.0);
            page.LeftMargin = Unit.FromCentimeter(2.0);
            page.RightMargin = Unit.FromCentimeter(2.0);
            page.HeaderDistance = Unit.FromCentimeter(1.0);
            page.FooterDistance = Unit.FromCentimeter(1.0);
            page.DifferentFirstPageHeaderFooter = true; // Deckblatt ohne Kopfzeile
            _section.PageSetup = page;

            var header = _section.Headers.Primary.AddParagraph();
            header.Format.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm), TabAlignment.Right);
            header.Format.Borders.Bottom.Width = 0.75;
            header.Format.Borders.Bottom.Color = AccentColor;
            header.Format.Borders.DistanceFromBottom = 3;
            header.AddFormattedText($"{ReportText.Product} · {ReportText.Title}", TextFormat.Bold);
            header.AddTab();
            header.AddText(ShortTargets());

            Footer(_section.Footers.Primary);
            Footer(_section.Footers.FirstPage);
        }

        private void Footer(HeaderFooter footer)
        {
            var p = footer.AddParagraph();
            p.Format.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm), TabAlignment.Right);
            p.Format.Borders.Top.Width = 0.5;
            p.Format.Borders.Top.Color = LineColor;
            p.Format.Borders.DistanceFromTop = 3;
            p.AddText(Texts.Format(Strings.Pdf_FooterCreated, ReportText.DateTime(data.Header.CreatedAt)));
            p.AddTab();
            AddWithFields(p, Strings.Pdf_PageOf, f => { if (f == 0) p.AddPageField(); else p.AddNumPagesField(); });
        }

        private void Cover()
        {
            var h = data.Header;
            var label = _section.AddParagraph(ReportText.Product.ToUpperInvariant());
            label.Format.SpaceBefore = Unit.FromCentimeter(2.0);
            label.Format.Font.Size = 11;
            label.Format.Font.Bold = true;
            label.Format.Font.Color = VioletColor;

            var title = _section.AddParagraph(ReportText.Title);
            title.Format.Font.Size = 30;
            title.Format.Font.Bold = true;
            title.Format.SpaceAfter = 2;

            var subtitle = _section.AddParagraph(Strings.Pdf_Subtitle);
            subtitle.Format.Font.Size = 12;
            subtitle.Format.Font.Color = MutedColor;
            subtitle.Format.SpaceAfter = 14;

            AccentBar();
            Spacer(12);

            var info = NewTable(framed: false, 4.0, 13.0);
            info.Borders.Visible = false;
            info.Style = StyleNames.Normal;
            info.TopPadding = 3;
            info.BottomPadding = 3;
            var targets = string.Join("\n", h.Targets.Take(CoverTargets));
            if (h.Targets.Count > CoverTargets)
                targets += Texts.Format(Strings.Pdf_MoreTargets, ReportText.Number(h.Targets.Count - CoverTargets));
            CoverRow(info, h.Targets.Count == 1 ? Strings.Pdf_Target : Texts.Format(Strings.Pdf_Targets, ReportText.Number(h.Targets.Count)), targets);
            CoverRow(info, Strings.Pdf_Period, ReportText.Period(h.StartedAt, h.FinishedAt));
            CoverRow(info, Strings.Pdf_Machine, h.Machine);
            CoverRow(info, Strings.Pdf_ScannedBy, h.ScannedBy);
            CoverRow(info, Strings.Pdf_ToolVersion, h.ToolVersion);
            CoverRow(info, Strings.Pdf_Scope, ReportText.Scope(h.Request));
            CoverRow(info, Strings.Pdf_ReportCreated, Texts.Format(Strings.Pdf_ReportCreatedWith, ReportText.DateTime(h.CreatedAt), h.ReportVersion).TrimEnd());
            // Ergebnis auf einen Blick – wer nur das Deckblatt sieht, weiß, ob es etwas zu tun gibt
            CoverRow(info, Strings.Col_Findings, data.Findings.Count == 0 ? Strings.Pdf_None
                : string.Join(" · ", new[] { Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info }
                    .Select(s => $"{ReportText.Number(data.CountFindings(s))} {ReportText.Severity(s)}")));
            if (data.Comparison is { } comparison)
                CoverRow(info, Strings.Pdf_ComparedWith, Texts.Format(Strings.Pdf_RunOf, ReportText.DateTime(comparison.OlderStartedAt)));

            Contents();

            var note = _section.AddParagraph(Strings.Pdf_Confidential);
            note.Style = SmallStyle;
            note.Format.SpaceBefore = Unit.FromCentimeter(1.2);
        }

        /// <summary>Inhaltsverzeichnis mit Seitenzahlen – beim Prüfen auf Papier findet man so jeden Abschnitt.</summary>
        private void Contents()
        {
            var heading = _section.AddParagraph(Strings.Pdf_Contents);
            heading.Format.SpaceBefore = Unit.FromCentimeter(0.9);
            heading.Format.SpaceAfter = 3;
            heading.Format.Font.Bold = true;
            heading.Format.Font.Color = VioletColor;
            heading.Format.KeepWithNext = true;
            foreach (var (title, bookmark) in _contents)
            {
                var p = _section.AddParagraph();
                p.Format.SpaceAfter = 1;
                p.Format.KeepWithNext = true;
                p.Format.TabStops.AddTabStop(Unit.FromCentimeter(10.0), TabAlignment.Right, TabLeader.Dots);
                p.AddText(title);
                p.AddTab();
                p.AddPageRefField(bookmark);
            }
        }

        private void CoverRow(Table table, string label, string value)
        {
            var row = table.AddRow();
            var l = row.Cells[0].AddParagraph(label);
            l.Format.Font.Color = MutedColor;
            var v = row.Cells[1].AddParagraph();
            v.Format.Font.Bold = true;
            text.Append(v, string.IsNullOrWhiteSpace(value) ? ReportText.Dash : value, CellWidth(table, 1), NormalSize, bold: true);
        }

        private void AccentBar()
        {
            var bar = NewTable(ContentWidthCm / 2, ContentWidthCm / 2);
            bar.Borders.Visible = false;
            var row = bar.AddRow();
            row.HeightRule = RowHeightRule.Exactly;
            row.Height = Unit.FromCentimeter(0.15);
            row.Cells[0].Shading.Color = AccentColor;
            row.Cells[1].Shading.Color = VioletColor;
        }

        private void Summary()
        {
            var h = data.Header;
            Heading1(Strings.Pdf_SectionSummary);
            var targets = h.Targets.Count == 1 ? Strings.Pdf_OneTarget : Texts.Format(Strings.Pdf_ManyTargets, ReportText.Number(h.Targets.Count));
            Paragraph(Texts.Format(h.Request.IncludeFiles ? Strings.Pdf_SummaryCheckedFiles : Strings.Pdf_SummaryChecked,
                    ReportText.Number(h.FoldersVisited), targets, ReportText.Number(h.FoldersStored), ReportText.Number(h.FilesVisited)))
                .Format.KeepWithNext = true;

            var numbers = NewTable(framed: false, 9.0, 4.0);
            numbers.Borders.Visible = false;
            numbers.Borders.Bottom.Visible = true;
            KeyValue(numbers, Strings.Pdf_FoldersChecked, ReportText.Number(h.FoldersVisited));
            if (h.Request.IncludeFiles || h.FilesVisited > 0) KeyValue(numbers, Strings.Pdf_FilesChecked, ReportText.Number(h.FilesVisited));
            KeyValue(numbers, Strings.Pdf_SectionFolders, ReportText.Number(h.FoldersStored));
            if (h.FilesStored > 0) KeyValue(numbers, Strings.Pdf_FilesWithOwn, ReportText.Number(h.FilesStored));
            KeyValue(numbers, Strings.Pdf_SectionShares, ReportText.Number(data.Shares.Count));
            KeyValue(numbers, Strings.Pdf_AccountsGroups, ReportText.Number(h.Principals));
            KeyValue(numbers, Strings.Pdf_PeopleWithAccess, $"{ReportText.Number(data.People.Count)} ({ReportText.Number(data.PeopleWithAccess)})");
            KeyValue(numbers, Strings.Pdf_UnreadableFolders, ReportText.Number(data.UnreadableFolders), warn: data.UnreadableFolders > 0);
            KeyValue(numbers, Strings.Pdf_ReadErrors, ReportText.Number(data.Errors.Count), warn: data.Errors.Count > 0);
            KeepStart(numbers, whole: true);

            Heading2(Strings.Pdf_FindingsBySeverity);
            SeverityChips();

            // Warnungen zuerst (auch die zur Kürzung dieses PDFs), dann die Lesehilfen
            Heading2(Strings.Pdf_Notes);
            foreach (var note in data.Notes.Where(n => n.Kind == NoteKind.Warnung)) Note(note.Kind, note.Text);
            if (Truncations() is { Count: > 0 } cut)
                Note(NoteKind.Warnung, Texts.Format(Strings.Pdf_TruncatedLists, string.Join(Strings.Text_ListSeparator, cut)));
            foreach (var note in data.Notes.Where(n => n.Kind != NoteKind.Warnung)) Note(note.Kind, note.Text);

            if (data.Comparison is { } c)
            {
                Heading2(Strings.Pdf_ComparisonHeading);
                if (c.ScopeNote.Length > 0) Note(NoteKind.Warnung, c.ScopeNote);
                if (!c.HasChanges)
                {
                    Paragraph(Texts.Format(Strings.Pdf_NoChangesSince, ReportText.DateTime(c.OlderStartedAt)));
                    return;
                }
                var table = NewTable(framed: false, 9.0, 4.0);
                table.Borders.Visible = false;
                KeyValue(table, Strings.Pdf_PreviousRun, ReportText.DateTime(c.OlderStartedAt));
                KeyValue(table, Strings.Cmp_NewFindings, ReportText.Number(c.NewFindings), warn: c.NewFindings > 0);
                KeyValue(table, Strings.Cmp_ResolvedFindings, ReportText.Number(c.ResolvedFindings));
                KeyValue(table, Strings.Cmp_ChangedAces, ReportText.Number(c.AceChanges));
                KeyValue(table, Strings.Cmp_ChangedMemberships, ReportText.Number(c.MembershipChanges));
                KeyValue(table, Strings.Pdf_FoldersAddedRemoved,
                    $"{ReportText.Number(c.FoldersAdded)} / {ReportText.Number(c.FoldersRemoved)}");
                KeyValue(table, Strings.Cmp_MoreAccess, ReportText.Number(c.MoreAccess), warn: c.MoreAccess > 0);
                KeyValue(table, Strings.Cmp_LessAccess, ReportText.Number(c.LessAccess));
                KeepStart(table, whole: true);
            }
        }

        /// <summary>Welche Listen im PDF gekürzt sind, z. B. „Befunde (1.000 von 4.512)“.</summary>
        private List<string> Truncations()
        {
            var cut = new List<string>();
            void Check(string what, int total, int limit)
            {
                if (total > limit) cut.Add(Texts.Format(Strings.Pdf_CutPart, what, ReportText.Number(limit), ReportText.Number(total)));
            }
            Check(Strings.Col_Findings, data.Findings.Count, limits.Findings);
            int longShares = data.Shares.Count(s => s.Readable && s.Permissions.Count > limits.ShareEntries);
            if (longShares > 0)
                cut.Add(Texts.Format(Texts.Plural(longShares, Strings.Pdf_CutSharesOne, Strings.Pdf_CutSharesMany),
                    ReportText.Number(limits.ShareEntries), ReportText.Number(longShares)));
            Check(Strings.Pdf_SectionFolders, data.Folders.Count, limits.Folders);
            Check(Strings.Col_People, data.PeopleWithAccess, limits.People);
            if (data.Comparison is { } c)
            {
                // Die Grenze gilt je Bereich (Zugriffe: je Ordner zusammengefasst) – gezählt wird, was zu sehen ist
                int shown = c.Rows.GroupBy(r => r.Area).Sum(g => g.Key == ChangeArea.Access
                    ? AccessGroups(g.ToList()).Take(limits.Changes).Sum(x => x.Count)
                    : Math.Min(g.Count(), limits.Changes));
                if (shown < c.Rows.Count) cut.Add(Texts.Format(Strings.Pdf_CutChanges, ReportText.Number(shown), ReportText.Number(c.Rows.Count)));
            }
            Check(Strings.Pdf_ErrorsLower, data.Errors.Count, limits.Errors);
            return cut;
        }

        private void SeverityChips()
        {
            // Vier Kacheln mit schmalen leeren Spalten dazwischen (Rahmen würden die Breite verfälschen)
            const double gap = 0.2, chip = (ContentWidthCm - 3 * gap) / 4;
            var chips = NewTable(chip, gap, chip, gap, chip, gap, chip);
            chips.Borders.Visible = false;
            chips.TopPadding = 4;
            chips.BottomPadding = 4;
            var row = chips.AddRow();
            var severities = new[] { Severity.Hoch, Severity.Mittel, Severity.Niedrig, Severity.Info };
            for (int i = 0; i < severities.Length; i++)
            {
                var (fill, font) = ReportColors.For(severities[i]);
                var cell = row.Cells[2 * i];
                cell.Shading.Color = Rgb(fill);
                var count = cell.AddParagraph(ReportText.Number(data.CountFindings(severities[i])));
                count.Format.Font.Size = 18;
                count.Format.Font.Bold = true;
                count.Format.Font.Color = Rgb(font);
                count.Format.Alignment = ParagraphAlignment.Center;
                var label = cell.AddParagraph(ReportText.Severity(severities[i]));
                label.Format.Font.Color = Rgb(font);
                label.Format.Alignment = ParagraphAlignment.Center;
            }
            Spacer(4);
        }

        private void Findings()
        {
            Heading1(Strings.Pdf_SectionFindings);
            if (data.Findings.Count == 0)
            {
                Paragraph(Strings.Pdf_NoFindings);
            }
            else
            {
                Intro(Texts.Format(Strings.Pdf_FindingsIntro, Texts.Quote(Strings.Pdf_SectionRules)));
                // Je Regel eine Tabelle, deren Kopf (Schwere, Regel, Titel) sich auf jeder Seite wiederholt –
                // so steht der Regeltitel einmal statt in jeder Zeile, und jede Seite bleibt für sich lesbar
                var totals = data.Findings.GroupBy(f => (f.Severity, f.RuleId)).ToDictionary(g => g.Key, g => g.Count());
                var shown = data.Findings.Take(limits.Findings).ToList();
                for (int start = 0, end; start < shown.Count; start = end)
                {
                    var key = (shown[start].Severity, shown[start].RuleId);
                    end = start + 1;
                    while (end < shown.Count && (shown[end].Severity, shown[end].RuleId) == key) end++;
                    if (start > 0) Spacer(6);
                    FindingGroup(shown.GetRange(start, end - start), totals[key]);
                }
                Truncated(data.Findings.Count, limits.Findings, Strings.Pdf_TruncatedFindings);
            }

            Heading2(Strings.Pdf_SectionRules);
            Intro(Strings.Pdf_RulesIntro);
            var rules = NewTable(1.2, 1.6, 1.7, 6.8, 5.7);
            HeaderRow(rules, Strings.Col_Rule, Strings.Col_Severity, Strings.Col_Findings, Strings.Col_TitleExplanation, Strings.Col_Recommendation);
            foreach (var r in data.Rules)
            {
                var row = rules.AddRow();
                CellText(rules, row, 0, r.Id);
                SeverityCell(row.Cells[1], r.Severity);
                CellText(rules, row, 2, ReportText.Number(r.Count)).Format.Alignment = ParagraphAlignment.Right;
                var p = CellText(rules, row, 3, r.Title, bold: true);
                if (r.Explanation.Length > 0)
                {
                    p.AddLineBreak();
                    text.Append(p, r.Explanation, CellWidth(rules, 3), TableSize);
                }
                CellText(rules, row, 4, r.Recommendation);
            }
            KeepStart(rules);
        }

        /// <summary>
        /// Befunde einer Regel: Kopfbalken in der Farbe der Schwere, darunter Pfad, Konto, Meldung. Regeln ohne
        /// Konto (z. B. unterbrochene Vererbung, nicht lesbar) bekommen keine leere Konto-Spalte.
        /// </summary>
        private void FindingGroup(IReadOnlyList<FindingRow> rows, int total)
        {
            var first = rows[0];
            bool accounts = rows.Any(f => f.Account.Length > 0);
            var table = accounts ? NewTable(6.6, 3.8, 6.6) : NewTable(8.5, 8.5);

            var bar = table.AddRow();
            bar.HeadingFormat = true;
            bar.TopPadding = 3;
            bar.BottomPadding = 3;
            var (fill, font) = ReportColors.For(first.Severity);
            var cell = bar.Cells[0];
            cell.MergeRight = table.Columns.Count - 1;
            cell.Shading.Color = Rgb(fill);
            var p = cell.AddParagraph();
            p.Format.Font.Bold = true;
            p.Format.Font.Color = Rgb(font);
            p.Format.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm) - 2 * PaddingPt - 1, TabAlignment.Right);
            p.AddText($"{ReportText.Severity(first.Severity)} · {ReportText.Clean(first.RuleId)} {ReportText.Clean(first.Title)}");
            p.AddTab();
            var count = Texts.Format(Texts.Plural(total, Strings.Pdf_FindingCountOne, Strings.Pdf_FindingCountMany), ReportText.Number(total));
            p.AddText(rows.Count < total ? Texts.Format(Strings.Pdf_ShownOf, count, ReportText.Number(rows.Count)) : count);

            if (accounts) HeaderRow(table, light: true, Strings.Col_Path, Strings.Col_Account, Strings.Col_Message);
            else HeaderRow(table, light: true, Strings.Col_Path, Strings.Col_Message);
            foreach (var f in rows)
            {
                var row = table.AddRow();
                CellText(table, row, 0, f.Path);
                if (accounts) CellText(table, row, 1, OrDash(f.Account));
                CellText(table, row, accounts ? 2 : 1, f.Message);
            }
            KeepStart(table);
        }

        private void Shares()
        {
            Heading1(Strings.Pdf_SectionShares);
            if (data.Shares.Count == 0)
            {
                Paragraph(Strings.Pdf_NoShares);
                return;
            }
            Intro(Strings.Pdf_SharesIntro);
            var table = NewTable(4.0, 3.6, 3.6, 5.8);
            HeaderRow(table, Strings.Col_Share, Strings.Col_Path, Strings.Col_Remark, Strings.Col_SharePermissions);
            foreach (var s in data.Shares)
            {
                var row = table.AddRow();
                var name = CellText(table, row, 0, s.Unc, bold: true);
                if (s.IsAdminShare)
                {
                    name.AddLineBreak();
                    name.AddFormattedText(Strings.Pdf_AdminShare, TextFormat.Italic);
                }
                CellText(table, row, 1, s.LocalPath.Length == 0 ? ReportText.Dash : s.LocalPath);
                CellText(table, row, 2, s.Remark);
                if (!s.Readable || s.Permissions.Count == 0)
                {
                    CellText(table, row, 3, s.PermissionsText, color: s.Readable ? null : WarningColor);
                    continue;
                }
                // Eine Tabellenzeile kann nicht über Seiten laufen – zu viele Einträge würden abgeschnitten
                foreach (var ace in s.Permissions.Take(limits.ShareEntries))
                    CellText(table, row, 3, $"{ace.Account}: {ace.Right}",
                        color: ace.IsDeny ? WarningColor : null);
                if (s.Permissions.Count > limits.ShareEntries)
                    CellText(table, row, 3, Texts.Format(Strings.Pdf_MoreEntries, ReportText.Number(s.Permissions.Count - limits.ShareEntries)), bold: true, color: WarningColor);
            }
            KeepStart(table);
        }

        private void Folders()
        {
            Heading1(Strings.Pdf_SectionFolders);
            Intro(Strings.Report_NoteListed,
                keepWithNext: data.Folders.Count > 0);
            if (data.Folders.Count == 0)
            {
                Paragraph(Strings.Pdf_NoFolders);
                return;
            }

            foreach (var f in data.Folders.Take(limits.Folders))
            {
                // Pfad, Kurzinfo und Tabelle bleiben zusammen: ein Ordnerkopf allein am Seitenende hilft niemandem
                var path = _section.AddParagraph();
                path.Format.SpaceBefore = 9;
                path.Format.SpaceAfter = 1;
                path.Format.KeepWithNext = true;
                text.Append(path, f.Path, ContentWidth, NormalSize, bold: true);

                var meta = new List<string> { Texts.Format(Strings.Pdf_Depth, f.Depth.ToString(CultureInfo.InvariantCulture)) };
                if (f.IsFile) meta.Add(Strings.Pdf_File);
                if (f.InheritanceBroken) meta.Add(Strings.Pdf_InheritanceBroken);
                else if (f.InheritanceDiffers) meta.Add(Strings.Pdf_InheritanceDiffers);
                meta.Add(Texts.Format(Strings.Pdf_Owner, f.Owner.Length == 0 ? Strings.Pdf_Unknown : f.Owner));
                if (f.ShareUnc is { Length: > 0 } share) meta.Add(Texts.Format(Strings.Pdf_ShareLabel, share));
                var info = _section.AddParagraph();
                info.Style = SmallStyle;
                info.Format.KeepWithNext = true;
                text.Append(info, string.Join(" · ", meta), ContentWidth, SmallSize);

                if (f.Error is not null)
                {
                    var error = _section.AddParagraph();
                    error.Format.Font.Color = WarningColor;
                    error.Format.KeepWithNext = f.Aces.Count > 0;
                    text.Append(error, Texts.Format(Strings.Pdf_NotReadable, f.Error), ContentWidth, NormalSize);
                    // Gelesene Einträge trotzdem zeigen (z. B. Rechte gelesen, aber Inhalt nicht auflistbar) – wie im Excel-Bericht
                    if (f.Aces.Count == 0) continue;
                }

                var explicitAces = f.Aces.Where(a => !a.IsInherited).ToList();
                var inherited = f.Aces.Where(a => a.IsInherited).ToList();
                var inheritedText = Texts.Format(Strings.Pdf_Inherited, string.Join(Strings.Text_ListSeparator, inherited.Select(a =>
                    a.IsDeny ? Texts.Format(Strings.Pdf_InheritedDenyAce, a.Account, a.Right) : $"{a.Account} ({a.Right})")));
                if (explicitAces.Count > 0)
                {
                    // „Recht“ breit genug für Verweigerungen wie „Löschen, Unterordner und Dateien löschen“ (ohne Trennung im Wort)
                    var table = NewTable(4.8, 1.9, 2.25, 2.05, 6.0);
                    HeaderRow(table, light: true, Strings.Col_Account, Strings.Col_AccountKind, Strings.Col_Right, Strings.Col_AllowDeny, Strings.Col_AppliesTo);
                    foreach (var a in explicitAces)
                    {
                        var row = table.AddRow();
                        Color? color = a.IsDeny ? WarningColor : null;
                        CellText(table, row, 0, a.Account, color: color);
                        CellText(table, row, 1, ReportText.Kind(a.Kind), color: color);
                        CellText(table, row, 2, a.Right, bold: a.IsDeny, color: color);
                        CellText(table, row, 3, ReportText.AllowDeny(a.IsDeny), bold: a.IsDeny, color: color);
                        CellText(table, row, 4, a.AppliesTo, color: color);
                    }
                    if (inherited.Count > 0)
                    {
                        // Als letzte Tabellenzeile statt als Absatz: so rutscht sie nicht allein auf die nächste Seite
                        var row = table.AddRow();
                        row.Borders.Left.Visible = false;
                        row.Borders.Right.Visible = false;
                        row.Borders.Bottom.Visible = false;
                        row.TopPadding = 2;
                        var cell = row.Cells[0];
                        cell.MergeRight = table.Columns.Count - 1;
                        var p = cell.AddParagraph();
                        p.Format.Font.Size = SmallSize;
                        p.Format.Font.Color = MutedColor;
                        text.Append(p, inheritedText, ContentWidth - 2 * PaddingPt - 1, SmallSize);
                    }
                    KeepStart(table, whole: true);
                }
                else if (inherited.Count > 0)
                {
                    var p = _section.AddParagraph();
                    p.Style = SmallStyle;
                    p.Format.SpaceBefore = 2;
                    text.Append(p, inheritedText, ContentWidth, SmallSize);
                }
                if (f.Aces.Count == 0)
                    Paragraph(Strings.Pdf_NoEntries, small: true);
            }
            Truncated(data.Folders.Count, limits.Folders, Strings.Pdf_TruncatedFolders);
        }

        private void People()
        {
            Heading1(Strings.Pdf_SectionPeople);
            var withAccess = data.People.Where(p => p.FolderCount > 0).ToList();
            Intro(Texts.Format(Strings.Pdf_PeopleIntro, ReportText.Number(withAccess.Count), ReportText.Number(data.People.Count),
                    Texts.Quote(Strings.Col_Folders), Texts.Quote(Strings.Sheet_AccessPerPerson)),
                keepWithNext: withAccess.Count > 0);
            if (withAccess.Count == 0) return;

            var table = NewTable(4.4, 1.5, 2.0, 6.5, 2.6);
            HeaderRow(table, Strings.Col_Person, Strings.Col_Folders, Strings.Col_HighestRight, Strings.Col_ViaGroups, Strings.Col_Note);
            foreach (var person in withAccess.Take(limits.People))
            {
                var row = table.AddRow();
                var name = CellText(table, row, 0, person.Name, bold: true);
                if (person.DisplayName.Length > 0 && !person.Name.EndsWith(person.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    name.AddLineBreak();
                    text.Append(name, person.DisplayName, CellWidth(table, 0), TableSize);
                }
                CellText(table, row, 1, ReportText.Number(person.FolderCount)).Format.Alignment = ParagraphAlignment.Right;
                CellText(table, row, 2, Rights.Label(person.Highest));
                CellText(table, row, 3, person.ViaGroups.Count > 0 ? string.Join(", ", person.ViaGroups)
                    : person.HasDirect ? Strings.Pdf_OnlyDirect : ReportText.Dash);
                if (person.IsDisabled) CellText(table, row, 4, Strings.Pdf_Disabled, bold: true, color: WarningColor);
                if (person.HasDirect) CellText(table, row, 4, Strings.Pdf_DirectlyGranted);
            }
            KeepStart(table);
            Truncated(withAccess.Count, limits.People, Strings.Pdf_TruncatedPeople);
            int without = data.People.Count - withAccess.Count;
            if (without > 0)
                Paragraph(Texts.Format(Strings.Pdf_PeopleWithout, ReportText.Number(without), Texts.Quote(Strings.Sheet_People)), small: true);
        }

        private void Changes(ComparisonData comparison)
        {
            Heading1(Strings.Pdf_SectionChanges);
            if (comparison.ScopeNote.Length > 0) Note(NoteKind.Warnung, comparison.ScopeNote);
            if (!comparison.HasChanges)
            {
                Intro(Texts.Format(Strings.Pdf_ComparedWithRun, ReportText.DateTime(comparison.OlderStartedAt)));
                Paragraph(Strings.Pdf_NoChanges);
                return;
            }
            Intro(Texts.Format(Strings.Pdf_ChangesIntro, ReportText.DateTime(comparison.OlderStartedAt)));

            // Je Bereich eine eigene Tabelle mit passenden Spalten; unbekannte Bereiche (neuere Version) hinten an
            var areas = ChangeArea.Order.Concat(comparison.Rows.Select(r => r.Area).Distinct().Except(ChangeArea.Order));
            foreach (var area in areas)
            {
                var rows = comparison.Rows.Where(r => r.Area == area).ToList();
                if (rows.Count == 0) continue;
                switch (area)
                {
                    case ChangeArea.Finding:
                        ChangeTable(Strings.Col_Findings, null, rows, [1.8, 5.4, 3.4, 6.4], [Strings.Col_Change, Strings.Col_Path, Strings.Col_Account, Strings.Col_Finding], (t, row, r) =>
                        {
                            bool added = r.Change == ChangeText.New;
                            CellText(t, row, 0, r.Change, bold: added, color: added ? WarningColor : null);
                            CellText(t, row, 1, r.Subject);
                            CellText(t, row, 2, OrDash(r.Account));
                            CellText(t, row, 3, r.After.Length > 0 ? r.After : r.Before);
                        });
                        break;
                    case ChangeArea.Ace:
                        ChangeTable(Strings.Pdf_PermissionEntries, Strings.Pdf_PermissionEntriesIntro,
                            rows, [2.3, 4.1, 2.8, 3.9, 3.9], [Strings.Col_Change, Strings.Col_Path, Strings.Col_Account, Strings.Col_Before, Strings.Col_After], (t, row, r) =>
                            {
                                CellText(t, row, 0, r.Change, bold: r.Change == ReportText.Change(ChangeKind.Hinzugefuegt));
                                CellText(t, row, 1, r.Subject);
                                CellText(t, row, 2, OrDash(r.Account));
                                CellText(t, row, 3, OrDash(r.Before));
                                CellText(t, row, 4, OrDash(r.After));
                            });
                        break;
                    case ChangeArea.Membership:
                        ChangeTable(Strings.Pdf_GroupMemberships, null, rows, [3.0, 7.0, 7.0], [Strings.Col_Change, Strings.Col_Group, Strings.Col_Member], (t, row, r) =>
                        {
                            CellText(t, row, 0, r.Change, bold: r.Change == ChangeText.Joined);
                            CellText(t, row, 1, r.Subject);
                            CellText(t, row, 2, OrDash(r.Account));
                        });
                        break;
                    case ChangeArea.Folder:
                        ChangeTable(Strings.Pdf_FolderChanges, null, rows, [4.5, 12.5], [Strings.Col_Change, Strings.Col_Path], (t, row, r) =>
                        {
                            CellText(t, row, 0, r.Change);
                            CellText(t, row, 1, r.Subject);
                        });
                        break;
                    case ChangeArea.Access:
                        AccessChanges(rows);
                        break;
                    default:
                        ChangeTable(area, null, rows, [2.4, 5.0, 3.4, 3.1, 3.1], [Strings.Col_Change, Strings.Col_PathOrGroup, Strings.Col_Account, Strings.Col_Before, Strings.Col_After], (t, row, r) =>
                        {
                            CellText(t, row, 0, r.Change);
                            CellText(t, row, 1, r.Subject);
                            CellText(t, row, 2, OrDash(r.Account));
                            CellText(t, row, 3, OrDash(r.Before));
                            CellText(t, row, 4, OrDash(r.After));
                        });
                        break;
                }
            }
        }

        /// <summary>Eine Tabelle je Änderungs-Bereich; die PDF-Grenze gilt je Bereich, damit keiner ganz verdrängt wird.</summary>
        private void ChangeTable(string title, string? intro, List<ChangeRow> rows, double[] widths, string[] headers,
            Action<Table, Row, ChangeRow> fill)
        {
            Heading2($"{title} ({ReportText.Number(rows.Count)})");
            if (intro is not null) Intro(intro);
            var table = NewTable(widths);
            HeaderRow(table, headers);
            foreach (var r in rows.Take(limits.Changes)) fill(table, table.AddRow(), r);
            KeepStart(table);
            Truncated(rows.Count, limits.Changes, Strings.Pdf_TruncatedChanges);
        }

        /// <summary>
        /// Geänderte Zugriffe von Personen, je Ordner und Richtung zusammengefasst: eine geänderte Gruppe im
        /// ACL betrifft schnell Hunderte Personen – Zeile für Zeile wären das Dutzende Seiten. Jede einzelne
        /// Person steht im Excel-Bericht.
        /// </summary>
        private void AccessChanges(List<ChangeRow> rows)
        {
            var groups = AccessGroups(rows);
            Heading2(Texts.Format(Strings.Pdf_PeopleAccess, ReportText.Number(rows.Count)));
            Intro(Texts.Format(Strings.Pdf_AccessChangesIntro, Texts.Quote(ChangeText.MoreAccess), Texts.Quote(Strings.Sheet_Changes)));
            var table = NewTable(2.6, 5.3, 5.9, 3.2);
            HeaderRow(table, Strings.Col_Change, Strings.Col_Path, Strings.Col_People, Strings.Col_BeforeAfter);
            foreach (var g in groups.Take(limits.Changes))
            {
                var row = table.AddRow();
                CellText(table, row, 0, g[0].Change, bold: g[0].Change == ChangeText.MoreAccess);
                CellText(table, row, 1, g[0].Subject);
                var names = g.Select(r => OrDash(r.Account)).ToList();
                const int maxNames = 15;
                var people = names.Count == 1 ? names[0]
                    : Texts.Format(Strings.Pdf_PeopleList, ReportText.Number(names.Count), string.Join(", ", names.Take(maxNames)))
                      + (names.Count > maxNames ? Texts.Format(Strings.Pdf_AndMore, ReportText.Number(names.Count - maxNames)) : "");
                CellText(table, row, 2, people);
                var steps = g.GroupBy(r => (r.Before, r.After))
                    .Select(t => $"{OrDash(t.Key.Before)} → {OrDash(t.Key.After)}" + (g.Count > 1 ? $" ({ReportText.Number(t.Count())})" : ""));
                CellText(table, row, 3, string.Join("\n", steps));
            }
            KeepStart(table);
            Truncated(groups.Count, limits.Changes, Strings.Pdf_TruncatedFolders);
        }

        /// <summary>Zugriffsänderungen je (Richtung, Pfad) in der Reihenfolge der Zeilen.</summary>
        private static List<List<ChangeRow>> AccessGroups(List<ChangeRow> rows) =>
            rows.GroupBy(r => (r.Change, r.Subject)).Select(g => g.ToList()).ToList();

        private void Errors()
        {
            Heading1(Strings.Pdf_SectionErrors);
            Intro(Strings.Pdf_ErrorsIntro);
            var table = NewTable(9.5, 7.5);
            HeaderRow(table, Strings.Col_Path, Strings.Col_Message);
            foreach (var e in data.Errors.Take(limits.Errors))
            {
                var row = table.AddRow();
                CellText(table, row, 0, e.Path);
                CellText(table, row, 1, e.Message);
            }
            KeepStart(table);
            Truncated(data.Errors.Count, limits.Errors, Strings.Pdf_TruncatedEntries);
        }

        private void SignOff()
        {
            var h = data.Header;
            var heading = Heading1(Strings.Pdf_SectionSignOff);
            heading.Format.SpaceBefore = 22;
            var intro = Paragraph(Texts.Format(Strings.Pdf_SignOffIntro, Texts.Quote(Strings.Pdf_Remarks)));
            intro.Format.KeepWithNext = true;

            // Bezug zum Lauf: das unterschriebene Blatt bleibt zuordenbar, auch wenn es getrennt abgelegt wird
            var reference = _section.AddParagraph();
            reference.Style = IntroStyle;
            reference.Format.SpaceAfter = 6;
            // {4} ist die Seitenzahl – ein Feld, das MigraDoc erst beim Setzen füllt: Text davor umbrechen, Feld, Rest anhängen
            var format = Texts.Format(h.Targets.Count == 1 ? Strings.Pdf_SignOffReferenceOne : Strings.Pdf_SignOffReferenceMany,
                ReportText.Period(h.StartedAt, h.FinishedAt), h.Machine.Length == 0 ? Strings.Pdf_UnknownMachine : h.Machine,
                ShortTargets(), ReportText.DateTime(h.CreatedAt), "{0}");
            var field = format.IndexOf("{0}", StringComparison.Ordinal);
            text.Append(reference, field < 0 ? format : format[..field], ContentWidth, SmallSize);
            if (field >= 0)
            {
                reference.AddNumPagesField();
                reference.AddText(format[(field + 3)..]);
            }

            var table = NewTable(framed: false, 3.5, 13.5);
            table.Style = StyleNames.Normal;
            table.Borders.Visible = false;
            void Line(string label, double heightCm)
            {
                var row = table.AddRow();
                row.Height = Unit.FromCentimeter(heightCm);
                row.HeightRule = RowHeightRule.AtLeast;
                row.VerticalAlignment = VerticalAlignment.Bottom;
                var l = row.Cells[0].AddParagraph(label);
                l.Format.Font.Bold = label.Length > 0;
                row.Cells[1].Borders.Bottom.Visible = true;
                row.Cells[1].Borders.Bottom.Width = 0.75;
                row.Cells[1].Borders.Bottom.Color = TextColor;
            }
            Line(Strings.Pdf_ReviewedBy, 1.1);
            Line(Strings.Pdf_Date, 1.1);
            Line(Strings.Pdf_Signature, 1.5);
            Line(Strings.Pdf_Remarks, 1.1);
            Line("", 0.9);
            Line("", 0.9);
            Line("", 0.9);
            // Der Block gehört zusammen auf eine Seite (gut 7 cm – passt sicher)
            table.Rows[0].KeepWith = table.Rows.Count - 1;
        }

        // ---- Bausteine ------------------------------------------------------------------------------

        private Paragraph Heading1(string title)
        {
            var p = _section.AddParagraph(title);
            p.Style = StyleNames.Heading1;
            Bookmark(p, title);
            return p;
        }

        private void Heading2(string title)
        {
            var p = _section.AddParagraph(title);
            p.Style = StyleNames.Heading2;
            Bookmark(p, title);
        }

        /// <summary>Sprungmarke für das Inhaltsverzeichnis, falls der Abschnitt dort steht.</summary>
        private void Bookmark(Paragraph heading, string title)
        {
            foreach (var (entry, bookmark) in _contents)
                if (entry == title) heading.AddBookmark(bookmark);
        }

        private Paragraph Paragraph(string content, bool small = false)
        {
            var p = _section.AddParagraph();
            if (small) p.Style = SmallStyle;
            text.Append(p, content, ContentWidth, small ? SmallSize : NormalSize);
            return p;
        }

        /// <summary>Einleitung unter einer Überschrift; ohne folgenden Inhalt normaler Kleintext.</summary>
        private void Intro(string content, bool keepWithNext = true)
        {
            var p = _section.AddParagraph();
            p.Style = keepWithNext ? IntroStyle : SmallStyle;
            text.Append(p, content, ContentWidth, SmallSize);
        }

        private void Note(NoteKind kind, string content)
        {
            var p = _section.AddParagraph();
            p.Format.LeftIndent = Unit.FromCentimeter(0.4);
            p.Format.FirstLineIndent = Unit.FromCentimeter(-0.4);
            p.AddText("•");
            p.AddTab();
            if (kind == NoteKind.Warnung)
            {
                var label = p.AddFormattedText(Strings.Pdf_Attention, TextFormat.Bold);
                label.Color = WarningColor;
            }
            p.Format.TabStops.AddTabStop(Unit.FromCentimeter(0.4));
            text.Append(p, content, Unit.FromCentimeter(ContentWidthCm - 0.4).Point, NormalSize);
        }

        private void Spacer(double points)
        {
            var p = _section.AddParagraph();
            p.Format.Font.Size = 1;
            p.Format.SpaceAfter = points;
        }

        /// <summary>Hinweis auf eine gekürzte Liste; <paramref name="what"/> im Dativ Plural („von 302 Ordnern“).</summary>
        /// <param name="format">Satz mit {0} = gezeigt, {1} = insgesamt (Strings.Pdf_Truncated…).</param>
        private void Truncated(int total, int shown, string format)
        {
            if (total <= shown) return;
            var p = _section.AddParagraph();
            p.Format.SpaceBefore = 4;
            p.Format.Font.Color = WarningColor;
            p.AddText(Texts.Format(format, ReportText.Number(shown), ReportText.Number(total)));
        }

        /// <summary>
        /// Text mit Feldern, die MigraDoc erst beim Setzen füllt (Seite, Seitenzahl): {0}, {1} … im übersetzten Text werden
        /// zu Feldern, damit die Reihenfolge der Sprache folgt („Seite 1 von 3“, „第 1 页，共 3 页“).
        /// </summary>
        private static void AddWithFields(Paragraph p, string format, Action<int> addField)
        {
            var parts = System.Text.RegularExpressions.Regex.Split(format, @"\{(\d)\}");
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0) { if (parts[i].Length > 0) p.AddText(parts[i]); }
                else addField(int.Parse(parts[i], CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// Neue Tabelle, die genau auf dem Seitenrand sitzt. MigraDoc rückt Tabellen ohne Angabe um minus
        /// Innenabstand ein (Zelltext fluchtet, der Rahmen ragt links hinaus). Gerahmte Tabellen bekommen
        /// Einzug 0; ungerahmte (Schlüssel/Wert, Unterschriften) zusätzlich keinen linken Innenabstand,
        /// damit Text und Linien mit dem Fließtext fluchten.
        /// </summary>
        private Table NewTable(params double[] widthsCm) => NewTable(framed: true, widthsCm);

        private Table NewTable(bool framed, params double[] widthsCm)
        {
            var table = _section.AddTable();
            table.Rows.LeftIndent = Unit.FromPoint(0);
            table.Style = TableStyle;
            table.Borders.Color = LineColor;
            table.Borders.Width = 0.5;
            table.LeftPadding = framed ? PaddingPt : 0;
            table.RightPadding = PaddingPt;
            table.TopPadding = 1.5;
            table.BottomPadding = 1.5;
            foreach (var w in widthsCm) table.AddColumn(Unit.FromCentimeter(w));
            return table;
        }

        private void HeaderRow(Table table, params string[] titles) => HeaderRow(table, light: false, titles);

        private void HeaderRow(Table table, bool light, params string[] titles)
        {
            var row = table.AddRow();
            row.HeadingFormat = true; // wiederholt sich auf jeder Seite
            row.Shading.Color = light ? AccentLightColor : AccentColor;
            row.Format.Font.Bold = true;
            row.Format.Font.Color = light ? TextColor : Colors.White;
            for (int i = 0; i < titles.Length; i++)
                text.Append(row.Cells[i].AddParagraph(), titles[i], CellWidth(table, i), TableSize, bold: true);
        }

        private Paragraph CellText(Table table, Row row, int column, string? content, bool bold = false, Color? color = null)
        {
            var p = row.Cells[column].AddParagraph();
            if (color is { } c) p.Format.Font.Color = c;
            text.Append(p, content, CellWidth(table, column), TableSize, bold);
            return p;
        }

        private static string OrDash(string value) => value.Length == 0 ? ReportText.Dash : value;

        private static void SeverityCell(Cell cell, Severity severity)
        {
            var (fill, font) = ReportColors.For(severity);
            cell.Shading.Color = Rgb(fill);
            var p = cell.AddParagraph(ReportText.Severity(severity));
            p.Format.Font.Bold = true;
            p.Format.Font.Color = Rgb(font);
        }

        private void KeyValue(Table table, string key, string value, bool warn = false)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(key);
            var v = row.Cells[1].AddParagraph(value);
            v.Format.Alignment = ParagraphAlignment.Right;
            v.Format.Font.Bold = true;
            if (warn) v.Format.Font.Color = WarningColor;
        }

        /// <summary>
        /// Hält den Tabellenkopf mit den ersten <paramref name="rows"/> Datenzeilen zusammen – mit
        /// <paramref name="whole"/> die ganze Tabelle, wenn sie klein genug ist. Zusammen mit KeepWithNext der
        /// Überschriften und Einleitungen davor steht so nichts davon allein am Seitenende. Der Block bleibt unter
        /// <see cref="KeepBudgetPt"/> (geschätzt); mindestens die erste Datenzeile gehört immer dazu.
        /// </summary>
        private void KeepStart(Table table, int rows = 3, bool whole = false)
        {
            int headers = table.Rows.Cast<Row>().TakeWhile(r => r.HeadingFormat).Count();
            int dataRows = table.Rows.Count - headers;
            if (table.Rows.Count < 2 || dataRows == 0) return;

            double height = 0;
            for (int r = 0; r < headers; r++) height += RowHeight(table, r);
            int keep = 0;
            int max = whole ? dataRows : Math.Min(rows, dataRows);
            for (int r = headers; r < headers + max; r++)
            {
                height += RowHeight(table, r);
                if (keep > 0 && height > KeepBudgetPt) break;
                keep++;
            }
            // Ganze Tabelle passt nicht: dann wie üblich nur der Anfang
            if (whole && keep < dataRows) keep = Math.Min(keep, rows);
            // Bliebe hinter dem Block genau eine Zeile, stünde sie allein auf der nächsten Seite – Block um eine kürzen,
            // dann halten die letzten beiden Zeilen zusammen
            if (dataRows - keep == 1 && keep > 1) keep--;
            // An die erste Datenzeile hängen, nicht an den Kopf: Zeilen, die an einer Kopfzeile hängen, wiederholt
            // MigraDoc sonst als Kopf auf jeder Folgeseite. Den Kopf setzt MigraDoc ohnehin nie ohne Datenzeile.
            var first = table.Rows[headers];
            if (keep > 1) first.KeepWith = Math.Max(first.KeepWith, keep - 1);
            // Und am Ende: die letzte Zeile (etwa „Geerbt: …“) nicht allein auf die nächste Seite
            if (dataRows - keep >= 2) table.Rows[^2].KeepWith = Math.Max(table.Rows[^2].KeepWith, 1);
        }

        /// <summary>Geschätzte Höhe einer Tabellenzeile in Punkt (fett gemessen – lieber zu hoch als zu niedrig).</summary>
        private double RowHeight(Table table, int index)
        {
            var row = table.Rows[index];
            double size = table.Style == StyleNames.Normal ? NormalSize : TableSize;
            double max = 0;
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var cell = row.Cells[c];
                double width = CellWidth(table, c);
                for (int m = 1; m <= cell.MergeRight && c + m < table.Columns.Count; m++) width += table.Columns[c + m].Width.Point;
                double height = 0;
                foreach (var p in cell.Elements.OfType<Paragraph>())
                    height += text.Lines(PlainText(p), width, size, bold: true) * size * LineFactor + 1;
                max = Math.Max(max, height);
            }
            max = Math.Max(max, row.Height.Point); // Mindesthöhe (Unterschriftslinien), 0 wenn nicht gesetzt
            return max + table.TopPadding.Point + table.BottomPadding.Point + 1;
        }

        /// <summary>Text eines Absatzes mit Zeilenumbrüchen – für die Höhenschätzung.</summary>
        private static string PlainText(Paragraph paragraph)
        {
            var sb = new StringBuilder();
            void Walk(IEnumerable<DocumentObject> elements)
            {
                foreach (var e in elements)
                {
                    switch (e)
                    {
                        case Text t: sb.Append(t.Content); break;
                        case FormattedText f: Walk(f.Elements.Cast<DocumentObject>()); break;
                        case Character { SymbolName: SymbolName.LineBreak }: sb.Append('\n'); break;
                        case Character { SymbolName: SymbolName.Tab }: sb.Append("    "); break;
                    }
                }
            }
            Walk(paragraph.Elements.Cast<DocumentObject>());
            return sb.ToString();
        }

        /// <summary>Nutzbare Breite einer Zelle in Punkt (Spalte minus Innenabstand und Linie).</summary>
        private static double CellWidth(Table table, int column) =>
            table.Columns[column].Width.Point - table.LeftPadding.Point - table.RightPadding.Point - 1;

        private string ShortTargets()
        {
            var targets = data.Header.Targets;
            var label = targets.Count switch
            {
                0 => "",
                1 => targets[0],
                _ => Texts.Format(Strings.Pdf_FirstTargetMore, targets[0], targets.Count - 1),
            };
            label = ReportText.Clean(label);
            if (label.Length <= 70) return label;
            int cut = char.IsHighSurrogate(label[68]) ? 68 : 69; // kein halbes Zeichen stehen lassen
            return label[..cut] + "…";
        }

        private static Color Rgb(string hex)
        {
            var value = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
    }
}

/// <summary>
/// Wie viele Einträge das PDF je Abschnitt höchstens zeigt (MigraDoc setzt grob 200 Tabellenzeilen je Sekunde);
/// <paramref name="Changes"/> gilt je Bereich der Änderungen. <paramref name="ShareEntries"/> begrenzt die
/// Freigabe-Berechtigungen in einer Tabellenzelle: MigraDoc kann eine Zeile nicht über Seiten teilen und schnitte
/// den Rest ohne Hinweis ab.
/// </summary>
internal sealed record PdfLimits(int Findings, int Folders, int People, int Changes, int Errors, int ShareEntries = 20)
{
    public static PdfLimits Default { get; } = new(Findings: 1_000, Folders: 250, People: 1_000, Changes: 1_000, Errors: 250);
}
