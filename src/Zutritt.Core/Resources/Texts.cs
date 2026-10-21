// SPDX-License-Identifier: MIT
using System.Globalization;

namespace Zutritt.Core.Resources;

/// <summary>
/// Texte mit Platzhaltern aus <see cref="Strings"/> füllen (Richtlinie 13: Platzhalter statt zusammengestückelter Sätze).
/// Zahlen und Daten in den Platzhaltern nach dem Gebietsschema der Oberfläche.
/// </summary>
public static class Texts
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>Name oder Begriff in den Anführungszeichen der Sprache: „x“, “x”, «x», « x ».</summary>
    public static string Quote(string text) => Format(Strings.Text_Quote, text);

    /// <summary>Aufzählung „a, b und c“ in der Sprache der Oberfläche.</summary>
    public static string JoinAnd(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count switch
        {
            0 => "",
            1 => items[0],
            _ => Format(Strings.Text_ListAnd, string.Join(Strings.Text_ListSeparator, items.Take(items.Count - 1)), items[^1]),
        };
    }

    /// <summary>
    /// Einzahl oder Mehrzahl. .NET kennt keine Mehrzahlregeln in .resx; für die fünf Sprachen genügen zwei Formen
    /// (Französisch: 0 und 1 sind Einzahl, Chinesisch: beide Formen gleich).
    /// </summary>
    public static string Plural(long count, string one, string many)
    {
        bool singular = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? count is 0 or 1 : count == 1;
        return singular ? one : many;
    }
}
