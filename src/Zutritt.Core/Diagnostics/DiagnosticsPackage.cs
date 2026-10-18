using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Zutritt.Core.Diagnostics;

/// <summary>
/// Diagnosepaket für den Support (Richtlinie 10): Version, Systemangaben und das Protokoll der letzten Tage in einer
/// ZIP-Datei. Standardmäßig ohne Namen, Pfade und Adressen (<see cref="Anonymizer"/>) – Kunden sollen es weitergeben
/// können, ohne vorher selbst etwas schwärzen zu müssen.
/// </summary>
public static class DiagnosticsPackage
{
    /// <summary>Vorschlag für den Dateinamen: Zutritt-Diagnose-JJJJ-MM-TT.zip.</summary>
    public static string SuggestedFileName(DateTime now) => $"Zutritt-Diagnose-{now:yyyy-MM-dd}.zip";

    /// <summary>Paket schreiben (ersetzt eine vorhandene Datei).</summary>
    /// <param name="info">Angaben der App (Version, Build, Domäne ja/nein, Datenbank …) in dieser Reihenfolge.</param>
    /// <param name="anonymize">Rechner-, Konto- und Servernamen, Pfade, SIDs und Adressen ersetzen.</param>
    public static void Write(string zipPath, IReadOnlyList<KeyValuePair<string, string>> info, bool anonymize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(info);
        var anonymizer = anonymize ? new Anonymizer() : null;
        string Clean(string text) => anonymizer?.Apply(text) ?? text;

        var temp = zipPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var text = new StringBuilder();
                text.AppendLine("Zutritt – Diagnosepaket");
                text.AppendLine(CultureInfo.InvariantCulture, $"Erstellt: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
                text.AppendLine(anonymize
                    ? "Anonymisiert: ja (Rechner-, Konto- und Servernamen, Pfade, SIDs und Adressen ersetzt)"
                    : "Anonymisiert: nein");
                text.AppendLine();
                foreach (var (key, value) in info) text.AppendLine(CultureInfo.InvariantCulture, $"{key}: {Clean(value)}");
                text.AppendLine(CultureInfo.InvariantCulture, $"Betriebssystem: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
                text.AppendLine(CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}");
                text.AppendLine(CultureInfo.InvariantCulture, $"Sprache: {CultureInfo.CurrentUICulture.Name}, Zeitzone: {TimeZoneInfo.Local.Id}");
                var logs = Log.Files();
                text.AppendLine(CultureInfo.InvariantCulture, $"Protokolle: {logs.Count} Datei(en) der letzten {Log.KeepDays} Tage");
                Add(zip, "info.txt", text.ToString());

                foreach (var file in logs)
                {
                    string content;
                    try
                    {
                        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                        content = reader.ReadToEnd();
                    }
                    catch (IOException e)
                    {
                        content = $"(nicht lesbar: {e.Message})";
                    }
                    Add(zip, "logs/" + Path.GetFileName(file), Clean(content));
                }
            }
            File.Move(temp, zipPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

/// <summary>
/// Ersetzt in Texten, was auf Personen, Rechner oder das Netz des Kunden schließen lässt: Rechner-, Benutzer- und
/// Domänenname dieses Rechners, UNC-Server und -Pfade, lokale Pfade, Konto-SIDs, „DOMÄNE\name“, Text in „…“,
/// IP- und E-Mail-Adressen. Gleiche Server werden gleich nummeriert, damit das Protokoll lesbar bleibt.
/// </summary>
public sealed partial class Anonymizer
{
    private readonly Dictionary<string, string> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly (string Value, string Replacement)[] _local;

    public Anonymizer() : this(Environment.MachineName, Environment.UserName, Environment.UserDomainName) { }

    internal Anonymizer(string machine, string user, string domain)
    {
        var local = new List<(string, string)>();
        // Längste zuerst, damit „PC-LABOR-01“ nicht als „PC“ + Rest ersetzt wird
        foreach (var (value, replacement) in new[] { (machine, "RECHNER"), (user, "BENUTZER"), (domain, "DOMÄNE") }
                     .Where(p => p.Item1.Length >= 2).OrderByDescending(p => p.Item1.Length))
            local.Add((value, replacement));
        _local = [.. local];
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Quoted().Replace(text, "„…“");
        text = Email().Replace(text, "<E-Mail>");
        text = Unc().Replace(text, m => $@"\\{Server(m.Groups["server"].Value)}{(m.Groups["rest"].Length > 0 ? @"\…" : "")}");
        text = DrivePath().Replace(text, m => m.Groups["drive"].Value + @":\…");
        text = AccountSid().Replace(text, "S-1-5-21-…");
        text = Account().Replace(text, "<Konto>");
        text = Ipv4().Replace(text, "<IP>");
        text = Ipv6().Replace(text, "<IP>");
        foreach (var (value, replacement) in _local)
            text = Regex.Replace(text, $@"(?<![\w-]){Regex.Escape(value)}(?![\w-])", replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text;
    }

    private string Server(string name)
    {
        if (!_servers.TryGetValue(name, out var replacement))
            _servers[name] = replacement = "SERVER-" + (_servers.Count + 1).ToString(CultureInfo.InvariantCulture);
        return replacement;
    }

    [GeneratedRegex(@"„[^“\r\n]*“")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    // \\server[\rest…] und \\?\UNC\server\…
    [GeneratedRegex(@"\\\\(\?\\UNC\\)?(?<server>[^\\\s„“""'|<>:*?]+)(?<rest>\\[^\s„“""'|<>]*)?")]
    private static partial Regex Unc();

    [GeneratedRegex(@"(?<![\w\\])(?<drive>[A-Za-z]):\\[^\s„“""'|<>]*")]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"S-1-5-21(-\d+){3,4}", RegexOptions.IgnoreCase)]
    private static partial Regex AccountSid();

    // DOMÄNE\name (nach den Pfaden, die dann schon ersetzt sind)
    [GeneratedRegex(@"(?<![\w\\.-])[\p{L}\d][\p{L}\d.-]{0,30}\\[\p{L}\d._$-]+")]
    private static partial Regex Account();

    [GeneratedRegex(@"\b(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}\b")]
    private static partial Regex Ipv4();

    // Mindestens vier Gruppen oder „::“ – Uhrzeiten wie 11:15:22 sind keine Adressen
    [GeneratedRegex(@"\b([0-9A-Fa-f]{1,4}:){4,7}[0-9A-Fa-f]{1,4}\b|\b([0-9A-Fa-f]{1,4}:)+:([0-9A-Fa-f]{1,4}:?)*[0-9A-Fa-f]{1,4}\b")]
    private static partial Regex Ipv6();
}
