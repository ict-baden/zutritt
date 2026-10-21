// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Reports;

/// <summary>
/// Sortiert Pfade als Baum: Abschnitt für Abschnitt statt Zeichen für Zeichen. Sonst landete
/// „Labor-Alt“ zwischen „Labor“ und „Labor\Archiv“, weil „-“ vor „\“ kommt.
/// </summary>
internal sealed class PathOrder : IComparer<string>
{
    public static readonly PathOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var a = x.AsSpan();
        var b = y.AsSpan();
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        while (true)
        {
            int ia = a.IndexOfAny(WindowsPath.Separator, '/');
            int ib = b.IndexOfAny(WindowsPath.Separator, '/');
            var sa = ia < 0 ? a : a[..ia];
            var sb = ib < 0 ? b : b[..ib];
            int c = compare.Compare(sa, sb, System.Globalization.CompareOptions.IgnoreCase);
            if (c != 0) return c;
            // Der kürzere Pfad (das Elternelement) kommt zuerst
            if (ia < 0 || ib < 0)
            {
                if (ia < 0 && ib < 0) break;
                return ia < 0 ? -1 : 1;
            }
            a = a[(ia + 1)..];
            b = b[(ib + 1)..];
        }
        // Gleich bis auf Groß/Klein oder Schreibweise: trotzdem stabil sortieren
        return string.CompareOrdinal(x, y);
    }
}
