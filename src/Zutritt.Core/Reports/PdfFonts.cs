using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace Zutritt.Core.Reports;

/// <summary>
/// Schriften für den PDF-Bericht. Der plattformneutrale PDFsharp-Build bringt keine Schriften mit und
/// verlangt einen Resolver, der genau einmal pro Prozess gesetzt werden darf – deshalb hier zentral,
/// gesperrt und nur, wenn noch niemand (z. B. die Oberfläche) einen eigenen gesetzt hat.
/// Jede angefragte Familie wird auf Segoe UI abgebildet (Fallback Arial, Tahoma, Verdana): der Bericht
/// braucht nur eine Schrift, und so scheitert auch MigraDocs interne Standardschrift nicht.
/// </summary>
internal sealed class PdfFonts : IFontResolver
{
    private sealed record FaceSet(string Family, string Regular, string? Bold, string? Italic, string? BoldItalic);

    private static readonly FaceSet[] Candidates =
    [
        new("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        new("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        new("Tahoma", "tahoma.ttf", "tahomabd.ttf", null, null),
        new("Verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
    ];

    private static readonly Lock Gate = new();
    private static string? _family;

    private readonly string _directory;
    private readonly FaceSet _faces;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    private PdfFonts(string directory, FaceSet faces)
    {
        _directory = directory;
        _faces = faces;
    }

    /// <summary>
    /// Schriftfamilie, mit der der Bericht gesetzt wird. Der erste Aufruf richtet den Resolver ein.
    /// </summary>
    /// <exception cref="InvalidOperationException">Unter %WINDIR%\Fonts gibt es keine der Schriften.</exception>
    public static string Family
    {
        get
        {
            lock (Gate) return _family ??= Initialize();
        }
    }

    private static string Initialize()
    {
        var current = GlobalFontSettings.FontResolver;
        if (current is PdfFonts own) return own._faces.Family;
        if (current is null)
        {
            var resolver = Create();
            try
            {
                GlobalFontSettings.FontResolver = resolver;
                return resolver._faces.Family;
            }
            catch (InvalidOperationException)
            {
                // PDFsharp wurde in diesem Prozess schon ohne Resolver benutzt – dann gilt der vorhandene Zustand
            }
        }
        // Fremder Resolver (z. B. UseWindowsFontsUnderWindows): Arial kennt praktisch jeder
        return "Arial";
    }

    private static PdfFonts Create()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(directory))
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        foreach (var faces in Candidates)
            if (File.Exists(Path.Combine(directory, faces.Regular)))
                return new PdfFonts(directory, faces);
        throw new InvalidOperationException(
            $"Für den PDF-Bericht wurde keine Schrift gefunden (gesucht: {string.Join(", ", Candidates.Select(c => c.Family))} in {directory}).");
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Gewünschten Schnitt nehmen, sonst den regulären und PDFsharp fett/kursiv nachbilden lassen
        string? exact = (isBold, isItalic) switch
        {
            (true, true) => _faces.BoldItalic,
            (true, false) => _faces.Bold,
            (false, true) => _faces.Italic,
            _ => _faces.Regular,
        };
        if (exact is not null && File.Exists(Path.Combine(_directory, exact)))
            return new FontResolverInfo(exact);
        if (isBold && isItalic && _faces.Bold is not null && File.Exists(Path.Combine(_directory, _faces.Bold)))
            return new FontResolverInfo(_faces.Bold, false, true);
        return new FontResolverInfo(_faces.Regular, isBold, isItalic);
    }

    public byte[]? GetFont(string faceName) =>
        _cache.GetOrAdd(faceName, name => File.ReadAllBytes(Path.Combine(_directory, name)));
}
