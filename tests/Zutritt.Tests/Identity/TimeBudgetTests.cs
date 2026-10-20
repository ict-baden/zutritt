// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Platform;

namespace Zutritt.Tests.Identity;

/// <summary>Ein Verzeichnis oder eine Kontenauflösung, die nie antwortet, hält den Lauf höchstens einmal kurz auf.</summary>
public sealed class TimeBudgetTests : IDisposable
{
    private const string Dom = "S-1-5-21-1000000001-1000000002-1000000003";
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    private readonly ManualResetEventSlim _never = new(false);

    public void Dispose()
    {
        _never.Set(); // hängende Hintergrundaufrufe freigeben
        _never.Dispose();
    }

    [Fact]
    public void Haengendes_Verzeichnis_wird_einmal_abgewartet_dann_nicht_mehr_gefragt()
    {
        var hanging = new HangingDirectory(_never);
        var resolver = new IdentityResolver([hanging], lookupFactory: null, machineDirectoryFactory: null, askScannedServers: false, owned: [])
        {
            CallTimeout = Short,
        };
        var scan = Scans.On(@"C:\Daten", Dom + "-2001", Dom + "-2002", Dom + "-2003");

        var clock = Stopwatch.StartNew();
        resolver.ResolveAll(scan, expandGroups: true);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"dauerte {clock.Elapsed}");
        Assert.Equal(1, hanging.Calls);
        Assert.All(scan.Principals.Values, p => Assert.Equal(PrincipalKind.Unbekannt, p.Kind));
        var note = Assert.Single(scan.Errors);
        Assert.Equal("dc.test", note.Path);
        Assert.Contains("antwortet nicht innerhalb von 300 Millisekunden", note.Message);
    }

    [Fact]
    public void Haengende_Kontenaufloesung_wird_einmal_abgewartet()
    {
        var lookup = new HangingLookup(_never);
        var resolver = new IdentityResolver([], _ => lookup, machineDirectoryFactory: null, askScannedServers: false, owned: [])
        {
            CallTimeout = Short,
        };
        var scan = Scans.On(@"C:\Daten", Dom + "-2001", Dom + "-2002");

        resolver.ResolveAll(scan, expandGroups: true);

        Assert.Equal(1, lookup.Calls);
        Assert.All(scan.Principals.Values, p => Assert.Equal(PrincipalKind.Unbekannt, p.Kind));
        Assert.Contains(scan.Errors, e => e.Message.StartsWith("Kontenauflösung antwortet nicht", StringComparison.Ordinal));
    }

    [Fact]
    public void Fehler_der_Arbeit_kommen_unveraendert_an()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TimeBudget.TryRun<int>(() => throw new InvalidOperationException("x"), Short, out _));
        Assert.Equal("x", ex.Message);
        Assert.True(TimeBudget.TryRun(() => 42, Short, out var value));
        Assert.Equal(42, value);
    }

    [Theory]
    [InlineData(60_000, "60 Sekunden")]
    [InlineData(120_000, "2 Minuten")]
    [InlineData(300, "300 Millisekunden")]
    public void Zeitlimit_in_Worten(int milliseconds, string expected)
        => Assert.Equal(expected, TimeBudget.Describe(TimeSpan.FromMilliseconds(milliseconds)));

    private sealed class HangingDirectory(ManualResetEventSlim never) : IAuthoritativeDirectory
    {
        public int Calls;
        public string SourceName => "dc.test";

        public LookupOutcome Find(string sid)
        {
            Interlocked.Increment(ref Calls);
            never.Wait();
            return LookupOutcome.Missing;
        }

        public MembersOutcome FindMembers(string groupSid)
        {
            Interlocked.Increment(ref Calls);
            never.Wait();
            return MembersOutcome.Missing;
        }

        public Principal? Lookup(string sid) => Find(sid).Principal;
        public IReadOnlyList<string>? GetMembers(string groupSid) => FindMembers(groupSid).Members;
    }

    private sealed class HangingLookup(ManualResetEventSlim never) : IAccountLookup
    {
        public int Calls;
        public string? Server => null;

        public AccountLookupResult Lookup(string sid)
        {
            Interlocked.Increment(ref Calls);
            never.Wait();
            return AccountLookupResult.NotMapped;
        }
    }
}
