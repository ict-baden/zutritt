// SPDX-License-Identifier: MIT
using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Platform.Windows.Scanning;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Windows.Analysis;
using Zutritt.Tests.Windows.Identity;

namespace Zutritt.Tests.Windows.Scanning;

/// <summary>Freigabe-Sicherheitsbeschreibungen (binär bzw. SDDL) lesen – über die Windows-Klassen.</summary>
public sealed class ShareDescriptorTests
{
    private const string User = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    [Fact]
    public void Typische_Freigabe_wird_in_Reihenfolge_gelesen()
    {
        var r = Parse($"O:BAG:SYD:(D;;0x10116;;;{User})(A;;0x1200a9;;;WD)(A;;FA;;;BA)(A;;0x1301bf;;;AU)");
        Assert.True(r.Readable);
        Assert.Null(r.Note);
        Assert.Equal(
            [
                new ShareAce(User, ShareAccess.Aendern, true),
                new ShareAce("S-1-1-0", ShareAccess.Lesen, false),
                new ShareAce("S-1-5-32-544", ShareAccess.Vollzugriff, false),
                new ShareAce("S-1-5-11", ShareAccess.Aendern, false),
            ], r.Aces);
    }

    [Fact]
    public void Null_DACL_heisst_Jeder_Vollzugriff()
    {
        var r = Parse("O:BAG:SYD:NO_ACCESS_CONTROL");
        Assert.True(r.Readable);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false)], r.Aces);
    }

    [Fact]
    public void Leere_DACL_heisst_niemand()
    {
        var r = Parse("O:BAG:SYD:");
        Assert.True(r.Readable);
        Assert.Empty(r.Aces);
    }

    [Fact]
    public void Nur_vererben_und_bedeutungslose_Eintraege_entfallen()
    {
        var r = Parse($"D:(A;OICIIO;FA;;;WD)(A;;0x100000;;;AU)(D;;DT;;;{User})(A;;0x1200a9;;;BU)");
        Assert.Equal([new ShareAce("S-1-5-32-545", ShareAccess.Lesen, false)], r.Aces);
    }

    [Fact]
    public void Bedingtes_Erlauben_zaehlt_bedingtes_Verweigern_nicht()
    {
        // Im Zweifel mehr Zugriff zeigen: Bedingungen kann der Bericht nicht auswerten.
        var r = Parse("D:(XA;;FA;;;WD;(Member_of {SID(BA)}))(XD;;FA;;;AU;(Member_of {SID(BA)}))");
        Assert.True(r.Readable);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false)], r.Aces);
    }

    [Fact]
    public void Kaputte_Beschreibung_ist_nicht_lesbar()
    {
        var r = ShareDescriptor.FromBinary([1, 0, 4, 0x80, 255, 255, 255, 255]);
        Assert.False(r.Readable);
        Assert.Empty(r.Aces);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public void Binaerform_wird_gelesen()
    {
        var sd = new RawSecurityDescriptor("D:(A;;0x1200a9;;;WD)");
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Lesen, false)], ShareDescriptor.FromBinary(bytes).Aces);
    }

    private static ShareSecurityResult Parse(string sddl) => ShareDescriptor.FromDescriptor(new RawSecurityDescriptor(sddl));
}
