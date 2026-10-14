using System.Security.AccessControl;
using System.Security.Principal;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Tests.TestSupport;
using static Zutritt.Tests.Scanning.FsTestHelper;

namespace Zutritt.Tests.Scanning;

public sealed class AclReaderTests
{
    private const InheritanceFlags CIOI = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    [Fact]
    public void Liest_Eintraege_exakt_in_Reihenfolge()
    {
        using var tmp = new TempFolder();
        var dir = tmp.Sub("exakt");
        var me = CurrentUserSid;
        SetRawDacl(dir, $"D:P(D;OICI;0x10116;;;{PowerUsers})(A;OICI;FA;;;SY)(A;OICIIO;GA;;;CO)(A;CI;0x1200a9;;;{NetworkService})(A;CINP;0x1301bf;;;{BackupOperators})(A;OICI;FA;;;{me})");

        var node = AclReader.Read(dir, 2, isFile: false);

        Assert.Null(node.Error);
        Assert.Equal(dir, node.Path);
        Assert.Equal(2, node.Depth);
        Assert.False(node.IsFile);
        Assert.True(node.InheritanceBroken);
        Assert.True(node.HasExplicitAces);
        Assert.Equal(
            [
                new AceEntry { Sid = PowerUsers, Rights = (FileSystemRights)0x10116, IsDeny = true, Inheritance = CIOI },
                new AceEntry { Sid = SystemSid, Rights = FileSystemRights.FullControl, Inheritance = CIOI },
                new AceEntry { Sid = CreatorOwner, Rights = (FileSystemRights)0x10000000, Inheritance = CIOI, Propagation = PropagationFlags.InheritOnly },
                new AceEntry { Sid = NetworkService, Rights = (FileSystemRights)0x1200A9, Inheritance = InheritanceFlags.ContainerInherit },
                new AceEntry { Sid = BackupOperators, Rights = (FileSystemRights)0x1301BF, Inheritance = InheritanceFlags.ContainerInherit, Propagation = PropagationFlags.NoPropagateInherit },
                new AceEntry { Sid = me, Rights = FileSystemRights.FullControl, Inheritance = CIOI },
            ], node.Aces);
        // Generische Rechte bleiben roh, die Abbildung macht Rights.ToLevel.
        Assert.Equal(AccessLevel.Vollzugriff, node.Aces[2].Level);
        Assert.True(node.Aces[2].InheritOnly);
    }

    [Fact]
    public void Geerbte_Eintraege_und_Besitzer()
    {
        using var tmp = new TempFolder();
        var parent = tmp.Sub("eltern");
        SetRawDacl(parent, $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1301bf;;;{NetworkService})");
        var child = tmp.Sub("eltern", "kind");

        var node = AclReader.Read(child, 1, isFile: false);

        Assert.Null(node.Error);
        Assert.False(node.InheritanceBroken);
        Assert.False(node.HasExplicitAces);
        Assert.All(node.Aces, a => Assert.True(a.IsInherited));
        Assert.Equal([SystemSid, CurrentUserSid, NetworkService], node.Aces.Select(a => a.Sid));
        var owner = new DirectoryInfo(child).GetAccessControl().GetOwner(typeof(SecurityIdentifier))!.Value;
        Assert.Equal(owner, node.OwnerSid);
    }

    [Fact]
    public void Stimmt_mit_der_DotNet_Sicht_ueberein()
    {
        // Gegenprobe über einen unabhängigen Weg (.NET liest per GetNamedSecurityInfo).
        using var tmp = new TempFolder();
        var dir = tmp.Sub("gegenprobe");
        Grant(dir, NetworkService, FileSystemRights.Modify);
        Grant(dir, PowerUsers, FileSystemRights.Write, type: AccessControlType.Deny);
        Grant(dir, BackupOperators, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit, PropagationFlags.InheritOnly);

        var node = AclReader.Read(dir, 0, false);
        var sec = new DirectoryInfo(dir).GetAccessControl();
        var expected = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => (r.IdentityReference.Value, (int)r.FileSystemRights, r.AccessControlType == AccessControlType.Deny, r.IsInherited, r.InheritanceFlags, r.PropagationFlags))
            .OrderBy(x => x.ToString()).ToList();
        var actual = node.Aces
            .Select(a => (a.Sid, (int)a.Rights, a.IsDeny, a.IsInherited, a.Inheritance, a.Propagation))
            .OrderBy(x => x.ToString()).ToList();

        Assert.Equal(expected, actual);
        Assert.Equal(sec.AreAccessRulesProtected, node.InheritanceBroken);
        Assert.Equal(sec.GetOwner(typeof(SecurityIdentifier))!.Value, node.OwnerSid);
    }

    [Fact]
    public void Vererbung_unterbrochen_wird_erkannt()
    {
        using var tmp = new TempFolder();
        var open = tmp.Sub("offen");
        var broken = tmp.Sub("geschuetzt");
        TempFolder.BreakInheritance(broken);

        Assert.False(AclReader.Read(open, 1, false).InheritanceBroken);
        var node = AclReader.Read(broken, 1, false);
        Assert.True(node.InheritanceBroken);
        // Die übernommenen Einträge sind jetzt explizit.
        Assert.True(node.HasExplicitAces);
    }

    [Fact]
    public void Datei_mit_eigenem_Recht()
    {
        using var tmp = new TempFolder();
        var file = Path.Combine(tmp.Sub("d"), "bericht.txt");
        WriteFile(file);
        GrantFile(file, NetworkService, FileSystemRights.Read);

        var node = AclReader.Read(file, 2, isFile: true);

        Assert.Null(node.Error);
        Assert.True(node.IsFile);
        Assert.Contains(node.Aces, a => a.Sid == NetworkService && !a.IsInherited && !a.IsDeny);
    }

    [Fact]
    public void Null_DACL_wird_Jeder_Vollzugriff()
    {
        using var tmp = new TempFolder();
        var dir = tmp.Sub("null-dacl");
        try
        {
            SetRawDacl(dir, "D:NO_ACCESS_CONTROL", protect: false);
            var node = AclReader.Read(dir, 1, false);
            Assert.Null(node.Error);
            var ace = Assert.Single(node.Aces);
            Assert.Equal("S-1-1-0", ace.Sid);
            Assert.Equal(AccessLevel.Vollzugriff, ace.Level);
            Assert.False(ace.IsInherited);
            Assert.False(ace.IsDeny);
        }
        finally { RestoreAccess(dir); }
    }

    [Fact]
    public void Leere_DACL_ist_kein_Zugriff_aber_lesbar()
    {
        // Besitzer darf trotzdem Rechte lesen und ändern (implizite Besitzerrechte).
        using var tmp = new TempFolder();
        var dir = tmp.Sub("leer");
        try
        {
            SetRawDacl(dir, "D:P");
            var node = PrivilegeScope.WithoutBackupPrivilege(() => AclReader.Read(dir, 1, false));
            Assert.Null(node.Error);
            Assert.Empty(node.Aces);
            Assert.True(node.InheritanceBroken);
        }
        finally { RestoreAccess(dir); }
    }

    [Fact]
    public void Nur_Recht_Berechtigungen_lesen_genuegt()
    {
        // Regression: CreateFile verlangt zusätzlich SYNCHRONIZE – damit wäre ein Ordner, auf den man nur
        // „Berechtigungen lesen“ hat, fälschlich „nicht lesbar“.
        using var tmp = new TempFolder();
        var dir = tmp.Sub("nur-rc");
        var file = Path.Combine(dir, "datei.txt");
        WriteFile(file);
        try
        {
            SetRawDacl(dir, $"D:P(A;OICI;FA;;;SY)(A;;RC;;;{CurrentUserSid})");
            SetRawDacl(file, $"D:P(A;;FA;;;SY)(A;;RC;;;{CurrentUserSid})");
            var (dirNode, fileNode) = PrivilegeScope.WithoutBackupPrivilege(() => (AclReader.Read(dir, 1, false), AclReader.Read(file, 2, true)));
            Assert.Null(dirNode.Error);
            Assert.Equal([SystemSid, CurrentUserSid], dirNode.Aces.Select(a => a.Sid));
            Assert.Null(fileNode.Error);
            Assert.Equal([SystemSid, CurrentUserSid], fileNode.Aces.Select(a => a.Sid));
        }
        finally
        {
            RestoreAccess(file);
            RestoreAccess(dir);
        }
    }

    [Fact]
    public void Nicht_lesbare_Rechte_werden_Fehler_ohne_Ausnahme()
    {
        using var tmp = new TempFolder();
        var dir = tmp.Sub("gesperrt");
        try
        {
            // BESITZERRECHTE entziehen dem Besitzer das implizite Leserecht – nur Rechte ändern bleibt (zum Aufräumen).
            SetRawDacl(dir, $"D:P(A;;WD;;;{OwnerRights})");
            var node = PrivilegeScope.WithoutBackupPrivilege(() => AclReader.Read(dir, 3, false));
            Assert.Equal("Berechtigungen nicht lesbar: Zugriff verweigert", node.Error);
            Assert.Empty(node.Aces);
            Assert.Equal(3, node.Depth);
        }
        finally { RestoreAccess(dir); }
    }

    [AdminFact]
    public async Task Mit_aktivem_Sicherungsrecht_auch_gesperrte_Rechte_lesbar()
    {
        // Mit eingeschaltetem SeBackupPrivilege umgeht das Öffnen mit Sicherungssemantik die Zugriffsliste.
        using var tmp = new TempFolder();
        var dir = tmp.Sub("gesperrt");
        try
        {
            SetRawDacl(dir, $"D:P(A;;WD;;;{OwnerRights})(A;;FA;;;{SystemSid})");
            var node = await PrivilegeScope.WithBackupPrivilegeAsync(() => Task.FromResult(AclReader.Read(dir, 1, false)));
            Assert.Null(node.Error);
            Assert.Equal([OwnerRights, SystemSid], node.Aces.Select(a => a.Sid));
        }
        finally { RestoreAccess(dir); }
    }

    [Fact]
    public void NT4_Liste_wird_wie_von_Windows_als_geerbt_erkannt()
    {
        // Ohne „automatisch geerbt“ am Elternordner schreibt Windows die Einträge der Kinder ohne Geerbt-Kennzeichen.
        using var tmp = new TempFolder();
        var parent = tmp.Sub("nt4");
        SetRawDacl(parent, $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{CurrentUserSid})(A;CI;0x1200a9;;;{NetworkService})");
        var child = tmp.Sub("nt4", "kind");
        // Zusätzlicher Eintrag, ebenfalls ohne Kennzeichen (wie von alten Werkzeugen geschrieben).
        SetRawDacl(child, $"D:(A;OICI;FA;;;SY)(A;OICI;FA;;;{CurrentUserSid})(A;CI;0x1200a9;;;{NetworkService})(A;OICI;0x1301bf;;;{PowerUsers})", protect: false);

        var node = AclReader.Read(child, 1, false);

        Assert.Null(node.Error);
        Assert.False(node.InheritanceBroken);
        Assert.Equal([PowerUsers], node.Aces.Where(a => !a.IsInherited).Select(a => a.Sid));
        Assert.Equal([SystemSid, CurrentUserSid, NetworkService], node.Aces.Where(a => a.IsInherited).Select(a => a.Sid));
        // Gegenprobe: .NET (über GetNamedSecurityInfo) sieht dasselbe.
        var rules = new DirectoryInfo(child).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        Assert.Equal(node.Aces.Select(a => (a.Sid, a.IsInherited)).OrderBy(x => x.ToString()),
            rules.Select(r => (r.IdentityReference.Value, r.IsInherited)).OrderBy(x => x.ToString()));
    }

    [Fact]
    public void NT4_Liste_ohne_Elterneintraege_gilt_als_geschuetzt()
    {
        using var tmp = new TempFolder();
        var parent = tmp.Sub("nt4");
        SetRawDacl(parent, $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;{CurrentUserSid})");
        var child = tmp.Sub("nt4", "eigen");
        SetRawDacl(child, $"D:(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1200a9;;;{NetworkService})", protect: false);

        var node = AclReader.Read(child, 1, false);

        Assert.Null(node.Error);
        Assert.True(node.InheritanceBroken);
        Assert.All(node.Aces, a => Assert.False(a.IsInherited));
        Assert.Equal(new DirectoryInfo(child).GetAccessControl().AreAccessRulesProtected, node.InheritanceBroken);
    }

    [Theory]
    [InlineData(@"C:\x", @"C:\")]
    [InlineData(@"C:\x\y", @"C:\x")]
    [InlineData(@"C:\x\y ", @"C:\x")]
    [InlineData(@"C:\x\", @"C:\")]
    [InlineData(@"C:\", null)]
    [InlineData(@"\\FS01\Labor", null)]
    [InlineData(@"\\FS01\Labor\QC", @"\\FS01\Labor")]
    [InlineData(@"\\FS01\Labor\QC\Roh", @"\\FS01\Labor\QC")]
    [InlineData(@"\\FS01", null)]
    [InlineData(@"\\?\Volume{0c1d}\x\y", @"\\?\Volume{0c1d}\x")]
    [InlineData(@"\\?\Volume{0c1d}\x", @"\\?\Volume{0c1d}\")]   // Wurzel mit „\“, sonst öffnete man das Laufwerk selbst
    [InlineData(@"\\?\Volume{0c1d}\", null)]
    [InlineData(@"\\.\C:\x", @"\\.\C:\")]
    public void ParentOf(string path, string? expected) => Assert.Equal(expected, AclReader.ParentOf(path));

    [Fact]
    public void Volume_GUID_Pfad_liest_denselben_Ordner()
    {
        // Regression: „\\?\Volume{…}\x“ wurde zu „<aktuelles Verzeichnis>\Volume{…}\x“ – also ein ganz anderer Pfad.
        using var tmp = new TempFolder();
        var dir = tmp.Sub("vol");
        Grant(dir, NetworkService, FileSystemRights.Modify);
        var volume = TestNative.VolumeGuidPath(Path.GetPathRoot(dir)!);
        var viaVolume = volume + dir[3..];
        var viaDevice = @"\\.\" + dir;

        var expected = AclReader.Read(dir, 1, false);
        foreach (var path in new[] { viaVolume, viaDevice })
        {
            var node = AclReader.Read(path, 1, false);
            Assert.Null(node.Error);
            Assert.Equal(path, node.Path);
            Assert.Equal(expected.Aces, node.Aces);
            Assert.Equal(expected.OwnerSid, node.OwnerSid);
        }
    }

    [Fact]
    public void Nicht_vorhandener_Pfad()
    {
        using var tmp = new TempFolder();
        var node = AclReader.Read(Path.Combine(tmp.Path, "gibt-es-nicht"), 1, false);
        Assert.Equal("Berechtigungen nicht lesbar: Pfad nicht gefunden", node.Error);
        Assert.Empty(node.Aces);
    }

    [Fact]
    public void Ungueltiger_Pfad()
    {
        var node = AclReader.Read("C:\\a\0b", 0, false);
        Assert.Equal("Ungültiger Pfad", node.Error);
    }

    [Fact]
    public void Junction_liefert_eigene_Rechte_nicht_die_des_Ziels()
    {
        using var tmp = new TempFolder();
        var target = tmp.Sub("ziel");
        Grant(target, NetworkService, FileSystemRights.Modify);
        var link = Path.Combine(tmp.Path, "verweis");
        using var junction = Junction(link, target);

        var own = AclReader.Read(link, 1, false);
        Assert.Null(own.Error);
        Assert.DoesNotContain(own.Aces, a => a.Sid == NetworkService);

        var followed = AclReader.Read(link, 0, false, followLinks: true);
        Assert.Contains(followed.Aces, a => a.Sid == NetworkService && !a.IsInherited);
    }

    [Fact]
    public void Lange_Pfade_ueber_260_Zeichen()
    {
        using var tmp = new TempFolder();
        var parts = Enumerable.Range(0, 12).Select(i => $"ordner-mit-langem-namen-{i:00}").ToArray();
        var deep = tmp.Sub(parts);
        Assert.True(deep.Length > 300);
        SetRawDacl(deep, $"D:P(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1200a9;;;{NetworkService})");

        var node = AclReader.Read(deep, parts.Length, false);

        Assert.Null(node.Error);
        Assert.Equal(deep, node.Path);
        Assert.True(node.InheritanceBroken);
        Assert.Contains(node.Aces, a => a.Sid == NetworkService);
    }

    [Fact]
    public void Leerzeichen_am_Namensende_liest_den_richtigen_Ordner()
    {
        // Ohne „\\?\“ würde Windows „x “ zu „x“ kürzen und die Rechte des Nachbarn liefern.
        using var tmp = new TempFolder();
        var plain = tmp.Sub("x");
        var spaced = Path.Combine(tmp.Path, "x ");
        Directory.CreateDirectory(@"\\?\" + spaced);
        try
        {
            SetRawDacl(spaced, $"D:P(A;OICI;FA;;;{CurrentUserSid})(A;OICI;0x1200a9;;;{NetworkService})");

            var spacedNode = AclReader.Read(spaced, 1, false);
            var plainNode = AclReader.Read(plain, 1, false);

            Assert.Null(spacedNode.Error);
            Assert.Equal(spaced, spacedNode.Path);
            Assert.Contains(spacedNode.Aces, a => a.Sid == NetworkService);
            Assert.DoesNotContain(plainNode.Aces, a => a.Sid == NetworkService);
        }
        finally { Directory.Delete(@"\\?\" + spaced); }
    }

    [Fact]
    public void Relativer_Pfad_und_Punkte_werden_aufgeloest()
    {
        using var tmp = new TempFolder();
        var dir = tmp.Sub("a", "b");
        var node = AclReader.Read(Path.Combine(tmp.Path, "a", "..", "a", "b"), 2, false);
        Assert.Null(node.Error);
        Assert.Equal(dir, node.Path);
    }

    [Fact]
    public void Unbekannte_Eintragstypen_machen_den_Knoten_unvollstaendig()
    {
        var raw = new RawSecurityDescriptor("D:P(A;;FA;;;SY)");
        raw.DiscretionaryAcl!.InsertAce(1, new CustomAce((AceType)0x15, AceFlags.None, [1, 2, 3, 4]));
        var node = AclReader.FromDescriptor(@"C:\x", 0, false, raw);
        Assert.Equal("Berechtigungen nicht vollständig auswertbar: unbekannter Eintragstyp 21", node.Error);
        Assert.Single(node.Aces);
    }

    [Fact]
    public void Bedingte_Verweigerung_zaehlt_nicht_bedingtes_Erlauben_schon()
    {
        var raw = new RawSecurityDescriptor("O:BAD:(XD;;FA;;;WD;(Member_of {SID(BA)}))(XA;;0x1200a9;;;AU;(Member_of {SID(BA)}))(A;;FA;;;SY)");
        var node = AclReader.FromDescriptor(@"C:\x", 0, false, raw);
        Assert.Null(node.Error);
        Assert.Equal(["S-1-5-11", "S-1-5-18"], node.Aces.Select(a => a.Sid));
        Assert.Equal("S-1-5-32-544", node.OwnerSid);
    }
}
