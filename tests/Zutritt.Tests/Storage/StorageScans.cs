using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;

namespace Zutritt.Tests.Storage;

/// <summary>Läufe für die Speicher-Tests: Randfälle und ein großer synthetischer Lauf.</summary>
internal static class StorageScans
{
    public const FileSystemRights GenericRead = unchecked((FileSystemRights)0x80000000);
    public const FileSystemRights GenericAll = (FileSystemRights)0x10000000;

    /// <summary>Beispiel-Lauf mit Befunden aller Schweregrade (SampleScan selbst hat keine).</summary>
    public static ScanResult SampleWithFindings(DateTimeOffset? started = null)
    {
        var scan = SampleScan.Create(started);
        scan.Findings.Add(new Finding { RuleId = "ZT01", Severity = Severity.Hoch, Path = @"\\FS01\Labor\Austausch", Sid = SampleScan.Everyone, Message = "„Jeder“ darf in „Austausch“ ändern." });
        scan.Findings.Add(new Finding { RuleId = "ZT05", Severity = Severity.Hoch, Path = @"\\FS01\Labor\QC", Sid = SampleScan.Max, Message = "Max Muster hat Vollzugriff." });
        scan.Findings.Add(new Finding { RuleId = "ZT02", Severity = Severity.Mittel, Path = @"\\FS01\Labor\Archiv", Sid = SampleScan.AuthenticatedUsers, Message = "Alle angemeldeten Benutzer dürfen lesen." });
        scan.Findings.Add(new Finding { RuleId = "ZT06", Severity = Severity.Niedrig, Path = @"\\FS01\Labor\Projekte\2019\Alt\Rohdaten", Message = "Vererbung in Tiefe 4 unterbrochen." });
        scan.Findings.Add(new Finding { RuleId = "ZT09", Severity = Severity.Info, Path = @"\\FS01\Labor\Personal", Message = "Ordner nicht lesbar: Zugriff verweigert." });
        return scan;
    }

    /// <summary>Alles, was beim Speichern schiefgehen könnte: Offsets, null vs. leer, generische Rechte, Umlaute, lange Pfade.</summary>
    public static ScanResult Edge()
    {
        var scan = new ScanResult
        {
            StartedAt = new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.FromMinutes(345)).AddTicks(1_234_567),
            FinishedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 1, TimeSpan.FromHours(-7)).AddTicks(1),
            Machine = "PRÜF-PC",
            ScannedBy = @"PRÜF-PC\Jürgen Größ",
            ToolVersion = "1.2.3-beta+5e562a9",
            Request = new ScanRequest
            {
                Targets = [@"C:\Daten; Archiv\Ä", @"\\fs01\Freigabe\", "."],
                IncludeFiles = true,
                MaxDepth = 7,
                SkipAdminShares = false,
                ResolveGroups = false,
                DeepInheritanceBreakDepth = 5,
            },
            FoldersVisited = 5_000_000_000L,
            FilesVisited = long.MaxValue,
        };

        scan.Shares.Add(new ShareInfo { Server = "PRÜF-PC", Name = "C$", LocalPath = @"C:\", IsAdminShare = true, PermissionsReadable = false });
        scan.Shares.Add(new ShareInfo
        {
            Server = "fs01", Name = "Freigabe", Remark = "„Prüfung“ 📁 'x' \"y\" ; -- DROP TABLE scan;",
            Permissions =
            [
                new ShareAce(SampleScan.Everyone, ShareAccess.Lesen, false),
                new ShareAce(SampleScan.Max, ShareAccess.Vollzugriff, true),
                new ShareAce(SampleScan.Administrators, ShareAccess.Keine, false),
                new ShareAce(SampleScan.Administrators, ShareAccess.Aendern, false),  // doppelt mit anderem Recht
            ],
        });
        scan.Shares.Add(new ShareInfo { Server = "fs01", Name = "Leer", Permissions = [] });

        scan.Folders.Add(new FolderNode { Path = @"C:\Daten; Archiv\Ä", Depth = 0 });  // ohne Einträge, ohne Besitzer, ShareUnc null
        var file = new FolderNode { Path = @"C:\Daten; Archiv\Ä\geheim.txt", Depth = 1, IsFile = true, ShareUnc = "", OwnerSid = SampleScan.Max, Error = "Pfad zu lang:\r\nZeile 2", InheritanceDiffers = true };
        file.Aces.Add(new AceEntry { Sid = SampleScan.CreatorOwner, Rights = GenericAll, Inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, Propagation = PropagationFlags.InheritOnly });
        scan.Folders.Add(file);

        var longPath = @"\\fs01\Freigabe\" + string.Join('\\', Enumerable.Range(1, 60).Select(i => $"Ebene-{i:D2}-Überlänge"));
        var deep = new FolderNode { Path = longPath, Depth = 60, ShareUnc = @"\\fs01\Freigabe", OwnerSid = SampleScan.Verwaist, InheritanceBroken = true };
        deep.Aces.Add(new AceEntry { Sid = SampleScan.Everyone, Rights = GenericRead | (FileSystemRights)0x20000000, Inheritance = InheritanceFlags.ContainerInherit, Propagation = PropagationFlags.InheritOnly | PropagationFlags.NoPropagateInherit });
        deep.Aces.Add(new AceEntry { Sid = SampleScan.Max, Rights = SampleScan.Modify, IsDeny = true, IsInherited = true, Inheritance = InheritanceFlags.ObjectInherit });
        deep.Aces.Add(new AceEntry { Sid = SampleScan.Erika, Rights = 0 });
        deep.Aces.Add(new AceEntry { Sid = SampleScan.Klaus, Rights = unchecked((FileSystemRights)(-1)) });
        deep.Aces.Add(new AceEntry { Sid = SampleScan.Erika, Rights = SampleScan.Read });  // gleiche SID zweimal
        scan.Folders.Add(deep);

        // Schlüssel klein, Principal.Sid groß: der Schlüssel ist, was ACEs referenzieren
        scan.Principals["s-1-5-21-1-2-3-1001"] = new Principal { Sid = "S-1-5-21-1-2-3-1001", Name = @"PRÜF-PC\jürgen", DisplayName = "Jürgen Größ", Kind = PrincipalKind.Benutzer, IsLocal = true };
        var group = new Principal { Sid = "S-1-5-21-1-2-3-1002", Name = @"PRÜF-PC\Prüfer", Kind = PrincipalKind.Gruppe, IsLocal = true, MembersUnknown = true };
        group.Members.AddRange(["s-1-5-21-1-2-3-1001", SampleScan.Everyone, SampleScan.Everyone, "S-1-5-21-9-9-9-1234"]);
        scan.Principals[group.Sid] = group;
        foreach (var kind in Enum.GetValues<PrincipalKind>())
            scan.Principals[$"S-1-5-21-7-7-7-{3000 + (int)kind}"] = new Principal { Sid = $"S-1-5-21-7-7-7-{3000 + (int)kind}", Name = $"Art {kind}", Kind = kind };
        scan.Principals[SampleScan.Klaus] = new Principal { Sid = SampleScan.Klaus, Name = "", DisplayName = "", Kind = PrincipalKind.Benutzer, IsDisabled = true };
        scan.Principals[SampleScan.Verwaist] = new Principal { Sid = SampleScan.Verwaist, Name = SampleScan.Verwaist, Kind = PrincipalKind.Verwaist };

        foreach (var severity in Enum.GetValues<Severity>())
            scan.Findings.Add(new Finding { RuleId = $"ZT{(int)severity + 1:D2}", Severity = severity, Path = longPath, Sid = SampleScan.Max, Message = $"Befund „{severity}“\nmit Zeilenumbruch" });
        scan.Findings.Add(new Finding { RuleId = "ZT06", Severity = Severity.Niedrig, Path = longPath, Sid = null, Message = "ohne Konto" });

        scan.Errors.Add(new ScanError(longPath, "Zugriff verweigert"));
        scan.Errors.Add(new ScanError(@"\\fs01\Freigabe\x", ""));
        return scan;
    }

    /// <summary>Großer Lauf: <paramref name="folders"/> Ordner mit je <paramref name="acesPerFolder"/> Einträgen.</summary>
    public static ScanResult Large(int folders, int acesPerFolder, DateTimeOffset? started = null)
    {
        var t = started ?? new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.FromHours(2));
        var scan = new ScanResult
        {
            StartedAt = t,
            FinishedAt = t.AddHours(2),
            Machine = "ADMIN-PC",
            ScannedBy = @"LAB\admin.max",
            ToolVersion = "1.0.0",
            Request = new ScanRequest { Targets = [@"\\FS01\Gross"] },
            FoldersVisited = folders * 10L,
        };
        scan.Shares.Add(new ShareInfo { Server = "FS01", Name = "Gross", LocalPath = @"E:\Gross", Permissions = [new ShareAce(SampleScan.Everyone, ShareAccess.Vollzugriff, false)] });

        const int PrincipalCount = 300;
        string Sid(int i) => $"{SampleScan.Domain}-{5000 + i}";
        for (var i = 0; i < PrincipalCount; i++)
        {
            var p = new Principal { Sid = Sid(i), Name = $@"LAB\konto{i:D3}", DisplayName = $"Konto {i}", Kind = i % 10 == 0 ? PrincipalKind.Gruppe : PrincipalKind.Benutzer, IsDisabled = i % 37 == 0 };
            if (p.Kind == PrincipalKind.Gruppe)
                for (var m = 1; m < 10; m++) p.Members.Add(Sid(i + m));
            scan.Principals[p.Sid] = p;
        }

        FileSystemRights[] rights = [SampleScan.Read, SampleScan.Modify, SampleScan.Full, GenericRead, FileSystemRights.ReadData | FileSystemRights.Synchronize];
        for (var i = 0; i < folders; i++)
        {
            var depth = i % 25;
            var f = new FolderNode
            {
                Path = $@"\\FS01\Gross\Bereich{i % 50:D2}\Ebene{depth:D2}\Ordner{i:D6}",
                Depth = depth,
                ShareUnc = @"\\FS01\Gross",
                OwnerSid = Sid(i % PrincipalCount),
                InheritanceBroken = i % 3 == 0,
                Error = i % 997 == 0 ? "Zugriff verweigert" : null,
            };
            for (var j = 0; j < acesPerFolder; j++)
            {
                f.Aces.Add(new AceEntry
                {
                    Sid = Sid((i + j * 7) % PrincipalCount),
                    Rights = rights[(i + j) % rights.Length],
                    IsDeny = (i + j) % 41 == 0,
                    IsInherited = j % 2 == 1,
                    Inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    Propagation = j == 4 ? PropagationFlags.InheritOnly : PropagationFlags.None,
                });
            }
            scan.Folders.Add(f);
        }

        for (var i = 0; i < folders / 20; i++)
            scan.Findings.Add(new Finding { RuleId = $"ZT{i % 13 + 1:D2}", Severity = (Severity)(i % 4), Path = scan.Folders[i * 20].Path, Sid = i % 5 == 0 ? null : Sid(i % PrincipalCount), Message = $"Befund {i}" });
        foreach (var f in scan.Folders.Where(f => f.Error is not null))
            scan.Errors.Add(new ScanError(f.Path, f.Error!));
        return scan;
    }
}
