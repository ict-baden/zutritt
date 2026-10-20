using System.IO.Compression;
using Zutritt.Core.Diagnostics;
using Zutritt.Core.Model;
using Zutritt.Core.Resources;
using Zutritt.Core.Scanning;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Diagnostics;

[CollectionDefinition("Protokoll", DisableParallelization = true)]
public sealed class LogCollection;

/// <summary>Protokoll und Diagnosepaket (Richtlinie 10) – das Protokoll ist global, deshalb nicht parallel zu anderen Tests.</summary>
[Collection("Protokoll")]
public sealed class LogTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        Log.Stop();
        Log.UseClock(() => DateTime.Now);
        _temp.Dispose();
    }

    [Fact]
    public void Ohne_Start_wird_nichts_geschrieben()
    {
        Log.Stop();
        Log.Info("nirgends");
        Assert.Empty(Log.Files());
    }

    [Fact]
    public void Eine_Datei_je_Tag_alte_werden_nach_sieben_Tagen_geloescht()
    {
        var day = new DateTime(2026, 10, 18, 9, 30, 0);
        // Alte Dateien liegen schon da
        File.WriteAllText(Path.Combine(_temp.Path, Log.FileName(day.AddDays(-7))), "zu alt");
        File.WriteAllText(Path.Combine(_temp.Path, Log.FileName(day.AddDays(-6))), "noch da");
        File.WriteAllText(Path.Combine(_temp.Path, "fremd.log"), "nicht unseres");
        Log.UseClock(() => day);

        Log.Start(_temp.Path);
        Log.Info("Start");
        Log.Error("Scan fehlgeschlagen", new InvalidOperationException("kaputt", new IOException("innen")));

        Assert.False(File.Exists(Path.Combine(_temp.Path, Log.FileName(day.AddDays(-7)))));
        Assert.True(File.Exists(Path.Combine(_temp.Path, "fremd.log")));
        var files = Log.Files();
        Assert.Equal([Log.FileName(day.AddDays(-6)), Log.FileName(day)], files.Select(Path.GetFileName));
        var today = File.ReadAllText(files[^1]);
        Assert.Contains("2026-10-18 09:30:00.000 INFO   Start", today);
        Assert.Contains("FEHLER Scan fehlgeschlagen: System.InvalidOperationException: kaputt | System.IO.IOException: innen", today);
    }

    [Fact]
    public void Tagesdatei_waechst_hoechstens_bis_zur_Grenze()
    {
        Log.Start(_temp.Path);
        var line = new string('x', 10_000);
        for (int i = 0; i < 200; i++) Log.Info(line);
        var size = new FileInfo(Log.Files().Single()).Length;
        Assert.InRange(size, Log.MaxBytesPerDay, Log.MaxBytesPerDay + 20_000);
    }

    [Fact]
    public void Scanende_protokolliert_Fehlerarten_ohne_Konten_und_Pfade()
    {
        Log.Start(_temp.Path);
        var result = new ScanResult();
        result.Errors.AddRange(
        [
            new(@"\\FS01\Labor", @"Mitglieder von „MUSTER\Labor-Alle“ nicht lesbar: Zugriff verweigert"),
            new(@"\\FS01\Labor", @"Mitglieder von „MUSTER\QC-Team“ nicht lesbar: Zugriff verweigert"),
            new(@"\\FS01", @"Active Directory „muster.example“ nicht erreichbar"),
            new(@"\\FS01\Austausch", @"Ungültiger lokaler Pfad der Freigabe: „D:\Daten\Austausch“"),
        ]);

        Scanner.LogFinished(result);

        var log = File.ReadAllText(Log.Files().Single());
        foreach (var secret in new[] { "Labor-Alle", "QC-Team", "MUSTER", "muster.example", "Daten", "Austausch" })
            Assert.DoesNotContain(secret, log, StringComparison.OrdinalIgnoreCase);
        // Gleiche Fehlerart mit verschiedenen Gruppen wird zusammengezählt
        Assert.Contains("WARN   2× Mitglieder von „…“ nicht lesbar: Zugriff verweigert", log);
        Assert.Contains("WARN   1× Active Directory „…“ nicht erreichbar", log);
    }

    [Fact]
    public void Diagnosepaket_enthaelt_Angaben_und_Protokoll_ohne_Namen()
    {
        Log.Start(_temp.Path);
        Log.Warning(@"3× Freigaben von „FS01“ nicht lesbar: Zugriff verweigert");
        Log.Info(@"Ziel \\FS01\Personal\Becker und D:\Daten\Labor, Konto LAB\anna.becker, SID S-1-5-21-1-2-3-1001, 10.1.2.3, " + Environment.MachineName);
        var zip = Path.Combine(_temp.Path, "diag.zip");

        DiagnosticsPackage.Write(zip, [new("Version", "0.1.0"), new("Datenbank", @"C:\Users\" + Environment.UserName + @"\AppData\Local\Zutritt\zutritt.db")], anonymize: true);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "info.txt");
        var info = Read(archive, "info.txt");
        Assert.Contains("Version: 0.1.0", info);
        Assert.Contains("Anonymisiert: ja", info);
        Assert.DoesNotContain(Environment.UserName, info, StringComparison.OrdinalIgnoreCase);
        var log = Read(archive, archive.Entries.Single(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).FullName);
        foreach (var secret in new[] { "FS01", "Personal", "Becker", "anna", "Labor", "1001", "10.1.2.3", Environment.MachineName })
            Assert.DoesNotContain(secret, log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"\\SERVER-1\…", log);
        Assert.Contains("Zugriff verweigert", log);
        Assert.False(File.Exists(zip + ".tmp"));
    }

    [Fact]
    public void Diagnosepaket_auf_Wunsch_mit_Namen()
    {
        Log.Start(_temp.Path);
        Log.Info(@"Ziel \\FS01\Labor");
        var zip = Path.Combine(_temp.Path, "diag.zip");

        DiagnosticsPackage.Write(zip, [], anonymize: false);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains("Anonymisiert: nein", Read(archive, "info.txt"));
        Assert.Contains(@"\\FS01\Labor", Read(archive, archive.Entries.Single(e => e.FullName.StartsWith("logs/", StringComparison.Ordinal)).FullName));
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}

public sealed class AnonymizerTests
{
    private readonly Anonymizer _anonymizer = new("PC-LABOR-07", "jdoe", "MUSTER");

    [Theory]
    [InlineData(@"Freigabe \\FS01\Labor\QC nicht lesbar", @"Freigabe \\SERVER-1\… nicht lesbar")]
    [InlineData(@"\\?\UNC\FS02\x", @"\\SERVER-1\…")]
    [InlineData(@"Pfad D:\Daten\Personal\x.txt fehlt", @"Pfad D:\… fehlt")]
    [InlineData(@"Konto MUSTER\anna.becker gesperrt", "Konto <Konto> gesperrt")]
    [InlineData("SID S-1-5-21-1111111111-2222222222-3333333333-1001", "SID S-1-5-21-…")]
    [InlineData("SID S-1-5-32-544 bleibt", "SID S-1-5-32-544 bleibt")]
    [InlineData("Freigaben von „FS01“ nicht lesbar", "Freigaben von „…“ nicht lesbar")]
    [InlineData("Server 192.168.10.20 antwortet nicht", "Server <IP> antwortet nicht")]
    [InlineData("an support@example.org", "an <E-Mail>")]
    [InlineData("Rechner PC-LABOR-07 von jdoe in MUSTER", "Rechner RECHNER von BENUTZER in DOMÄNE")]
    [InlineData("2026-10-18 11:15:22.123 INFO Scan beendet nach 4,2 s", "2026-10-18 11:15:22.123 INFO Scan beendet nach 4,2 s")]
    [InlineData("Version 0.1.0-ci.12, .NET 10.0.0", "Version 0.1.0-ci.12, .NET 10.0.0")]
    public void Ersetzt_Namen_Pfade_und_Adressen(string input, string expected) => Assert.Equal(expected, _anonymizer.Apply(input));

    public static TheoryData<string> Languages => [.. Zutritt.Core.Localization.UiLanguage.Supported];

    [Theory]
    [MemberData(nameof(Languages))]
    public void Namen_in_Scanmeldungen_jeder_Sprache_werden_ersetzt(string language)
    {
        // Befund der Prüfung: Nur „…“ wurde ersetzt – in en, es, fr und zh-Hans landeten Gruppen und Domänen im
        // Protokoll und im „anonymisierten“ Diagnosepaket
        using var _ = TestCulture.Use(language);
        string[] messages =
        [
            Texts.Format(Strings.Ad_Unreachable, "firma.local", "Server antwortet nicht"),
            Texts.Format(Strings.Ad_MembersUnreadable, "Labor-Alle", "firma.local", "Zeitlimit"),
            Texts.Format(Strings.Ad_AccountUnreadable, "Projekt-Gast", "firma.local", "Zeitlimit"),
            Texts.Format(Strings.Local_MembersUnreadable, "Projekt-Leser", "Zugriff verweigert"),
            Texts.Format(Strings.Share_InvalidName, "Geheim-Archiv"),
            Texts.Format(Strings.Text_Quote, "Vertrieb Süd"),
        ];
        foreach (var message in messages)
        {
            var clean = _anonymizer.Apply(message);
            foreach (var name in new[] { "firma.local", "Labor-Alle", "Projekt-Gast", "Projekt-Leser", "Geheim-Archiv", "Vertrieb Süd" })
                Assert.False(clean.Contains(name, StringComparison.OrdinalIgnoreCase), $"{language}: „{name}“ steht noch in „{clean}“");
        }
    }

    [Theory]
    [InlineData("Members of “Labor-Alle” (“firma.local”) not readable: timeout", "Members of „…“ („…“) not readable: timeout")]
    [InlineData("Membres de « Labor-Alle » illisibles", "Membres de „…“ illisibles")]
    [InlineData("Miembros de «Labor-Alle»", "Miembros de „…“")]
    [InlineData("无法读取“Labor-Alle”的成员", "无法读取„…“的成员")]
    [InlineData("Datei \"Gehälter.xlsx\" gesperrt", "Datei „…“ gesperrt")]
    public void Ersetzt_Text_in_Anfuehrungszeichen_jeder_Sprache(string input, string expected) =>
        Assert.Equal(expected, _anonymizer.Apply(input));

    [Fact]
    public void Gleiche_Server_bekommen_gleiche_Nummern()
    {
        var text = _anonymizer.Apply(@"\\FS01\a \\FS02\b \\fs01\c");
        Assert.Equal(@"\\SERVER-1\… \\SERVER-2\… \\SERVER-1\…", text);
    }
}
