// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>
/// Sortiert Pfade wie einen Baum: Abschnitt für Abschnitt, Groß/Klein egal. So steht ein Unterordner direkt
/// unter seinem Elternordner (bei reinem Zeichenvergleich käme „Labor-Alt“ vor „Labor\Archiv“).
/// </summary>
public sealed class PathOrder : IComparer<string>
{
    public static PathOrder Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var a = x.AsSpan();
        var b = y.AsSpan();
        while (true)
        {
            int ia = a.IndexOf(WindowsPath.Separator), ib = b.IndexOf(WindowsPath.Separator);
            var segA = ia < 0 ? a : a[..ia];
            var segB = ib < 0 ? b : b[..ib];
            int c = segA.CompareTo(segB, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            if (ia < 0 || ib < 0)
            {
                // Gleicher Abschnitt: der kürzere Pfad (Elternordner) zuerst
                if (ia < 0 && ib < 0) return 0;
                return ia < 0 ? -1 : 1;
            }
            a = a[(ia + 1)..];
            b = b[(ib + 1)..];
        }
    }

    /// <summary>Pfad ohne abschließenden Trenner (außer bei Laufwerkswurzeln wie „C:\“).</summary>
    internal static string Normalize(string path) => WindowsPath.TrimEndKeepShort(path);

    /// <summary>Elternpfad oder null, wenn es keinen sinnvollen gibt (Laufwerk, \\server\freigabe).</summary>
    internal static string? Parent(string path) => WindowsPath.Parent(path);

    /// <summary>Anzahl Ebenen von <paramref name="ancestor"/> bis <paramref name="path"/>, 0 = gleich, null = nicht darunter.</summary>
    internal static int? Distance(string ancestor, string path)
    {
        ancestor = Normalize(ancestor);
        path = Normalize(path);
        if (path.Equals(ancestor, StringComparison.OrdinalIgnoreCase)) return 0;
        var prefix = ancestor.EndsWith(WindowsPath.Separator) ? ancestor : ancestor + WindowsPath.Separator;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = path.AsSpan(prefix.Length);
        if (rest.IsEmpty) return null;
        return rest.Count(WindowsPath.Separator) + 1;
    }
}
