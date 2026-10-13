using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Zutritt.Core.Model;

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
    public static void Write(ScanResult scan, ComparisonResult? comparison, string path)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ReportFile.FullPath(path); // Ziel prüfen, bevor die Auswertung läuft
        Write(ReportDataBuilder.Build(scan, comparison), path);
    }

    internal static void Write(ReportData data, string path, PdfLimits? limits = null)
    {
        ReportFile.FullPath(path);
        // Erst vollständig rendern, dann schreiben: scheitert das Rendern, bleibt die Zieldatei unberührt
        var renderer = new PdfDocumentRenderer { Document = CreateDocument(data, limits) };
        renderer.RenderDocument();
        var pdf = renderer.PdfDocument;
        pdf.Info.Title = ReportText.Title;
        pdf.Info.Author = ReportText.Product;
        pdf.Info.Subject = ReportText.Clean(string.Join("; ", data.Header.Targets));
        pdf.Info.Creator = $"{ReportText.Product} {data.Header.ReportVersion}".TrimEnd();
        ReportFile.Write(path, stream => pdf.Save(stream, false));
    }

    /// <summary>Baut das MigraDoc-Dokument (ohne zu rendern) – für Tests und Vorschau.</summary>
    internal static Document CreateDocument(ReportData data, PdfLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var family = PdfFonts.Family;
        using var text = new PdfText(family);
        return new Layout(data, limits ?? PdfLimits.Default, text, family).Build();
    }

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

        private static readonly Color TextColor = Rgb(ReportColors.Text);
        private static readonly Color MutedColor = Rgb(ReportColors.Muted);
        private static readonly Color LineColor = Rgb(ReportColors.Line);
        private static readonly Color AccentColor = Rgb(ReportColors.AccentBlue);
        private static readonly Color VioletColor = Rgb(ReportColors.AccentViolet);
        private static readonly Color AccentLightColor = Rgb(ReportColors.AccentLight);
        private static readonly Color WarningColor = Rgb(ReportColors.Warning);

        private readonly Document _doc = new();
        private Section _section = null!;

        public Document Build()
        {
            _doc.Info.Title = ReportText.Title;
            _doc.Info.Author = ReportText.Product;
            _doc.Info.Subject = ReportText.Clean(string.Join("; ", data.Header.Targets));
            DefineStyles();
            _section = _doc.AddSection();
            SetupPage();

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
            p.AddText($"Erstellt am {ReportText.DateTime(data.Header.CreatedAt)} · Vertraulich");
            p.AddTab();
            p.AddText("Seite ");
            p.AddPageField();
            p.AddText(" von ");
            p.AddNumPagesField();
        }

        private void Cover()
        {
            var h = data.Header;
            var label = _section.AddParagraph(ReportText.Product.ToUpperInvariant());
            label.Format.SpaceBefore = Unit.FromCentimeter(3.0);
            label.Format.Font.Size = 11;
            label.Format.Font.Bold = true;
            label.Format.Font.Color = VioletColor;

            var title = _section.AddParagraph(ReportText.Title);
            title.Format.Font.Size = 30;
            title.Format.Font.Bold = true;
            title.Format.SpaceAfter = 2;

            var subtitle = _section.AddParagraph("Freigaben und Ordnerrechte – wer darf worauf zugreifen und über welche Gruppe");
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
            CoverRow(info, "Ziele", string.Join("\n", h.Targets));
            CoverRow(info, "Zeitraum", ReportText.Period(h.StartedAt, h.FinishedAt));
            CoverRow(info, "Rechner", h.Machine);
            CoverRow(info, "Gescannt von", h.ScannedBy);
            CoverRow(info, "Zutritt-Version", h.ToolVersion);
            CoverRow(info, "Umfang", ReportText.Scope(h.Request));
            CoverRow(info, "Bericht erstellt", $"{ReportText.DateTime(h.CreatedAt)} mit Zutritt {h.ReportVersion}".TrimEnd());

            var note = _section.AddParagraph("Vertraulich – enthält Angaben zu Konten und Zugriffsrechten. "
                + "Nur für den internen Gebrauch und für Prüfer bestimmt.");
            note.Style = SmallStyle;
            note.Format.SpaceBefore = Unit.FromCentimeter(2.0);
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
            Heading1("Zusammenfassung");
            var files = h.Request.IncludeFiles ? $" und {ReportText.Number(h.FilesVisited)} Dateien" : "";
            Paragraph($"Geprüft wurden {ReportText.Number(h.FoldersVisited)} Ordner{files} unter "
                + $"{(h.Targets.Count == 1 ? "einem Ziel" : $"{ReportText.Number(h.Targets.Count)} Zielen")}. "
                + $"{ReportText.Number(h.FoldersStored)} davon haben eigene Rechte oder sind Wurzeln und sind in diesem Bericht aufgeführt.");

            var numbers = NewTable(framed: false, 9.0, 4.0);
            numbers.Borders.Visible = false;
            numbers.Borders.Bottom.Visible = true;
            KeyValue(numbers, "Ordner geprüft", ReportText.Number(h.FoldersVisited));
            if (h.Request.IncludeFiles || h.FilesVisited > 0) KeyValue(numbers, "Dateien geprüft", ReportText.Number(h.FilesVisited));
            KeyValue(numbers, "Ordner mit eigenen Rechten", ReportText.Number(h.FoldersStored));
            if (h.FilesStored > 0) KeyValue(numbers, "Dateien mit eigenen Rechten", ReportText.Number(h.FilesStored));
            KeyValue(numbers, "Freigaben", ReportText.Number(data.Shares.Count));
            KeyValue(numbers, "Konten und Gruppen", ReportText.Number(h.Principals));
            KeyValue(numbers, "Personen (davon mit Zugriff)", $"{ReportText.Number(data.People.Count)} ({ReportText.Number(data.PeopleWithAccess)})");
            KeyValue(numbers, "Nicht lesbare Ordner", ReportText.Number(data.UnreadableFolders), warn: data.UnreadableFolders > 0);
            KeyValue(numbers, "Fehler beim Lesen", ReportText.Number(data.Errors.Count), warn: data.Errors.Count > 0);

            Heading2("Befunde je Schwere");
            SeverityChips();

            Heading2("Hinweise");
            foreach (var note in data.Notes) Note(note.Kind, note.Text);
            if (Truncations() is { Count: > 0 } cut)
                Note(NoteKind.Warnung, "Dieses PDF zeigt bei langen Listen nur einen Teil – vollständig ist der Excel-Bericht: "
                    + string.Join(", ", cut) + ".");

            if (data.Comparison is { } c)
            {
                Heading2("Vergleich mit dem letzten Lauf");
                if (!c.HasChanges)
                {
                    Paragraph($"Keine Änderungen seit dem Lauf vom {ReportText.DateTime(c.OlderStartedAt)}.");
                    return;
                }
                var table = NewTable(framed: false, 9.0, 4.0);
                table.Borders.Visible = false;
                KeyValue(table, "Vorheriger Lauf", ReportText.DateTime(c.OlderStartedAt));
                KeyValue(table, "Neue Befunde", ReportText.Number(c.NewFindings), warn: c.NewFindings > 0);
                KeyValue(table, "Behobene Befunde", ReportText.Number(c.ResolvedFindings));
                KeyValue(table, "Personen-Zugriffe erweitert", ReportText.Number(c.MoreAccess));
                KeyValue(table, "Personen-Zugriffe eingeschränkt", ReportText.Number(c.LessAccess));
                KeyValue(table, "Geänderte Berechtigungseinträge", ReportText.Number(c.AceChanges));
                KeyValue(table, "Ordner neu mit eigenen Rechten / ohne eigene Rechte mehr",
                    $"{ReportText.Number(c.FoldersAdded)} / {ReportText.Number(c.FoldersRemoved)}");
                KeyValue(table, "Geänderte Gruppenmitgliedschaften", ReportText.Number(c.MembershipChanges));
            }
        }

        /// <summary>Welche Listen im PDF gekürzt sind, z. B. „Befunde (1.000 von 4.512)“.</summary>
        private List<string> Truncations()
        {
            var cut = new List<string>();
            void Check(string what, int total, int limit)
            {
                if (total > limit) cut.Add($"{what} ({ReportText.Number(limit)} von {ReportText.Number(total)})");
            }
            Check("Befunde", data.Findings.Count, limits.Findings);
            int longShares = data.Shares.Count(s => s.Readable && s.Permissions.Count > limits.ShareEntries);
            if (longShares > 0)
                cut.Add($"Freigabe-Berechtigungen (höchstens {ReportText.Number(limits.ShareEntries)} Einträge je Freigabe, "
                    + $"betrifft {(longShares == 1 ? "1 Freigabe" : $"{ReportText.Number(longShares)} Freigaben")})");
            Check("Ordner mit eigenen Rechten", data.Folders.Count, limits.Folders);
            Check("Personen", data.PeopleWithAccess, limits.People);
            if (data.Comparison is { } c) Check("Änderungen", c.Rows.Count, limits.Changes);
            Check("nicht geprüfte Bereiche", data.Errors.Count, limits.Errors);
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
            Heading1("Befunde");
            if (data.Findings.Count == 0)
            {
                Paragraph("Keine Befunde – keine der Prüfregeln hat angeschlagen.");
            }
            else
            {
                Paragraph("Nach Schwere sortiert. Erklärung und Empfehlung je Regel stehen unter „Prüfregeln und Empfehlungen“.", small: true);
                var table = NewTable(1.7, 1.3, 5.0, 3.3, 5.7);
                HeaderRow(table, "Schwere", "Regel", "Pfad", "Konto", "Meldung");
                foreach (var f in data.Findings.Take(limits.Findings))
                {
                    var row = table.AddRow();
                    SeverityCell(row.Cells[0], f.Severity);
                    CellText(table, row, 1, f.RuleId);
                    CellText(table, row, 2, f.Path);
                    CellText(table, row, 3, f.Account.Length == 0 ? ReportText.Dash : f.Account);
                    var message = CellText(table, row, 4, f.Title, bold: true);
                    message.AddLineBreak();
                    text.Append(message, f.Message, CellWidth(table, 4), TableSize);
                }
                Truncated(data.Findings.Count, limits.Findings, "Befunde");
            }

            Heading2("Prüfregeln und Empfehlungen");
            var rules = NewTable(1.2, 1.7, 1.7, 6.7, 5.7);
            HeaderRow(rules, "Regel", "Schwere", "Befunde", "Titel und Erklärung", "Empfehlung");
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
        }

        private void Shares()
        {
            Heading1("Freigaben");
            if (data.Shares.Count == 0)
            {
                Paragraph("Keine Freigaben im Lauf – es wurden nur lokale Pfade gescannt.");
                return;
            }
            Paragraph("Über das Netz gilt jeweils die strengere von Freigabe- und NTFS-Berechtigung.", small: true);
            var table = NewTable(4.3, 3.4, 3.0, 6.3);
            HeaderRow(table, "Freigabe", "Pfad", "Bemerkung", "Freigabe-Rechte");
            foreach (var s in data.Shares)
            {
                var row = table.AddRow();
                var name = CellText(table, row, 0, s.Unc, bold: true);
                if (s.IsAdminShare)
                {
                    name.AddLineBreak();
                    name.AddFormattedText("administrative Freigabe", TextFormat.Italic);
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
                    CellText(table, row, 3, $"{ace.Account}: {(ace.IsDeny ? "Verweigern " : "")}{Rights.Label(ace.Access)}",
                        color: ace.IsDeny ? WarningColor : null);
                if (s.Permissions.Count > limits.ShareEntries)
                    CellText(table, row, 3, $"… und {ReportText.Number(s.Permissions.Count - limits.ShareEntries)} weitere Einträge "
                        + "(vollständig im Excel-Bericht)", bold: true, color: WarningColor);
            }
        }

        private void Folders()
        {
            Heading1("Ordner mit eigenen Rechten");
            Paragraph("Aufgeführt sind die Wurzeln der Ziele und alle Ordner mit eigenen Rechten (explizite Einträge oder "
                + "unterbrochene Vererbung). Alle übrigen Ordner erben ihre Rechte vom nächsthöheren aufgeführten Ordner.", small: true);
            double width = Unit.FromCentimeter(ContentWidthCm).Point;

            foreach (var f in data.Folders.Take(limits.Folders))
            {
                var path = _section.AddParagraph();
                path.Format.SpaceBefore = 9;
                path.Format.SpaceAfter = 1;
                path.Format.KeepWithNext = true;
                text.Append(path, f.Path, width, NormalSize, bold: true);

                var meta = new List<string> { $"Tiefe {f.Depth.ToString(CultureInfo.InvariantCulture)}" };
                if (f.IsFile) meta.Add("Datei");
                if (f.InheritanceBroken) meta.Add("Vererbung unterbrochen");
                meta.Add("Besitzer: " + (f.Owner.Length == 0 ? "unbekannt" : f.Owner));
                if (f.ShareUnc is { Length: > 0 } share) meta.Add("Freigabe: " + share);
                var info = _section.AddParagraph();
                info.Style = SmallStyle;
                info.Format.KeepWithNext = true;
                text.Append(info, string.Join(" · ", meta), width, SmallSize);

                if (f.Error is not null)
                {
                    var error = _section.AddParagraph();
                    error.Format.Font.Color = WarningColor;
                    text.Append(error, "Nicht lesbar: " + f.Error, width, NormalSize);
                    // Gelesene Einträge trotzdem zeigen (z. B. Rechte gelesen, aber Inhalt nicht auflistbar) – wie im Excel-Bericht
                    if (f.Aces.Count == 0) continue;
                }

                var explicitAces = f.Aces.Where(a => !a.IsInherited).ToList();
                var inherited = f.Aces.Where(a => a.IsInherited).ToList();
                if (explicitAces.Count > 0)
                {
                    var table = NewTable(5.3, 1.9, 2.0, 2.3, 5.5);
                    HeaderRow(table, light: true, "Konto", "Art", "Recht", "Typ", "Gilt für");
                    foreach (var a in explicitAces)
                    {
                        var row = table.AddRow();
                        Color? color = a.IsDeny ? WarningColor : null;
                        CellText(table, row, 0, a.Account, color: color);
                        CellText(table, row, 1, ReportText.Kind(a.Kind), color: color);
                        CellText(table, row, 2, Rights.Label(a.Level), bold: a.IsDeny, color: color);
                        CellText(table, row, 3, ReportText.AllowDeny(a.IsDeny), bold: a.IsDeny, color: color);
                        CellText(table, row, 4, a.AppliesTo, color: color);
                    }
                }
                if (inherited.Count > 0)
                {
                    var p = _section.AddParagraph();
                    p.Style = SmallStyle;
                    p.Format.SpaceBefore = 2;
                    text.Append(p, "Geerbt: " + string.Join(", ", inherited.Select(a =>
                        $"{a.Account} ({(a.IsDeny ? "verweigert: " : "")}{Rights.Label(a.Level)})")), width, SmallSize);
                }
                if (f.Aces.Count == 0)
                    Paragraph("Keine Einträge – niemand hat Zugriff (nur der Besitzer kann Berechtigungen ändern).", small: true);
            }
            Truncated(data.Folders.Count, limits.Folders, "Ordner");
        }

        private void People()
        {
            Heading1("Personen-Übersicht");
            var withAccess = data.People.Where(p => p.FolderCount > 0).ToList();
            Paragraph($"{ReportText.Number(withAccess.Count)} von {ReportText.Number(data.People.Count)} Benutzerkonten haben "
                + "mindestens Auflisten-Recht auf einen aufgeführten Ordner (effektiv, über alle Gruppen und nach Abzug von Verweigerungen).",
                small: true);
            if (withAccess.Count == 0) return;

            var table = NewTable(4.8, 1.5, 2.2, 6.5, 2.0);
            HeaderRow(table, "Person", "Ordner", "Höchstes Recht", "Über Gruppen", "Hinweis");
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
                    : person.HasDirect ? "nur direkt" : ReportText.Dash);
                if (person.IsDisabled) CellText(table, row, 4, "deaktiviert", bold: true, color: WarningColor);
                if (person.HasDirect) CellText(table, row, 4, "direkt berechtigt");
            }
            Truncated(withAccess.Count, limits.People, "Personen");
            int without = data.People.Count - withAccess.Count;
            if (without > 0)
                Paragraph($"{ReportText.Number(without)} weitere Benutzerkonten ohne Zugriff auf die aufgeführten Ordner "
                    + "stehen im Excel-Bericht (Blatt „Personen“).", small: true);
        }

        private void Changes(ComparisonData comparison)
        {
            Heading1("Änderungen seit dem letzten Lauf");
            Paragraph($"Vergleich mit dem Lauf vom {ReportText.DateTime(comparison.OlderStartedAt)}.", small: true);
            if (!comparison.HasChanges)
            {
                Paragraph("Keine Änderungen seit dem letzten Lauf.");
                return;
            }
            var table = NewTable(2.7, 2.6, 4.2, 3.0, 4.5);
            HeaderRow(table, "Bereich", "Änderung", "Pfad bzw. Gruppe", "Konto", "Vorher → Nachher");
            foreach (var c in comparison.Rows.Take(limits.Changes))
            {
                var row = table.AddRow();
                CellText(table, row, 0, c.Area);
                bool alarming = c.Change is "neu" or "mehr Zugriff" or "aufgenommen";
                CellText(table, row, 1, c.Change, bold: alarming);
                CellText(table, row, 2, c.Subject);
                CellText(table, row, 3, c.Account.Length == 0 ? ReportText.Dash : c.Account);
                var p = row.Cells[4].AddParagraph();
                if (c.Before.Length > 0)
                {
                    p.AddFormattedText("vorher: ", TextFormat.Italic);
                    text.Append(p, c.Before, CellWidth(table, 4), TableSize);
                }
                if (c.Before.Length > 0 && c.After.Length > 0) p.AddLineBreak();
                if (c.After.Length > 0)
                {
                    p.AddFormattedText("nachher: ", TextFormat.Italic);
                    text.Append(p, c.After, CellWidth(table, 4), TableSize);
                }
            }
            Truncated(comparison.Rows.Count, limits.Changes, "Änderungen");
        }

        private void Errors()
        {
            Heading1("Nicht geprüfte Bereiche");
            Paragraph("Diese Pfade konnten nicht gelesen werden – über ihre Berechtigungen macht der Bericht keine Aussage.", small: true);
            var table = NewTable(9.5, 7.5);
            HeaderRow(table, "Pfad", "Meldung");
            foreach (var e in data.Errors.Take(limits.Errors))
            {
                var row = table.AddRow();
                CellText(table, row, 0, e.Path);
                CellText(table, row, 1, e.Message);
            }
            Truncated(data.Errors.Count, limits.Errors, "Einträge");
        }

        private void SignOff()
        {
            var heading = Heading1("Prüfung und Freigabe");
            heading.Format.SpaceBefore = 22;
            var intro = Paragraph("Mit der Unterschrift wird bestätigt, dass die aufgeführten Berechtigungen und Befunde geprüft wurden. "
                + "Abweichungen und vereinbarte Maßnahmen sind unter „Bemerkungen“ vermerkt.");
            intro.Format.KeepWithNext = true;
            intro.Format.SpaceAfter = 6;

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
            Line("Geprüft von", 1.1);
            Line("Datum", 1.1);
            Line("Unterschrift", 1.5);
            Line("Bemerkungen", 1.1);
            Line("", 0.9);
            Line("", 0.9);
            Line("", 0.9);
            // Der Block gehört zusammen auf eine Seite
            table.Rows[0].KeepWith = table.Rows.Count - 1;
        }

        // ---- Bausteine ------------------------------------------------------------------------------

        private Paragraph Heading1(string title)
        {
            var p = _section.AddParagraph(title);
            p.Style = StyleNames.Heading1;
            return p;
        }

        private void Heading2(string title) => _section.AddParagraph(title).Style = StyleNames.Heading2;

        private Paragraph Paragraph(string content, bool small = false)
        {
            var p = _section.AddParagraph();
            if (small) p.Style = SmallStyle;
            text.Append(p, content, Unit.FromCentimeter(ContentWidthCm).Point, small ? SmallSize : NormalSize);
            return p;
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
                var label = p.AddFormattedText("Achtung: ", TextFormat.Bold);
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

        private void Truncated(int total, int shown, string what)
        {
            if (total <= shown) return;
            var p = _section.AddParagraph();
            p.Format.SpaceBefore = 4;
            p.Format.Font.Color = WarningColor;
            p.AddText($"Gekürzt: {ReportText.Number(shown)} von {ReportText.Number(total)} {what} gezeigt – vollständig im Excel-Bericht.");
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
                _ => $"{targets[0]} (+{targets.Count - 1} weitere)",
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
/// Wie viele Einträge das PDF je Abschnitt höchstens zeigt (MigraDoc setzt grob 200 Tabellenzeilen je Sekunde).
/// <paramref name="ShareEntries"/> begrenzt die Freigabe-Berechtigungen in einer Tabellenzelle: MigraDoc kann eine
/// Zeile nicht über Seiten teilen und schnitte den Rest ohne Hinweis ab.
/// </summary>
internal sealed record PdfLimits(int Findings, int Folders, int People, int Changes, int Errors, int ShareEntries = 20)
{
    public static PdfLimits Default { get; } = new(Findings: 1_000, Folders: 250, People: 1_000, Changes: 1_000, Errors: 250);
}
