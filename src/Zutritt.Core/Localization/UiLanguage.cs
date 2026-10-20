// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zutritt.Core.Storage;

namespace Zutritt.Core.Localization;

/// <summary>
/// Sprache von Oberfläche, Befehlszeile und Berichten (Richtlinie 13): folgt dem Betriebssystem, lässt sich auf der
/// Seite <i>Info</i> umstellen und per Konfigurationsdatei vorgeben (Verteilung per GPO, Intune). Fehlt ein Text, zeigt
/// .NET den englischen aus Strings.resx.
/// </summary>
/// <remarks>
/// Vorrang: Umgebungsvariable <see cref="EnvironmentVariable"/> (Skripte, geplante Aufgaben), dann die Einstellung des
/// Benutzers (<c>%LocalAppData%\Zutritt\settings.json</c>), dann die Vorgabe für den Rechner
/// (<c>%ProgramData%\Zutritt\settings.json</c>), dann die Sprache des Systems, sonst Englisch. Beide Dateien:
/// <c>{ "language": "de" }</c> – erlaubt sind <c>en</c>, <c>de</c>, <c>es</c>, <c>fr</c>, <c>zh-Hans</c>; leer oder
/// unbekannt heißt „wie das System“.
/// </remarks>
public static class UiLanguage
{
    /// <summary>Umgebungsvariable, die Einstellung und Systemsprache übersteuert, z. B. <c>ZUTRITT_LANGUAGE=en</c>.</summary>
    public const string EnvironmentVariable = "ZUTRITT_LANGUAGE";

    /// <summary>Rückfallsprache, steht in Strings.resx.</summary>
    public const string Fallback = "en";

    /// <summary>Name der Einstellungsdatei, im Datenordner des Benutzers und im Ordner für alle Benutzer.</summary>
    public const string SettingsFileName = "settings.json";

    private const string LanguageProperty = "language";

    /// <summary>Sprachen mit eigener Sprachdatei, in der Reihenfolge der Auswahlliste.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["en", "de", "es", "fr", "zh-Hans"];

    /// <summary>Sprache des Systems beim Start, bevor Zutritt sie überschreibt.</summary>
    public static CultureInfo System { get; } = CultureInfo.CurrentUICulture;

    /// <summary>Einstellung des Benutzers: <c>%LocalAppData%\Zutritt\settings.json</c>.</summary>
    public static string UserSettingsPath => Path.Combine(DatabaseLocation.DataFolder, SettingsFileName);

    /// <summary>Vorgabe für alle Benutzer des Rechners: <c>%ProgramData%\Zutritt\settings.json</c>.</summary>
    public static string MachineSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Zutritt", SettingsFileName);

    /// <summary>Wert für die Einstellung: "" (Systemsprache) oder ein Code aus <see cref="Supported"/>.</summary>
    public static string Normalize(string? setting)
    {
        var code = setting?.Trim() ?? "";
        // „zh“, „zh-CN“, „zh-Hans-CN“: vereinfachtes Chinesisch
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            && !code.Contains("Hant", StringComparison.OrdinalIgnoreCase) && !code.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
            && !code.EndsWith("HK", StringComparison.OrdinalIgnoreCase) && !code.EndsWith("MO", StringComparison.OrdinalIgnoreCase))
            return "zh-Hans";
        return Supported.FirstOrDefault(s => s.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    /// <summary>Die Sprache, die gilt: die eingestellte, sonst die des Systems, wenn es sie gibt, sonst Englisch.</summary>
    public static string Resolve(string? setting, CultureInfo system)
    {
        var chosen = Normalize(setting);
        if (chosen.Length > 0) return chosen;
        for (var c = system; !c.Equals(CultureInfo.InvariantCulture); c = c.Parent)
        {
            // Traditionelles Chinesisch (zh-TW, zh-Hant) bekommt Englisch, nicht vereinfachtes
            if (c.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                var name = c.Name;
                bool simplified = name.Equals("zh", StringComparison.OrdinalIgnoreCase) || name.Contains("Hans", StringComparison.OrdinalIgnoreCase)
                                  || name.EndsWith("-CN", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-SG", StringComparison.OrdinalIgnoreCase);
                return simplified ? "zh-Hans" : Fallback;
            }
            var match = Supported.FirstOrDefault(s => s.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return Fallback;
    }

    /// <summary>
    /// Stellt die Sprache für alle Threads ein; danach erzeugte Texte kommen in dieser Sprache. Zahlen und Daten
    /// bleiben im Gebietsschema des Systems (Richtlinie 13, Regel 5).
    /// </summary>
    public static CultureInfo Apply()
    {
        string code;
        try
        {
            code = Resolve(Configured(), System);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Die Sprache darf den Start nie verhindern (GUI und geplante Befehlszeile): dann wie das System
            code = Resolve(null, System);
        }
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    /// <summary>Was eingestellt ist (Umgebungsvariable, Benutzer, Rechner) – "" heißt Systemsprache.</summary>
    public static string Configured()
    {
        var forced = Normalize(Environment.GetEnvironmentVariable(EnvironmentVariable));
        if (forced.Length > 0) return forced;
        var user = Read(UserSettingsPath);
        return user.Length > 0 ? user : Read(MachineSettingsPath);
    }

    /// <summary>Die Einstellung des Benutzers ("" = Systemsprache), ohne Umgebungsvariable und Rechnervorgabe.</summary>
    public static string UserSetting() => Read(UserSettingsPath);

    /// <summary>
    /// Speichert die Wahl des Benutzers ("" = Systemsprache). Andere Einträge der Datei bleiben erhalten. Gilt ab dem
    /// nächsten Start.
    /// </summary>
    /// <exception cref="IOException">Die Datei ließ sich nicht schreiben.</exception>
    /// <exception cref="UnauthorizedAccessException">Keine Berechtigung für die Datei.</exception>
    public static void SaveUserSetting(string? code) => Write(UserSettingsPath, Normalize(code));

    /// <summary>Name einer Sprache in ihr selbst („Deutsch“, „English“, „中文(简体)“), für die Auswahlliste.</summary>
    public static string NativeName(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        var name = culture.NativeName;
        return name.Length == 0 ? code : char.ToUpper(name[0], culture) + name[1..];
    }

    private static readonly JsonDocumentOptions ReadOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    internal static string Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions);
            return Normalize(node?[LanguageProperty]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Kaputte oder fremde Datei (auch ein doppelter Schlüssel – JsonObject wirft dann ArgumentException), keine
            // Rechte: wie das System. Die Sprache darf den Start nie verhindern, auch nicht über eine verteilte Vorgabe.
            return "";
        }
    }

    internal static void Write(string path, string code)
    {
        JsonObject root;
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        try
        {
            root = text.Length > 0 ? JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject ?? [] : [];
            // JsonObject liest erst hier: ein doppelter Schlüssel wirft ArgumentException
            root[LanguageProperty] = code;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            // Kaputte Datei: neu, nur mit der Sprache
            root = new JsonObject { [LanguageProperty] = code };
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
