// SPDX-License-Identifier: MIT
using Zutritt.Core.Identity;
using Zutritt.Core.Model;

namespace Zutritt.Tests.Identity;

/// <summary>
/// Windows-Fehlercodes für die LSA-Attrappe: Text wie bei der Plattform („… (Fehler N)“), Einordnung „Quelle ausgefallen“
/// wie <c>WindowsAccountLookup</c> – dass beide übereinstimmen, prüft Windows/Identity/WindowsAccountLookupTests.
/// </summary>
internal static class LookupErrors
{
    public const int AccessDenied = 5;
    public const int NoLogonServers = 1311;
    public const int NoneMapped = 1332;
    public const int RpcServerUnavailable = 1722;
    public const int TrustedDomainFailure = 1788;
    public const int TrustedRelationshipFailure = 1789;

    /// <summary>Fehler, nach denen der Resolver die Quelle für den Rest des Laufs nicht mehr fragt.</summary>
    public static readonly int[] SourceDown = [5, 53, 67, 1203, 1231, 1232, 1722, 1727, 1753];

    public static AccountLookupResult Result(int error) => error == NoneMapped
        ? AccountLookupResult.NotMapped
        : AccountLookupResult.Failed($"Testfehler (Fehler {error})", SourceDown.Contains(error));
}

/// <summary>LSA-Attrappe: feste Antworten je SID, alles andere „nicht zugeordnet“ (oder ein fester Fehler).</summary>
internal sealed class FakeLookup(string? server = null) : IAccountLookup
{
    private readonly Dictionary<string, AccountLookupResult> _answers = new(StringComparer.OrdinalIgnoreCase);

    public string? Server { get; } = server;
    public int Calls { get; private set; }
    public List<string> Asked { get; } = [];

    /// <summary>Windows-Fehlercode für alle SIDs ohne feste Antwort; 0 = „nicht zugeordnet“ (1332).</summary>
    public int DefaultError { get; set; }

    public FakeLookup Map(string sid, string domain, string name, PrincipalKind kind)
    {
        _answers[sid] = new AccountLookupResult(LookupStatus.Gefunden, domain, name, kind);
        return this;
    }

    public FakeLookup Fail(string sid, int error)
    {
        _answers[sid] = LookupErrors.Result(error);
        return this;
    }

    public AccountLookupResult Lookup(string sid)
    {
        Calls++;
        Asked.Add(sid);
        if (_answers.TryGetValue(sid, out var r)) return r;
        return LookupErrors.Result(DefaultError == 0 ? LookupErrors.NoneMapped : DefaultError);
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
    : FakeAuthoritativeDirectory(WindowsPath.Server(server ?? Environment.MachineName), machineSid + "-", data), IMachineDirectory
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
            f.Aces.Add(new AceEntry { Sid = sid, Rights = FileRights.ReadAndExecute, Inheritance = AceInheritance.ContainerInherit | AceInheritance.ObjectInherit });
        return f;
    }

    /// <summary>Resolver nur mit Attrappen – fragt weder Windows noch das Netz.</summary>
    public static IdentityResolver Resolver(FakeLookup lookup, params IDirectory[] directories) =>
        new(directories, _ => lookup, machineDirectoryFactory: null, askScannedServers: false, owned: []);
}
