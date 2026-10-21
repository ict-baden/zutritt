// SPDX-License-Identifier: MIT
using Zutritt.Core.Reports;

namespace Zutritt.Tests.TestSupport;

/// <summary>Test, der den PDF-Bericht setzt. Ohne passende Schrift auf dem System (z. B. Linux ohne DejaVu) übersprungen.</summary>
public sealed class PdfFactAttribute : FactAttribute
{
    public PdfFactAttribute()
    {
        if (!PdfFonts.IsAvailable) Skip = PdfFontMissing.Reason;
    }
}

/// <summary>Wie <see cref="PdfFactAttribute"/>, für Theorien.</summary>
public sealed class PdfTheoryAttribute : TheoryAttribute
{
    public PdfTheoryAttribute()
    {
        if (!PdfFonts.IsAvailable) Skip = PdfFontMissing.Reason;
    }
}

/// <summary>
/// PDF-Test, der prüft, dass Texte in ihre Spalten passen. Die Spaltenbreiten sind auf Segoe UI abgestimmt – der Bericht
/// entsteht unter Windows. Mit einer breiteren Schrift (DejaVu Sans unter Linux) umbricht er früher; dann übersprungen.
/// </summary>
public sealed class PdfLayoutFactAttribute : FactAttribute
{
    public PdfLayoutFactAttribute()
    {
        var family = PdfFonts.AvailableFamily;
        if (family is null) Skip = PdfFontMissing.Reason;
        else if (family != "Segoe UI") Skip = $"Spaltenbreiten sind auf Segoe UI abgestimmt, hier gibt es nur {family}.";
    }
}

internal static class PdfFontMissing
{
    public const string Reason = "Keine Schrift für den PDF-Bericht (Segoe UI, Arial, DejaVu Sans, Liberation Sans …) auf diesem System.";
}
