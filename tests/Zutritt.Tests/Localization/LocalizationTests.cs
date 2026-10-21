// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Zutritt.Core;
using Zutritt.Core.Analysis;
using Zutritt.Core.Localization;
using Zutritt.Core.Reports;
using Zutritt.Core.Resources;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Localization;

/// <summary>Sprachdateien, Sprachwahl und Fehlercodes (Richtlinie 13).</summary>
public sealed partial class LocalizationTests
{
    // Die Sprachdateien liegen als Kopie neben der Test-DLL (Zutritt.Tests.csproj): In der CI schreibt
    // ContinuousIntegrationBuild die Quellpfade auf /_/ um, ein Weg über [CallerFilePath] fände sie dort nicht.
    private static string ResourceDir() => Path.Combine(AppContext.BaseDirectory, "Resources");

    private static string FileOf(string code) => code == UiLanguage.Fallback ? "Strings.resx" : $"Strings.{code}.resx";

    private static XDocument Document(string code) => XDocument.Load(Path.Combine(ResourceDir(), FileOf(code)));

    private static Dictionary<string, string> Load(string code) =>
        Document(code).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static SortedSet<int> Placeholders(string text) =>
        new(Placeholder().Matches(text.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));

    public static TheoryData<string> OtherLanguages => new(UiLanguage.Supported.Where(c => c != UiLanguage.Fallback));

    public static TheoryData<string> AllLanguages => new(UiLanguage.Supported);

    [Fact]
    public void Es_gibt_fuenf_Sprachen() => Assert.Equal(["en", "de", "es", "fr", "zh-Hans"], UiLanguage.Supported);

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Jede_Sprache_hat_dieselben_Schluessel_wie_Englisch(string code)
    {
        var en = Load(UiLanguage.Fallback);
        Assert.True(en.Count > 900, $"nur {en.Count} Texte in Strings.resx");
        Assert.Equal(en.Keys.Order(StringComparer.Ordinal), Load(code).Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Platzhalter_stimmen_in_allen_Sprachen_ueberein(string code)
    {
        var en = Load(UiLanguage.Fallback);
        foreach (var (key, text) in Load(code))
            Assert.True(Placeholders(en[key]).SetEquals(Placeholders(text)), $"{code}: {key} – „{en[key]}“ vs. „{text}“");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Keine_leeren_Texte_und_jeder_Text_laesst_sich_formatieren(string code)
    {
        var args = Enumerable.Range(0, 10).Select(i => (object)i).ToArray();
        foreach (var (key, text) in Load(code))
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"{code}: {key} ist leer");
            // Fehlerhafte Klammern fielen sonst erst zur Laufzeit als FormatException auf
            if (Placeholders(text).Count > 0)
            {
                var ex = Record.Exception(() => string.Format(CultureInfo.InvariantCulture, text, args));
                Assert.True(ex is null, $"{code}: {key} lässt sich nicht formatieren: {ex?.Message}");
            }
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Maschinell_vorübersetzte_Sprachen_sind_als_ungeprueft_markiert(string code)
    {
        // Richtlinie 13: vor dem ersten öffentlichen Release liest ein Muttersprachler gegen – bis dahin steht es in der Datei
        var comment = Document(code).Root!.Nodes().OfType<XComment>().FirstOrDefault()?.Value ?? "";
        Assert.Matches("(?i)automática|automatique|机器翻译", comment);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("zh-Hans")]
    public void Keine_offenen_Uebersetzungen(string code)
    {
        foreach (var data in Document(code).Root!.Elements("data"))
            Assert.DoesNotContain("TODO-übersetzen", (string?)data.Element("comment") ?? "");
    }

    [Theory]
    [InlineData("de", "")]
    [InlineData("en", "")]
    [InlineData("es", " (traducción automática)")]
    [InlineData("fr", " (traduction automatique)")]
    [InlineData("zh-Hans", "（机器翻译）")]
    public void Sprachauswahl_kennzeichnet_maschinelle_Uebersetzung(string code, string suffix) =>
        // Den Sprachnamen selbst liefert ICU bzw. Windows (z. B. „简体中文“ oder „中文(简体)“)
        Assert.Equal(UiLanguage.NativeName(code) + suffix, UiLanguage.ChoiceName(code));

    [Fact]
    public void Deutsch_ist_vollstaendig_und_ohne_Markierung()
    {
        foreach (var data in Document("de").Root!.Elements("data"))
            Assert.DoesNotContain("TODO-übersetzen", (string?)data.Element("comment") ?? "");
    }

    [Theory]
    [InlineData("", "de-DE", "de")]
    [InlineData("", "de-CH", "de")]
    [InlineData("", "en-US", "en")]
    [InlineData("", "fr-CA", "fr")]
    [InlineData("", "es-MX", "es")]
    [InlineData("", "zh-CN", "zh-Hans")]
    [InlineData("", "zh-Hans-SG", "zh-Hans")]
    [InlineData("", "zh-TW", "en")]
    [InlineData("", "it-IT", "en")]
    [InlineData("", "", "en")]
    [InlineData("en", "de-DE", "en")]
    [InlineData(" DE ", "en-US", "de")]
    [InlineData("zh", "en-US", "zh-Hans")]
    [InlineData("xx", "fr-FR", "fr")]
    public void Sprache_folgt_Einstellung_sonst_System_sonst_Englisch(string setting, string system, string expected) =>
        Assert.Equal(expected, UiLanguage.Resolve(setting, CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void Einstellung_wird_bereinigt()
    {
        Assert.Equal("", UiLanguage.Normalize(null));
        Assert.Equal("", UiLanguage.Normalize("klingonisch"));
        Assert.Equal("de", UiLanguage.Normalize("DE"));
        Assert.Equal("zh-Hans", UiLanguage.Normalize("zh-cn"));
        Assert.Equal("", UiLanguage.Normalize("zh-TW"));
    }

    [Fact]
    public void Ohne_eigene_Wahl_gilt_die_Vorgabe_des_Rechners_sonst_Windows()
    {
        // Befund der Prüfung: Die Auswahl zeigte „Wie Windows (Deutsch)“, obwohl die Vorgabe des Rechners Englisch war
        var german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal(("en", true), UiLanguage.WithoutUserChoice("en", german));
        Assert.Equal(("zh-Hans", true), UiLanguage.WithoutUserChoice("zh-CN", german));
        Assert.Equal(("de", false), UiLanguage.WithoutUserChoice("", german));
        Assert.Equal(("de", false), UiLanguage.WithoutUserChoice("klingonisch", german));
        Assert.Equal(("en", false), UiLanguage.WithoutUserChoice("", CultureInfo.GetCultureInfo("ja-JP")));
    }

    [Fact]
    public void Einstellungsdatei_wird_gelesen_und_geschrieben_ohne_Fremdes_zu_verlieren()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "sub", "settings.json");
        Assert.Equal("", UiLanguage.Read(path));                       // fehlt: wie das System

        UiLanguage.Write(path, "fr");
        Assert.Equal("fr", UiLanguage.Read(path));

        File.WriteAllText(path, """{ "language": "es", "other": 42 }""");
        UiLanguage.Write(path, "");
        Assert.Equal("", UiLanguage.Read(path));
        Assert.Contains("\"other\": 42", File.ReadAllText(path));

        File.WriteAllText(path, "kein JSON");
        Assert.Equal("", UiLanguage.Read(path));                       // kaputt: Start nie verhindern

        // Doppelter Schlüssel (von Hand oder per GPO verteilt): JsonObject wirft dabei ArgumentException
        File.WriteAllText(path, """{ "language": "de", "language": "en" }""");
        Assert.Equal("", UiLanguage.Read(path));
        UiLanguage.Write(path, "es");
        Assert.Equal("es", UiLanguage.Read(path));

        File.WriteAllText(path, """{ "language": 42 }""");
        Assert.Equal("", UiLanguage.Read(path));
        File.WriteAllText(path, "[1, 2]");
        UiLanguage.Write(path, "fr");
        Assert.Equal("fr", UiLanguage.Read(path));
    }

    [Fact]
    public void Texte_kommen_in_der_eingestellten_Sprache()
    {
        string Severity(string culture)
        {
            using var _ = TestCulture.Use(culture);
            return Strings.Severity_High;
        }
        Assert.Equal("High", Severity("en-US"));
        Assert.Equal("Hoch", Severity("de-DE"));
        Assert.Equal("Alta", Severity("es-ES"));
        Assert.Equal("Élevée", Severity("fr-FR"));
        Assert.Equal("高", Severity("zh-Hans"));
        Assert.Equal("High", Severity("it-IT"));                         // fehlende Sprache → Englisch
    }

    [Fact]
    public void Pruefregeln_folgen_der_Sprache()
    {
        using (TestCulture.Use("en-US"))
            Assert.Equal("Broad group can write", FindingRules.Get("ZT01")!.Title);
        Assert.Equal("Breite Gruppe darf schreiben", FindingRules.Get("ZT01")!.Title);
        Assert.Equal("Freigabe: alle dürfen ändern", FindingRules.Get("ZT08")!.Title);
    }

    [Fact]
    public void Aufzaehlung_und_Anfuehrungszeichen_je_Sprache()
    {
        Assert.Equal("„a“, „b“ und „c“", Texts.JoinAnd([Texts.Quote("a"), Texts.Quote("b"), Texts.Quote("c")]));
        using (TestCulture.Use("en-US"))
            Assert.Equal("“a”, “b” and “c”", Texts.JoinAnd([Texts.Quote("a"), Texts.Quote("b"), Texts.Quote("c")]));
        using (TestCulture.Use("fr-FR"))
        {
            Assert.Equal("« a »", Texts.Quote("a"));
            Assert.Equal("one", Texts.Plural(0, "one", "many"));      // Französisch: 0 ist Einzahl
        }
        Assert.Equal("many", Texts.Plural(0, "one", "many"));
        Assert.Equal("one", Texts.Plural(1, "one", "many"));
        Assert.Equal("", Texts.JoinAnd([]));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Excel_Bericht_in_jeder_Sprache(string code)
    {
        using var temp = new TempFolder();
        using var _ = TestCulture.Use(code);
        var scan = ReportsScan();
        var path = Path.Combine(temp.Path, "bericht.xlsx");

        ExcelReport.Write(scan, ScanComparer.Compare(ReportsScan(), scan), path);

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        Assert.Contains(workbook.Worksheets, w => w.Name == Strings.Sheet_Findings);
        Assert.All(workbook.Worksheets, w => Assert.True(w.Name.Length <= 31, w.Name));
    }

    [PdfTheory]
    [MemberData(nameof(AllLanguages))]
    public void Pdf_Bericht_in_jeder_Sprache(string code)
    {
        using var temp = new TempFolder();
        using var _ = TestCulture.Use(code);
        var scan = ReportsScan();
        var path = Path.Combine(temp.Path, "bericht.pdf");

        PdfReport.Write(scan, ScanComparer.Compare(ReportsScan(), scan), path);

        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Fact]
    public void Chinesische_Zeichen_werden_fuer_die_Ersatzschrift_abgetrennt()
    {
        Assert.Equal([("D:\\Daten\\", false), ("研发部", true)], PdfText.Runs("D:\\Daten\\研发部"));
        // Leerzeichen bleiben beim Abschnitt davor
        Assert.Equal([("Labor ", false), ("研发部 ", true), ("2026", false)], PdfText.Runs("Labor 研发部 2026"));
        Assert.Equal([("nur Latein", false)], PdfText.Runs("nur Latein"));
        Assert.True(PdfText.IsCjk('研'));
        Assert.True(PdfText.IsCjk('ア'));
        Assert.True(PdfText.IsCjk('한'));
        Assert.False(PdfText.IsCjk('Ä'));
    }

    [Fact]
    public void Fehlercodes_sind_eindeutig_und_einheitlich()
    {
        Assert.Equal(ErrorCodes.All.Count, ErrorCodes.All.Distinct().Count());
        Assert.All(ErrorCodes.All, c => Assert.Matches(@"^ZUT-\d{4}$", c));
        Assert.Equal("Kaputt. (Fehler ZUT-0203)", ErrorCodes.WithCode("Kaputt.", ErrorCodes.DatabaseSave));
        Assert.Equal(ErrorCodes.FileAccess, ErrorCodes.Of(new IOException("x")));
        Assert.Equal(ErrorCodes.Unexpected, ErrorCodes.Of(new InvalidOperationException("x")));
        Assert.Equal(ErrorCodes.ReportWrite, ErrorCodes.Of(new ReportWriteException("p", "x")));
        Assert.Equal(ErrorCodes.DatabaseRead, ErrorCodes.Of(new Core.Storage.ScanStoreException("x")));
    }

    [Fact]
    public void Fehlercodes_stehen_in_der_Doku()
    {
        // docs/error-codes.md liegt als Kopie neben der Test-DLL (wie die Sprachdateien)
        var doc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "error-codes.md"));
        Assert.All(ErrorCodes.All, c => Assert.Contains(c, doc));
    }

    private static Core.Model.ScanResult ReportsScan()
    {
        var scan = SampleScan.Create();
        scan.Findings.AddRange(FindingRules.Evaluate(scan));
        return scan;
    }
}
