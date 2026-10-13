using System.Reflection;
using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Core.Storage;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

public sealed class ScanStoreRoundtripTests
{
    [Fact]
    public void Beispiel_Lauf_kommt_vollstaendig_zurueck()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = StorageScans.SampleWithFindings();

        var id = store.Save(scan);
        var loaded = store.Load(id);

        Assert.NotNull(loaded);
        DeepAssert.Equal(scan, loaded);
        // Stichproben, damit der Vergleich nicht unbemerkt leer läuft
        Assert.Equal(6, loaded.Folders.Count);
        Assert.Equal(15, loaded.Principals.Count);
        Assert.Equal([SampleScan.Max, SampleScan.Erika, SampleScan.Klaus], loaded.Principals[SampleScan.DomainUsers].Members);
        Assert.True(loaded.Principals[SampleScan.Klaus].IsDisabled);
        Assert.Equal(PrincipalKind.Verwaist, loaded.Principals[SampleScan.Verwaist].Kind);
        Assert.Equal(new ShareAce(SampleScan.Everyone, ShareAccess.Vollzugriff, false), Assert.Single(loaded.Shares[0].Permissions));
        Assert.Equal("Zugriff verweigert", loaded.Folders.Single(f => f.Path.EndsWith("Personal")).Error);
        Assert.Contains(loaded.Folders.Single(f => f.Path.EndsWith("Archiv")).Aces, a => a.IsDeny && a.Sid == SampleScan.Max);
        Assert.Equal(1234, loaded.FoldersVisited);
        Assert.Equal(5, loaded.Findings.Count);
    }

    [Fact]
    public void Randfaelle_kommen_unveraendert_zurueck()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = StorageScans.Edge();

        var loaded = store.Load(store.Save(scan))!;

        DeepAssert.Equal(scan, loaded);
        Assert.Equal(TimeSpan.FromMinutes(345), loaded.StartedAt.Offset);
        Assert.Equal(scan.StartedAt.Ticks, loaded.StartedAt.Ticks);
        Assert.Null(loaded.Folders[0].ShareUnc);
        Assert.Equal("", loaded.Folders[1].ShareUnc);
        Assert.Null(loaded.Folders[0].Error);
        Assert.Equal(StorageScans.GenericAll, loaded.Folders[1].Aces[0].Rights);
        Assert.Equal(unchecked((FileSystemRights)(-1)), loaded.Folders[2].Aces[3].Rights);
        Assert.Equal(PropagationFlags.InheritOnly | PropagationFlags.NoPropagateInherit, loaded.Folders[2].Aces[0].Propagation);
        Assert.False(loaded.Shares[0].PermissionsReadable);
        Assert.Empty(loaded.Shares[0].Permissions);
        Assert.Equal(7, loaded.Request.MaxDepth);
        Assert.False(loaded.Request.ResolveGroups);
        Assert.Null(loaded.Findings[^1].Sid);
        Assert.Equal(long.MaxValue, loaded.FilesVisited);

        // Schlüssel und Principal.Sid dürfen sich unterscheiden; Wörterbuch bleibt unabhängig von Groß/Klein
        var key = loaded.Principals.Keys.First();
        Assert.Equal("s-1-5-21-1-2-3-1001", key);
        Assert.Equal("S-1-5-21-1-2-3-1001", loaded.Principals[key].Sid);
        Assert.Same(loaded.Principals[key], loaded.Principals["S-1-5-21-1-2-3-1001"]);
        Assert.Same(StringComparer.OrdinalIgnoreCase, loaded.Principals.Comparer);
    }

    [Fact]
    public void Leerer_Lauf_kommt_zurueck()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = new ScanResult { StartedAt = DateTimeOffset.UnixEpoch, FinishedAt = DateTimeOffset.UnixEpoch, Machine = "", Request = new ScanRequest { Targets = [] } };

        DeepAssert.Equal(scan, store.Load(store.Save(scan)));
    }

    [Fact]
    public void Save_setzt_Nummern_fuer_Lauf_und_Ordner()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var first = SampleScan.Create();
        var second = SampleScan.Create();

        var id1 = store.Save(first);
        var id2 = store.Save(second);

        Assert.Equal(id1, first.Id);
        Assert.Equal(id2, second.Id);
        Assert.True(id1 > 0);
        Assert.True(id2 > id1);
        var folderIds = first.Folders.Concat(second.Folders).Select(f => f.Id).ToList();
        Assert.All(folderIds, fid => Assert.True(fid > 0));
        Assert.Equal(folderIds.Count, folderIds.Distinct().Count());
        Assert.Equal(first.Folders.Select(f => f.Id), store.Load(id1)!.Folders.Select(f => f.Id));
    }

    [Fact]
    public void Erneutes_Speichern_legt_neuen_Lauf_an_und_laesst_den_alten_unveraendert()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = SampleScan.Create();
        var id1 = store.Save(scan);
        var snapshot = store.Load(id1)!;

        scan.Findings.Add(new Finding { RuleId = "ZT99", Severity = Severity.Info, Path = "x", Message = "nachträglich" });
        var id2 = store.Save(scan);

        Assert.NotEqual(id1, id2);
        Assert.Equal(id2, scan.Id);
        DeepAssert.Equal(snapshot, store.Load(id1));
        Assert.Single(store.Load(id2)!.Findings);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Unbekannte_Nummer_ergibt_null()
    {
        using var db = new TestDb();
        using var store = db.Open();
        store.Save(SampleScan.Create());

        Assert.Null(store.Load(0));
        Assert.Null(store.Load(-1));
        Assert.Null(store.Load(999_999));
    }

    [Fact]
    public void Daten_ueberleben_Schliessen_und_Oeffnen()
    {
        using var db = new TestDb();
        var scan = StorageScans.Edge();
        long id;
        using (var store = db.Open()) id = store.Save(scan);

        using var reopened = db.Open();
        DeepAssert.Equal(scan, reopened.Load(id));
    }

    [Fact]
    public void Fluechtiger_Speicher_funktioniert()
    {
        using var store = new ScanStore(":memory:");
        var scan = StorageScans.SampleWithFindings();

        DeepAssert.Equal(scan, store.Load(store.Save(scan)));
        Assert.Equal(":memory:", store.DatabasePath);
    }

    [Fact]
    public void Rechte_stehen_vorzeichenlos_in_der_Datenbank()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var scan = new ScanResult { Request = new ScanRequest { Targets = ["x"] } };
        var f = new FolderNode { Path = "x" };
        f.Aces.Add(new AceEntry { Sid = SampleScan.Everyone, Rights = StorageScans.GenericRead });
        scan.Folders.Add(f);
        store.Save(scan);

        // Datenbank-Werkzeuge zeigen 2147483648 statt -2147483648 – für Prüfer lesbar
        Assert.Equal(0x80000000L, TestDb.Scalar(store.Connection, "SELECT rights FROM ace;"));
        Assert.Equal(StorageScans.GenericRead, store.Load(scan.Id)!.Folders[0].Aces[0].Rights);
    }

    [Fact]
    public void Unbekannte_Kontoart_einer_neueren_Version_wird_Unbekannt()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var id = store.Save(SampleScan.Create());
        TestDb.Execute(store.Connection, $"UPDATE principal SET kind = 'Dienstkonto' WHERE sid = '{SampleScan.Max}';");
        TestDb.Execute(store.Connection, $"UPDATE principal SET kind = '3' WHERE sid = '{SampleScan.Erika}';");

        var loaded = store.Load(id)!;

        Assert.Equal(PrincipalKind.Unbekannt, loaded.Principals[SampleScan.Max].Kind);
        Assert.Equal(PrincipalKind.Unbekannt, loaded.Principals[SampleScan.Erika].Kind);
    }

    [Fact]
    public void Beschaedigte_Daten_fuehren_zu_deutscher_Fehlermeldung_statt_Ersatzwerten()
    {
        using var db = new TestDb();
        using var store = db.Open();
        var id = store.Save(SampleScan.Create());
        TestDb.Execute(store.Connection, $"UPDATE scan SET request_json = '{{kaputt' WHERE id = {id};");

        var ex = Assert.Throws<ScanStoreException>(() => store.Load(id));
        Assert.Contains("ungültige Daten", ex.Message);
        Assert.Contains($"Lauf {id}", ex.Message);
    }

    [Fact]
    public void DeepAssert_findet_Abweichungen_in_der_Tiefe()
    {
        // Gegenprobe: ohne sie könnte ein kaputter Vergleich alle Roundtrip-Tests grün machen
        var a = SampleScan.Create();
        Assert.Null(DeepAssert.FindDifference(a, SampleScan.Create(), "scan"));

        var b = SampleScan.Create();
        b.Folders[1].Aces[3] = b.Folders[1].Aces[3] with { Rights = SampleScan.Modify };
        Assert.Contains("Folders[1].Aces[3].Rights", DeepAssert.FindDifference(a, b, "scan"));

        var c = SampleScan.Create();
        c.StartedAt = c.StartedAt.ToOffset(TimeSpan.Zero);  // gleicher Zeitpunkt, anderer Offset
        Assert.Contains("StartedAt", DeepAssert.FindDifference(a, c, "scan"));

        var d = SampleScan.Create();
        d.Principals[SampleScan.QcTeam].Members.Add(SampleScan.Max);
        Assert.Contains("Members", DeepAssert.FindDifference(a, d, "scan"));

        var e = SampleScan.Create();
        e.Request = e.Request with { MaxDepth = 3 };
        Assert.Contains("Request.MaxDepth", DeepAssert.FindDifference(a, e, "scan"));
    }

    /// <summary>
    /// Stolperdraht: Bekommt das Modell ein neues Feld, muss der Speicher es kennen – sonst fehlt es
    /// nach dem Laden stillschweigend im Prüfbericht. Dann Schema-Migration ergänzen und diese Liste anpassen.
    /// </summary>
    [Theory]
    [InlineData(typeof(ScanResult), "Errors,FilesVisited,Findings,FinishedAt,Folders,FoldersVisited,Id,Machine,Principals,Request,ScannedBy,Shares,StartedAt,ToolVersion")]
    [InlineData(typeof(ScanRequest), "DeepInheritanceBreakDepth,IncludeFiles,MaxDepth,ResolveGroups,SkipAdminShares,Targets")]
    [InlineData(typeof(ShareInfo), "IsAdminShare,LocalPath,Name,Permissions,PermissionsReadable,Remark,Server,UncPath")]
    [InlineData(typeof(ShareAce), "Access,IsDeny,Sid")]
    [InlineData(typeof(FolderNode), "Aces,Depth,Error,HasExplicitAces,Id,InheritanceBroken,IsFile,IsRoot,OwnerSid,Path,ShareUnc")]
    [InlineData(typeof(AceEntry), "Inheritance,InheritOnly,IsDeny,IsInherited,Level,Propagation,Rights,Sid")]
    [InlineData(typeof(Principal), "DisplayName,IsDisabled,IsLocal,Kind,Label,Members,MembersUnknown,Name,Sid")]
    [InlineData(typeof(Finding), "Key,Message,Path,RuleId,Severity,Sid")]
    [InlineData(typeof(ScanError), "Message,Path")]
    public void Speicher_kennt_alle_Felder_des_Modells(Type type, string known)
    {
        var actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order(StringComparer.Ordinal);
        Assert.Equal(known.Split(',').Order(StringComparer.Ordinal), actual);
    }
}
