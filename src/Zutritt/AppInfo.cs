using System.Reflection;

namespace Zutritt;

/// <summary>
/// Angaben über das Programm selbst – für die Info-Seite, <c>--version</c>/<c>--info</c> und das Diagnosepaket.
/// Firma und Copyright kommen aus den Assembly-Angaben (Directory.Build.props), damit ein Wechsel der Firmierung
/// eine einzige Änderung ist.
/// </summary>
public static class AppInfo
{
    public const string Name = "Zutritt";
    public const string RepositoryUrl = "https://github.com/ict-baden/zutritt";
    public const string SupportUrl = RepositoryUrl + "/blob/main/SUPPORT.md";
    public const string PrivacyUrl = RepositoryUrl + "/blob/main/docs/data-and-privacy.md";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>
    /// Impressum – es gibt nur eines, auf der Webseite (Richtlinie 07). Die heutige Webseite hat es nur unter dieser
    /// Adresse („/impressum“ ergibt 404); die neue Webseite leitet sie später weiter.
    /// </summary>
    public const string ImpressumUrl = "https://ict-baden.de/index.html?page=imprint";

    /// <summary>Webseite – ⚠ noch offen (OFFEN.md); bis dahin die Organisation auf GitHub.</summary>
    public const string WebsiteUrl = "https://github.com/ict-baden";

    public const string LicenseName = "MIT-Lizenz";

    /// <summary>Stimmt: Zutritt hat keine Telemetrie, keinen Update-Check und baut keine Verbindungen nach außen auf.</summary>
    public const string NoDataSentence = "Diese Software sendet keine Daten an ICT-Baden.";

    private const string FallbackPublisher = "ICT-Baden";

    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    private static readonly string Informational =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>Version ohne Build-Anhang, z. B. „0.1.0“ oder „0.1.0-ci.12“; „unbekannt“, wenn sie fehlt.</summary>
    public static string Version { get; } = ParseVersion(Informational);

    /// <summary>Commit-Kürzel (7 Zeichen) aus dem Build-Anhang „+&lt;commit&gt;“, null ohne.</summary>
    public static string? Commit { get; } = ParseCommit(Informational);

    /// <summary>„0.1.0-ci.12 (Build abc1234)“ bzw. nur die Version.</summary>
    public static string VersionWithBuild => Commit is null ? Version : $"{Version} (Build {Commit})";

    /// <summary>Herausgeber (AssemblyCompany), solange die Firmierung offen ist „ICT-Baden“.</summary>
    public static string Publisher { get; } =
        Self.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company is { Length: > 0 } company && company != Name ? company : FallbackPublisher;

    /// <summary>„© 2026 ICT-Baden“ (AssemblyCopyright).</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright is { Length: > 0 } copyright ? copyright : $"© 2026 {Publisher}";

    internal static string ParseVersion(string informational)
    {
        var version = informational.Split('+')[0].Trim();
        return version.Length > 0 ? version : "unbekannt";
    }

    internal static string? ParseCommit(string informational)
    {
        int plus = informational.IndexOf('+');
        if (plus < 0) return null;
        var commit = informational[(plus + 1)..].Trim();
        if (commit.Length == 0) return null;
        return commit.Length > 7 ? commit[..7] : commit;
    }
}
