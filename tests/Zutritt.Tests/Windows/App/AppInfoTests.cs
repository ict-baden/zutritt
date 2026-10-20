using System.Diagnostics;
using System.Text.RegularExpressions;
using Zutritt.UI;

namespace Zutritt.Tests.Windows.App;

/// <summary>Angaben der Info-Seite und von --version/--info, Lizenztexte, Links (Richtlinien 07 und 09).</summary>
public sealed class AppInfoTests
{
    [Theory]
    [InlineData("0.1.0+0123456789abcdef0123456789abcdef01234567", "0.1.0", "0123456")]
    [InlineData("0.1.0-ci.12+abc1234", "0.1.0-ci.12", "abc1234")]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("0.1.0+", "0.1.0", null)]
    [InlineData("", "unbekannt", null)]
    public void Version_und_Build_aus_der_Assembly(string informational, string version, string? commit)
    {
        Assert.Equal(version, AppInfo.ParseVersion(informational));
        Assert.Equal(commit, AppInfo.ParseCommit(informational));
    }

    [Fact]
    public void Die_App_gibt_sich_nie_als_1_0_aus()
    {
        // 1.0.0 erst nach den Kriterien aus Richtlinie 04 – die Basisversion steht in der csproj
        Assert.DoesNotMatch(@"^1\.0\.0", AppInfo.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
    }

    [Fact]
    public void Herausgeber_und_Copyright_kommen_aus_den_Assembly_Angaben()
    {
        Assert.StartsWith("ICT-Baden", AppInfo.Publisher);
        Assert.Matches(@"^© \d{4} ICT-Baden", AppInfo.Copyright);
        Assert.Equal("Diese Software sendet keine Daten an ICT-Baden.", AppInfo.NoDataSentence);
    }

    [Fact]
    public void Lizenztexte_sind_eingebettet()
    {
        var names = typeof(AppInfo).Assembly.GetManifestResourceNames();
        Assert.Contains(LicenseTexts.LicenseFile, names);
        Assert.Contains(LicenseTexts.NoticesFile, names);
        Assert.Contains("MIT License", LicenseTexts.License());
        Assert.Contains("THIRD-PARTY NOTICES", LicenseTexts.ThirdPartyNotices());
    }

    [Theory]
    [InlineData(AppInfo.RepositoryUrl)]
    [InlineData(AppInfo.SupportUrl)]
    [InlineData(AppInfo.PrivacyUrl)]
    [InlineData(AppInfo.LicenseUrl)]
    [InlineData(AppInfo.ImpressumUrl)]
    [InlineData(AppInfo.WebsiteUrl)]
    [InlineData("http://example.org/pfad?x=1")]
    public void Links_mit_http_und_https_sind_erlaubt(string url)
    {
        Assert.True(Links.IsAllowed(url, out var uri));
        Assert.NotNull(uri);
    }

    [Fact]
    public void Impressum_zeigt_auf_die_erreichbare_Seite()
    {
        // „/impressum“ gibt es auf der heutigen Webseite nicht (404), nur index.html?page=imprint; die neue leitet weiter
        Assert.Equal("https://ict-baden.de/index.html?page=imprint", AppInfo.ImpressumUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData(@"\\server\freigabe\datei.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:support@example.org")]
    [InlineData("ms-settings:privacy")]
    [InlineData("calc.exe")]
    [InlineData("https://")]
    public void Andere_Adressen_werden_nie_geoeffnet(string? url)
    {
        Assert.False(Links.IsAllowed(url, out _));
        if (url is not null) Assert.Contains("nur Adressen mit http oder https", Links.Open(url));
    }

    [Fact]
    public void Pfade_im_Profil_zeigen_keinen_Benutzernamen()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(@"%LocalAppData%\Zutritt\zutritt.db", Labels.WithVariables(Path.Combine(local, "Zutritt", "zutritt.db")));
        Assert.Equal(@"%TEMP%\Zutritt-Demo.db", Labels.WithVariables(Path.Combine(Path.GetTempPath(), "Zutritt-Demo.db")));
        Assert.Equal(@"D:\Daten\zutritt.db", Labels.WithVariables(@"D:\Daten\zutritt.db"));
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void Version_auf_der_Befehlszeile(string command)
    {
        var (code, output) = RunExe(command);
        Assert.Equal(0, code);
        Assert.Matches(new Regex(@"^Zutritt \d+\.\d+\.\d+\S*( \(Build [0-9a-f]{1,7}\))?\s*$"), output.Trim());
    }

    [Fact]
    public void Info_auf_der_Befehlszeile_mit_denselben_Angaben_wie_die_Info_Seite()
    {
        var (code, output) = RunExe("--info");
        Assert.Equal(0, code);
        foreach (var expected in new[] { "MIT", AppInfo.RepositoryUrl, AppInfo.SupportUrl, AppInfo.PrivacyUrl, AppInfo.ImpressumUrl, "THIRD-PARTY-NOTICES.txt", "sendet keine Daten an ICT-Baden" })
            Assert.Contains(expected, output);
    }

    [Fact]
    public void Lizenzen_auf_der_Befehlszeile()
    {
        var (code, output) = RunExe("--lizenzen");
        Assert.Equal(0, code);
        Assert.Contains("MIT License", output);
        Assert.Contains("THIRD-PARTY NOTICES", output);
    }

    [Fact]
    public void Version_mit_weiteren_Angaben_ist_ein_Fehler()
    {
        var (code, _) = RunExe("--version", "x");
        Assert.Equal(3, code);
    }

    /// <summary>Zutritt.exe als eigener Prozess – so wie in einer geplanten Aufgabe, Ausgabe über eine Pipe.</summary>
    private static (int Code, string Output) RunExe(params string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Zutritt.exe");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60_000), "Zutritt.exe hat nicht rechtzeitig geendet");
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}
