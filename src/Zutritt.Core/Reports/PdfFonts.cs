using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace Zutritt.Core.Reports;

/// <summary>
/// Schriften für den PDF-Bericht. Der plattformneutrale PDFsharp-Build bringt keine Schriften mit und
/// verlangt einen Resolver, der genau einmal pro Prozess gesetzt werden darf – deshalb hier zentral,
/// gesperrt und nur, wenn noch niemand (z. B. die Oberfläche) einen eigenen gesetzt hat.
/// Jede angefragte Familie wird auf die erste gefundene Schrift abgebildet: unter Windows Segoe UI (Rückfall Arial,
/// Tahoma, Verdana), unter Linux und macOS DejaVu Sans oder Liberation Sans. Der Bericht braucht nur eine Schrift,
/// und so scheitert auch MigraDocs interne Standardschrift nicht.
/// </summary>
internal sealed class PdfFonts : IFontResolver
{
    /// <summary>Eine Schriftfamilie mit den Dateinamen ihrer Schnitte (ohne Ordner).</summary>
    internal sealed record FaceSet(string Family, string Regular, string? Bold, string? Italic, string? BoldItalic);

    /// <summary>Gefundene Schrift: Familie und die vollständigen Pfade der vorhandenen Schnitte, Schlüssel = Dateiname.</summary>
    internal sealed record Found(FaceSet Faces, IReadOnlyDictionary<string, string> Files);

    internal static readonly FaceSet[] Candidates =
    [
        new("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        new("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        new("Tahoma", "tahoma.ttf", "tahomabd.ttf", null, null),
        new("Verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
        new("DejaVu Sans", "DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
        new("Liberation Sans", "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"),
    ];

    private static readonly Lock Gate = new();
    private static string? _family;
    private static Found? _found;
    private static bool _searched;

    private readonly Found _fonts;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    private PdfFonts(Found fonts) => _fonts = fonts;

    /// <summary>
    /// Schriftfamilie, mit der der Bericht gesetzt wird. Der erste Aufruf richtet den Resolver ein.
    /// </summary>
    /// <exception cref="InvalidOperationException">In den Schriftordnern des Systems gibt es keine der Schriften.</exception>
    public static string Family
    {
        get
        {
            lock (Gate) return _family ??= Initialize();
        }
    }

    /// <summary>Gibt es auf diesem System eine passende Schrift? (Tests überspringen den PDF-Bericht sonst.)</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (Gate) return GlobalFontSettings.FontResolver is { } and not PdfFonts || Search() is not null;
        }
    }

    /// <summary>Familie der Schrift, die der Bericht auf diesem System nähme (ohne den Resolver einzurichten), null = keine.</summary>
    public static string? AvailableFamily
    {
        get
        {
            lock (Gate) return Search()?.Faces.Family;
        }
    }

    /// <summary>
    /// Ordner, in denen gesucht wird: der Schriftordner des Systems (unter Windows %WINDIR%\Fonts), dann die üblichen
    /// Ordner unter Linux und macOS. Unterordner werden mit durchsucht (Linux legt Schriften je Paket ab).
    /// </summary>
    internal static IEnumerable<string> Directories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string?[] all =
        [
            system,
            windows.Length > 0 ? Path.Combine(windows, "Fonts") : null,
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            home.Length > 0 ? Path.Combine(home, ".local", "share", "fonts") : null,
            home.Length > 0 ? Path.Combine(home, ".fonts") : null,
            "/Library/Fonts",
            "/System/Library/Fonts",
            home.Length > 0 ? Path.Combine(home, "Library", "Fonts") : null,
        ];
        return all.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.Ordinal)!;
    }

    private static string Initialize()
    {
        var current = GlobalFontSettings.FontResolver;
        if (current is PdfFonts own) return own._fonts.Faces.Family;
        if (current is null)
        {
            var found = Search() ?? throw new InvalidOperationException(
                $"Für den PDF-Bericht wurde keine Schrift gefunden (gesucht: {string.Join(", ", Candidates.Select(c => c.Family))} " +
                $"in {string.Join(", ", Directories())}). Unter Linux hilft z. B. das Paket fonts-dejavu-core.");
            var resolver = new PdfFonts(found);
            try
            {
                GlobalFontSettings.FontResolver = resolver;
                return found.Faces.Family;
            }
            catch (InvalidOperationException)
            {
                // PDFsharp wurde in diesem Prozess schon ohne Resolver benutzt – dann gilt der vorhandene Zustand
            }
        }
        // Fremder Resolver (z. B. UseWindowsFontsUnderWindows): Arial kennt praktisch jeder
        return "Arial";
    }

    /// <summary>Erste vorhandene Schrift aus <see cref="Candidates"/>, einmal je Prozess gesucht.</summary>
    private static Found? Search()
    {
        if (_searched) return _found;
        _searched = true;
        var directories = Directories().Where(Directory.Exists).ToList();
        foreach (var faces in Candidates)
        {
            if (Find(faces, directories) is { } found) return _found = found;
        }
        return null;
    }

    /// <summary>Sucht die Schnitte einer Familie: erst direkt in den Ordnern, dann in deren Unterordnern.</summary>
    internal static Found? Find(FaceSet faces, IReadOnlyList<string> directories)
    {
        string[] names = [faces.Regular, .. new[] { faces.Bold, faces.Italic, faces.BoldItalic }.OfType<string>()];
        foreach (var directory in directories)
        {
            var regular = Path.Combine(directory, faces.Regular);
            if (File.Exists(regular)) return Collect(faces, names, Path.GetDirectoryName(regular)!);
        }
        foreach (var directory in directories)
        {
            var regular = SearchBelow(directory, faces.Regular);
            if (regular is not null) return Collect(faces, names, Path.GetDirectoryName(regular)!);
        }
        return null;
    }

    private static Found Collect(FaceSet faces, string[] names, string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) files[name] = path;
        }
        return new Found(faces, files);
    }

    private static string? SearchBelow(string directory, string fileName)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive, MaxRecursionDepth = 6 };
            return Directory.EnumerateFiles(directory, fileName, options).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var faces = _fonts.Faces;
        // Gewünschten Schnitt nehmen, sonst den regulären und PDFsharp fett/kursiv nachbilden lassen
        string? exact = (isBold, isItalic) switch
        {
            (true, true) => faces.BoldItalic,
            (true, false) => faces.Bold,
            (false, true) => faces.Italic,
            _ => faces.Regular,
        };
        if (exact is not null && _fonts.Files.ContainsKey(exact))
            return new FontResolverInfo(exact);
        if (isBold && isItalic && faces.Bold is not null && _fonts.Files.ContainsKey(faces.Bold))
            return new FontResolverInfo(faces.Bold, false, true);
        return new FontResolverInfo(faces.Regular, isBold, isItalic);
    }

    public byte[]? GetFont(string faceName) =>
        _cache.GetOrAdd(faceName, name => File.ReadAllBytes(_fonts.Files[name]));
}
