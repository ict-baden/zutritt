// SPDX-License-Identifier: MIT
using System.Reflection;
using Zutritt.Core.Resources;

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
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>Impressum – es gibt nur eines, auf der Webseite (Richtlinie 07).</summary>
    public const string ImpressumUrl = "https://ict-baden.de/impressum/";

    /// <summary>
    /// Datenschutzerklärung von Zutritt auf der Webseite (Richtlinie 07) – gleiche Form für alle Apps:
    /// https://ict-baden.de/datenschutz/&lt;slug&gt;/. Die Einzelheiten stehen in docs/data-and-privacy.md.
    /// </summary>
    public const string PrivacyUrl = "https://ict-baden.de/datenschutz/zutritt/";

    /// <summary>Webseite: die Produktseite von Zutritt.</summary>
    public const string WebsiteUrl = "https://ict-baden.de/produkte/zutritt/";

    public static string LicenseName => Strings.App_LicenseName;

    /// <summary>Stimmt: Zutritt hat keine Telemetrie, keinen Update-Check und baut keine Verbindungen nach außen auf.</summary>
    public static string NoDataSentence => Strings.App_NoDataSentence;

    private const string FallbackPublisher = "ICT Baden GmbH";

    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    private static readonly string Informational =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>Version ohne Build-Anhang, z. B. „0.1.0“ oder „0.1.0-ci.12“; „unbekannt“, wenn sie fehlt.</summary>
    public static string Version { get; } = ParseVersion(Informational);

    /// <summary>Commit-Kürzel (7 Zeichen) aus dem Build-Anhang „+&lt;commit&gt;“, null ohne.</summary>
    public static string? Commit { get; } = ParseCommit(Informational);

    /// <summary>„0.1.0-ci.12 (Build abc1234)“ bzw. nur die Version.</summary>
    public static string VersionWithBuild => Commit is null ? Version : Texts.Format(Strings.Ui_VersionWithBuild, Version, Commit);

    /// <summary>Herausgeber (AssemblyCompany): die Firmierung aus dem Impressum, „ICT Baden GmbH“.</summary>
    public static string Publisher { get; } =
        Self.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company is { Length: > 0 } company && company != Name ? company : FallbackPublisher;

    /// <summary>„© 2026 ICT Baden GmbH“ (AssemblyCopyright).</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright is { Length: > 0 } copyright ? copyright : $"© 2026 {Publisher}";

    internal static string ParseVersion(string informational)
    {
        var version = informational.Split('+')[0].Trim();
        return version.Length > 0 ? version : Strings.Pdf_Unknown;
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
