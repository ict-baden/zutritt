using System.Security.AccessControl;
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
            Ace(AuthenticatedUsers, FileSystemRights.ReadAndExecute), Ace(AuthenticatedUsers, Read)));
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

    public static AceEntry Ace(string sid, FileSystemRights rights, bool inherited = false, bool deny = false) => new()
    {
        Sid = sid, Rights = rights, IsInherited = inherited, IsDeny = deny,
        Inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
    };

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
