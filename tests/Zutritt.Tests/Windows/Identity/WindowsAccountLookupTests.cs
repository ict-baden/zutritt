// SPDX-License-Identifier: MIT
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Platform.Windows.Identity;
using Zutritt.Tests.Identity;

namespace Zutritt.Tests.Windows.Identity;

/// <summary>Einordnung der Windows-Fehlercodes und Kontotypen von LookupAccountSid für den plattformneutralen Resolver.</summary>
public sealed class WindowsAccountLookupTests
{
    [Theory]
    [InlineData((int)Native.SidNameUse.User, PrincipalKind.Benutzer)]
    [InlineData((int)Native.SidNameUse.Group, PrincipalKind.Gruppe)]
    [InlineData((int)Native.SidNameUse.Alias, PrincipalKind.Gruppe)]
    [InlineData((int)Native.SidNameUse.WellKnownGroup, PrincipalKind.Bekannt)]
    [InlineData((int)Native.SidNameUse.Computer, PrincipalKind.Computer)]
    [InlineData((int)Native.SidNameUse.Label, PrincipalKind.Bekannt)]
    [InlineData((int)Native.SidNameUse.LogonSession, PrincipalKind.Bekannt)]
    [InlineData((int)Native.SidNameUse.DeletedAccount, PrincipalKind.Verwaist)]
    [InlineData((int)Native.SidNameUse.Unknown, PrincipalKind.Unbekannt)]
    [InlineData((int)Native.SidNameUse.Invalid, PrincipalKind.Unbekannt)]
    public void Kontotyp_wird_zur_Art(int use, PrincipalKind kind)
    {
        var r = new WindowsLookupResult(LookupStatus.Gefunden, "LAB", "max", (Native.SidNameUse)use, 0);
        Assert.Equal(kind, r.ToNeutral().Kind);
        Assert.Equal(@"LAB\max", r.ToNeutral().FullName);
    }

    [Fact]
    public void Nicht_zugeordnet_ist_kein_Fehler()
        => Assert.Equal(LookupStatus.NichtZugeordnet, WindowsAccountLookup.FromError(Native.ERROR_NONE_MAPPED).Status);

    [Fact]
    public void Fehler_kommen_als_deutscher_Text_mit_Nummer()
    {
        var r = WindowsAccountLookup.FromError(Native.RPC_S_SERVER_UNAVAILABLE);
        Assert.Equal(LookupStatus.Fehler, r.Status);
        Assert.Equal("RPC-Server nicht verfügbar (Fehler 1722)", r.Error);
        Assert.True(r.SourceDown);
        Assert.False(WindowsAccountLookup.FromError(Native.ERROR_TRUSTED_DOMAIN_FAILURE).SourceDown);
    }

    /// <summary>Die LSA-Attrappe der plattformneutralen Tests ordnet genauso ein wie die echte Anbindung.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(53)]
    [InlineData(67)]
    [InlineData(1203)]
    [InlineData(1231)]
    [InlineData(1232)]
    [InlineData(1311)]
    [InlineData(1332)]
    [InlineData(1722)]
    [InlineData(1726)]
    [InlineData(1727)]
    [InlineData(1753)]
    [InlineData(1788)]
    [InlineData(1789)]
    public void Attrappe_und_Windows_ordnen_gleich_ein(int error)
    {
        var windows = WindowsAccountLookup.FromError(error);
        var fake = LookupErrors.Result(error);
        Assert.Equal(windows.Status, fake.Status);
        Assert.Equal(windows.SourceDown, fake.SourceDown);
    }
}
