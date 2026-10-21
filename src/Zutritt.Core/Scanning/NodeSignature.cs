// SPDX-License-Identifier: MIT
using System.Text;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Fingerabdruck der Rechte eines Ordners: was auf ihn selbst wirkt und was er an Unterordner weitergibt. Damit
/// erkennt der Scanner Ordner, die nur geerbte Einträge haben und trotzdem anders sind als ihr Elternordner – die
/// Auswertung leitet nicht gespeicherte Ordner vom nächsthöheren gespeicherten ab. Das passiert z. B. bei
/// <list type="bullet">
/// <item>ERSTELLER-BESITZER: Wer einen Ordner anlegt, bekommt darauf einen eigenen (geerbten) Eintrag,</item>
/// <item>Einträgen „nur dieser Ordner“ oder „nur Dateien“ am Elternordner (wirken nicht auf Unterordner),</item>
/// <item>verschobenen Ordnern, die ihre alten geerbten Einträge mitnehmen – auch dann, wenn sie zufällig dieselben
/// Rechte haben wie der neue Elternordner, sie aber anders weitervererben (etwa vererbbar statt „nur eine Ebene“).</item>
/// </list>
/// </summary>
internal static class NodeSignature
{
    /// <summary>
    /// Wirksame und weitergegebene Einträge zusammen. Bei normaler Vererbung stimmt der weitergegebene Teil immer mit
    /// dem des Elternordners überein; der wirksame Teil weicht nur ab, wenn der Elternordner Einträge hat, die gar nicht
    /// oder nur eine Ebene weit vererbt werden, oder durch ERSTELLER-BESITZER.
    /// </summary>
    /// <returns>null, wenn die Rechte des Knotens nicht (vollständig) gelesen werden konnten.</returns>
    public static string? Of(FolderNode node) => node.Error is null ? Build(node, withInheritable: true) : null;

    /// <summary>Nur die wirksamen Einträge (je SID und Erlauben/Verweigern, generische Rechte aufgelöst).</summary>
    /// <remarks>Ob ein Eintrag geerbt oder explizit ist, spielt für die Rechte keine Rolle und zählt daher nicht.</remarks>
    public static string Effective(FolderNode node) => Build(node, withInheritable: false);

    private static string Build(FolderNode node, bool withInheritable)
    {
        var effective = new Dictionary<(string Sid, bool Deny), uint>();
        var inheritable = new Dictionary<(string Sid, bool Deny), uint>();
        foreach (var ace in node.Aces)
        {
            // SYNCHRONIZE ist für Rechte bedeutungslos, wird beim Vererben aber teils ergänzt.
            var mask = FileAccessMask.MapGeneric((uint)ace.Rights) & ~FileAccessMask.Synchronize;
            var key = (ace.Sid.ToUpperInvariant(), ace.IsDeny);
            if (!ace.InheritOnly) Merge(effective, key, mask);
            if (withInheritable && PassesToSubfolders(ace)) Merge(inheritable, key, mask);
        }
        var sb = new StringBuilder();
        Append(sb, effective);
        if (withInheritable)
        {
            sb.Append('#');
            Append(sb, inheritable);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Wirksame Rechte, die eine Datei in <paramref name="folder"/> bei normaler Vererbung hätte (Einträge mit
    /// „Objekte erben“, ERSTELLER-BESITZER durch den Besitzer der Datei ersetzt) – im Format von <see cref="Effective"/>.
    /// </summary>
    public static string InheritedByFile(FolderNode folder, string fileOwnerSid)
    {
        var expected = new FolderNode { Path = folder.Path, IsFile = true };
        foreach (var ace in folder.Aces)
        {
            if ((ace.Inheritance & AceInheritance.ObjectInherit) == 0) continue;
            var sid = ace.Sid.Equals(CreatorOwner, StringComparison.OrdinalIgnoreCase) ? fileOwnerSid : ace.Sid;
            expected.Aces.Add(ace with { Sid = sid, Inheritance = AceInheritance.None, Propagation = AcePropagation.None });
        }
        return Effective(expected);
    }

    private const string CreatorOwner = "S-1-3-0";

    /// <summary>
    /// Gibt der Eintrag an Unterordner weiter, und zwar so, dass auch deren Unterordner ihn wieder erben?
    /// „Container erben“ ohne „nicht weitergeben“ – genau diese Einträge kommen bei normaler Vererbung beim Kind
    /// wieder als vererbbare an (ERSTELLER-BESITZER bleibt dabei als Platzhalter erhalten).
    /// </summary>
    private static bool PassesToSubfolders(AceEntry ace)
        => (ace.Inheritance & AceInheritance.ContainerInherit) != 0 && (ace.Propagation & AcePropagation.NoPropagateInherit) == 0;

    private static void Merge(Dictionary<(string Sid, bool Deny), uint> into, (string Sid, bool Deny) key, uint mask)
        => into[key] = into.TryGetValue(key, out var existing) ? existing | mask : mask;

    private static void Append(StringBuilder sb, Dictionary<(string Sid, bool Deny), uint> entries)
    {
        foreach (var ((sid, deny), mask) in entries.OrderBy(e => e.Key.Sid, StringComparer.Ordinal).ThenBy(e => e.Key.Deny))
            sb.Append(sid).Append(deny ? "|D|" : "|A|").Append(mask.ToString("X")).Append(';');
    }
}
