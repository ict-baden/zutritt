// SPDX-License-Identifier: MIT
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;

namespace Zutritt.Demo;

/// <summary>
/// Großer erfundener Lauf für Messungen (verstecktes <c>--messen</c>): Fileserver \\FS02 eines Unternehmens mit
/// einstellbar vielen gespeicherten Ordnern (Standard 50.000) und Personen (Standard 3.000) – Abteilungen,
/// Projekte, Home-Ordner, ein tiefes Archiv und ein Austausch-Ordner. Dazu ein älterer Lauf mit kleinen
/// Unterschieden für den Vergleich. Immer gleich (fester Startwert), Befunde aus den echten Regeln.
/// </summary>
internal static class LargeDemo
{
    private const string Server = "FS02";
    private const string Domain = "S-1-5-21-1844674407-3709551615-2718281828";
    private const string Everyone = "S-1-1-0";
    private const string AuthUsers = "S-1-5-11";
    private const string Interactive = "S-1-5-4";
    private const string System = "S-1-5-18";
    private const string CreatorOwner = "S-1-3-0";
    private const string Administrators = "S-1-5-32-544";
    private const string BuiltinUsers = "S-1-5-32-545";
    private const string DomainAdmins = Domain + "-512";
    private const string DomainUsers = Domain + "-513";

    private const FileRights Read = FileRights.ReadAndExecute | FileRights.Synchronize;
    private const FileRights Modify = FileRights.Modify | FileRights.Synchronize;
    private const FileRights Full = FileRights.FullControl;
    private const FileRights DeleteOnly = FileRights.Delete | FileRights.DeleteSubdirectoriesAndFiles;
    private const AceInheritance CIOI = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit;

    private static readonly string[] Departments =
        ["Vertrieb", "Einkauf", "Entwicklung", "Produktion", "Qualität", "Labor", "Personal", "Finanzen", "IT", "Logistik", "Marketing", "Geschäftsleitung"];
    private static readonly string[] Areas =
        ["Allgemein", "Berichte", "Planung", "Vorlagen", "Verträge", "Schulungen", "Audits", "Kunden", "Lieferanten", "Budget", "Protokolle", "Richtlinien"];
    private static readonly string[] Topics =
        ["Angebote", "Auswertungen", "Besprechungen", "Dokumentation", "Entwürfe", "Freigaben", "Korrespondenz", "Messdaten", "Präsentationen",
         "Prüfberichte", "Rechnungen", "Statistik", "Unterlagen", "Zeichnungen", "Zertifikate", "Checklisten"];
    private static readonly string[] ProjectParts = ["Verträge", "Angebote", "Messdaten", "Freigaben"];
    /// <summary>Älterer Lauf (eine Woche vorher) und aktueller Lauf, beide noch ohne Id.</summary>
    public static (ScanResult Older, ScanResult Newer) Create(int folders, int people)
    {
        folders = Math.Max(1_000, folders);
        people = Math.Max(50, people);
        var started = new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.FromHours(2));
        var newer = new Builder(folders, people, started).Build();
        var older = new Builder(folders, people, started.AddDays(-7)).Build();
        MakeOlder(older);
        newer.Findings.AddRange(FindingRules.Evaluate(newer));
        older.Findings.AddRange(FindingRules.Evaluate(older));
        return (older, newer);
    }

    /// <summary>Kleine, feste Unterschiede: ein paar Ordner neu, ein paar Rechte geändert, Mitgliedschaften verschoben.</summary>
    private static void MakeOlder(ScanResult older)
    {
        older.Folders.RemoveAll(f => !f.IsRoot && Pick(f.Path, 8));
        foreach (var f in older.Folders.Where(f => Pick(f.Path + "|recht", 5)))
        {
            int i = f.Aces.FindIndex(a => !a.IsInherited && !a.IsDeny);
            if (i >= 0) f.Aces[i] = f.Aces[i] with { Rights = f.Aces[i].Rights == Read ? Modify : Read };
        }
        foreach (var g in older.Principals.Values.Where(p => p.Kind == PrincipalKind.Gruppe && p.Members.Count > 3 && Pick(p.Sid, 60)))
            g.Members.RemoveAt(g.Members.Count - 1);
        older.FoldersVisited -= older.FoldersVisited / 200;
    }

    /// <summary>Stabile Auswahl von etwa <paramref name="permille"/> Promille – unabhängig von der Reihenfolge der Erzeugung.</summary>
    private static bool Pick(string key, int permille)
    {
        uint h = 2166136261;
        foreach (char c in key) h = (h ^ char.ToUpperInvariant(c)) * 16777619;
        return h % 1000 < permille;
    }

    private sealed class Builder(int folderTarget, int peopleTarget, DateTimeOffset started)
    {
        private readonly Random _rng = new(35);
        private readonly ScanResult _scan = new()
        {
            StartedAt = started,
            FinishedAt = started.AddMinutes(41).AddSeconds(17),
            Machine = "ADMIN-PC12",
            ScannedBy = @"KONZERN\adm.zutritt",
            ToolVersion = AppInfo.Version,
            Request = new ScanRequest { Targets = [WindowsPath.Server(Server)] },
        };
        private readonly List<string> _users = [];
        private readonly List<string> _disabledUsers = [];
        private readonly List<string> _orphans = [];
        private readonly Dictionary<string, (string All, string Lead, string Readers)> _departments = [];
        private int _rid = 2000;

        public ScanResult Build()
        {
            CreatePrincipals();
            CreateShares();
            int home = Math.Min(peopleTarget, folderTarget * 6 / 100);
            int exchange = Math.Max(20, folderTarget * 4 / 1000);
            int projects = folderTarget * 40 / 100;
            int archive = folderTarget * 10 / 100;
            int departments = folderTarget - home - exchange - projects - archive - 5;

            Home(home);
            Exchange(exchange);
            Projects(projects);
            Archive(archive);
            DepartmentShare(departments);
            _scan.FoldersVisited = _scan.Folders.Count * 12L + 4_711;
            return _scan;
        }

        // ================= Konten =================

        private void CreatePrincipals()
        {
            Known(Everyone, "Jeder");
            Known(AuthUsers, @"NT-AUTORITÄT\Authentifizierte Benutzer");
            Known(Interactive, @"NT-AUTORITÄT\INTERAKTIV");
            Known(System, @"NT-AUTORITÄT\SYSTEM");
            Known(CreatorOwner, "ERSTELLER-BESITZER");

            // Durchnummerierte Konten statt Personennamen – auch die Messdaten nennen keine Personen
            for (int i = 0; i < peopleTarget; i++)
            {
                var number = (i + 1).ToString("00000", global::System.Globalization.CultureInfo.InvariantCulture);
                var sid = NextSid();
                bool disabled = _rng.Next(100) < 3;
                _scan.Principals[sid] = new Principal
                {
                    Sid = sid, Name = @"KONZERN\benutzer" + number, DisplayName = "Benutzer " + number, Kind = PrincipalKind.Benutzer, IsDisabled = disabled,
                };
                _users.Add(sid);
                if (disabled) _disabledUsers.Add(sid);
            }
            var admin = NextSid();
            _scan.Principals[admin] = new Principal { Sid = admin, Name = @"KONZERN\adm.zutritt", DisplayName = "Zutritt-Administration", Kind = PrincipalKind.Benutzer };

            Group(DomainAdmins, @"KONZERN\Domänen-Admins", admin);
            Group(DomainUsers, @"KONZERN\Domänen-Benutzer", [.. _users, admin]);
            Group(Administrators, @"VORDEFINIERT\Administratoren", DomainAdmins);
            Group(BuiltinUsers, @"VORDEFINIERT\Benutzer", Interactive, AuthUsers, DomainUsers);

            // Abteilungen: jede Person in genau einer, Leitung und Lesegruppe dazu
            var perDepartment = _users.GroupBy(_ => Departments[_rng.Next(Departments.Length)]).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var d in Departments)
            {
                var members = perDepartment.GetValueOrDefault(d) ?? [];
                var all = Group(NextSid(), $@"KONZERN\{d}-Alle", [.. members]);
                var lead = Group(NextSid(), $@"KONZERN\{d}-Leitung", [.. members.Take(3)]);
                var readers = Group(NextSid(), $@"KONZERN\{d}-Lesen");
                _departments[d] = (all, lead, readers);
            }
            // Lesegruppen enthalten andere Abteilungen (verschachtelt)
            foreach (var d in Departments)
            {
                var readers = _scan.Principals[_departments[d].Readers];
                for (int k = 0; k < 3; k++) readers.Members.Add(_departments[Departments[_rng.Next(Departments.Length)]].All);
                readers.Members.Add(_departments["Geschäftsleitung"].All);
            }
            // Leere Gruppen (Regel ZT11) und gelöschte Konten (ZT04)
            for (int i = 0; i < 8; i++) Group(NextSid(), $@"KONZERN\Alt-Projekt-{2010 + i}");
            for (int i = 0; i < 40; i++)
            {
                var sid = NextSid();
                _scan.Principals[sid] = new Principal { Sid = sid, Name = sid, Kind = PrincipalKind.Verwaist };
                _orphans.Add(sid);
            }
        }

        private string NextSid() => $"{Domain}-{_rid++}";


        private void Known(string sid, string name) =>
            _scan.Principals[sid] = new Principal { Sid = sid, Name = name, DisplayName = AccountName.LastPart(name), Kind = PrincipalKind.Bekannt };

        private string Group(string sid, string name, params string[] members)
        {
            var g = new Principal { Sid = sid, Name = name, DisplayName = AccountName.LastPart(name), Kind = PrincipalKind.Gruppe, IsLocal = name.StartsWith("VORDEFINIERT", StringComparison.Ordinal) };
            g.Members.AddRange(members);
            _scan.Principals[sid] = g;
            return sid;
        }

        private string RandomUser() => _users[_rng.Next(_users.Count)];

        // ================= Freigaben =================

        private void CreateShares()
        {
            void Share(string name, string path, string remark, params ShareAce[] permissions) =>
                _scan.Shares.Add(new ShareInfo { Server = Server, Name = name, LocalPath = path, Remark = remark, Permissions = [.. permissions] });
            Share("Abteilungen", @"D:\Freigaben\Abteilungen", "Ablage der Abteilungen", new ShareAce(Everyone, ShareAccess.Vollzugriff, false));
            Share("Projekte", @"D:\Freigaben\Projekte", "Kundenprojekte", new ShareAce(AuthUsers, ShareAccess.Aendern, false), new ShareAce(Administrators, ShareAccess.Vollzugriff, false));
            Share("Home", @"E:\Home", "Persönliche Ordner", new ShareAce(AuthUsers, ShareAccess.Vollzugriff, false));
            Share("Archiv", @"F:\Archiv", "Abgeschlossene Vorgänge", new ShareAce(Everyone, ShareAccess.Lesen, false), new ShareAce(Administrators, ShareAccess.Vollzugriff, false));
            Share("Austausch", @"D:\Freigaben\Austausch", "Für alle", new ShareAce(Everyone, ShareAccess.Vollzugriff, false));
        }

        // ================= Ordner =================

        /// <summary>Gespeicherter Ordner; erbt die vererbbaren Einträge des Elternordners, wenn die Vererbung nicht unterbrochen ist.</summary>
        private FolderNode Folder(FolderNode? parent, string name, bool broken, string owner, params AceEntry[] explicitAces)
        {
            var path = parent is null ? WindowsPath.Unc(Server, name) : WindowsPath.Join(parent.Path, name);
            var share = parent is null ? path : parent.ShareUnc;
            var node = new FolderNode
            {
                Path = path, Depth = parent is null ? 0 : parent.Depth + 1, InheritanceBroken = parent is null || broken, ShareUnc = share, OwnerSid = owner,
            };
            node.Aces.AddRange(explicitAces);
            if (parent is not null && !broken)
                foreach (var ace in parent.Aces)
                    if ((ace.Inheritance & AceInheritance.ContainerInherit) != 0)
                        node.Aces.Add(ace.IsInherited ? ace : ace with { IsInherited = true, Propagation = AcePropagation.None });
            _scan.Folders.Add(node);
            return node;
        }

        private void Unreadable(FolderNode parent, string name, string error)
        {
            var path = WindowsPath.Join(parent.Path, name);
            _scan.Errors.Add(new ScanError(path, error));
            _scan.Folders.Add(new FolderNode { Path = path, Depth = parent.Depth + 1, ShareUnc = parent.ShareUnc, Error = error });
        }

        private FolderNode Root(string share, params AceEntry[] extra) =>
            Folder(null, share, broken: true, Administrators, [A(System, Full), A(Administrators, Full), A(CreatorOwner, Full, inheritOnly: true), .. extra]);

        private void Home(int count)
        {
            var root = Root("Home", A(DomainUsers, FileRights.ReadData | FileRights.Synchronize, inheritance: AceInheritance.None));
            foreach (var user in _users.Take(count))
            {
                var account = AccountName.LastPart(_scan.Principals[user].Name);
                // Meist legt ein Skript den Ordner an (Besitzer Administratoren), manchmal die Person selbst
                var owner = _rng.Next(10) == 0 ? user : Administrators;
                Folder(root, account, broken: false, owner, A(user, Modify));
            }
        }

        private void Exchange(int count)
        {
            var root = Root("Austausch", A(DomainUsers, Modify));
            for (int i = 0; i < count; i++)
            {
                var topic = $"{Topics[i % Topics.Length]}-{i / Topics.Length + 1:00}";
                switch (i % 3)
                {
                    case 0: Folder(root, topic, broken: false, RandomUser(), A(Everyone, Modify)); break;
                    case 1: Folder(root, topic, broken: true, Administrators, A(System, Full), A(Administrators, Full), A(AuthUsers, Read)); break;
                    default: Folder(root, topic, broken: false, Administrators, A(_departments[Departments[i % Departments.Length]].All, Modify)); break;
                }
            }
        }

        private void Projects(int count)
        {
            var root = Root("Projekte", A(DomainUsers, Read, inheritance: AceInheritance.None));
            int perProject = 1 + ProjectParts.Length;
            int projects = Math.Max(1, count / perProject);
            const int FirstYear = 2014, Years = 13;
            var years = Enumerable.Range(FirstYear, Years).Select(y => Folder(root, y.ToString(global::System.Globalization.CultureInfo.InvariantCulture), broken: false, Administrators)).ToList();
            for (int p = 0; p < projects; p++)
            {
                int yearIndex = p * Years / projects;
                var year = years[yearIndex];
                int number = p - yearIndex * projects / Years + 1;
                var name = $"P{FirstYear + yearIndex}-{number:000} {Topics[_rng.Next(Topics.Length)]}";
                var dept = _departments[Departments[_rng.Next(Departments.Length)]];
                string team = dept.All;
                if (yearIndex >= Years - 5)
                {
                    // Neuere Projekte haben eine eigene Teamgruppe
                    var members = Enumerable.Range(0, 4 + _rng.Next(9)).Select(_ => RandomUser()).Distinct().ToArray();
                    team = Group(NextSid(), $@"KONZERN\P{FirstYear + yearIndex}-{number:000}-Team", members);
                }
                var explicitAces = new List<AceEntry> { A(team, Modify) };
                if (_rng.Next(40) == 0) explicitAces.Add(A(RandomUser(), Modify));           // direkt an Person (ZT03)
                if (_rng.Next(150) == 0) explicitAces.Add(A(team, Full));                    // Vollzugriff (ZT05)
                var project = Folder(year, name, broken: false, _rng.Next(25) == 0 ? RandomUser() : Administrators, [.. explicitAces]);

                Folder(project, ProjectParts[0], broken: true, Administrators,
                    A(System, Full), A(Administrators, Full), A(_departments["Vertrieb"].Lead, Modify), A(team, Read));
                Folder(project, ProjectParts[1], broken: false, Administrators, A(_departments["Vertrieb"].All, Modify));
                var data = Folder(project, ProjectParts[2], broken: false, Administrators, A(_departments["Labor"].All, Modify));
                if (_rng.Next(80) == 0) data.Aces.Insert(0, A(team, DeleteOnly, deny: true));      // Verweigern (ZT07)
                Folder(project, ProjectParts[3], broken: false, Administrators, A(_departments["Qualität"].Lead, Modify));
            }
        }

        private void Archive(int count)
        {
            var root = Root("Archiv", A(DomainUsers, Read));
            var years = new Dictionary<int, FolderNode>();
            int made = 0;
            for (int year = 2000; made < count; year = year >= 2013 ? 2000 : year + 1)
            {
                if (!years.TryGetValue(year, out var y))
                {
                    years[year] = y = Folder(root, year.ToString(global::System.Globalization.CultureInfo.InvariantCulture), broken: false, Administrators, A(_departments["Geschäftsleitung"].All, Read));
                    made++;
                }
                foreach (var d in Departments)
                {
                    if (made >= count) break;
                    // Lange Ketten: jede Ebene mit eigenen Rechten, ab und zu tief unterbrochene Vererbung (ZT06)
                    var current = Folder(y, $"{d}-{_rng.Next(1000):000}", broken: false, Administrators, A(_departments[d].Readers, Read));
                    made++;
                    int depth = 3 + _rng.Next(12);
                    for (int level = 0; level < depth && made < count; level++, made++)
                    {
                        bool broken = _rng.Next(25) == 0;
                        var aces = broken
                            ? new[] { A(System, Full), A(Administrators, Full), A(_departments[d].Lead, Read) }
                            : _rng.Next(60) == 0 ? new[] { A(_orphans[_rng.Next(_orphans.Count)], Read) }
                            : new[] { A(_departments[d].All, Read) };
                        current = Folder(current, $"{Topics[_rng.Next(Topics.Length)]} {level + 1}", broken, Administrators, aces);
                    }
                }
            }
        }

        private void DepartmentShare(int count)
        {
            var root = Root("Abteilungen");
            int made = 0, unreadable = Math.Max(5, count / 400);
            int perDepartment = Math.Max(10, count / Departments.Length);
            foreach (var d in Departments)
            {
                var (all, lead, readers) = _departments[d];
                var dept = Folder(root, d, broken: true, Administrators,
                    A(System, Full), A(Administrators, Full), A(CreatorOwner, Full, inheritOnly: true),
                    A(all, Modify), A(lead, d is "IT" or "Geschäftsleitung" ? Full : Modify), A(readers, Read));
                made++;
                int budget = Math.Min(perDepartment, count - made);
                if (d == "Vertrieb")
                {
                    // Ein sehr breiter Ordner: ein Unterordner je Kunde, jeder mit eigenen Rechten
                    var customers = Folder(dept, "Kundenakten", broken: false, Administrators, A(_departments["Einkauf"].Readers, Read));
                    int n = budget * 2 / 3;
                    for (int i = 0; i < n; i++)
                        Folder(customers, $"K{i + 10001} Kunde", broken: false, Administrators, A(lead, Full));
                    made += n + 1;
                    budget -= n + 1;
                }
                for (int a = 0; a < Areas.Length && budget > 0; a++)
                {
                    var area = Folder(dept, Areas[a], broken: _rng.Next(4) == 0, Administrators,
                        _rng.Next(4) == 0 ? [A(System, Full), A(Administrators, Full), A(lead, Modify)] : [A(readers, Read)]);
                    made++;
                    budget--;
                    int topics = budget / (Areas.Length - a);
                    for (int t = 0; t < topics; t++, made++, budget--)
                    {
                        if (unreadable > 0 && _rng.Next(200) == 0)
                        {
                            Unreadable(area, $"{Topics[t % Topics.Length]} {t / Topics.Length + 1} (gesperrt)", "Zugriff verweigert");
                            unreadable--;
                            continue;
                        }
                        var ace = _rng.Next(30) == 0 ? A(RandomUser(), Modify)
                            : _rng.Next(200) == 0 ? A(_disabledUsers.Count > 0 ? _disabledUsers[_rng.Next(_disabledUsers.Count)] : RandomUser(), Read)
                            : _rng.Next(300) == 0 ? A(DomainUsers, Modify)
                            : A(_departments[Departments[_rng.Next(Departments.Length)]].Readers, Read);
                        Folder(area, $"{Topics[t % Topics.Length]} {t / Topics.Length + 1}", broken: false, Administrators, ace);
                    }
                }
            }
        }

        private static AceEntry A(string sid, FileRights rights, bool deny = false, bool inheritOnly = false,
            AceInheritance inheritance = CIOI) => new()
        {
            Sid = sid, Rights = rights, IsDeny = deny, Inheritance = inheritance,
            Propagation = inheritOnly ? AcePropagation.InheritOnly : AcePropagation.None,
        };
    }
}
