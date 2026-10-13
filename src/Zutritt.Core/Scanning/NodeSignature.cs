using System.Text;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Fingerabdruck der Einträge, die auf einen Ordner selbst wirken. Damit erkennt der Scanner Ordner, die nur geerbte
/// Einträge haben und trotzdem andere Rechte als ihr Elternordner – die Auswertung nimmt für nicht gespeicherte Ordner
/// sonst die Rechte des nächsthöheren gespeicherten an. Das passiert z. B. bei
/// <list type="bullet">
/// <item>ERSTELLER-BESITZER: Wer einen Ordner anlegt, bekommt darauf einen eigenen (geerbten) Eintrag,</item>
/// <item>Einträgen „nur dieser Ordner“ oder „nur Dateien“ am Elternordner (wirken nicht auf Unterordner),</item>
/// <item>verschobenen Ordnern, die ihre alten geerbten Einträge mitnehmen.</item>
/// </list>
/// </summary>
internal static class NodeSignature
{
    /// <summary>
    /// Wirksame Einträge je (SID, Erlauben/Verweigern) mit aufgelösten generischen Rechten, sortiert.
    /// Ob ein Eintrag geerbt oder explizit ist, spielt für die Rechte keine Rolle und zählt daher nicht.
    /// </summary>
    /// <returns>null, wenn die Rechte des Knotens nicht (vollständig) gelesen werden konnten.</returns>
    public static string? Of(FolderNode node)
    {
        if (node.Error is not null) return null;
        var merged = new Dictionary<(string Sid, bool Deny), uint>();
        foreach (var ace in node.Aces)
        {
            if (ace.InheritOnly) continue;
            // SYNCHRONIZE ist für Rechte bedeutungslos, wird beim Vererben aber teils ergänzt.
            var mask = FileAccessMask.MapGeneric((uint)ace.Rights) & ~FileAccessMask.Synchronize;
            var key = (ace.Sid.ToUpperInvariant(), ace.IsDeny);
            merged[key] = merged.TryGetValue(key, out var existing) ? existing | mask : mask;
        }

        var sb = new StringBuilder();
        foreach (var ((sid, deny), mask) in merged.OrderBy(e => e.Key.Sid, StringComparer.Ordinal).ThenBy(e => e.Key.Deny))
            sb.Append(sid).Append(deny ? "|D|" : "|A|").Append(mask.ToString("X")).Append(';');
        return sb.ToString();
    }
}
