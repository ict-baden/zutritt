using Zutritt.Core.Analysis;
using Zutritt.Core.Model;

namespace Zutritt.Core.Reports;

/// <summary>
/// Bereitet einen Lauf (und optional den Vergleich mit dem vorherigen) für die Berichte auf.
/// Effektive Rechte und Regeltexte kommen aus der Auswertung (<see cref="EffectiveAccess"/>,
/// <see cref="FindingRules"/>), damit Bericht und Oberfläche dieselben Zahlen zeigen. Die Befunde
/// selbst stammen aus dem Lauf – der Bericht bewertet nicht neu.
/// </summary>
internal static class ReportDataBuilder
{
    /// <summary>
    /// Obergrenze für Einzelzugriffe (Person × Ordner) aller Personen zusammen. Wenn „Domänen-Benutzer“
    /// oben lesen darf, sind das schnell Millionen – die Summen je Person bleiben trotzdem vollständig.
    /// </summary>
    public const int DefaultMaxPersonGrants = 200_000;

    // Gruppen, die praktisch alle Benutzer enthalten (siehe Spezifikation „Breite Gruppen“)
    private static readonly HashSet<string> BroadWellKnown = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-1-0", "S-1-5-11", "S-1-5-4", "S-1-5-2",
    };

    /// <param name="progress">Meldet, woran gerade gearbeitet wird (deutscher Satz für die Oberfläche).</param>
    public static ReportData Build(ScanResult scan, ComparisonResult? comparison, IProgress<string>? progress = null) =>
        Build(scan, comparison, DefaultMaxPersonGrants, progress);

    /// <summary>Mit der echten Auswertung; <paramref name="maxPersonGrants"/> nur für Tests kleiner.</summary>
    internal static ReportData Build(ScanResult scan, ComparisonResult? comparison, int maxPersonGrants, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(scan);
        // Alle Personen auf einmal: je Ordner-Klasse nur einmal gerechnet, Einzelzugriffe nur für die, die der Bericht
        // zeigt. Je Person über alle Ordner gerechnet dauerte das bei 3.000 Personen × 50.000 Ordnern drei Minuten.
        var access = new Lazy<PeopleAccess>(() => PeopleAccess.Compute(new AccessContext(scan),
            scan.Principals.Values.Where(p => p.Kind == PrincipalKind.Benutzer).Select(p => p.Sid)));
        return Build(scan, comparison, FindingRules.All, new SummaryAccess(access), DateTimeOffset.Now, maxPersonGrants, progress);
    }

    /// <summary>Variante mit austauschbarer Auswertung – für Tests ohne <see cref="EffectiveAccess"/>.</summary>
    internal static ReportData Build(ScanResult scan, ComparisonResult? comparison, IReadOnlyList<RuleInfo> rules,
        Func<string, IReadOnlyList<AccessGrant>> forPerson, DateTimeOffset createdAt, int maxPersonGrants = DefaultMaxPersonGrants)
    {
        ArgumentNullException.ThrowIfNull(forPerson);
        return Build(scan, comparison, rules, new ListAccess(forPerson), createdAt, maxPersonGrants, progress: null);
    }

    private static ReportData Build(ScanResult scan, ComparisonResult? comparison, IReadOnlyList<RuleInfo> rules,
        IPersonAccess access, DateTimeOffset createdAt, int maxPersonGrants, IProgress<string>? progress)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(rules);

        var names = new NameLookup(scan, comparison?.Older);
        var ruleMap = new Dictionary<string, RuleInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules) ruleMap.TryAdd(rule.Id, rule);

        progress?.Report("Ordner und Befunde werden aufbereitet …");
        var folders = BuildFolders(scan, names);
        var findings = BuildFindings(scan, ruleMap, names);
        progress?.Report($"Effektive Rechte von {ReportText.Number(scan.Principals.Values.Count(p => p.Kind == PrincipalKind.Benutzer))} Personen werden berechnet …");
        var people = BuildPeople(scan, access, maxPersonGrants, folders);
        progress?.Report("Gruppen und Freigaben werden aufbereitet …");
        var groups = BuildGroups(scan, names);
        var shares = BuildShares(scan, names);
        var errors = scan.Errors.OrderBy(e => e.Path, PathOrder.Instance).ToList();
        ComparisonData? changes = null;
        if (comparison is not null)
        {
            progress?.Report("Änderungen seit dem letzten Lauf werden aufbereitet …");
            changes = BuildComparison(comparison, ruleMap, names);
        }

        return new ReportData
        {
            Header = BuildHeader(scan, createdAt),
            Findings = findings,
            Rules = BuildRules(scan, ruleMap),
            Folders = folders,
            People = people,
            Groups = groups,
            Shares = shares,
            Errors = errors,
            Notes = BuildNotes(scan, folders, shares, people, maxPersonGrants, comparison),
            Comparison = changes,
        };
    }

    private static ReportHeader BuildHeader(ScanResult scan, DateTimeOffset createdAt)
    {
        IReadOnlyList<string> targets = scan.Request.Targets.Count > 0
            ? scan.Request.Targets
            : scan.Folders.Where(f => f.IsRoot).Select(f => f.Path).ToList();
        return new ReportHeader
        {
            Targets = targets,
            StartedAt = scan.StartedAt,
            FinishedAt = scan.FinishedAt,
            CreatedAt = createdAt,
            Machine = scan.Machine,
            ScannedBy = scan.ScannedBy,
            ToolVersion = ReportText.Version(scan.ToolVersion),
            ReportVersion = ReportText.ProductVersion,
            Request = scan.Request,
            FoldersVisited = scan.FoldersVisited,
            FilesVisited = scan.FilesVisited,
            FoldersStored = scan.Folders.Count(f => !f.IsFile),
            FilesStored = scan.Folders.Count(f => f.IsFile),
            Principals = scan.Principals.Count,
        };
    }

    private static List<FindingRow> BuildFindings(ScanResult scan, Dictionary<string, RuleInfo> rules, NameLookup names) =>
        scan.Findings
            .Select(f => ToRow(f, rules, names))
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path, PathOrder.Instance)
            .ThenBy(f => f.Account, ReportText.NameOrder)
            .ToList();

    private static FindingRow ToRow(Finding f, Dictionary<string, RuleInfo> rules, NameLookup names)
    {
        rules.TryGetValue(f.RuleId, out var rule);
        return new FindingRow(f.Severity, f.RuleId, rule?.Title ?? f.RuleId, f.Path,
            string.IsNullOrEmpty(f.Sid) ? "" : names.Name(f.Sid), f.Sid, f.Message, rule?.Recommendation ?? "");
    }

    private static List<RuleRow> BuildRules(ScanResult scan, Dictionary<string, RuleInfo> rules)
    {
        var counts = scan.Findings.GroupBy(f => f.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Severity: g.Max(f => f.Severity)), StringComparer.OrdinalIgnoreCase);

        var rows = rules.Values
            .Select(r => new RuleRow(r.Id, r.Severity, r.Title, r.Explanation, r.Recommendation, counts.GetValueOrDefault(r.Id).Count))
            .ToList();
        // Befunde einer Regel, die diese Version nicht kennt (z. B. aus einem neueren Lauf): trotzdem aufführen
        foreach (var (id, c) in counts)
            if (!rules.ContainsKey(id))
                rows.Add(new RuleRow(id, c.Severity, id, "", "", c.Count));
        return rows.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<FolderRow> BuildFolders(ScanResult scan, NameLookup names) =>
        scan.Folders
            .OrderBy(f => f.Path, PathOrder.Instance)
            .Select(f => new FolderRow
            {
                Path = f.Path,
                Depth = f.Depth,
                IsFile = f.IsFile,
                InheritanceBroken = f.InheritanceBroken,
                InheritanceDiffers = f.InheritanceDiffers,
                Owner = string.IsNullOrEmpty(f.OwnerSid) ? "" : names.Name(f.OwnerSid),
                OwnerSid = f.OwnerSid,
                ShareUnc = f.ShareUnc,
                Error = f.Error,
                Aces = f.Aces.Select(a => new AceRow(names.Name(a.Sid), a.Sid, names.Kind(a.Sid), a.Level, a.Rights, a.IsDeny,
                    a.IsInherited, ReportText.AppliesTo(a.Inheritance, a.Propagation, f.IsFile))).ToList(),
            })
            .ToList();

    private static List<PersonRow> BuildPeople(ScanResult scan, IPersonAccess access, int maxGrants, List<FolderRow> folders)
    {
        var users = scan.Principals.Values
            .Where(p => p.Kind == PrincipalKind.Benutzer)
            .OrderBy(p => p.Label, ReportText.NameOrder)
            .ThenBy(p => p.Sid, StringComparer.OrdinalIgnoreCase);

        // Baum-Reihenfolge der Ordner einmal bestimmen und je Person nur noch Ränge vergleichen. Der Pfadvergleich
        // (Abschnitte, deutsche Sortierung) ist teuer – je Person neu sortiert kostete er bei 1.000 Personen ×
        // 5.000 Ordnern rund 20 s. Pfade außerhalb der gespeicherten Ordner landen am Ende, untereinander sortiert.
        var rank = new Dictionary<string, int>(folders.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders) rank.TryAdd(folder.Path, rank.Count);

        var rows = new List<PersonRow>();
        int budget = Math.Max(0, maxGrants);
        foreach (var user in users)
        {
            var summary = access.Summary(user.Sid);

            // Erst sammeln, dann einmal sortieren – der deutsche Namensvergleich ist je Zugriff zu teuer
            var via = new SortedSet<string>(summary.Via.Where(n => !string.IsNullOrWhiteSpace(n)), ReportText.NameOrder);

            // Einzelzugriffe nur für ganze Personen übernehmen – halbe Listen wären irreführend. Erst dann anlegen:
            // Bei großen Läufen sind es zig Millionen, der Bericht zeigt davon höchstens maxGrants.
            bool keep = summary.Folders <= budget;
            List<PersonGrantRow> grantRows = [];
            if (keep)
            {
                budget -= summary.Folders;
                grantRows = access.Grants(user.Sid)
                    .Where(g => g.Effective > AccessLevel.Keine)
                    .OrderBy(g => rank.TryGetValue(g.Path, out var r) ? r : int.MaxValue)
                    .ThenBy(g => g.Path, PathOrder.Instance)
                    .Select(g => new PersonGrantRow(g.Path, g.Effective, g.Ntfs, g.ViaShare,
                        g.Via.Count == 0 ? "direkt" : string.Join(" → ", g.Via), g.ReducedByDeny))
                    .ToList();
            }

            rows.Add(new PersonRow
            {
                Name = user.Label,
                DisplayName = user.DisplayName,
                Sid = user.Sid,
                IsDisabled = user.IsDisabled,
                FolderCount = summary.Folders,
                Highest = summary.Highest,
                ViaGroups = via.ToList(),
                HasDirect = summary.HasDirect,
                Grants = grantRows,
                GrantsComplete = keep,
            });
        }
        return rows;
    }

    /// <summary>Zugriffe je Person: Summen für alle, Einzelzugriffe nur für die, die der Bericht aufführt.</summary>
    private interface IPersonAccess
    {
        PersonAccessSummary Summary(string sid);
        IReadOnlyList<AccessGrant> Grants(string sid);
    }

    /// <summary>Die echte Auswertung über alle Personen auf einmal (<see cref="PeopleAccess"/>).</summary>
    private sealed class SummaryAccess(Lazy<PeopleAccess> access) : IPersonAccess
    {
        public PersonAccessSummary Summary(string sid) => access.Value.Summary(sid);
        public IReadOnlyList<AccessGrant> Grants(string sid) => access.Value.Grants(sid);
    }

    /// <summary>Austauschbare Auswertung je Person (Tests): Summen aus der vollständigen Liste.</summary>
    private sealed class ListAccess(Func<string, IReadOnlyList<AccessGrant>> forPerson) : IPersonAccess
    {
        private string? _sid;
        private List<AccessGrant> _grants = [];

        public PersonAccessSummary Summary(string sid)
        {
            var grants = Load(sid);
            var via = new HashSet<string>(grants.SelectMany(g => g.Via), StringComparer.Ordinal);
            return new PersonAccessSummary(grants.Count, grants.Count == 0 ? AccessLevel.Keine : grants.Max(g => g.Effective),
                via, grants.Any(g => g.Via.Count == 0));
        }

        public IReadOnlyList<AccessGrant> Grants(string sid) => Load(sid);

        private List<AccessGrant> Load(string sid)
        {
            if (_sid != sid)
            {
                // Defensiv: nur echte Zugriffe zählen, auch falls die Auswertung „Kein Zugriff“ mitliefert
                _grants = (forPerson(sid) ?? []).Where(g => g.Effective > AccessLevel.Keine).ToList();
                _sid = sid;
            }
            return _grants;
        }
    }

    private static List<GroupRow> BuildGroups(ScanResult scan, NameLookup names)
    {
        // Wie oft steht eine SID selbst in einer Ordner-ACL (je Ordner einmal gezählt)
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in scan.Folders)
            foreach (var sid in folder.Aces.Select(a => a.Sid).Distinct(StringComparer.OrdinalIgnoreCase))
                used[sid] = used.GetValueOrDefault(sid) + 1;

        return scan.Principals.Values
            .Where(p => p.Kind == PrincipalKind.Gruppe)
            .OrderBy(p => p.Label, ReportText.NameOrder)
            .ThenBy(p => p.Sid, StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildGroup(scan, g, names, used.GetValueOrDefault(g.Sid)))
            .ToList();
    }

    /// <summary>Mitglieder transitiv zählen – iterativ und mit „schon gesehen“, damit Zyklen (A ∈ B ∈ A) enden.</summary>
    private static GroupRow BuildGroup(ScanResult scan, Principal group, NameLookup names, int usedInFolders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { group.Sid };
        var people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var broad = new SortedSet<string>(ReportText.NameOrder);
        var unreadable = new SortedSet<string>(ReportText.NameOrder);
        var queue = new Queue<string>(group.Members);

        while (queue.Count > 0)
        {
            var sid = queue.Dequeue();
            if (!seen.Add(sid)) continue;
            if (!scan.Principals.TryGetValue(sid, out var member)) continue;
            switch (member.Kind)
            {
                case PrincipalKind.Benutzer:
                    people.Add(sid);
                    break;
                case PrincipalKind.Bekannt when BroadWellKnown.Contains(sid):
                    broad.Add(member.Label);
                    break;
                case PrincipalKind.Gruppe:
                    if (member.MembersUnknown) unreadable.Add(member.Label);
                    foreach (var m in member.Members) queue.Enqueue(m);
                    break;
            }
        }

        var notes = new List<string>();
        if (group.MembersUnknown) notes.Add("Mitglieder nicht lesbar");
        if (unreadable.Count > 0)
            notes.Add($"Mitglieder von {string.Join(", ", unreadable.Select(ReportText.Quote))} nicht lesbar – Anzahl unvollständig");
        if (broad.Count > 0)
            notes.Add($"enthält {string.Join(", ", broad.Select(ReportText.Quote))} – damit praktisch alle Benutzer");

        return new GroupRow
        {
            Name = group.Label,
            Sid = group.Sid,
            DirectCount = group.Members.Count,
            DirectMembers = group.Members.Select(names.Name).OrderBy(n => n, ReportText.NameOrder).ToList(),
            TotalPeople = people.Count,
            Complete = !group.MembersUnknown && unreadable.Count == 0,
            UsedInFolders = usedInFolders,
            Note = string.Join("; ", notes),
        };
    }

    private static List<ShareRow> BuildShares(ScanResult scan, NameLookup names) =>
        scan.Shares
            .OrderBy(s => s.UncPath, PathOrder.Instance)
            .Select(s =>
            {
                var aces = s.Permissions.Select(a => new ShareAceRow(names.Name(a.Sid), a.Sid, a.Access, a.IsDeny)).ToList();
                string text = !s.PermissionsReadable ? "nicht lesbar"
                    : aces.Count == 0 ? "keine Einträge – kein Zugriff über das Netz"
                    : string.Join("; ", aces.Select(a => $"{a.Account}: {a.Right}"));
                return new ShareRow
                {
                    Unc = s.UncPath,
                    LocalPath = s.LocalPath,
                    Remark = s.Remark,
                    IsAdminShare = s.IsAdminShare,
                    Readable = s.PermissionsReadable,
                    Permissions = aces,
                    PermissionsText = text,
                };
            })
            .ToList();

    private static List<ReportNote> BuildNotes(ScanResult scan, List<FolderRow> folders, List<ShareRow> shares,
        List<PersonRow> people, int maxGrants, ComparisonResult? comparison)
    {
        var warnings = new List<string>();
        var request = scan.Request;

        var unreadable = folders.Where(f => f.Error is not null).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (unreadable.Count > 0)
            warnings.Add($"{ReportText.Number(unreadable.Count)} Ordner nicht lesbar – dort konnte nichts geprüft werden.");
        int otherErrors = scan.Errors.Count(e => !unreadable.Contains(e.Path));
        if (otherErrors > 0)
        {
            // „weitere“ nur, wenn schon nicht lesbare Ordner genannt sind
            var more = unreadable.Count > 0;
            warnings.Add(otherErrors == 1
                ? $"1 {(more ? "weiterer " : "")}Fehler beim Lesen (z. B. Server oder Freigabe nicht erreichbar)."
                : $"{ReportText.Number(otherErrors)} {(more ? "weitere " : "")}Fehler beim Lesen (z. B. Server oder Freigaben nicht erreichbar).");
        }

        int sharesUnreadable = shares.Count(s => !s.Readable);
        if (sharesUnreadable > 0)
            warnings.Add($"Bei {Count(sharesUnreadable, "Freigabe", "Freigaben")} waren die Freigabe-Berechtigungen nicht lesbar – "
                + "der Zugriff ist dort nur nach den NTFS-Rechten bewertet.");

        if (request.MaxDepth is { } depth)
            warnings.Add(depth <= 0
                ? "Nur die Wurzeln der Ziele wurden geprüft (Tiefe 0) – Unterordner sind nicht erfasst."
                : $"Die Tiefe war auf {Count(depth, "Ebene", "Ebenen")} begrenzt – tiefer liegende Ordner sind nicht erfasst.");

        if (!request.ResolveGroups)
            warnings.Add("Gruppen wurden nicht bis zur Person aufgelöst – die Personen-Sicht ist unvollständig.");

        int groupsUnreadable = scan.Principals.Values.Count(p => p.Kind == PrincipalKind.Gruppe && p.MembersUnknown);
        if (groupsUnreadable > 0)
            warnings.Add($"Bei {Count(groupsUnreadable, "Gruppe", "Gruppen")} konnten die Mitglieder nicht gelesen werden – "
                + "Personen darin fehlen in der Personen-Sicht.");

        int truncated = people.Count(p => !p.GrantsComplete);
        if (truncated > 0)
            warnings.Add($"Die Einzelzugriffe je Person sind auf {ReportText.Number(maxGrants)} Zeilen begrenzt; "
                + $"für {Count(truncated, "Person", "Personen")} sind nur die Summen enthalten.");

        // Vergleich mit anderem Umfang: zuerst, denn er relativiert alle Zahlen der Änderungen
        if (comparison is { ScopeDiffers: true }) warnings.Insert(0, ScanScope.Describe(comparison));

        var notes = warnings.Select(w => new ReportNote(NoteKind.Warnung, w)).ToList();
        notes.Add(new ReportNote(NoteKind.Hinweis,
            "Aufgeführt sind die Wurzeln der Ziele und alle Ordner mit eigenen Rechten (explizite Einträge, unterbrochene oder "
            + "abweichende Vererbung). Alle übrigen Ordner erben ihre Rechte vom nächsthöheren aufgeführten Ordner."));
        notes.Add(new ReportNote(NoteKind.Hinweis,
            "Effektives Recht = alle erlaubten Rechte der Person und ihrer Gruppen zusammen, abzüglich verweigerter Rechte."));
        if (shares.Count > 0 || scan.Folders.Any(f => f.ShareUnc is not null))
            notes.Add(new ReportNote(NoteKind.Hinweis,
                "Über das Netz gilt jeweils die strengere von Freigabe- und NTFS-Berechtigung; „Effektiv“ berücksichtigt beides."));
        return notes;
    }

    private static ComparisonData BuildComparison(ComparisonResult c, Dictionary<string, RuleInfo> rules, NameLookup names)
    {
        var rows = new List<ChangeRow>();
        var isFile = c.Newer.Folders.Concat(c.Older.Folders)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().IsFile, StringComparer.OrdinalIgnoreCase);

        string Describe(Finding f)
        {
            var title = rules.TryGetValue(f.RuleId, out var r) ? r.Title : f.RuleId;
            return $"{f.RuleId} {title} ({ReportText.Severity(f.Severity)}): {f.Message}";
        }

        foreach (var f in c.NewFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId).ThenBy(f => f.Path, PathOrder.Instance))
            rows.Add(new ChangeRow(ChangeArea.Finding, ChangeText.New, f.Path, Account(f.Sid), "", Describe(f)));
        foreach (var f in c.ResolvedFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId).ThenBy(f => f.Path, PathOrder.Instance))
            rows.Add(new ChangeRow(ChangeArea.Finding, ChangeText.Resolved, f.Path, Account(f.Sid), Describe(f), ""));

        foreach (var a in c.AceChanges.OrderBy(a => a.Path, PathOrder.Instance).ThenBy(a => names.Name(a.Sid), ReportText.NameOrder))
        {
            bool file = isFile.GetValueOrDefault(a.Path);
            // Gleiche Stufe, andere Maske (z. B. Sonderrechte): Maske zeigen, sonst sähe es nach „keine Änderung“ aus
            bool mask = a.Before is not null && a.After is not null && a.Before.Level == a.After.Level;
            rows.Add(new ChangeRow(ChangeArea.Ace, ReportText.Change(a.Kind), a.Path, names.Name(a.Sid),
                a.Before is null ? "" : ReportText.Ace(a.Before, file, mask),
                a.After is null ? "" : ReportText.Ace(a.After, file, mask)));
        }

        foreach (var m in c.MembershipChanges.OrderBy(m => names.Name(m.GroupSid), ReportText.NameOrder).ThenBy(m => names.Name(m.MemberSid), ReportText.NameOrder))
            rows.Add(new ChangeRow(ChangeArea.Membership, ChangeText.Membership(m.Kind),
                names.Name(m.GroupSid), names.Name(m.MemberSid),
                m.Kind == ChangeKind.Hinzugefuegt ? "" : "Mitglied", m.Kind == ChangeKind.Entfernt ? "" : "Mitglied"));

        foreach (var p in c.FoldersAdded.OrderBy(p => p, PathOrder.Instance))
            rows.Add(new ChangeRow(ChangeArea.Folder, ChangeText.FolderAdded, p, "", "", "eigene Rechte"));
        foreach (var p in c.FoldersRemoved.OrderBy(p => p, PathOrder.Instance))
            rows.Add(new ChangeRow(ChangeArea.Folder, ChangeText.FolderRemoved, p, "", "eigene Rechte", ""));

        // Folgen für Personen zuletzt, „Mehr Zugriff“ (das Risiko) vor „Weniger Zugriff“
        foreach (var a in c.AccessChanges.OrderBy(a => a.After > a.Before ? 0 : 1).ThenBy(a => a.Path, PathOrder.Instance)
                     .ThenBy(a => names.Name(a.Sid), ReportText.NameOrder))
            rows.Add(new ChangeRow(ChangeArea.Access, a.After > a.Before ? ChangeText.MoreAccess : ChangeText.LessAccess, a.Path, names.Name(a.Sid),
                Rights.Label(a.Before), Rights.Label(a.After)));

        return new ComparisonData
        {
            OlderStartedAt = c.Older.StartedAt,
            ScopeNote = ScanScope.Describe(c),
            NewFindings = c.NewFindings.Count,
            ResolvedFindings = c.ResolvedFindings.Count,
            MoreAccess = c.AccessChanges.Count(a => a.After > a.Before),
            LessAccess = c.AccessChanges.Count(a => a.After < a.Before),
            AceChanges = c.AceChanges.Count,
            FoldersAdded = c.FoldersAdded.Count,
            FoldersRemoved = c.FoldersRemoved.Count,
            MembershipChanges = c.MembershipChanges.Count,
            Rows = rows,
        };

        string Account(string? sid) => string.IsNullOrEmpty(sid) ? "" : names.Name(sid);
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{ReportText.Number(n)} {many}";

    /// <summary>
    /// Namen zu SIDs: zuerst aus dem aktuellen Lauf, dann aus dem älteren (gelöschte Konten kennt nur
    /// noch der), dann der deutsche Name bekannter Prinzipale (BESITZERRECHTE u. a. stehen nicht immer im
    /// Lauf), sonst die SID selbst – nie leer, damit im Bericht immer etwas Nachprüfbares steht.
    /// </summary>
    private sealed class NameLookup(ScanResult primary, ScanResult? secondary)
    {
        public string Name(string sid)
        {
            if (primary.Principals.TryGetValue(sid, out var p)) return p.Label;
            if (secondary is not null && secondary.Principals.TryGetValue(sid, out var o)) return o.Label;
            return SidCatalog.GermanName(sid) ?? sid;
        }

        public PrincipalKind Kind(string sid) =>
            primary.Principals.TryGetValue(sid, out var p) ? p.Kind
            : SidCatalog.GermanName(sid) is not null || SidCatalog.IsSpecialIdentity(sid) ? PrincipalKind.Bekannt
            : PrincipalKind.Unbekannt;
    }
}
