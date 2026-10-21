// SPDX-License-Identifier: MIT
using System.Collections;
using System.Reflection;

namespace Zutritt.Tests.Storage;

/// <summary>
/// Vergleicht zwei Objektbäume über alle öffentlichen Eigenschaften. Per Reflexion, damit ein neues Feld im
/// Modell automatisch mitgeprüft wird. Zeitpunkte werden mit Offset verglichen (<c>==</c> ignoriert ihn).
/// </summary>
internal static class DeepAssert
{
    public static void Equal(object? expected, object? actual, string root = "scan")
    {
        var diff = FindDifference(expected, actual, root);
        if (diff is not null) Assert.Fail(diff);
    }

    public static string? FindDifference(object? expected, object? actual, string path)
    {
        if (expected is null || actual is null)
            return expected is null && actual is null ? null : $"{path}: erwartet {Show(expected)}, tatsächlich {Show(actual)}";

        if (expected is DateTimeOffset e && actual is DateTimeOffset a)
            return e.EqualsExact(a) ? null : $"{path}: erwartet {e:o}, tatsächlich {a:o}";

        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string or decimal or DateTime or TimeSpan or Guid)
            return Equals(expected, actual) ? null : $"{path}: erwartet {Show(expected)}, tatsächlich {Show(actual)}";

        if (expected is IDictionary ed)
        {
            if (actual is not IDictionary ad) return $"{path}: tatsächlich kein Wörterbuch";
            var ek = ed.Keys.Cast<object>().ToList();
            var ak = ad.Keys.Cast<object>().ToList();
            if (ek.Count != ak.Count) return $"{path}: {ek.Count} statt {ak.Count} Einträge";
            for (var i = 0; i < ek.Count; i++)
            {
                // Reihenfolge zählt: Oberfläche und Berichte zählen in dieser Reihenfolge auf
                if (!Equals(ek[i], ak[i])) return $"{path}: Schlüssel {i} erwartet {Show(ek[i])}, tatsächlich {Show(ak[i])}";
                var d = FindDifference(ed[ek[i]], ad[ak[i]], $"{path}[{ek[i]}]");
                if (d is not null) return d;
            }
            return null;
        }

        if (expected is IEnumerable ee)
        {
            // Der Listentyp darf abweichen (Array vs. List nach JSON), der Inhalt nicht
            if (actual is not IEnumerable ae) return $"{path}: tatsächlich keine Liste";
            var el = ee.Cast<object?>().ToList();
            var al = ae.Cast<object?>().ToList();
            if (el.Count != al.Count) return $"{path}: erwartet {el.Count} Elemente, tatsächlich {al.Count}";
            for (var i = 0; i < el.Count; i++)
            {
                var d = FindDifference(el[i], al[i], $"{path}[{i}]");
                if (d is not null) return d;
            }
            return null;
        }

        if (type != actual.GetType()) return $"{path}: Typ {type.Name} statt {actual.GetType().Name}";
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            var d = FindDifference(p.GetValue(expected), p.GetValue(actual), $"{path}.{p.Name}");
            if (d is not null) return d;
        }
        return null;
    }

    private static string Show(object? o) => o switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => o.ToString() ?? "?",
    };
}
