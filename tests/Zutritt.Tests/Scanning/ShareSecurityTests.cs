using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Tests.Scanning;

public sealed class ShareSecurityTests
{
    private const string User = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    [Theory]
    [InlineData(0x001F01FFu, ShareAccess.Vollzugriff)]  // Vollzugriff, wie die Oberfläche ihn setzt
    [InlineData(0x001301BFu, ShareAccess.Aendern)]      // Ändern
    [InlineData(0x001200A9u, ShareAccess.Lesen)]        // Lesen
    [InlineData(0x10000000u, ShareAccess.Vollzugriff)]  // GENERIC_ALL
    [InlineData(0x80000000u, ShareAccess.Lesen)]        // GENERIC_READ
    [InlineData(0x40000000u, ShareAccess.Aendern)]      // GENERIC_WRITE
    [InlineData(0x20000000u, ShareAccess.Lesen)]        // GENERIC_EXECUTE: über SMB lässt sich damit lesen
    [InlineData(0x00040000u, ShareAccess.Vollzugriff)]  // nur WRITE_DAC: kann sich alles geben
    [InlineData(0x00080000u, ShareAccess.Vollzugriff)]  // nur WRITE_OWNER
    [InlineData(0x00000002u, ShareAccess.Aendern)]      // nur Schreiben
    [InlineData(0x00010000u, ShareAccess.Aendern)]      // nur Löschen
    [InlineData(0x00000001u, ShareAccess.Lesen)]        // nur Daten lesen
    [InlineData(0x00100000u, ShareAccess.Keine)]        // nur SYNCHRONIZE
    [InlineData(0x00020080u, ShareAccess.Keine)]        // nur Attribute + Berechtigungen lesen
    [InlineData(0x00000000u, ShareAccess.Keine)]
    public void AllowLevel_bewertet_nach_Bits(uint mask, ShareAccess expected)
        => Assert.Equal(expected, ShareSecurity.AllowLevel(mask));

    [Theory]
    [InlineData(0x001F01FFu, ShareAccess.Lesen)]        // Verweigern Vollzugriff sperrt schon das Lesen
    [InlineData(0x001200A9u, ShareAccess.Lesen)]        // Verweigern Lesen
    [InlineData(0x00010116u, ShareAccess.Aendern)]      // Verweigern Ändern (nur das Kästchen „Ändern“)
    [InlineData(0x001301BFu, ShareAccess.Lesen)]        // Ändern enthält Lesen
    [InlineData(0x000C0000u, ShareAccess.Vollzugriff)]  // WRITE_DAC + WRITE_OWNER
    [InlineData(0x10000000u, ShareAccess.Lesen)]        // GENERIC_ALL
    [InlineData(0x40000000u, ShareAccess.Keine)]        // GENERIC_WRITE ohne Löschen: Löschen bleibt möglich
    [InlineData(0x00010000u, ShareAccess.Keine)]        // nur Löschen gesperrt: nicht als „Ändern verweigert“ werten
    [InlineData(0x00000001u, ShareAccess.Keine)]        // nur READ_DATA: Ausführen erlaubt weiter Lesen
    [InlineData(0x00040000u, ShareAccess.Keine)]        // nur WRITE_DAC: Besitz übernehmen bleibt
    public void DenyLevel_zaehlt_nur_vollstaendige_Sperren(uint mask, ShareAccess expected)
        => Assert.Equal(expected, ShareSecurity.DenyLevel(mask));

    [Fact]
    public void Fehlende_Beschreibung_normale_Freigabe_ist_Jeder_Vollzugriff()
    {
        // So meldet es auch Get-SmbShareAccess für eine per NetShareAdd ohne Beschreibung angelegte Freigabe.
        var r = ShareSecurity.ForMissingDescriptor(isSpecial: false);
        Assert.True(r.Readable);
        Assert.Equal([new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, false)], r.Aces);
    }

    [Fact]
    public void Fehlende_Beschreibung_admin_Freigabe_ist_nicht_lesbar()
    {
        var r = ShareSecurity.ForMissingDescriptor(isSpecial: true);
        Assert.False(r.Readable);
        Assert.Empty(r.Aces);
        Assert.Contains("Administrative Freigabe", r.Note);
    }
}
