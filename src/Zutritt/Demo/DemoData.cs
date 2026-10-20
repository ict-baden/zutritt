// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Demo;

/// <summary>
/// Eingebauter Demo-Lauf: erfundener Fileserver \\FS01 eines Pharma-Mittelständlers mit den Freigaben Labor, QM
/// und Produktion, Rollenkonten statt Personennamen und allen Befund-Arten (ZT01–ZT14), dazu ein Lauf eine Woche früher für den
/// Vergleich. Keine echten Kundendaten. Die Befunde sind hier von Hand gesetzt (passend zu den Regeln), damit die
/// Demo ohne Auswertung auskommt und immer gleich aussieht.
/// </summary>
public static class DemoData
{
    // ---------- Bekannte Prinzipale ----------
    private const string Everyone = "S-1-1-0";
    private const string AuthUsers = "S-1-5-11";
    private const string Interactive = "S-1-5-4";
    private const string System = "S-1-5-18";
    private const string CreatorOwner = "S-1-3-0";
    private const string Administrators = "S-1-5-32-544";
    private const string BuiltinUsers = "S-1-5-32-545";

    // ---------- Erfundene Domäne MUSTER ----------
    private const string Domain = "S-1-5-21-2847561930-1639847205-3958126407";
    private const string DomainAdmins = Domain + "-512";
    private const string DomainUsers = Domain + "-513";

    private const string QcLeitungKonto = Domain + "-1101";
    private const string Labor01 = Domain + "-1102";
    private const string Qc01 = Domain + "-1103";
    private const string Labor02 = Domain + "-1104";
    private const string Qm01 = Domain + "-1105";
    private const string Qm02 = Domain + "-1106";
    private const string Qa01 = Domain + "-1107";
    private const string Instandhaltung01 = Domain + "-1108";
    private const string SchichtA01 = Domain + "-1109";
    private const string Labor03 = Domain + "-1110";           // ausgeschieden, deaktiviert
    private const string ProduktionLeitungKonto = Domain + "-1111";
    private const string SchichtB01 = Domain + "-1112";
    private const string Mikro01 = Domain + "-1113";
    private const string SchichtA02 = Domain + "-1114";
    private const string Einkauf01 = Domain + "-1115";
    private const string Geschaeftsleitung01 = Domain + "-1116";
    private const string ExtAuditor = Domain + "-1117";     // externes Audit, nach dem Audit deaktiviert
    private const string SvcHplc = Domain + "-1118";
    private const string SvcBackup = Domain + "-1119";
    private const string AdmIt = Domain + "-1120";
    private const string Orphan = Domain + "-1188";         // gelöschtes Konto

    private const string QcTeam = Domain + "-1201";
    private const string QaTeam = Domain + "-1202";
    private const string LaborAlle = Domain + "-1203";
    private const string MikroTeam = Domain + "-1204";
    private const string QmTeam = Domain + "-1205";
    private const string QmLeitung = Domain + "-1206";
    private const string QmLeser = Domain + "-1207";
    private const string ProduktionAlle = Domain + "-1208";
    private const string SchichtA = Domain + "-1209";
    private const string SchichtB = Domain + "-1210";
    private const string ProduktionLeitung = Domain + "-1211";
    private const string Instandhaltung = Domain + "-1212";
    private const string Geschaeftsleitung = Domain + "-1213";
    private const string Einkauf = Domain + "-1214";
    private const string FileserverAdmins = Domain + "-1215";
    private const string AltProjekt = Domain + "-1216";     // leere Gruppe

    private const FileRights Read = FileRights.ReadAndExecute | FileRights.Synchronize;
    private const FileRights Write = FileRights.ReadAndExecute | FileRights.Write | FileRights.Synchronize;
    private const FileRights Modify = FileRights.Modify | FileRights.Synchronize;
    private const FileRights Full = FileRights.FullControl;
    private const FileRights DeleteOnly = FileRights.Delete | FileRights.DeleteSubdirectoriesAndFiles;

    private const AceInheritance CIOI = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit;

    /// <summary>Älterer Lauf (eine Woche vorher) und aktueller Lauf, beide noch ohne Id.</summary>
    public static (ScanResult Older, ScanResult Newer) Create()
    {
        var newer = new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.FromHours(2));
        return (Build(newer.AddDays(-7), older: true), Build(newer, older: false));
    }

    private static ScanResult Build(DateTimeOffset started, bool older)
    {
        var b = new Builder(started, older);
        b.Principals();
        b.Shares();
        b.Labor();
        b.Qm();
        b.Produktion();
        return b.Scan;
    }

    private sealed class Builder(DateTimeOffset started, bool older)
    {
        public readonly ScanResult Scan = new()
        {
            StartedAt = started,
            FinishedAt = started.AddMinutes(4).AddSeconds(older ? 21 : 37),
            Machine = "ADMIN-PC07",
            ScannedBy = @"MUSTER\adm.it",
            ToolVersion = AppInfo.Version,
            Request = new ScanRequest { Targets = [@"\\FS01"] },
            FoldersVisited = older ? 18_690 : 18_734,
            FilesVisited = 0,
        };

        // ================= Konten und Gruppen =================

        public void Principals()
        {
            Known(Everyone, "Jeder");
            Known(AuthUsers, @"NT-AUTORITÄT\Authentifizierte Benutzer");
            Known(Interactive, @"NT-AUTORITÄT\INTERAKTIV");
            Known(System, @"NT-AUTORITÄT\SYSTEM");
            Known(CreatorOwner, "ERSTELLER-BESITZER");

            Group(Administrators, @"VORDEFINIERT\Administratoren", local: true, DomainAdmins, FileserverAdmins);
            Group(BuiltinUsers, @"VORDEFINIERT\Benutzer", local: true, Interactive, AuthUsers, DomainUsers);

            User(QcLeitungKonto, "qc.leitung", "QC-Leitung");
            User(Labor01, "labor.ma01", "Labor Mitarbeitende 01");
            User(Qc01, "qc.ma01", "QC Mitarbeitende 01");
            User(Labor02, "labor.ma02", "Labor Mitarbeitende 02");
            User(Qm01, "qm.ma01", "QM Mitarbeitende 01");
            User(Qm02, "qm.ma02", "QM Mitarbeitende 02");
            User(Qa01, "qa.ma01", "QA Mitarbeitende 01");
            User(Instandhaltung01, "ih.ma01", "Instandhaltung Mitarbeitende 01");
            User(SchichtA01, "prod.schicht-a.01", "Produktion Schicht A 01");
            User(Labor03, "labor.ma03", "Labor Mitarbeitende 03", disabled: true);
            User(ProduktionLeitungKonto, "prod.leitung", "Produktionsleitung");
            User(SchichtB01, "prod.schicht-b.01", "Produktion Schicht B 01");
            User(Mikro01, "mikro.ma01", "Mikrobiologie Mitarbeitende 01");
            User(SchichtA02, "prod.schicht-a.02", "Produktion Schicht A 02");
            User(Einkauf01, "einkauf.ma01", "Einkauf Mitarbeitende 01");
            User(Geschaeftsleitung01, "gl.ma01", "Geschäftsleitung 01");
            User(ExtAuditor, "ext.auditor", "Externes Audit 2026", disabled: !older);
            User(SvcHplc, "svc-hplc", "Dienstkonto HPLC-Datensystem");
            User(SvcBackup, "svc-backup", "Dienstkonto Datensicherung");
            User(AdmIt, "adm.it", "IT-Administration");
            Scan.Principals[Orphan] = new Principal { Sid = Orphan, Name = Orphan, Kind = PrincipalKind.Verwaist };

            string[] allUsers = [QcLeitungKonto, Labor01, Qc01, Labor02, Qm01, Qm02, Qa01, Instandhaltung01,
                SchichtA01, Labor03, ProduktionLeitungKonto, SchichtB01, Mikro01, SchichtA02, Einkauf01, Geschaeftsleitung01, ExtAuditor, SvcHplc, SvcBackup, AdmIt];
            Group(DomainAdmins, @"MUSTER\Domänen-Admins", local: false, AdmIt);
            Group(DomainUsers, @"MUSTER\Domänen-Benutzer", local: false, allUsers);
            // Neu seit dem letzten Lauf: Das Konto der QC-Leitung ist jetzt auch im QC-Team
            Group(QcTeam, @"MUSTER\QC-Team", local: false, older ? [Qc01] : [Qc01, QcLeitungKonto]);
            Group(QaTeam, @"MUSTER\QA-Team", local: false, Qa01);
            Group(MikroTeam, @"MUSTER\Mikrobiologie", local: false, Mikro01);
            Group(LaborAlle, @"MUSTER\Labor-Alle", local: false, QcTeam, MikroTeam, Labor01, Labor02, Labor03);
            Group(QmTeam, @"MUSTER\QM-Team", local: false, Qm01, Qm02);
            Group(QmLeitung, @"MUSTER\QM-Leitung", local: false, Qm01);
            Group(SchichtA, @"MUSTER\Produktion-Schicht-A", local: false, SchichtA01, SchichtA02);
            Group(SchichtB, @"MUSTER\Produktion-Schicht-B", local: false, SchichtB01);
            Group(ProduktionLeitung, @"MUSTER\Produktion-Leitung", local: false, ProduktionLeitungKonto);
            Group(Instandhaltung, @"MUSTER\Instandhaltung", local: false, Instandhaltung01);
            Group(ProduktionAlle, @"MUSTER\Produktion-Alle", local: false, SchichtA, SchichtB, ProduktionLeitung, Instandhaltung);
            Group(QmLeser, @"MUSTER\QM-Dokumente-Lesen", local: false, LaborAlle, ProduktionAlle, QaTeam);
            Group(Geschaeftsleitung, @"MUSTER\Geschäftsleitung", local: false, Geschaeftsleitung01);
            Group(Einkauf, @"MUSTER\Einkauf", local: false, Einkauf01);
            Group(FileserverAdmins, @"MUSTER\Fileserver-Admins", local: false, AdmIt);
            Group(AltProjekt, @"MUSTER\Alt-Projekt-2019", local: false);
        }

        private void Known(string sid, string name) =>
            Scan.Principals[sid] = new Principal { Sid = sid, Name = name, DisplayName = AccountName.LastPart(name), Kind = PrincipalKind.Bekannt };

        private void User(string sid, string account, string display, bool disabled = false) =>
            Scan.Principals[sid] = new Principal
            {
                Sid = sid, Name = @"MUSTER\" + account, DisplayName = display, Kind = PrincipalKind.Benutzer, IsDisabled = disabled,
            };

        private void Group(string sid, string name, bool local, params string[] members)
        {
            var g = new Principal { Sid = sid, Name = name, DisplayName = AccountName.LastPart(name), Kind = PrincipalKind.Gruppe, IsLocal = local };
            g.Members.AddRange(members);
            Scan.Principals[sid] = g;
        }

        // ================= Freigaben =================

        public void Shares()
        {
            Scan.Shares.Add(new ShareInfo
            {
                Server = "FS01", Name = "Labor", LocalPath = @"D:\Freigaben\Labor", Remark = "Labordaten QC und Mikrobiologie",
                Permissions = [new ShareAce(Everyone, ShareAccess.Vollzugriff, false)],
            });
            Scan.Shares.Add(new ShareInfo
            {
                Server = "FS01", Name = "QM", LocalPath = @"D:\Freigaben\QM", Remark = "Qualitätsmanagement",
                // Der externe Auditor darf über das Netz höchstens lesen – Verweigern „ab Ändern“ (Lesen bleibt erlaubt)
                Permissions = [new ShareAce(AuthUsers, ShareAccess.Aendern, false), new ShareAce(Administrators, ShareAccess.Vollzugriff, false),
                    new ShareAce(ExtAuditor, ShareAccess.Aendern, true)],
            });
            Scan.Shares.Add(new ShareInfo
            {
                Server = "FS01", Name = "Produktion", LocalPath = @"E:\Produktion", Remark = "Chargen, Rezepturen, Wartung",
                PermissionsReadable = false,
            });
        }

        // ================= Labor =================

        public void Labor()
        {
            const string S = @"\\FS01\Labor";
            Folder(S, "", 0, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(CreatorOwner, Full, inheritOnly: true), A(LaborAlle, Modify));
            Finding("ZT08", Severity.Info, S, Everyone, "Freigabe-Berechtigung: „Jeder“ hat Vollzugriff – der Schutz liegt allein bei den NTFS-Rechten.");

            Folder(S, @"\QC", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QcTeam, Modify), A(QcLeitungKonto, Full));
            Finding("ZT03", Severity.Mittel, S + @"\QC", QcLeitungKonto, "„MUSTER\\qc.leitung“ ist direkt berechtigt (Vollzugriff) statt über eine Gruppe.");
            Finding("ZT05", Severity.Hoch, S + @"\QC", QcLeitungKonto, "„MUSTER\\qc.leitung“ hat Vollzugriff und darf damit auch Berechtigungen ändern.");

            if (older)
            {
                Folder(S, @"\QC\Methoden", 2, broken: false, Administrators,
                    A(Everyone, Read), I(System, Full), I(Administrators, Full), I(QcTeam, Modify), I(QcLeitungKonto, Full));
                Finding("ZT02", Severity.Mittel, S + @"\QC\Methoden", Everyone, "„Jeder“ darf hier lesen.");
            }
            else
            {
                Folder(S, @"\QC\Methoden", 2, broken: false, Administrators,
                    A(QaTeam, Read), I(System, Full), I(Administrators, Full), I(QcTeam, Modify), I(QcLeitungKonto, Full));
            }

            Folder(S, @"\QC\Rohdaten", 2, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QcTeam, Write), A(SvcHplc, Modify));
            Finding("ZT03", Severity.Mittel, S + @"\QC\Rohdaten", SvcHplc, "„MUSTER\\svc-hplc“ ist direkt berechtigt (Ändern) statt über eine Gruppe.");

            Folder(S, @"\QC\Rohdaten\HPLC-03", 3, broken: false, Administrators,
                A(QcTeam, DeleteOnly, deny: true), I(System, Full), I(Administrators, Full), I(QcTeam, Write), I(SvcHplc, Modify));
            Finding("ZT07", Severity.Info, S + @"\QC\Rohdaten\HPLC-03", QcTeam, "Verweigern-Eintrag für „MUSTER\\QC-Team“ (Löschen).");

            Folder(S, @"\QC\Prüfpläne", 2, broken: false, Administrators,
                A(ProduktionLeitung, Read), I(System, Full), I(Administrators, Full), I(QcTeam, Modify), I(QcLeitungKonto, Full));

            if (!older)
            {
                // Neu seit dem letzten Lauf: Austausch-Ordner, in dem „Jeder“ ändern darf
                Folder(S, @"\Austausch", 1, broken: false, Labor01,
                    A(Everyone, Modify), I(System, Full), I(Administrators, Full), I(LaborAlle, Modify));
                Finding("ZT01", Severity.Hoch, S + @"\Austausch", Everyone, "„Jeder“ darf hier ändern.");
                Finding("ZT10", Severity.Niedrig, S + @"\Austausch", Labor01, "Besitzer ist das Benutzerkonto „MUSTER\\labor.ma01“.");
            }

            Folder(S, @"\Archiv", 1, broken: false, Administrators,
                A(AuthUsers, Read), A(Labor02, Modify, deny: true), I(System, Full), I(Administrators, Full), I(LaborAlle, Modify));
            Finding("ZT02", Severity.Mittel, S + @"\Archiv", AuthUsers, "„NT-AUTORITÄT\\Authentifizierte Benutzer“ darf hier lesen.");
            Finding("ZT07", Severity.Info, S + @"\Archiv", Labor02, "Verweigern-Eintrag für „MUSTER\\labor.ma02“ (Ändern).");

            Folder(S, @"\Projekte", 1, broken: false, Administrators,
                A(Geschaeftsleitung, Read), I(System, Full), I(Administrators, Full), I(LaborAlle, Modify));

            Folder(S, @"\Projekte\2019\Alt\Rohdaten", 4, broken: true, Administrators,
                A(Administrators, Full), A(Orphan, Modify), A(Labor03, Read), A(AltProjekt, Read));
            const string Alt = S + @"\Projekte\2019\Alt\Rohdaten";
            Finding("ZT06", Severity.Niedrig, Alt, null, "Vererbung ist in Ebene 4 unterbrochen – eine leicht übersehene Ausnahme.");
            Finding("ZT04", Severity.Mittel, Alt, Orphan, $"Die verwaiste SID {Orphan} ist berechtigt (Ändern).");
            Finding("ZT03", Severity.Mittel, Alt, Labor03, "„MUSTER\\labor.ma03“ ist direkt berechtigt (Lesen) statt über eine Gruppe.");
            Finding("ZT12", Severity.Mittel, Alt, Labor03, "Das deaktivierte Konto „MUSTER\\labor.ma03“ ist noch berechtigt (Lesen).");
            Finding("ZT11", Severity.Niedrig, Alt, AltProjekt, "Die Gruppe „MUSTER\\Alt-Projekt-2019“ hat keine Mitglieder, ist aber berechtigt (Lesen).");

            Folder(S, @"\Projekte\2026-Stabilität", 2, broken: false, Administrators,
                A(QaTeam, Modify), I(System, Full), I(Administrators, Full), I(LaborAlle, Modify), I(Geschaeftsleitung, Read));

            Folder(S, @"\Mikrobiologie", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(MikroTeam, Modify), A(QcTeam, Read));
            Folder(S, @"\Mikrobiologie\Umgebungsmonitoring", 2, broken: false, Administrators,
                A(ProduktionLeitung, Read), I(System, Full), I(Administrators, Full), I(MikroTeam, Modify), I(QcTeam, Read));
            Folder(S, @"\Mikrobiologie\Nährmedien", 2, broken: false, Administrators,
                A(Einkauf, Read), I(System, Full), I(Administrators, Full), I(MikroTeam, Modify), I(QcTeam, Read));

            Folder(S, @"\Geräte", 1, broken: false, Administrators,
                A(DomainUsers, Read), I(System, Full), I(Administrators, Full), I(LaborAlle, Modify));
            Finding("ZT02", Severity.Mittel, S + @"\Geräte", DomainUsers, "„MUSTER\\Domänen-Benutzer“ darf hier lesen.");
            Folder(S, @"\Geräte\Logbücher", 2, broken: false, Administrators,
                A(Instandhaltung, Modify), I(System, Full), I(Administrators, Full), I(LaborAlle, Modify), I(DomainUsers, Read));

            Unreadable(S, @"\Personal", 1, "Zugriff verweigert");
            Finding("ZT09", Severity.Info, S + @"\Personal", null, "Ordner nicht lesbar: Zugriff verweigert. Der Inhalt ist nicht geprüft.");

            Folder(S, @"\Vorlagen", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(LaborAlle, Read), A(QmTeam, Modify));
        }

        // ================= QM =================

        public void Qm()
        {
            const string S = @"\\FS01\QM";
            Folder(S, "", 0, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(CreatorOwner, Full, inheritOnly: true), A(QmTeam, Modify), A(QmLeser, Read));
            Finding("ZT08", Severity.Info, S, AuthUsers, "Freigabe-Berechtigung: „Authentifizierte Benutzer“ dürfen ändern – der Schutz liegt allein bei den NTFS-Rechten.");

            Folder(S, @"\SOPs", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QmTeam, Modify), A(QmLeser, Read));
            Folder(S, @"\SOPs\Entwürfe", 2, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QmTeam, Modify), A(Qm01, Modify));
            Finding("ZT03", Severity.Mittel, S + @"\SOPs\Entwürfe", Qm01, "„MUSTER\\qm.ma01“ ist direkt berechtigt (Ändern) statt über eine Gruppe.");
            Folder(S, @"\SOPs\Freigegeben", 2, broken: false, Administrators,
                A(AuthUsers, Read), I(System, Full), I(Administrators, Full), I(QmTeam, Modify), I(QmLeser, Read));
            Finding("ZT02", Severity.Mittel, S + @"\SOPs\Freigegeben", AuthUsers, "„NT-AUTORITÄT\\Authentifizierte Benutzer“ darf hier lesen.");
            Folder(S, @"\SOPs\Archiv", 2, broken: false, Administrators,
                A(ProduktionAlle, Read, deny: true), I(System, Full), I(Administrators, Full), I(QmTeam, Modify), I(QmLeser, Read));
            Finding("ZT07", Severity.Info, S + @"\SOPs\Archiv", ProduktionAlle, "Verweigern-Eintrag für „MUSTER\\Produktion-Alle“ (Lesen).");

            if (!older)
            {
                // Neu seit dem letzten Lauf: Jemand hat „Labormethoden“ aus der Freigabe Labor hierher verschoben (gleiches
                // Laufwerk D:). Windows nimmt dabei die alten geerbten Rechte mit – Labor-Alle darf dort weiter ändern,
                // das QM-Team kommt nicht hinein, und die Einträge von „SOPs“ fehlen ganz.
                var moved = Folder(S, @"\SOPs\Labormethoden", 2, broken: false, Administrators,
                    I(System, Full), I(Administrators, Full), I(LaborAlle, Modify));
                moved.InheritanceDiffers = true;
                Finding("ZT14", Severity.Niedrig, S + @"\SOPs\Labormethoden", null,
                    "Geerbte Einträge passen nicht zum übergeordneten Ordner – vermutlich verschoben. „MUSTER\\Labor-Alle“ kommt nicht von dort, sondern vom früheren Ort.");
            }

            Folder(S, @"\Audits", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QmLeitung, Modify), A(Geschaeftsleitung, Read));
            Folder(S, @"\Audits\2026", 2, broken: false, Administrators,
                A(ExtAuditor, Read), I(System, Full), I(Administrators, Full), I(QmLeitung, Modify), I(Geschaeftsleitung, Read));
            Finding("ZT03", Severity.Mittel, S + @"\Audits\2026", ExtAuditor, "„MUSTER\\ext.auditor“ ist direkt berechtigt (Lesen) statt über eine Gruppe.");
            if (!older)
                Finding("ZT12", Severity.Mittel, S + @"\Audits\2026", ExtAuditor, "Das deaktivierte Konto „MUSTER\\ext.auditor“ ist noch berechtigt (Lesen).");

            Folder(S, @"\Abweichungen", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QmTeam, Modify), A(QcTeam, Modify), A(ProduktionLeitung, Modify));
            Folder(S, @"\CAPA", 1, broken: false, Administrators,
                A(QaTeam, Full), I(System, Full), I(Administrators, Full), I(QmTeam, Modify), I(QmLeser, Read));
            Finding("ZT05", Severity.Hoch, S + @"\CAPA", QaTeam, "„MUSTER\\QA-Team“ hat Vollzugriff und darf damit auch Berechtigungen ändern.");
            Folder(S, @"\Schulungen", 1, broken: false, Administrators,
                A(DomainUsers, Modify), I(System, Full), I(Administrators, Full), I(QmTeam, Modify), I(QmLeser, Read));
            Finding("ZT01", Severity.Hoch, S + @"\Schulungen", DomainUsers, "„MUSTER\\Domänen-Benutzer“ darf hier ändern.");
            Folder(S, @"\Lieferanten", 1, broken: false, Administrators,
                A(Einkauf, Modify), I(System, Full), I(Administrators, Full), I(QmTeam, Modify), I(QmLeser, Read));

            if (older)
            {
                // Im letzten Lauf noch vorhanden, inzwischen aufgeräumt
                Folder(S, @"\Temp", 1, broken: false, Qm01,
                    A(Everyone, Modify), I(System, Full), I(Administrators, Full), I(QmTeam, Modify), I(QmLeser, Read));
                Finding("ZT01", Severity.Hoch, S + @"\Temp", Everyone, "„Jeder“ darf hier ändern.");
                Finding("ZT10", Severity.Niedrig, S + @"\Temp", Qm01, "Besitzer ist das Benutzerkonto „MUSTER\\qm.ma01“.");
            }

            Folder(S, @"\Validierung", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(QmTeam, Modify), A(QaTeam, Modify));
            const string Alt = S + @"\Validierung\Computersysteme\2020\Altsystem";
            Folder(S, @"\Validierung\Computersysteme\2020\Altsystem", 4, broken: true, Qm02,
                A(Administrators, Full), A(QaTeam, Read));
            Finding("ZT06", Severity.Niedrig, Alt, null, "Vererbung ist in Ebene 4 unterbrochen – eine leicht übersehene Ausnahme.");
            Finding("ZT10", Severity.Niedrig, Alt, Qm02, "Besitzer ist das Benutzerkonto „MUSTER\\qm.ma02“.");

            Folder(S, @"\Management-Review", 1, broken: true, Orphan,
                A(System, Full), A(Administrators, Full), A(Geschaeftsleitung, Modify), A(QmLeitung, Modify));
            Finding("ZT04", Severity.Mittel, S + @"\Management-Review", Orphan, $"Besitzer ist die verwaiste SID {Orphan}.");
        }

        // ================= Produktion =================

        public void Produktion()
        {
            const string S = @"\\FS01\Produktion";
            Folder(S, "", 0, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(CreatorOwner, Full, inheritOnly: true), A(ProduktionAlle, Modify));
            Finding("ZT13", Severity.Info, S, null, "Die Freigabe-Berechtigungen von \\\\FS01\\Produktion ließen sich nicht lesen – der Zugriff über das Netz ist nur nach NTFS bewertet.");

            Folder(S, @"\Chargenprotokolle", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(ProduktionLeitung, Modify), A(SchichtA, Write), A(SchichtB, Write), A(QaTeam, Read));
            Folder(S, @"\Chargenprotokolle\2026", 2, broken: false, Administrators,
                A(SchichtB, DeleteOnly, deny: true), I(System, Full), I(Administrators, Full), I(ProduktionLeitung, Modify),
                I(SchichtA, Write), I(SchichtB, Write), I(QaTeam, Read));
            Finding("ZT07", Severity.Info, S + @"\Chargenprotokolle\2026", SchichtB, "Verweigern-Eintrag für „MUSTER\\Produktion-Schicht-B“ (Löschen).");

            Folder(S, @"\Rezepturen", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(ProduktionLeitung, Modify), A(ProduktionAlle, Read));
            Folder(S, @"\Schichtbuch", 1, broken: false, Administrators,
                A(SchichtA, Modify), A(SchichtB, Modify), I(System, Full), I(Administrators, Full), I(ProduktionAlle, Modify));

            Folder(S, @"\Wartung", 1, broken: true, Administrators,
                A(System, Full), A(Administrators, Full), A(Instandhaltung, Modify), A(SvcBackup, Read));
            Finding("ZT03", Severity.Mittel, S + @"\Wartung", SvcBackup, "„MUSTER\\svc-backup“ ist direkt berechtigt (Lesen) statt über eine Gruppe.");
            // Seit dem letzten Lauf von Lesen auf Ändern erweitert
            Folder(S, @"\Wartung\Anlage-3", 2, broken: false, Administrators,
                A(Instandhaltung01, older ? Read : Modify), I(System, Full), I(Administrators, Full), I(Instandhaltung, Modify), I(SvcBackup, Read));
            Finding("ZT03", Severity.Mittel, S + @"\Wartung\Anlage-3", Instandhaltung01,
                $"„MUSTER\\ih.ma01“ ist direkt berechtigt ({(older ? "Lesen" : "Ändern")}) statt über eine Gruppe.");

            Folder(S, @"\Linie-1\SPS\Backup", 3, broken: true, Administrators,
                A(Administrators, Full), A(Instandhaltung, Full));
            Finding("ZT06", Severity.Niedrig, S + @"\Linie-1\SPS\Backup", null, "Vererbung ist in Ebene 3 unterbrochen – eine leicht übersehene Ausnahme.");
            Finding("ZT05", Severity.Hoch, S + @"\Linie-1\SPS\Backup", Instandhaltung, "„MUSTER\\Instandhaltung“ hat Vollzugriff und darf damit auch Berechtigungen ändern.");

            Unreadable(S, @"\Linie-2\Messdaten\Export", 3, "Der Pfad ist zu lang.");
            Finding("ZT09", Severity.Info, S + @"\Linie-2\Messdaten\Export", null, "Ordner nicht lesbar: Der Pfad ist zu lang. Der Inhalt ist nicht geprüft.");

            Folder(S, @"\Kantine", 1, broken: false, Administrators,
                A(Everyone, Read), I(System, Full), I(Administrators, Full), I(ProduktionAlle, Modify));
            Finding("ZT02", Severity.Mittel, S + @"\Kantine", Everyone, "„Jeder“ darf hier lesen.");
        }

        // ================= Bausteine =================

        private FolderNode Folder(string share, string relative, int depth, bool broken, string owner, params AceEntry[] aces)
        {
            var f = new FolderNode { Path = share + relative, Depth = depth, InheritanceBroken = broken, ShareUnc = share, OwnerSid = owner };
            f.Aces.AddRange(aces);
            Scan.Folders.Add(f);
            return f;
        }

        private void Unreadable(string share, string relative, int depth, string error)
        {
            Scan.Errors.Add(new ScanError(share + relative, error));
            Scan.Folders.Add(new FolderNode { Path = share + relative, Depth = depth, ShareUnc = share, Error = error });
        }

        private void Finding(string rule, Severity severity, string path, string? sid, string message) =>
            Scan.Findings.Add(new Finding { RuleId = rule, Severity = severity, Path = path, Sid = sid, Message = message });

        /// <summary>Expliziter Eintrag (gilt für diesen Ordner, Unterordner und Dateien).</summary>
        private static AceEntry A(string sid, FileRights rights, bool deny = false, bool inheritOnly = false) => new()
        {
            Sid = sid, Rights = rights, IsDeny = deny, Inheritance = CIOI,
            Propagation = inheritOnly ? AcePropagation.InheritOnly : AcePropagation.None,
        };

        /// <summary>Geerbter Eintrag.</summary>
        private static AceEntry I(string sid, FileRights rights) => new()
        {
            Sid = sid, Rights = rights, IsInherited = true, Inheritance = CIOI, Propagation = AcePropagation.None,
        };
    }
}
