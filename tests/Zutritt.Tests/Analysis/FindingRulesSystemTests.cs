using System.Security.AccessControl;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using static Zutritt.Tests.Analysis.ScanBuilder;
using S = Zutritt.Tests.TestSupport.SampleScan;

namespace Zutritt.Tests.Analysis;

/// <summary>
/// ZT14 und die Regeln gegen Fehlalarme durch Windows-eigene Konten und Ordner – die Fälle stammen aus dem Härtetest
/// auf C:\Program Files, C:\ProgramData, C:\Users und C:\Windows\System32.
/// </summary>
public sealed class FindingRulesSystemTests
{
    private const FileSystemRights Read = S.Read;
    private const FileSystemRights Modify = S.Modify;
    private const FileSystemRights Full = S.Full;
    private const string WindowManager = "S-1-5-90-0-2";
    private const string CapabilityGroup = "S-1-5-32-989523358-1667815365-316973287-129935811-3358257576-2796868252-2781088163-715581871";
    private const string AppCapability = "S-1-15-3-1024-3635283841-2530182609-996808640-1887759898-3848208603-3313616867-983405619-2501854204";

    private static IReadOnlyList<(string Rule, string? Sid)> Keys(ScanBuilder b) =>
        FindingRules.Evaluate(b.Scan).Select(f => (f.RuleId, f.Sid)).ToList();

    private static IEnumerable<string> Rules(ScanBuilder b) => FindingRules.Evaluate(b.Scan).Select(f => f.RuleId);

    // ---------- ZT14 ----------

    [Fact]
    public void ZT14_nur_mit_Kennzeichen_des_Scanners_und_mit_den_fremden_Konten()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\max").Group(Sid(1001), @"T\Einkauf");
        b.Folder("", 0, Allow(S.Administrators, Full), Allow(Sid(1001), Modify));
        var moved = b.Folder(@"\Projekt", 1, Allow(S.Administrators, Full, inherited: true), Allow(u, Modify, inherited: true));
        moved.InheritanceDiffers = true;
        b.Folder(@"\Normal", 1, Allow(S.Administrators, Full, inherited: true), Allow(Sid(1001), Modify, inherited: true));

        var finding = Assert.Single(FindingRules.Evaluate(b.Scan), f => f.RuleId == "ZT14");

        Assert.Equal(Severity.Niedrig, finding.Severity);
        Assert.Equal(moved.Path, finding.Path);
        Assert.Null(finding.Sid);
        Assert.Equal(@"Geerbte Einträge passen nicht zum übergeordneten Ordner – vermutlich verschoben. „T\max“ kommt nicht von dort, sondern vom früheren Ort.", finding.Message);
        var rule = FindingRules.Get("ZT14")!;
        Assert.Equal("Rechte weichen von der Vererbung ab", rule.Title);
        Assert.Contains("verschobene Ordner", rule.Explanation);
        Assert.Contains("Vererbung neu anwenden", rule.Recommendation);
    }

    [Fact]
    public void ZT14_nicht_bei_unterbrochener_Vererbung_Fehler_oder_Windows_Ordner()
    {
        var b = new ScanBuilder();
        b.Folder("", 0, Allow(S.Administrators, Full));
        var broken = new FolderNode { Path = ScanBuilder.Share + @"\A", Depth = 1, InheritanceBroken = true, InheritanceDiffers = true, ShareUnc = ScanBuilder.Share };
        broken.Aces.Add(Allow(S.Administrators, Full));
        var failed = new FolderNode { Path = ScanBuilder.Share + @"\B", Depth = 1, Error = "Zugriff verweigert", InheritanceDiffers = true, ShareUnc = ScanBuilder.Share };
        var system = new FolderNode { Path = ScanBuilder.Share + @"\C", Depth = 1, OwnerSid = SidCatalog.TrustedInstaller, InheritanceDiffers = true, ShareUnc = ScanBuilder.Share };
        system.Aces.Add(Allow(S.Administrators, Full, inherited: true));
        b.Scan.Folders.AddRange([broken, failed, system]);

        Assert.DoesNotContain("ZT14", Rules(b));
    }

    // ---------- Windows-eigene Konten ----------

    [Fact]
    public void Virtuelles_Konto_ist_keine_Person_auch_wenn_es_als_Benutzer_aufgeloest_ist()
    {
        // Ältere Läufe (oder ein Resolver ohne Korrektur) kennen „Window Manager\DWM-2“ als Benutzer
        var b = new ScanBuilder().User(WindowManager, @"Window Manager\DWM-2", disabled: true);
        var f = b.Folder("", 0, Allow(WindowManager, Full), Allow(S.Administrators, Full));
        f.OwnerSid = WindowManager;

        var rules = Rules(b).ToList();
        Assert.DoesNotContain("ZT03", rules);
        Assert.DoesNotContain("ZT10", rules);
        Assert.DoesNotContain("ZT12", rules);
        Assert.DoesNotContain("ZT05", rules);
    }

    [Fact]
    public void Faehigkeitsgruppe_mit_Vollzugriff_ist_kein_Nicht_Admin()
    {
        // So in C:\ProgramData\Microsoft\Provisioning: sah aus wie eine VORDEFINIERT-Gruppe, war „Unbekannt“ → ZT05 Hoch
        foreach (var kind in new[] { PrincipalKind.Unbekannt, PrincipalKind.Bekannt })
        {
            var b = new ScanBuilder();
            b.Scan.Principals[CapabilityGroup] = new Principal { Sid = CapabilityGroup, Name = CapabilityGroup, Kind = kind };
            b.Folder("", 0, Allow(CapabilityGroup, Full), Allow(S.Administrators, Full));
            Assert.Empty(Keys(b));
        }
    }

    [Fact]
    public void Leere_VORDEFINIERT_Gruppe_ist_kein_Befund_eine_eigene_leere_Gruppe_schon()
    {
        var b = new ScanBuilder().Group(SidCatalog.BackupOperators, @"VORDEFINIERT\Sicherungs-Operatoren").Group(Sid(1003), @"T\Alt-Projekt");
        b.Folder("", 0, Allow(SidCatalog.BackupOperators, FileSystemRights.Write), Allow(Sid(1003), Read));

        Assert.Equal([("ZT11", Sid(1003))], Keys(b).Where(k => k.Rule == "ZT11"));
    }

    // ---------- Breite Gruppen ----------

    [Fact]
    public void Breite_Gruppe_nur_mit_Durchsuchen_oder_Attributen_darf_nicht_lesen()
    {
        // „Jeder: Ordner durchsuchen“ (häufig über Freigabewurzeln) und „Attribute lesen“ zeigen keinen Inhalt
        var b = new ScanBuilder();
        b.Folder("", 0, Allow(S.Everyone, FileSystemRights.Traverse | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize),
            Allow(S.AuthenticatedUsers, FileSystemRights.ReadPermissions, inheritance: InheritanceFlags.None));
        b.Folder(@"\X", 1, Allow(S.Everyone, FileSystemRights.ListDirectory, inheritance: InheritanceFlags.None));

        Assert.Equal([("ZT02", S.Everyone)], Keys(b));
        Assert.Equal(@"\\SRV\Daten\X", FindingRules.Evaluate(b.Scan).Single().Path);
    }

    [Fact]
    public void Kompatibilitaets_Verknuepfung_von_Windows_ist_kein_Befund()
    {
        // C:\Users\Default\Anwendungsdaten u. a.: „Jeder“ Lesen, aber „Ordner auflisten“ verweigert, nur dieser Ordner
        var b = new ScanBuilder();
        b.Folder("", 0, Deny(S.Everyone, FileSystemRights.ListDirectory, inheritance: InheritanceFlags.None),
            Allow(S.Everyone, Read, inheritance: InheritanceFlags.None), Allow(S.Administrators, Full, inheritance: InheritanceFlags.None));

        Assert.Empty(Keys(b));
    }

    [Theory]
    [InlineData(FileSystemRights.ListDirectory, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)] // vererbt
    [InlineData(FileSystemRights.ReadData | FileSystemRights.Delete, InheritanceFlags.None)]                          // mehr als Auflisten
    public void Andere_Verweigerungen_fuer_Jeder_bleiben_ZT07(FileSystemRights denied, InheritanceFlags inheritance)
    {
        var b = new ScanBuilder();
        b.Folder("", 0, Deny(S.Everyone, denied, inheritance: inheritance), Allow(S.Administrators, Full));
        Assert.Contains(("ZT07", S.Everyone), Keys(b));
    }

    // ---------- Von Windows verwaltete Ordner ----------

    [Fact]
    public void Windows_Ordner_mit_Besitzer_TrustedInstaller_nur_echte_Gefahren()
    {
        var u = Sid(2001);
        var b = new ScanBuilder().User(u, @"T\max");
        var f = b.Folder(@"\Programme\Komponente\de-DE", 3,
            Allow(SidCatalog.TrustedInstaller, Full), Allow(S.Administrators, Modify), Allow(S.BuiltinUsers, Read),
            Allow(S.AuthenticatedUsers, FileSystemRights.Write), Allow(u, Full));
        f.OwnerSid = SidCatalog.TrustedInstaller;
        var broken = new FolderNode { Path = f.Path + @"\Unterordner", Depth = 4, InheritanceBroken = true, OwnerSid = SidCatalog.TrustedInstaller, ShareUnc = f.ShareUnc };
        broken.Aces.Add(Allow(SidCatalog.TrustedInstaller, Full));
        broken.Aces.Add(Allow(S.BuiltinUsers, Read));
        b.Scan.Folders.Add(broken);

        var keys = Keys(b);
        // „Benutzer dürfen lesen“ und die versteckte Ausnahme sind so gewollt …
        Assert.DoesNotContain(keys, k => k.Rule is "ZT02" or "ZT06" or "ZT03" or "ZT10");
        // … Schreibrecht für alle und Vollzugriff für ein einzelnes Konto bleiben Befunde
        Assert.Contains(("ZT01", S.AuthenticatedUsers), keys);
        Assert.Contains(("ZT05", u), keys);
    }

    [Fact]
    public void Fehlerbericht_des_Benutzers_ist_kein_Befund_fremder_Vollzugriff_schon()
    {
        // C:\ProgramData\Microsoft\Windows\WER\ReportArchive\…: Administratoren, der Benutzer (Besitzer) mit Vollzugriff
        // und SCHREIBGESCHÜTZT – von Windows so angelegt
        var u = Sid(2001); var other = Sid(2002);
        var b = new ScanBuilder().User(u, @"T\max").User(other, @"T\erika");
        var f = b.Folder(@"\WER\ReportArchive\AppCrash_x", 5,
            Allow(S.Administrators, Full), Allow(u, Full), Allow(SidCatalog.WriteRestricted, (FileSystemRights)0x120116));
        f.OwnerSid = u;
        f.Aces.Add(Allow(other, Full));

        var keys = Keys(b);
        Assert.DoesNotContain(keys, k => k.Sid == u);
        Assert.Contains(("ZT05", other), keys);
        Assert.DoesNotContain(keys, k => k.Rule == "ZT06");
    }

    [Fact]
    public void App_Paket_Ordner_mit_Faehigkeit_ist_Windows_Ordner()
    {
        // C:\Program Files\WindowsApps\<Paket>: Besitzer SYSTEM, „Benutzer“ lesen explizit, dazu die Fähigkeit des Pakets
        var b = new ScanBuilder();
        var f = b.Folder("", 0, Allow(S.BuiltinUsers, Read), Allow(AppCapability, Read), Allow(S.System, Full));
        f.OwnerSid = S.System;
        Assert.True(FindingRules.IsWindowsManaged(f));
        Assert.Empty(Keys(b));
    }

    [Fact]
    public void Datenordner_mit_gleichen_Rechten_bleibt_ein_Befund()
    {
        // Gegenprobe: ohne Windows-eigene Kennung ist „Benutzer dürfen lesen“ auf einem Dateiserver ein echter Befund
        var b = new ScanBuilder();
        var f = b.Folder("", 0, Allow(S.BuiltinUsers, Read), Allow(S.System, Full), Allow(S.Administrators, Full));
        f.OwnerSid = S.System;
        Assert.False(FindingRules.IsWindowsManaged(f));
        Assert.Equal([("ZT02", S.BuiltinUsers)], Keys(b));
    }

    [Fact]
    public void Tiefe_Ausnahme_nur_fuer_Administratoren_und_Windows_ist_kein_Befund()
    {
        // C:\ProgramData\Packages\<Paket>\<SID>: geschützt, nur SYSTEM und Administratoren – schottet ab, versteckt nichts
        var b = new ScanBuilder();
        b.Folder("", 0, Allow(S.Administrators, Full), Allow(Sid(1001), Read));
        var adminOnly = Broken(@"\A\B\C", Allow(S.System, Full), Allow(S.Administrators, Full), Allow(SidCatalog.NetworkService, Read),
            Allow(S.CreatorOwner, Full, propagation: PropagationFlags.InheritOnly), Deny(S.Everyone, FileSystemRights.Delete));
        var withGroup = Broken(@"\A\B\D", Allow(S.Administrators, Full), Allow(Sid(1001), Read));
        var withEveryone = Broken(@"\A\B\E", Allow(S.Administrators, Full), Allow(S.Everyone, FileSystemRights.Traverse));

        FolderNode Broken(string relative, params AceEntry[] aces)
        {
            var f = new FolderNode { Path = ScanBuilder.Share + relative, Depth = 3, ShareUnc = ScanBuilder.Share, OwnerSid = S.Administrators, InheritanceBroken = true };
            f.Aces.AddRange(aces);
            b.Scan.Folders.Add(f);
            return f;
        }

        Assert.Equal([withGroup.Path, withEveryone.Path], FindingRules.Evaluate(b.Scan).Where(f => f.RuleId == "ZT06").Select(f => f.Path));
        Assert.False(FindingRules.GrantsBeyondAdmins(adminOnly));
    }

    [Fact]
    public void Dienst_SID_eines_nicht_installierten_Dienstes_ist_nicht_verwaist()
    {
        const string service = "S-1-5-80-3906544942-1489856346-3706913989-164347954-1900376235";
        var b = new ScanBuilder().Known(service, $"Nicht installierter Dienst ({service})");
        b.Folder("", 0, Allow(service, Full), Allow(S.Administrators, Full));
        Assert.Empty(Keys(b));
    }
}
