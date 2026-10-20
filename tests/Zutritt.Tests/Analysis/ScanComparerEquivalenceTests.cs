// SPDX-License-Identifier: MIT
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

/// <summary>
/// Der Vergleich überspringt Ordner und Personen, bei denen sich nachweislich nichts ändern kann, und rechnet je
/// Token-Klasse nur einmal. Hier wird gegen die schlichte Rechnung „jede Person auf jedem Pfad“ geprüft – mit
/// zufälligen Läufen (feste Startwerte), damit auch ungewöhnliche Kombinationen vorkommen.
/// </summary>
public sealed class ScanComparerEquivalenceTests
{
    private static readonly AceInheritance[] InheritanceChoices =
        [CIOI, AceInheritance.None, AceInheritance.ContainerInherit, AceInheritance.ObjectInherit];

    private static readonly FileRights[] RightsChoices =
    [
        S.Read, S.Modify, S.Full, FileRights.Write, FileRights.Delete, FileRights.ListDirectory,
        FileRights.ChangePermissions, unchecked((FileRights)0x80000000),
    ];

    private static readonly string[] Paths =
    [
        Share, Share + @"\A", Share + @"\A\B", Share + @"\A\B\C", Share + @"\A\D", Share + @"\E", Share + @"\E\F\G",
        @"D:\Lokal", @"D:\Lokal\X", @"D:\Lokal\X\Y", @"D:\Lokal\Z",
    ];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Ergebnis_gleich_der_vollen_Rechnung(int seed)
    {
        var random = new Random(seed);
        var older = RandomScan(random);
        var newer = Mutate(older, random);

        var expected = BruteForce(older, newer).Order().ToList();
        var actual = ScanComparer.Compare(older, newer).AccessChanges
            .Select(c => $"{c.Path.ToUpperInvariant()}|{c.Sid.ToUpperInvariant()}|{c.Before}|{c.After}").Order().ToList();
        Assert.Equal(expected, actual);
    }

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (int i = 1; i <= 150; i++) data.Add(i);
        return data;
    }

    /// <summary>Die schlichte Rechnung: jede Person auf jedem in einem der Läufe gespeicherten Pfad.</summary>
    private static List<string> BruteForce(ScanResult older, ScanResult newer)
    {
        bool membership = older.Request.ResolveGroups && newer.Request.ResolveGroups;
        var oldCtx = new AccessContext(older, membership);
        var newCtx = new AccessContext(newer, membership);
        var people = older.Principals.Values.Concat(newer.Principals.Values).Where(p => p.Kind == PrincipalKind.Benutzer)
            .Select(p => p.Sid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var paths = older.Folders.Concat(newer.Folders).Where(f => f.Error is null)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First());

        var changes = new List<string>();
        foreach (var folder in paths)
        {
            var before = oldCtx.NodeAt(folder.Path, folder.IsFile);
            var after = newCtx.NodeAt(folder.Path, folder.IsFile);
            if (before is null || after is null) continue;
            foreach (var sid in people)
            {
                var b = EffectiveAccess.EffectiveLevel(new AccessContext(older, membership), before, sid);
                var a = EffectiveAccess.EffectiveLevel(new AccessContext(newer, membership), after, sid);
                if (a != b) changes.Add($"{folder.Path.ToUpperInvariant()}|{sid.ToUpperInvariant()}|{b}|{a}");
            }
        }
        return changes;
    }

    private static string[] Users => [Sid(2001), Sid(2002), Sid(2003), Sid(2004), Sid(2005), Sid(2006)];
    private static string[] Groups => [Sid(1001), Sid(1002), Sid(1003), Sid(1004)];

    private static string[] Trustees =>
    [
        .. Users, .. Groups, S.Everyone, S.AuthenticatedUsers, S.BuiltinUsers, S.Administrators, S.CreatorOwner,
        SidCatalog.OwnerRights, SidCatalog.Network, SidCatalog.Interactive, Sid(2999),
    ];

    private static T Pick<T>(Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];

    internal static ScanResult RandomScan(Random random)
    {
        var b = new ScanBuilder();
        b.Scan.Request = b.Scan.Request with { ResolveGroups = random.Next(4) > 0 };
        foreach (var u in Users) b.User(u, $@"T\u{u[^4..]}");
        b.Known(S.Everyone, "Jeder").Known(S.AuthenticatedUsers, @"NT-AUTORITÄT\Authentifizierte Benutzer")
            .Group(S.BuiltinUsers, @"VORDEFINIERT\Benutzer", S.AuthenticatedUsers).Group(S.Administrators, @"VORDEFINIERT\Administratoren");
        foreach (var g in Groups)
        {
            // Mitglieder auch Gruppen – Zyklen ausdrücklich erlaubt
            var members = Users.Concat(Groups).Where(_ => random.Next(4) == 0).ToArray();
            b.Group(g, $@"T\g{g[^4..]}", members);
        }
        b.WithShare(RandomSharePermissions(random));

        foreach (var path in Paths)
        {
            // Wurzeln immer, sonst etwa die Hälfte gespeichert; gelegentlich nicht lesbar
            bool root = path.Equals(Share, StringComparison.OrdinalIgnoreCase) || path.Equals(@"D:\Lokal", StringComparison.OrdinalIgnoreCase);
            if (!root && random.Next(2) == 0) continue;
            b.Scan.Folders.Add(RandomNode(random, path, root));
        }
        return b.Scan;
    }

    private static ShareAce[] RandomSharePermissions(Random random) =>
        Enumerable.Range(0, random.Next(1, 4))
            .Select(_ => new ShareAce(Pick(random, Trustees), (ShareAccess)random.Next(1, 4), random.Next(6) == 0))
            .ToArray();

    private static FolderNode RandomNode(Random random, string path, bool root)
    {
        bool shared = path.StartsWith(Share, StringComparison.OrdinalIgnoreCase);
        var node = new FolderNode
        {
            Path = random.Next(5) == 0 ? path.ToUpperInvariant() : path,
            Depth = path.Count(c => c == '\\') - (shared ? 3 : 1),
            ShareUnc = shared ? Share : null,
            OwnerSid = Pick(random, [.. Users, .. Groups, S.Administrators]),
            InheritanceBroken = root || random.Next(3) == 0,
            Error = !root && random.Next(12) == 0 ? "Zugriff verweigert" : null,
        };
        for (int i = random.Next(1, 6); i > 0; i--) node.Aces.Add(RandomAce(random));
        return node;
    }

    private static AceEntry RandomAce(Random random) => new()
    {
        Sid = Pick(random, Trustees),
        Rights = Pick(random, RightsChoices),
        IsDeny = random.Next(5) == 0,
        IsInherited = random.Next(3) == 0,
        Inheritance = Pick(random, InheritanceChoices),
        Propagation = random.Next(6) switch
        {
            0 => AcePropagation.InheritOnly,
            1 => AcePropagation.NoPropagateInherit,
            _ => AcePropagation.None,
        },
    };

    /// <summary>Kopie mit zufälligen Änderungen an Einträgen, Besitzern, Mitgliedschaften, Freigabe und gespeicherten Ordnern.</summary>
    private static ScanResult Mutate(ScanResult source, Random random)
    {
        var scan = new ScanResult
        {
            Request = source.Request with { ResolveGroups = random.Next(5) == 0 ? !source.Request.ResolveGroups : source.Request.ResolveGroups },
        };
        foreach (var p in source.Principals.Values)
        {
            if (p.Kind == PrincipalKind.Benutzer && random.Next(10) == 0) continue; // Konto fehlt im neuen Lauf
            var copy = new Principal { Sid = p.Sid, Name = p.Name, Kind = p.Kind };
            copy.Members.AddRange(p.Members);
            if (p.Kind == PrincipalKind.Gruppe && random.Next(3) == 0)
            {
                var someone = Pick(random, Users);
                if (!copy.Members.Remove(someone)) copy.Members.Add(someone);
            }
            scan.Principals[p.Sid] = copy;
        }
        if (random.Next(8) == 0)
        {
            var neu = Sid(2100 + random.Next(5));
            scan.Principals[neu] = new Principal { Sid = neu, Name = @"T\neu", Kind = PrincipalKind.Benutzer };
            scan.Principals[Pick(random, Groups)].Members.Add(neu);
        }

        foreach (var share in source.Shares)
            scan.Shares.Add(random.Next(4) == 0 ? share with { Permissions = [.. RandomSharePermissions(random)] }
                : random.Next(10) == 0 ? share with { PermissionsReadable = false, Permissions = [] } : share);

        foreach (var f in source.Folders)
        {
            if (!f.IsRoot && random.Next(8) == 0) continue; // Ausnahme aufgehoben
            var copy = new FolderNode
            {
                Path = f.Path, Depth = f.Depth, ShareUnc = f.ShareUnc, InheritanceBroken = f.InheritanceBroken, Error = f.Error,
                OwnerSid = random.Next(6) == 0 ? Pick(random, [.. Users, .. Groups]) : f.OwnerSid,
            };
            copy.Aces.AddRange(f.Aces);
            switch (random.Next(5))
            {
                case 0: copy.Aces.Add(RandomAce(random)); break;
                case 1 when copy.Aces.Count > 0: copy.Aces.RemoveAt(random.Next(copy.Aces.Count)); break;
                case 2 when copy.Aces.Count > 0:
                    int i = random.Next(copy.Aces.Count);
                    copy.Aces[i] = copy.Aces[i] with { Rights = Pick(random, RightsChoices) };
                    break;
            }
            scan.Folders.Add(copy);
        }
        // Neue Ausnahmen
        foreach (var path in Paths.Where(p => random.Next(6) == 0))
            if (!scan.Folders.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                scan.Folders.Add(RandomNode(random, path, root: false));
        return scan;
    }
}
