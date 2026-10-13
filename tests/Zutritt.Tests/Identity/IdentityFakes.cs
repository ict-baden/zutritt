using System.Security.AccessControl;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;

namespace Zutritt.Tests.Identity;

/// <summary>LSA-Attrappe: feste Antworten je SID, alles andere „nicht zugeordnet“ (oder ein fester Fehler).</summary>
internal sealed class FakeLookup(string? server = null) : IAccountLookup
{
    private readonly Dictionary<string, AccountLookupResult> _answers = new(StringComparer.OrdinalIgnoreCase);

    public string? Server { get; } = server;
    public int Calls { get; private set; }
    public List<string> Asked { get; } = [];

    /// <summary>Fehlercode für alle SIDs ohne feste Antwort; 0 = „nicht zugeordnet“ (1332).</summary>
    public int DefaultError { get; set; }

    public FakeLookup Map(string sid, string domain, string name, Native.SidNameUse use)
    {
        _answers[sid] = new AccountLookupResult(LookupStatus.Gefunden, domain, name, use, 0);
        return this;
    }

    public FakeLookup Fail(string sid, int error)
    {
        _answers[sid] = new AccountLookupResult(error == Native.ERROR_NONE_MAPPED ? LookupStatus.NichtZugeordnet : LookupStatus.Fehler,
            "", "", Native.SidNameUse.Unknown, error);
        return this;
    }

    public AccountLookupResult Lookup(string sid)
    {
        Calls++;
        Asked.Add(sid);
        if (_answers.TryGetValue(sid, out var r)) return r;
        return DefaultError == 0
            ? new AccountLookupResult(LookupStatus.NichtZugeordnet, "", "", Native.SidNameUse.Unknown, Native.ERROR_NONE_MAPPED)
            : new AccountLookupResult(LookupStatus.Fehler, "", "", Native.SidNameUse.Unknown, DefaultError);
    }
}

/// <summary>Zählt Anfragen an ein beliebiges Verzeichnis.</summary>
internal sealed class CountingDirectory(IDirectory inner) : IDirectory
{
    public List<string> Lookups { get; } = [];
    public List<string> MemberQueries { get; } = [];
    public Action<string>? OnLookup { get; set; }

    public Principal? Lookup(string sid)
    {
        Lookups.Add(sid);
        OnLookup?.Invoke(sid);
        return inner.Lookup(sid);
    }

    public IReadOnlyList<string>? GetMembers(string groupSid)
    {
        MemberQueries.Add(groupSid);
        return inner.GetMembers(groupSid);
    }
}

/// <summary>Verzeichnis, das bei jeder Anfrage eine Ausnahme wirft (fehlerhafte Quelle).</summary>
internal sealed class ThrowingDirectory : IDirectory
{
    public Principal? Lookup(string sid) => throw new InvalidOperationException("Testfehler beim Lookup");
    public IReadOnlyList<string>? GetMembers(string groupSid) => throw new InvalidOperationException("Testfehler bei Mitgliedern");
}

/// <summary>
/// Verbindliches Verzeichnis für Tests: zuständig für alle SIDs mit einem Präfix; Konten aus einem
/// <see cref="FakeDirectory"/>, alles andere „nicht vorhanden“ – oder komplett „nicht erreichbar“.
/// Optional an einen Rechner gebunden (wie eine lokale Kontendatenbank).
/// </summary>
internal class FakeAuthoritativeDirectory(string sourceName, string sidPrefix, FakeDirectory? data = null) : IAuthoritativeDirectory
{
    public FakeDirectory Data { get; } = data ?? new FakeDirectory();
    public bool Unreachable { get; set; }
    public string SourceName { get; } = sourceName;
    public int FindCalls { get; private set; }
    public int MemberCalls { get; private set; }

    private bool Responsible(string sid) =>
        sid.StartsWith(sidPrefix, StringComparison.OrdinalIgnoreCase) || (WellKnownSids.IsBuiltin(sid) && this is IMachineDirectory);

    public LookupOutcome Find(string sid)
    {
        FindCalls++;
        if (!Responsible(sid)) return LookupOutcome.NotResponsible;
        if (Unreachable) return LookupOutcome.Unreachable($"{SourceName} nicht erreichbar (Test).");
        var p = Data.Lookup(sid);
        return p is null ? LookupOutcome.Missing : LookupOutcome.Found(p);
    }

    public MembersOutcome FindMembers(string groupSid)
    {
        MemberCalls++;
        if (!Responsible(groupSid)) return MembersOutcome.NotResponsible;
        if (Unreachable) return MembersOutcome.Unreachable($"{SourceName} nicht erreichbar (Test).");
        var m = Data.GetMembers(groupSid);
        return m is null ? MembersOutcome.Missing : MembersOutcome.Found(m);
    }

    public Principal? Lookup(string sid) => Find(sid).Principal;
    public IReadOnlyList<string>? GetMembers(string groupSid) => FindMembers(groupSid).Members;
}

/// <summary>Lokale Kontendatenbank eines (erfundenen) Rechners: zuständig für VORDEFINIERT und das Rechner-Präfix.</summary>
internal sealed class FakeMachineDirectory(string? server, string machineSid, FakeDirectory? data = null)
    : FakeAuthoritativeDirectory(@"\\" + (server ?? Environment.MachineName), machineSid + "-", data), IMachineDirectory
{
    public string? Server { get; } = server;
}

/// <summary>Kleine Bausteine für synthetische Läufe.</summary>
internal static class Scans
{
    public static ScanResult On(string path, params string[] aceSids) => OwnedBy(path, "", aceSids);

    public static ScanResult OwnedBy(string path, string owner, params string[] aceSids)
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = [path] } };
        scan.Folders.Add(Folder(path, owner, aceSids));
        return scan;
    }

    public static FolderNode Folder(string path, string owner, params string[] aceSids)
    {
        var f = new FolderNode { Path = path, OwnerSid = owner };
        foreach (var sid in aceSids)
            f.Aces.Add(new AceEntry { Sid = sid, Rights = FileSystemRights.ReadAndExecute, Inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit });
        return f;
    }

    /// <summary>Resolver nur mit Attrappen – fragt weder Windows noch das Netz.</summary>
    public static IdentityResolver Resolver(FakeLookup lookup, params IDirectory[] directories) =>
        new(directories, _ => lookup, machineDirectoryFactory: null, askScannedServers: false, owned: []);
}
