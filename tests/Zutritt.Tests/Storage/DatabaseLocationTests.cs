using System.Security.AccessControl;
using System.Security.Principal;
using Zutritt.Core.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

/// <summary>
/// Auswahl des Standardpfads. Geprüft an Ersatzordnern mit echten Verweigern-Einträgen, damit der Test
/// nichts unter %ProgramData% anlegt.
/// </summary>
public sealed class DatabaseLocationTests
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    [Fact]
    public void Beschreibbarer_gemeinsamer_Ordner_wird_bevorzugt_und_angelegt()
    {
        using var tmp = new TempFolder();
        var programData = tmp.Sub("ProgramData");
        var local = tmp.Sub("Local");

        var path = DatabaseLocation.Choose(programData, local);

        Assert.Equal(Path.Combine(programData, "Zutritt", "zutritt.db"), path);
        Assert.True(Directory.Exists(Path.Combine(programData, "Zutritt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(programData, "Zutritt")));  // Schreibprobe aufgeräumt
    }

    [Fact]
    public void Nicht_anlegbarer_Ordner_fuehrt_ins_Profil()
    {
        using var tmp = new TempFolder();
        var programData = tmp.Sub("ProgramData");
        var local = tmp.Sub("Local");
        File.WriteAllText(Path.Combine(programData, "Zutritt"), "Datei statt Ordner");

        Assert.Equal(Path.Combine(local, "Zutritt", "zutritt.db"), DatabaseLocation.Choose(programData, local));
    }

    [Fact]
    public void Geerbtes_Schreibverbot_fuehrt_ins_Profil()
    {
        // Ein Verweigern-Eintrag für „Ordner erstellen“ hält einen Admin auf diesem Rechner nicht auf (gemessen) –
        // entscheidend ist deshalb die Schreibprobe im angelegten Ordner, nicht das Anlegen selbst.
        using var tmp = new TempFolder();
        var programData = tmp.Sub("ProgramData");
        var local = tmp.Sub("Local");

        WithDeny(programData, FileSystemRights.CreateFiles, () =>
            Assert.Equal(Path.Combine(local, "Zutritt", "zutritt.db"), DatabaseLocation.Choose(programData, local)),
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
    }

    [Fact]
    public void Vorhandener_Ordner_ohne_Schreibrecht_fuehrt_ins_Profil()
    {
        using var tmp = new TempFolder();
        var programData = tmp.Sub("ProgramData");
        var zutritt = tmp.Sub("ProgramData", "Zutritt");
        var local = tmp.Sub("Local");

        WithDeny(zutritt, FileSystemRights.CreateFiles, () =>
            Assert.Equal(Path.Combine(local, "Zutritt", "zutritt.db"), DatabaseLocation.Choose(programData, local)));
        // Nach dem Aufheben des Verbots ist derselbe Ordner wieder erste Wahl
        Assert.Equal(Path.Combine(zutritt, "zutritt.db"), DatabaseLocation.Choose(programData, local));
    }

    [Fact]
    public void Fremde_nicht_beschreibbare_Datei_fuehrt_ins_Profil()
    {
        // Typisch: ein Admin hat die Datei angelegt, ein normaler Benutzer darf den Ordner beschreiben, die Datei nicht
        using var tmp = new TempFolder();
        var programData = tmp.Sub("ProgramData");
        var local = tmp.Sub("Local");
        var file = Path.Combine(tmp.Sub("ProgramData", "Zutritt"), "zutritt.db");
        File.WriteAllBytes(file, []);

        WithDeny(file, FileSystemRights.WriteData, () =>
            Assert.Equal(Path.Combine(local, "Zutritt", "zutritt.db"), DatabaseLocation.Choose(programData, local)), isFile: true);
        Assert.Equal(file, DatabaseLocation.Choose(programData, local));
    }

    [Fact]
    public void Vorhandene_beschreibbare_Datei_bleibt_im_gemeinsamen_Ordner()
    {
        using var tmp = new TempFolder();
        var programData = tmp.Sub("ProgramData");
        var file = Path.Combine(tmp.Sub("ProgramData", "Zutritt"), "zutritt.db");
        using (var store = new ScanStore(file)) store.Save(SampleScan.Create());

        Assert.Equal(file, DatabaseLocation.Choose(programData, tmp.Sub("Local")));
        using var reopened = new ScanStore(file);
        Assert.Single(reopened.List());  // Probe hat die Datei nicht verändert
    }

    [Fact]
    public void Ohne_gemeinsamen_Ordner_gilt_das_Profil()
    {
        using var tmp = new TempFolder();
        var local = tmp.Sub("Local");

        Assert.Equal(Path.Combine(local, "Zutritt", "zutritt.db"), DatabaseLocation.Choose("", local));
    }

    [Fact]
    public void Echter_Standardpfad_liegt_unter_ProgramData_oder_LocalAppData()
    {
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Zutritt");
        var existed = Directory.Exists(programData);
        try
        {
            var path = ScanStore.DefaultPath;

            Assert.EndsWith(@"\Zutritt\zutritt.db", path);
            Assert.True(
                path == Path.Combine(programData, "zutritt.db") ||
                path == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zutritt", "zutritt.db"),
                path);
        }
        finally
        {
            // Nichts am System hinterlassen: nur den Ordner entfernen, den dieser Test leer angelegt hat
            if (!existed && Directory.Exists(programData) && !Directory.EnumerateFileSystemEntries(programData).Any())
                Directory.Delete(programData);
        }
    }

    /// <summary>Verweigern-Eintrag für das eigene Konto setzen, Aktion ausführen, Eintrag sicher wieder entfernen.</summary>
    private static void WithDeny(string path, FileSystemRights rights, Action action,
        InheritanceFlags inheritance = InheritanceFlags.None, bool isFile = false)
    {
        var rule = new FileSystemAccessRule(Me, rights, inheritance, PropagationFlags.None, AccessControlType.Deny);
        FileSystemSecurity Get() => isFile ? new FileInfo(path).GetAccessControl() : new DirectoryInfo(path).GetAccessControl();
        void Set(FileSystemSecurity s)
        {
            if (isFile) new FileInfo(path).SetAccessControl((FileSecurity)s);
            else new DirectoryInfo(path).SetAccessControl((DirectorySecurity)s);
        }

        var sec = Get();
        sec.AddAccessRule(rule);
        Set(sec);
        try
        {
            action();
        }
        finally
        {
            // Ein Verweigern-Eintrag schlägt das Erlauben, mit dem TempFolder aufräumt – also hier entfernen
            var clean = Get();
            clean.RemoveAccessRuleAll(new FileSystemAccessRule(Me, rights, AccessControlType.Deny));
            Set(clean);
        }
    }
}
