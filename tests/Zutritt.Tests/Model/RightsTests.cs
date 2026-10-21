// SPDX-License-Identifier: MIT
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.Analysis;
using static Zutritt.Tests.Analysis.ScanBuilder;

namespace Zutritt.Tests.Model;

/// <summary>Bezeichnungen der Rechte, wie Bericht und Oberfläche sie zeigen.</summary>
public sealed class RightsTests
{
    [Theory]
    [InlineData(ShareAccess.Lesen, "Verweigert: alles (ab Lesen)")]
    [InlineData(ShareAccess.Aendern, "Verweigert: Ändern und Vollzugriff (ab Ändern)")]
    [InlineData(ShareAccess.Vollzugriff, "Verweigert: Vollzugriff")]
    [InlineData(ShareAccess.Keine, "Verweigert: nichts (ohne Wirkung)")]
    public void Freigabe_Verweigern_nennt_was_gesperrt_ist(ShareAccess deniedFrom, string expected) =>
        Assert.Equal(expected, Rights.DenyLabel(deniedFrom));

    [Fact]
    public void Freigabe_Eintrag_Erlauben_wie_Stufe_Verweigern_ausgeschrieben()
    {
        Assert.Equal("Lesen", Rights.Label(new ShareAce("S-1-1-0", ShareAccess.Lesen, IsDeny: false)));
        Assert.Equal("Vollzugriff", Rights.Label(new ShareAce("S-1-1-0", ShareAccess.Vollzugriff, IsDeny: false)));
        Assert.Equal("Verweigert: alles (ab Lesen)", Rights.Label(new ShareAce("S-1-1-0", ShareAccess.Lesen, IsDeny: true)));
        Assert.Equal(Rights.DenyLabel(ShareAccess.Aendern), Rights.Label(new ShareAce("S-1-1-0", ShareAccess.Aendern, IsDeny: true)));
    }

    /// <summary>Die Bezeichnung passt zur Auswertung: Was „ab“ der Stufe gesperrt heißt, bleibt darunter erlaubt.</summary>
    [Theory]
    [InlineData(ShareAccess.Lesen, ShareAccess.Keine)]
    [InlineData(ShareAccess.Aendern, ShareAccess.Lesen)]
    [InlineData(ShareAccess.Vollzugriff, ShareAccess.Aendern)]
    public void Bezeichnung_passt_zur_Auswertung(ShareAccess deniedFrom, ShareAccess remaining)
    {
        var b = new ScanBuilder().User(Sid(2001), @"T\anna");
        b.WithShare(new ShareAce(SidCatalog.Everyone, ShareAccess.Vollzugriff, false), new ShareAce(SidCatalog.Everyone, deniedFrom, true));
        var root = b.Folder("", 0, Allow(SidCatalog.Everyone, FileRights.FullControl));
        var grant = EffectiveAccess.ForFolder(b.Scan, root).Single();
        Assert.Equal(Rights.CapByShare(AccessLevel.Vollzugriff, remaining), grant.ViaShare);
        Assert.StartsWith("Verweigert: ", Rights.DenyLabel(deniedFrom));
    }

    [Theory]
    [InlineData(FileRights.Modify | FileRights.Synchronize, false, "Ändern")]
    [InlineData(FileRights.ReadAndExecute | FileRights.Synchronize, false, "Lesen")]
    [InlineData(FileRights.Synchronize, false, "Synchronisieren")]
    [InlineData(FileRights.FullControl, true, "Vollzugriff")]
    [InlineData(FileRights.Modify | FileRights.Synchronize, true, "Ändern")]
    [InlineData(FileRights.Delete | FileRights.DeleteSubdirectoriesAndFiles, true, "Löschen, Unterordner und Dateien löschen")]
    // Als Stufe wäre das „Vollzugriff“ – gesperrt ist aber nur das Ändern der Rechte und die Besitzübernahme
    [InlineData(FileRights.ChangePermissions | FileRights.TakeOwnership, true, "Berechtigungen ändern, Besitz übernehmen")]
    public void NTFS_Eintrag_Erlauben_als_Stufe_Verweigern_als_gesperrte_Rechte(FileRights rights, bool deny, string expected) =>
        Assert.Equal(expected, Rights.EntryLabel(rights, deny));
}
