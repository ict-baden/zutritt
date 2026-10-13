using System.Security.AccessControl;
using Zutritt.Core.Model;

namespace Zutritt.Tests.TestSupport;

/// <summary>
/// Synthetischer Lauf über \\FS01\Labor mit typischen Problemen. Gemeinsame Grundlage für Tests von
/// Auswertung, Speicher, Vergleich und Berichten.
/// </summary>
public static class SampleScan
{
    // Bekannte SIDs
    public const string Everyone = "S-1-1-0";
    public const string AuthenticatedUsers = "S-1-5-11";
    public const string System = "S-1-5-18";
    public const string Administrators = "S-1-5-32-544";
    public const string BuiltinUsers = "S-1-5-32-545";
    public const string CreatorOwner = "S-1-3-0";

    // Domäne LAB (erfunden)
    public const string Domain = "S-1-5-21-1111111111-2222222222-3333333333";
    public const string DomainAdmins = Domain + "-512";
    public const string DomainUsers = Domain + "-513";
    public const string QcTeam = Domain + "-1101";
    public const string LaborAlle = Domain + "-1102";
    public const string LeereGruppe = Domain + "-1103";
    public const string Max = Domain + "-2001";
    public const string Erika = Domain + "-2002";
    public const string Klaus = Domain + "-2003";      // deaktiviert
    public const string Verwaist = Domain + "-2999";   // gelöschtes Konto

    public const FileSystemRights Read = FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize;
    public const FileSystemRights Modify = FileSystemRights.Modify | FileSystemRights.Synchronize;
    public const FileSystemRights Full = FileSystemRights.FullControl;

    public static ScanResult Create(DateTimeOffset? started = null)
    {
        var t = started ?? new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(2));
        var scan = new ScanResult
        {
            StartedAt = t,
            FinishedAt = t.AddMinutes(3),
            Machine = "ADMIN-PC",
            ScannedBy = @"LAB\admin.max",
            ToolVersion = "1.0.0",
            Request = new ScanRequest { Targets = [@"\\FS01\Labor"] },
            FoldersVisited = 1234,
            FilesVisited = 0,
        };

        scan.Shares.Add(new ShareInfo
        {
            Server = "FS01", Name = "Labor", LocalPath = @"D:\Labor", Remark = "Labordaten",
            Permissions = [new ShareAce(Everyone, ShareAccess.Vollzugriff, false)],
        });

        Principal P(string sid, string name, PrincipalKind kind, params string[] members)
        {
            var p = new Principal { Sid = sid, Name = name, DisplayName = name.Split('\\').Last(), Kind = kind };
            p.Members.AddRange(members);
            scan.Principals[sid] = p;
            return p;
        }
        P(Everyone, "Jeder", PrincipalKind.Bekannt);
        P(AuthenticatedUsers, @"NT-AUTORITÄT\Authentifizierte Benutzer", PrincipalKind.Bekannt);
        P(System, @"NT-AUTORITÄT\SYSTEM", PrincipalKind.Bekannt);
        P(CreatorOwner, "ERSTELLER-BESITZER", PrincipalKind.Bekannt);
        P(Administrators, @"VORDEFINIERT\Administratoren", PrincipalKind.Gruppe, DomainAdmins);
        P(BuiltinUsers, @"VORDEFINIERT\Benutzer", PrincipalKind.Gruppe, AuthenticatedUsers, DomainUsers);
        P(DomainAdmins, @"LAB\Domänen-Admins", PrincipalKind.Gruppe);
        P(DomainUsers, @"LAB\Domänen-Benutzer", PrincipalKind.Gruppe, Max, Erika, Klaus);
        P(QcTeam, @"LAB\QC-Team", PrincipalKind.Gruppe, Erika);
        P(LaborAlle, @"LAB\Labor-Alle", PrincipalKind.Gruppe, QcTeam, Max);
        P(LeereGruppe, @"LAB\Alt-Projekt", PrincipalKind.Gruppe);
        P(Max, @"LAB\max.muster", PrincipalKind.Benutzer).DisplayName = "Max Muster";
        P(Erika, @"LAB\erika.beispiel", PrincipalKind.Benutzer).DisplayName = "Erika Beispiel";
        var klaus = P(Klaus, @"LAB\klaus.alt", PrincipalKind.Benutzer);
        klaus.DisplayName = "Klaus Alt";
        klaus.IsDisabled = true;
        scan.Principals[Verwaist] = new Principal { Sid = Verwaist, Name = Verwaist, Kind = PrincipalKind.Verwaist };

        const InheritanceFlags CIOI = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        AceEntry Ace(string sid, FileSystemRights r, bool inherited = false, bool deny = false) =>
            new() { Sid = sid, Rights = r, IsInherited = inherited, IsDeny = deny, Inheritance = CIOI, Propagation = PropagationFlags.None };

        FolderNode F(string rel, int depth, bool broken, string owner, params AceEntry[] aces)
        {
            var f = new FolderNode { Path = @"\\FS01\Labor" + rel, Depth = depth, InheritanceBroken = broken, ShareUnc = @"\\FS01\Labor", OwnerSid = owner };
            f.Aces.AddRange(aces);
            scan.Folders.Add(f);
            return f;
        }

        // Wurzel: sauber, Labor-Alle darf ändern, Admins voll
        F("", 0, true, Administrators,
            Ace(System, Full), Ace(Administrators, Full), Ace(LaborAlle, Modify), Ace(CreatorOwner, Full));
        // QC: Vererbung unterbrochen, QC-Team ändern, Max direkt Vollzugriff (zwei Befunde)
        F(@"\QC", 1, true, Administrators,
            Ace(System, Full), Ace(Administrators, Full), Ace(QcTeam, Modify), Ace(Max, Full));
        // Austausch: Jeder darf ändern (hoch), dazu geerbte Rechte; Besitzer ist ein normaler Benutzer
        F(@"\Austausch", 1, false, Max,
            Ace(Everyone, Modify), Ace(System, Full, inherited: true), Ace(Administrators, Full, inherited: true), Ace(LaborAlle, Modify, inherited: true));
        // Tief versteckte Ausnahme: Vererbung in Tiefe 4 unterbrochen, verwaiste SID, deaktiviertes Konto, leere Gruppe
        F(@"\Projekte\2019\Alt\Rohdaten", 4, true, Administrators,
            Ace(Administrators, Full), Ace(Verwaist, Modify), Ace(Klaus, Read), Ace(LeereGruppe, Read));
        // Archiv: Authentifizierte Benutzer lesen, Max verweigert
        F(@"\Archiv", 1, false, Administrators,
            Ace(AuthenticatedUsers, Read), Ace(Max, Modify, deny: true), Ace(System, Full, inherited: true), Ace(Administrators, Full, inherited: true), Ace(LaborAlle, Modify, inherited: true));

        scan.Errors.Add(new ScanError(@"\\FS01\Labor\Personal", "Zugriff verweigert"));
        scan.Folders.Add(new FolderNode { Path = @"\\FS01\Labor\Personal", Depth = 1, ShareUnc = @"\\FS01\Labor", Error = "Zugriff verweigert" });
        return scan;
    }
}
