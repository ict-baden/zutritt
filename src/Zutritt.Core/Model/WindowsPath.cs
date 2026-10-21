// SPDX-License-Identifier: MIT
namespace Zutritt.Core.Model;

/// <summary>
/// Pfade auf den gescannten Windows-Systemen: lokal („D:\Daten\Labor“) oder als UNC-Pfad („\\FS01\Labor\QC“).
/// </summary>
/// <remarks>
/// Gescannte Pfade sind immer Windows-Pfade – auch wenn Auswertung, Vergleich oder Bericht auf einem anderen System
/// laufen. Sie sind Daten, keine Pfade dieses Rechners: <c>Path.Combine</c> und <c>Path.GetDirectoryName</c> wären unter
/// Linux falsch (Trenner „/“). Alle Zerlegungen gescannter Pfade laufen deshalb über diese Klasse. Dateien dieses
/// Rechners (Datenbank, Berichte, Bilder) bilden dagegen weiter <c>Path.Combine</c>/<c>Path.Join</c>.
/// </remarks>
public static class WindowsPath
{
    /// <summary>Trenner in Windows-Pfaden.</summary>
    public const char Separator = '\\';

    /// <summary>Anfang eines UNC-Pfads („\\“).</summary>
    public const string UncPrefix = @"\\";

    /// <summary>Ist es ein UNC-Pfad (\\server\…)?</summary>
    public static bool IsUnc(string path) => path.StartsWith(UncPrefix, StringComparison.Ordinal);

    /// <summary>„\\server\freigabe“.</summary>
    public static string Unc(string server, string share) => UncPrefix + server + Separator + share;

    /// <summary>„\\server“.</summary>
    public static string Server(string server) => UncPrefix + server;

    /// <summary>Kind-Pfad bilden, ohne den Namen anzufassen (kein Kürzen, keine Zeichenprüfung).</summary>
    public static string Join(string directory, string name)
        => directory.EndsWith(Separator) ? directory + name : directory + Separator + name;

    /// <summary>Abschließende Trenner entfernen, außer bei Laufwerkswurzeln („C:\“) und wenn nichts übrig bliebe.</summary>
    public static string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd(Separator);
        if (trimmed.Length == 2 && trimmed[1] == ':') return trimmed + Separator;
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>Wie <see cref="TrimEnd"/>, aber nur bei Pfaden länger als „C:\“ (kurze Pfade bleiben, wie sie sind).</summary>
    public static string TrimEndKeepShort(string path) => path.Length > 3 && path.EndsWith(Separator) ? path.TrimEnd(Separator) : path;

    /// <summary>Letzter Abschnitt („\\FS01\Labor\QC“ → „QC“); ohne Trenner der ganze Text.</summary>
    public static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd(Separator);
        int cut = trimmed.LastIndexOf(Separator);
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }

    /// <summary>
    /// Übergeordneter Ordner oder null, wenn es keinen sinnvollen gibt: an Laufwerkswurzeln („C:\“) und bei
    /// „\\server\freigabe“ (der kleinsten Einheit eines UNC-Pfads). „C:\Daten“ → „C:\“.
    /// </summary>
    public static string? Parent(string path)
    {
        path = TrimEndKeepShort(path);
        if (path.Length <= 3 && path.Length >= 2 && path[1] == ':') return null; // Laufwerkswurzel
        int i = path.LastIndexOf(Separator);
        if (i <= 0) return null;
        if (IsUnc(path))
        {
            int serverEnd = path.IndexOf(Separator, 2);
            if (serverEnd < 0 || i <= serverEnd) return null;
        }
        var parent = path[..i];
        return parent.Length == 2 && parent[1] == ':' ? parent + Separator : parent;
    }

    /// <summary>Liegt <paramref name="path"/> in <paramref name="directory"/> oder ist es selbst (Groß/Klein egal, nur ganze Namen)?</summary>
    public static bool IsSameOrBelow(string path, string directory)
    {
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Length == directory.Length) return true;
        return directory.EndsWith(Separator) || path[directory.Length] == Separator;
    }

    /// <summary>Abschnitte eines Pfads ohne leere Teile („\\FS01\Labor“ → FS01, Labor).</summary>
    public static string[] Segments(string path) => path.Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>„/“ durch „\“ ersetzen (Eingaben wie „//server/freigabe“).</summary>
    public static string WithBackslashes(string path) => path.Replace('/', Separator);
}

/// <summary>Kontennamen in der Windows-Schreibweise „DOMÄNE\name“.</summary>
public static class AccountName
{
    /// <summary>„MUSTER\QC-Team“ → „QC-Team“; ohne Domäne oder mit Trenner am Ende der ganze Name.</summary>
    public static string ShortName(string name)
    {
        int cut = name.LastIndexOf(WindowsPath.Separator);
        return cut >= 0 && cut < name.Length - 1 ? name[(cut + 1)..] : name;
    }

    /// <summary>„MUSTER\QC-Team“ → „QC-Team“, wie <c>name.Split('\\')[^1]</c> (bei Trenner am Ende leer).</summary>
    public static string LastPart(string name)
    {
        int cut = name.LastIndexOf(WindowsPath.Separator);
        return cut < 0 ? name : name[(cut + 1)..];
    }

    /// <summary>„DOMÄNE\name“ bzw. nur der Teil, der nicht leer ist.</summary>
    public static string Join(string domain, string name)
        => string.IsNullOrEmpty(domain) ? name : string.IsNullOrEmpty(name) ? domain : domain + WindowsPath.Separator + name;
}
