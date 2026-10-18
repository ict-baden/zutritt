using Zutritt.Core.Identity;

namespace Zutritt.Tests.Identity;

/// <summary>
/// SIDs ohne <c>SecurityIdentifier</c> – die Erwartungen stammen aus <c>SecurityIdentifier</c> unter Windows
/// (Value bzw. AccountDomainSid), damit sich durch die plattformneutrale Fassung nichts ändert.
/// </summary>
public sealed class SidTextTests
{
    [Theory]
    [InlineData("S-1-5-21-1-2-3-1000", "S-1-5-21-1-2-3-1000")]
    [InlineData("s-1-5-18", "S-1-5-18")]
    [InlineData("S-1-5-021-1-2-3-1000", "S-1-5-21-1-2-3-1000")]
    [InlineData("S-01-5-18", "S-1-5-18")]
    [InlineData("S-1-05-18", "S-1-5-18")]
    [InlineData("S-1-0x000000000005-18", "S-1-5-18")]
    [InlineData("S-1-0x1000000000-1", "S-1-68719476736-1")]
    [InlineData("S-1-4294967296-1", "S-1-4294967296-1")]
    [InlineData("S-1-281474976710655-1", "S-1-281474976710655-1")]
    [InlineData("S-1-5-4294967295", "S-1-5-4294967295")]
    [InlineData("S-1-5-4294967296", "S-1-5-4294967295")]
    [InlineData("S-1-5-0x12", "S-1-5-18")]
    [InlineData("S-1-5-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15", "S-1-5-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15")]
    [InlineData("S-1-12-1-2-3-4-5", "S-1-12-1-2-3-4-5")]
    public void Gueltige_SIDs_wie_Windows(string input, string expected)
    {
        Assert.True(SidText.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(" S-1-5-18")]
    [InlineData("S-1-5-18 ")]
    [InlineData("S-1-5")]
    [InlineData("S-1-5-+18")]
    [InlineData("S-1-5--18")]
    [InlineData("S-2-5-18")]
    [InlineData("S-1-5-18-")]
    [InlineData("S-1--5")]
    [InlineData("S-1-5-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15-16")]
    [InlineData("S-1-281474976710656-1")]
    [InlineData("S-1-5-21-1-2-3-x")]
    [InlineData("S-1-5- 18")]
    [InlineData("S-1-5-18a")]
    public void Ungueltige_SIDs_wie_Windows(string input)
    {
        Assert.False(SidText.TryNormalize(input, out _));
        Assert.False(SidText.IsValid(input));
    }

    [Fact]
    public void Normalize_laesst_Ungueltiges_stehen()
    {
        Assert.Equal("S-1-5-18", SidText.Normalize(" s-1-5-18 "));
        Assert.Equal("kein-sid", SidText.Normalize(" kein-sid "));
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1000", "S-1-5-21-1-2-3")]
    [InlineData("S-1-5-21-1-2-3", "S-1-5-21-1-2-3")]
    [InlineData("S-1-5-21-1-2-3-4-5", "S-1-5-21-1-2-3")]
    [InlineData("S-1-5-21-0-0-0-500", "S-1-5-21-0-0-0")]
    [InlineData("S-1-5-21-1-2", null)]
    [InlineData("S-1-5-32-544", null)]
    [InlineData("S-1-5-18", null)]
    [InlineData("S-1-12-1-2-3-4-5", null)]
    [InlineData("kein-sid", null)]
    public void Domaenenteil_wie_Windows(string sid, string? expected) => Assert.Equal(expected, SidText.AccountDomainOf(sid));
}
