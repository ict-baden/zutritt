using System.Security.AccessControl;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>
/// Was ein Unterordner bzw. eine Datei bei normaler Vererbung von seinem Ordner erben müsste – zum Vergleich mit den
/// tatsächlich geerbten Einträgen (<see cref="FolderNode.InheritanceDiffers"/>). Anders als <see cref="NodeSignature"/>,
/// das nur „anders als der Elternordner“ erkennt (und damit auch ganz normale Fälle wie „nur dieser Ordner“), bildet
/// diese Klasse die Vererbungsregeln von Windows nach:
/// <list type="bullet">
/// <item>Unterordner erben Einträge mit „Container erben“ (wirksam und, ohne „nicht weitergeben“, wieder vererbbar)
/// sowie Einträge nur mit „Objekte erben“ als „nur vererben“ für ihre Dateien;</item>
/// <item>Dateien erben alle Einträge mit „Objekte erben“;</item>
/// <item>ERSTELLER-BESITZER wird beim wirksamen Teil durch den Ersteller ersetzt (= in aller Regel der Besitzer),
/// ERSTELLERGRUPPE durch dessen primäre Gruppe (die der Scanner nicht kennt – geduldet für genau ein Konto);</item>
/// <item>generische Rechte werden aufgelöst, SYNCHRONIZE zählt nicht (Windows ergänzt es teils beim Vererben).</item>
/// </list>
/// Verglichen werden Rechte je (SID, Erlauben/Verweigern) und Wirkung – nicht die einzelnen Einträge, denn Windows
/// teilt einen Eintrag mit generischen Rechten beim Vererben in einen wirksamen und einen „nur vererben“-Eintrag auf.
/// </summary>
internal sealed class InheritanceExpectation
{
    private const string CreatorOwner = "S-1-3-0";
    private const string CreatorGroup = "S-1-3-1";
    private const string CreatorOwnerServer = "S-1-3-2";
    private const string CreatorGroupServer = "S-1-3-3";

    private readonly bool _forFile;
    private readonly Dictionary<(string Sid, bool Deny), uint> _effective = [];
    private readonly Dictionary<(string Sid, bool Deny), uint> _toFolders = [];
    private readonly Dictionary<(string Sid, bool Deny), uint> _toFiles = [];
    // Wirksame Rechte aus ERSTELLER-BESITZER bzw. ERSTELLERGRUPPE, je Erlauben (0) und Verweigern (1)
    private readonly uint[] _creatorOwner = new uint[2];
    private readonly uint[] _creatorGroup = new uint[2];

    private InheritanceExpectation(bool forFile) => _forFile = forFile;

    /// <summary>Erwartung für die Unterordner von <paramref name="parent"/>.</summary>
    public static InheritanceExpectation ForSubfolders(FolderNode parent)
    {
        var e = new InheritanceExpectation(forFile: false);
        foreach (var ace in parent.Aces)
        {
            bool container = (ace.Inheritance & InheritanceFlags.ContainerInherit) != 0;
            bool objects = (ace.Inheritance & InheritanceFlags.ObjectInherit) != 0;
            bool noPropagate = (ace.Propagation & PropagationFlags.NoPropagateInherit) != 0;
            var mask = Mask(ace);
            if (container)
            {
                e.AddEffective(ace.Sid, ace.IsDeny, mask);
                if (noPropagate) continue;
                Merge(e._toFolders, Key(ace), mask);
                if (objects) Merge(e._toFiles, Key(ace), mask);
            }
            else if (objects && !noPropagate)
            {
                // Nur „Objekte erben“: Der Unterordner gibt ihn als „nur vererben“ an seine Dateien weiter
                Merge(e._toFiles, Key(ace), mask);
            }
        }
        return e;
    }

    /// <summary>Erwartung für die Dateien in <paramref name="folder"/>.</summary>
    public static InheritanceExpectation ForFiles(FolderNode folder)
    {
        var e = new InheritanceExpectation(forFile: true);
        foreach (var ace in folder.Aces)
            if ((ace.Inheritance & InheritanceFlags.ObjectInherit) != 0)
                e.AddEffective(ace.Sid, ace.IsDeny, Mask(ace));
        return e;
    }

    /// <summary>
    /// Passen die geerbten Einträge von <paramref name="node"/> zu dieser Erwartung? Ohne Aussage (true) bei
    /// unterbrochener Vererbung und bei Fehlern – dann gibt es nichts zu vergleichen.
    /// </summary>
    public bool Matches(FolderNode node)
    {
        if (node.Error is not null || node.InheritanceBroken) return true;

        var effective = new Dictionary<(string Sid, bool Deny), uint>();
        // Dateien geben nichts weiter – für sie (der häufigste Fall) keine weiteren Wörterbücher
        var toFolders = _forFile ? null : new Dictionary<(string Sid, bool Deny), uint>();
        var toFiles = _forFile ? null : new Dictionary<(string Sid, bool Deny), uint>();
        foreach (var ace in node.Aces)
        {
            if (!ace.IsInherited) continue;
            var mask = Mask(ace);
            if (!ace.InheritOnly) Merge(effective, Key(ace), mask);
            if (toFolders is null || toFiles is null) continue;
            bool noPropagate = (ace.Propagation & PropagationFlags.NoPropagateInherit) != 0;
            if (noPropagate) continue;
            if ((ace.Inheritance & InheritanceFlags.ContainerInherit) != 0) Merge(toFolders, Key(ace), mask);
            if ((ace.Inheritance & InheritanceFlags.ObjectInherit) != 0) Merge(toFiles, Key(ace), mask);
        }

        if (toFolders is not null && toFiles is not null && (!SameEntries(toFolders, _toFolders) || !SameEntries(toFiles, _toFiles))) return false;
        return EffectiveMatches(effective, node.OwnerSid);
    }

    /// <summary>
    /// Wirksamer Teil: alles Erwartete muss da sein; was darüber hinausgeht, muss sich aus ERSTELLER-BESITZER bzw.
    /// ERSTELLERGRUPPE erklären lassen.
    /// </summary>
    /// <remarks>
    /// Der Ersteller ist in aller Regel der Besitzer. Wurde der Besitz später übernommen, steht der Eintrag des
    /// Erstellers unter einer anderen SID – das gilt als normal, wenn genau ein Konto genau die Rechte von
    /// ERSTELLER-BESITZER hat (sonst wäre jeder Ordner nach „Besitz übernehmen“ ein Befund). Dafür bleibt ein
    /// verschobener Ordner unerkannt, dessen einziger fremder Eintrag zufällig genau diese Rechte hat.
    /// Die primäre Gruppe des Erstellers kennt der Scanner nicht: ERSTELLERGRUPPE erklärt höchstens ein Konto.
    /// </remarks>
    private bool EffectiveMatches(Dictionary<(string Sid, bool Deny), uint> actual, string ownerSid)
    {
        foreach (var (key, expected) in _effective)
            if (!actual.TryGetValue(key, out var have) || (have & expected) != expected) return false;

        var owner = ownerSid.ToUpperInvariant();
        var creatorUsed = new string?[2];
        var groupUsed = new string?[2];
        foreach (var (key, have) in actual)
        {
            var rest = have & ~_effective.GetValueOrDefault(key);
            if (rest == 0) continue;
            int d = key.Deny ? 1 : 0;
            var creator = _creatorOwner[d];
            if (key.Sid == owner && (rest & ~creator) == 0) continue;
            // Früherer Besitzer als Ersteller: genau dessen Rechte, und nur ein Konto
            if (creator != 0 && (rest & ~creator) == 0 && (have & creator) == creator && creatorUsed[d] is null)
            {
                creatorUsed[d] = key.Sid;
                continue;
            }
            if ((rest & ~_creatorGroup[d]) == 0 && (groupUsed[d] is null || groupUsed[d] == key.Sid))
            {
                groupUsed[d] = key.Sid;
                continue;
            }
            return false;
        }
        return true;
    }

    private void AddEffective(string sid, bool deny, uint mask)
    {
        int d = deny ? 1 : 0;
        if (IsOneOf(sid, CreatorOwner, CreatorOwnerServer)) _creatorOwner[d] |= mask;
        else if (IsOneOf(sid, CreatorGroup, CreatorGroupServer)) _creatorGroup[d] |= mask;
        else Merge(_effective, (sid.ToUpperInvariant(), deny), mask);
    }

    private static bool IsOneOf(string sid, string a, string b)
        => sid.Equals(a, StringComparison.OrdinalIgnoreCase) || sid.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool SameEntries(Dictionary<(string Sid, bool Deny), uint> a, Dictionary<(string Sid, bool Deny), uint> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, mask) in a)
            if (!b.TryGetValue(key, out var other) || other != mask) return false;
        return true;
    }

    private static (string Sid, bool Deny) Key(AceEntry ace) => (ace.Sid.ToUpperInvariant(), ace.IsDeny);

    // SYNCHRONIZE ist für Rechte bedeutungslos, wird beim Vererben aber teils ergänzt.
    private static uint Mask(AceEntry ace) => FileAccessMask.MapGeneric((uint)ace.Rights) & ~FileAccessMask.Synchronize;

    private static void Merge(Dictionary<(string Sid, bool Deny), uint> into, (string Sid, bool Deny) key, uint mask)
    {
        if (mask == 0) return;
        into[key] = into.TryGetValue(key, out var existing) ? existing | mask : mask;
    }
}
