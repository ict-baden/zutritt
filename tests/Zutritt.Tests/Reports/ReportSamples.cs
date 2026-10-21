// SPDX-License-Identifier: MIT
using System.Globalization;
using Zutritt.Core.Model;
using Zutritt.Core.Reports;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Reports;

/// <summary>
/// Handgebaute Berichtsdaten aus <see cref="SampleScan"/>: Befunde, Regeltexte und effektive Rechte sind
/// hier von Hand nach der Spezifikation bestimmt – so lassen sich Builder und Renderer ohne die
/// (parallel gebaute) Auswertung testen.
/// </summary>
internal static class ReportSamples
{
    public const string Root = @"\\FS01\Labor";
    public const string Qc = Root + @"\QC";
    public const string Austausch = Root + @"\Austausch";
    public const string Rohdaten = Root + @"\Projekte\2019\Alt\Rohdaten";
    public const string Archiv = Root + @"\Archiv";
    public const string Personal = Root + @"\Personal";

    /// <summary>Nur im älteren Lauf bekannt (inzwischen gelöscht).</summary>
    public const string Former = Domain + "-2500";

    public static readonly DateTimeOffset CreatedAt = new(2026, 10, 14, 10, 15, 0, TimeSpan.FromHours(2));

    /// <summary>SampleScan samt den Befunden, die die Spezifikation für ihn erwartet.</summary>
    public static ScanResult Scan()
    {
        var scan = SampleScan.Create();
        scan.Findings.AddRange(Findings());
        return scan;
    }

    public static ReportData Data(ScanResult? scan = null, ComparisonResult? comparison = null, int maxPersonGrants = ReportDataBuilder.DefaultMaxPersonGrants)
    {
        scan ??= Scan();
        return ReportDataBuilder.Build(scan, comparison, Rules(), ForPerson, CreatedAt, maxPersonGrants);
    }

    public static ReportData DataWithComparison() => Data(Scan(), Comparison());

    // Hoch 2, Mittel 5, Niedrig 3, Info 3 – insgesamt 13
    public static List<Finding> Findings() =>
    [
        F("ZT01", Severity.Hoch, Austausch, Everyone, "„Jeder“ darf in „Austausch“ ändern."),
        F("ZT02", Severity.Mittel, Archiv, AuthenticatedUsers, "„Authentifizierte Benutzer“ dürfen „Archiv“ lesen."),
        F("ZT03", Severity.Mittel, Qc, Max, "Recht direkt an „max.muster“ vergeben."),
        F("ZT03", Severity.Mittel, Rohdaten, Klaus, "Recht direkt an „klaus.alt“ vergeben."),
        F("ZT04", Severity.Mittel, Rohdaten, Verwaist, "Verwaiste SID in der ACL."),
        F("ZT05", Severity.Hoch, Qc, Max, "„max.muster“ hat Vollzugriff."),
        F("ZT06", Severity.Niedrig, Rohdaten, null, "Vererbung in Tiefe 4 unterbrochen."),
        F("ZT07", Severity.Info, Archiv, Max, "Verweigern-Eintrag für „max.muster“."),
        F("ZT08", Severity.Info, Root, Everyone, "Freigabe: „Jeder“ hat Vollzugriff."),
        F("ZT09", Severity.Info, Personal, null, "Ordner nicht lesbar: Zugriff verweigert."),
        F("ZT10", Severity.Niedrig, Austausch, Max, "Besitzer ist „max.muster“."),
        F("ZT11", Severity.Niedrig, Rohdaten, LeereGruppe, "Leere Gruppe „Alt-Projekt“ berechtigt."),
        F("ZT12", Severity.Mittel, Rohdaten, Klaus, "Deaktiviertes Konto „klaus.alt“ berechtigt."),
    ];

    public static Finding F(string rule, Severity severity, string path, string? sid, string message) =>
        new() { RuleId = rule, Severity = severity, Path = path, Sid = sid, Message = message };

    public static List<RuleInfo> Rules() =>
    [
        new("ZT01", Severity.Hoch, "Breite Gruppe darf schreiben", "Jeder im Netz kann Dateien ändern.", "Schreibrecht nur an Fachgruppen vergeben."),
        new("ZT02", Severity.Mittel, "Breite Gruppe darf lesen", "Alle angemeldeten Benutzer können lesen.", "Leserecht auf Fachgruppen beschränken."),
        new("ZT03", Severity.Mittel, "Recht direkt an Person vergeben", "Direkte Rechte sind schwer zu pflegen.", "Person in eine Gruppe aufnehmen."),
        new("ZT04", Severity.Mittel, "Verwaiste SID", "Das Konto existiert nicht mehr.", "Eintrag entfernen."),
        new("ZT05", Severity.Hoch, "Vollzugriff für Nicht-Admins", "Darf Rechte ändern.", "Auf „Ändern“ reduzieren."),
        new("ZT06", Severity.Niedrig, "Tief versteckte Ausnahme", "Unterbrochene Vererbung tief im Baum.", "Ausnahme dokumentieren oder auflösen."),
        new("ZT07", Severity.Info, "Verweigern-Eintrag", "Verweigerungen sind schwer nachvollziehbar.", "Durch fehlende Erlaubnis ersetzen."),
        new("ZT08", Severity.Info, "Freigabe: Jeder Vollzugriff", "Schutz liegt allein bei NTFS.", "Bewusst so lassen oder einschränken."),
        new("ZT09", Severity.Info, "Ordner nicht lesbar", "Nicht alles geprüft.", "Mit ausreichenden Rechten erneut scannen."),
        new("ZT10", Severity.Niedrig, "Besitzer ist ein Benutzerkonto", "Besitzer darf immer Rechte ändern.", "Besitz an Administratoren übertragen."),
        new("ZT11", Severity.Niedrig, "Leere Gruppe berechtigt", "Gruppe ohne Mitglieder.", "Eintrag oder Gruppe entfernen."),
        new("ZT12", Severity.Mittel, "Deaktiviertes Konto berechtigt", "Konto ist deaktiviert.", "Eintrag entfernen."),
        new("ZT13", Severity.Info, "Freigabe-Berechtigungen nicht lesbar", "Freigabe-Rechte unbekannt.", "Mit Adminrechten erneut scannen."),
    ];

    /// <summary>Effektive Zugriffe von Hand (Regeln der Spezifikation: Allow-ODER minus Deny, Freigabe „Jeder: Vollzugriff“).</summary>
    public static IReadOnlyList<AccessGrant> ForPerson(string sid) => sid switch
    {
        Max =>
        [
            G(Qc, Max, AccessLevel.Vollzugriff),
            G(Root, Max, AccessLevel.Aendern, @"LAB\Labor-Alle"),
            G(Austausch, Max, AccessLevel.Aendern, "Jeder"),
        ],
        Erika =>
        [
            G(Root, Erika, AccessLevel.Aendern, @"LAB\QC-Team", @"LAB\Labor-Alle"),
            G(Qc, Erika, AccessLevel.Aendern, @"LAB\QC-Team"),
            G(Austausch, Erika, AccessLevel.Aendern, "Jeder"),
            G(Archiv, Erika, AccessLevel.Aendern, @"LAB\QC-Team", @"LAB\Labor-Alle"),
        ],
        Klaus =>
        [
            G(Austausch, Klaus, AccessLevel.Aendern, "Jeder"),
            G(Rohdaten, Klaus, AccessLevel.Lesen),
            G(Archiv, Klaus, AccessLevel.Lesen, @"NT-AUTORITÄT\Authentifizierte Benutzer"),
        ],
        _ => [],
    };

    public static AccessGrant G(string path, string sid, AccessLevel level, params string[] via) =>
        new() { Path = path, Sid = sid, Ntfs = level, ViaShare = level, Via = via };

    /// <summary>Vergleich mit einem Lauf eine Woche früher, Änderungen von Hand eingetragen.</summary>
    public static ComparisonResult Comparison()
    {
        var newer = Scan();
        var older = SampleScan.Create(newer.StartedAt.AddDays(-7));
        older.Principals[Former] = new Principal { Sid = Former, Name = @"LAB\ehemalig", Kind = PrincipalKind.Benutzer };
        older.Principals[QcTeam].Members.Add(Former);

        var c = new ComparisonResult { Older = older, Newer = newer };
        var everyoneModify = Ace(Everyone, Modify);
        c.AceChanges.Add(new AceChange(Austausch, ChangeKind.Hinzugefuegt, Everyone, null, everyoneModify));
        c.AceChanges.Add(new AceChange(Qc, ChangeKind.Geaendert, Max, Ace(Max, Modify), Ace(Max, Full)));
        c.AceChanges.Add(new AceChange(Archiv, ChangeKind.Geaendert, AuthenticatedUsers,
            Ace(AuthenticatedUsers, FileRights.ReadAndExecute), Ace(AuthenticatedUsers, Read)));
        c.FoldersAdded.Add(Austausch);
        c.FoldersRemoved.Add(Root + @"\Alt");
        c.NewFindings.Add(newer.Findings.First(f => f.RuleId == "ZT01"));
        c.ResolvedFindings.Add(F("ZT03", Severity.Mittel, Root + @"\Alt", Former, "Recht direkt an „ehemalig“ vergeben."));
        c.AccessChanges.Add(new AccessChange(Qc, Max, AccessLevel.Aendern, AccessLevel.Vollzugriff));
        c.AccessChanges.Add(new AccessChange(Archiv, Klaus, AccessLevel.Aendern, AccessLevel.Lesen));
        c.MembershipChanges.Add(new MembershipChange(QcTeam, Former, ChangeKind.Entfernt));
        c.MembershipChanges.Add(new MembershipChange(LaborAlle, Max, ChangeKind.Hinzugefuegt));
        return c;
    }

    public static AceEntry Ace(string sid, FileRights rights, bool inherited = false, bool deny = false) => new()
    {
        Sid = sid, Rights = rights, IsInherited = inherited, IsDeny = deny,
        Inheritance = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit,
    };

    private static readonly string[] FirstNames = ["anna", "ben", "carla", "david", "elena", "felix", "greta", "hannes", "ida", "jonas",
        "katrin", "lukas", "marie", "nils", "olga", "paul", "quirin", "rosa", "stefan", "tanja"];
    private static readonly string[] LastNames = ["müller", "schmidt", "schneider", "fischer", "weber", "meyer", "wagner", "becker",
        "schulz", "hoffmann", "schäfer", "koch", "bauer", "richter", "klein", "wolf", "schröder", "neumann", "schwarz", "zimmermann"];

    /// <summary>
    /// Wirklichkeitsnaher mittelgroßer Lauf für Layout-Tests: 300 Ordner verschiedener Tiefe (einige mit sehr langen
    /// Namen ohne Leerzeichen), 80 Personen in 16 Gruppen, breite Gruppen, direkte Rechte, verwaiste SIDs,
    /// Verweigerungen, nicht lesbare Ordner, eine Freigabe ohne lesbare Rechte. Befunde trägt der Aufrufer ein.
    /// <paramref name="changed"/> ändert einige Einträge und Mitgliedschaften – für einen Vergleich zweier Läufe.
    /// </summary>
    public static ScanResult Realistic(DateTimeOffset started, bool changed)
    {
        const string Archive = @"\\FS01\Qualitaetssicherung_und_Dokumentenlenkung_Archiv_Langzeit";
        var rnd = new Random(42); // fest: beide Läufe haben dieselbe Struktur
        var scan = SampleScan.Create(started);
        scan.Folders.Clear();
        scan.Errors.Clear();
        scan.FoldersVisited = 48_213;
        scan.FinishedAt = started.AddMinutes(17);
        scan.Request = scan.Request with { Targets = [Root, Archive] };
        scan.Shares.Add(new ShareInfo
        {
            Server = "FS01", Name = Archive[7..], LocalPath = @"E:\Shares\QS\Dokumentenlenkung\Archiv",
            Remark = "Gelenkte Dokumente (SOPs, Arbeitsanweisungen) – nur QS schreibt",
            Permissions = [new ShareAce(AuthenticatedUsers, ShareAccess.Aendern, false), new ShareAce(Administrators, ShareAccess.Vollzugriff, false)],
        });
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Scans$", LocalPath = @"E:\Scans", PermissionsReadable = false });

        string[] groupNames = ["GG-Labor-QC-Lesen", "GG-Labor-QC-Ändern", "GG-Labor-Mikrobiologie", "GG-Produktion-Schicht-A", "GG-Produktion-Schicht-B",
            "GG-QS-Dokumentenlenkung", "GG-QS-Lesen", "GG-Einkauf", "GG-Geschäftsleitung", "GG-IT-Admins", "GG-Projekt-Stabilität-2024",
            "GG-Projekt-Validierung-HPLC", "GG-Personalabteilung", "GG-Externe-Berater", "GG-Lager", "GG-Alle-Mitarbeitenden-Standort-Baden"];
        var groups = new List<string>();
        foreach (var name in groupNames)
        {
            var sid = $"{Domain}-{3000 + groups.Count}";
            scan.Principals[sid] = new Principal { Sid = sid, Name = $@"LAB\{name}", Kind = PrincipalKind.Gruppe };
            groups.Add(sid);
        }
        var users = new List<string>();
        for (int u = 0; u < 80; u++)
        {
            var sid = $"{Domain}-{4000 + u}";
            var first = FirstNames[u % FirstNames.Length];
            var last = LastNames[(u * 7 + u / 20) % LastNames.Length];
            scan.Principals[sid] = new Principal
            {
                Sid = sid, Name = $@"LAB\{first}.{last}{(u >= 40 ? (u % 10).ToString(CultureInfo.InvariantCulture) : "")}",
                DisplayName = $"{char.ToUpperInvariant(first[0])}{first[1..]} {char.ToUpperInvariant(last[0])}{last[1..]}"
                    + (u == 5 ? " (extern, Beratung Validierung Computergestützter Systeme)" : ""),
                Kind = PrincipalKind.Benutzer, IsDisabled = u % 17 == 3,
            };
            users.Add(sid);
            for (int m = 1 + rnd.Next(4); m > 0; m--) scan.Principals[groups[rnd.Next(groups.Count)]].Members.Add(sid);
            scan.Principals[groups[^1]].Members.Add(sid);
        }
        scan.Principals[groups[3]].Members.Add(groups[4]); // verschachtelt
        scan.Principals[groups[13]].MembersUnknown = changed;
        if (changed) scan.Principals[groups[1]].Members.Add(users[3]);
        const string Orphan = Domain + "-9999";
        scan.Principals[Orphan] = new Principal { Sid = Orphan, Name = Orphan, Kind = PrincipalKind.Verwaist };

        const AceInheritance CIOI = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit;
        AceEntry A(string sid, FileRights rights, bool inherited = false, bool deny = false, AceInheritance inheritance = CIOI,
            AcePropagation propagation = AcePropagation.None) =>
            new() { Sid = sid, Rights = rights, IsInherited = inherited, IsDeny = deny, Inheritance = inheritance, Propagation = propagation };

        foreach (var root in new[] { Root, Archive })
        {
            var node = new FolderNode { Path = root, ShareUnc = root, OwnerSid = Administrators, InheritanceBroken = true };
            node.Aces.AddRange([A(SampleScan.System, Full), A(Administrators, Full), A(groups[^1], Read),
                A(CreatorOwner, Full, propagation: AcePropagation.InheritOnly)]);
            scan.Folders.Add(node);
        }

        string[] areas = ["QC", "Mikrobiologie", "Produktion", "Projekte", "Validierung", "Austausch", "Archiv", "Personal"];
        string[] sub = ["Rohdaten", "Auswertungen", "Berichte", "SOPs", "Geräte", "Chargen", "Freigaben", "Prüfpläne", "Messdaten_HPLC_Agilent_1260", "Altbestand"];
        for (int i = 0; i < 300; i++)
        {
            string area = areas[i % areas.Length];
            var parts = new List<string> { area };
            for (int d = 1, depth = 1 + rnd.Next(6); d < depth; d++)
                parts.Add(d == 1 && area == "Projekte" ? $"2024-{i:D3} Stabilitätsstudie Charge {100 + i}" : sub[rnd.Next(sub.Length)] + (d > 3 ? $"_{i}" : ""));
            if (i % 37 == 0)
                parts.Add("Sehr_langer_Ordnername_ohne_Leerzeichen_der_in_keine_Spalte_passt_" + new string('x', 40) + $"_{i}");
            string root = i % 9 == 0 ? Archive : Root;
            var node = new FolderNode
            {
                Path = $@"{root}\{string.Join('\\', parts)}_{i:D3}", Depth = parts.Count, ShareUnc = root,
                OwnerSid = i % 23 == 0 ? users[i % users.Count] : Administrators, InheritanceBroken = i % 5 == 0,
            };
            scan.Folders.Add(node);
            if (i % 41 == 7)
            {
                node.Error = "Zugriff verweigert";
                scan.Errors.Add(new ScanError(node.Path, node.Error));
                continue;
            }
            for (int a = 1 + rnd.Next(4); a > 0; a--)
            {
                string sid = rnd.Next(20) switch
                {
                    0 => Everyone,
                    1 => AuthenticatedUsers,
                    2 => DomainUsers,
                    3 or 4 => users[rnd.Next(users.Count)],
                    5 => Orphan,
                    _ => groups[rnd.Next(groups.Count - 1)],
                };
                var rights = rnd.Next(4) switch { 0 => Read, 1 => Modify, 2 => Full, _ => Read | FileRights.Write };
                if (changed && i % 11 == 0) rights = Modify;
                node.Aces.Add(A(sid, rights, deny: rnd.Next(25) == 0, inheritance: rnd.Next(6) == 0 ? AceInheritance.None : CIOI));
            }
            if (changed && i % 13 == 0) node.Aces.Add(A(groups[2], Modify));
            if (!node.InheritanceBroken)
                node.Aces.AddRange([A(SampleScan.System, Full, inherited: true), A(Administrators, Full, inherited: true), A(groups[^1], Read, inherited: true)]);
        }
        scan.Errors.Add(new ScanError(@"\\FS02\Altdaten", "Der Netzwerkpfad wurde nicht gefunden."));
        return scan;
    }

    /// <summary>Großer synthetischer Lauf für Zeit- und Speicherbudgets.</summary>
    public static ScanResult Large(int folders, int acesPerFolder, int users)
    {
        var scan = SampleScan.Create();
        scan.Folders.Clear();
        var groupSids = Enumerable.Range(0, 50).Select(i => $"{Domain}-{5000 + i}").ToArray();
        foreach (var (sid, i) in groupSids.Select((s, i) => (s, i)))
            scan.Principals[sid] = new Principal { Sid = sid, Name = $@"LAB\Gruppe-{i:D2}", Kind = PrincipalKind.Gruppe };
        for (int u = 0; u < users; u++)
        {
            var sid = $"{Domain}-{10000 + u}";
            scan.Principals[sid] = new Principal { Sid = sid, Name = $@"LAB\benutzer{u:D5}", DisplayName = $"Benutzer {u}", Kind = PrincipalKind.Benutzer };
            scan.Principals[groupSids[u % groupSids.Length]].Members.Add(sid);
        }
        for (int f = 0; f < folders; f++)
        {
            var node = new FolderNode
            {
                Path = $@"\\FS01\Labor\Bereich{f / 1000:D2}\Projekt{f / 50:D4}\Ordner{f:D6}",
                Depth = 3, InheritanceBroken = f % 7 == 0, ShareUnc = Root, OwnerSid = Administrators,
            };
            for (int a = 0; a < acesPerFolder; a++)
                node.Aces.Add(Ace(groupSids[(f + a) % groupSids.Length], a % 2 == 0 ? Modify : Read, inherited: a > 1));
            scan.Folders.Add(node);
        }
        return scan;
    }
}
