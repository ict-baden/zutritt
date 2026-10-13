using System.Security.AccessControl;
using Zutritt.Core.Model;

namespace Zutritt.Tests.Analysis;

/// <summary>Baut kleine, gezielte Läufe für die Auswertungs-Tests (Freigabe \\SRV\Daten, Domäne „T“).</summary>
internal sealed class ScanBuilder
{
    public const string Domain = "S-1-5-21-100-200-300";
    public const string Share = @"\\SRV\Daten";
    public const InheritanceFlags CIOI = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    public ScanResult Scan { get; } = new()
    {
        Machine = "TEST-PC",
        ScannedBy = @"T\pruefer",
        Request = new ScanRequest { Targets = [Share] },
    };

    public static string Sid(int rid) => $"{Domain}-{rid}";

    public ScanBuilder User(string sid, string name, bool disabled = false)
    {
        Scan.Principals[sid] = new Principal { Sid = sid, Name = name, Kind = PrincipalKind.Benutzer, IsDisabled = disabled };
        return this;
    }

    public ScanBuilder Group(string sid, string name, params string[] members) => Add(sid, name, PrincipalKind.Gruppe, members);

    public ScanBuilder Known(string sid, string name, params string[] members) => Add(sid, name, PrincipalKind.Bekannt, members);

    public ScanBuilder Orphan(string sid)
    {
        Scan.Principals[sid] = new Principal { Sid = sid, Name = sid, Kind = PrincipalKind.Verwaist };
        return this;
    }

    private ScanBuilder Add(string sid, string name, PrincipalKind kind, string[] members)
    {
        var p = new Principal { Sid = sid, Name = name, Kind = kind };
        p.Members.AddRange(members);
        Scan.Principals[sid] = p;
        return this;
    }

    /// <summary>Freigabe \\SRV\Daten mit den angegebenen Freigabe-Rechten.</summary>
    public ScanBuilder WithShare(params ShareAce[] permissions)
    {
        Scan.Shares.Add(new ShareInfo { Server = "SRV", Name = "Daten", LocalPath = @"D:\Daten", Permissions = [.. permissions] });
        return this;
    }

    /// <summary>Ordner unter der Freigabe; <paramref name="relative"/> leer = Wurzel.</summary>
    public FolderNode Folder(string relative, int depth, params AceEntry[] aces) =>
        Node(Share + relative, depth, Share, aces);

    /// <summary>Ordner ohne Freigabe (lokaler Pfad).</summary>
    public FolderNode LocalFolder(string path, int depth, params AceEntry[] aces) => Node(path, depth, null, aces);

    private FolderNode Node(string path, int depth, string? share, AceEntry[] aces)
    {
        var f = new FolderNode { Path = path, Depth = depth, ShareUnc = share, OwnerSid = "S-1-5-32-544" };
        f.Aces.AddRange(aces);
        Scan.Folders.Add(f);
        return f;
    }

    public static AceEntry Allow(string sid, FileSystemRights rights, bool inherited = false,
        InheritanceFlags inheritance = CIOI, PropagationFlags propagation = PropagationFlags.None) =>
        new() { Sid = sid, Rights = rights, IsInherited = inherited, Inheritance = inheritance, Propagation = propagation };

    public static AceEntry Deny(string sid, FileSystemRights rights, bool inherited = false,
        InheritanceFlags inheritance = CIOI, PropagationFlags propagation = PropagationFlags.None) =>
        new() { Sid = sid, Rights = rights, IsDeny = true, IsInherited = inherited, Inheritance = inheritance, Propagation = propagation };
}
