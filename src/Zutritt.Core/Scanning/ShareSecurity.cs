// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;
using Zutritt.Core.Resources;

namespace Zutritt.Core.Scanning;

/// <summary>Ergebnis der Auswertung einer Freigabe-Sicherheitsbeschreibung.</summary>
/// <param name="Note">Warum die Rechte nicht (vollständig) lesbar sind, sonst null.</param>
internal sealed record ShareSecurityResult(List<ShareAce> Aces, bool Readable, string? Note)
{
    public static ShareSecurityResult NotReadable(string note) => new([], false, note);
}

/// <summary>
/// Stufen der Freigabe-Berechtigungen aus Zugriffsmasken. Die Sicherheitsbeschreibung selbst liest die Plattform
/// (unter Windows <c>Zutritt.Platform.Windows.Scanning.ShareDescriptor</c>).
/// Leitlinie: Im Zweifel lieber zu viel Zugriff zeigen als zu wenig – ein Audit darf nichts übersehen.
/// </summary>
internal static class ShareSecurity
{
    public const string Everyone = "S-1-1-0";

    /// <summary>
    /// Freigabe ohne Sicherheitsbeschreibung. Windows lässt dann jeden mit Vollzugriff zu (so zeigt es auch
    /// Get-SmbShareAccess) – „Jeder: Lesen“ ist nur die Vorgabe der Oberfläche, die immer eine Beschreibung speichert.
    /// Administrative Freigaben (C$, ADMIN$) ohne Beschreibung nutzen dagegen eine interne Vorgabe des Serverdienstes,
    /// die je nach Windows-Version abweichen kann: Die gilt als nicht lesbar, statt geraten zu werden.
    /// </summary>
    public static ShareSecurityResult ForMissingDescriptor(bool isSpecial) => isSpecial
        ? ShareSecurityResult.NotReadable(Strings.Scan_AdminShareDefault)
        : new([new ShareAce(Everyone, ShareAccess.Vollzugriff, false)], true, null);

    /// <summary>
    /// Erlauben-Eintrag: höchste Stufe, für die mindestens ein kennzeichnendes Bit gesetzt ist
    /// (Vollzugriff 0x1F01FF, Ändern 0x1301BF, Lesen 0x1200A9 – aber nach Bits, nicht nur exakt).
    /// Wer Berechtigungen oder Besitz ändern darf, kann sich alles andere selbst geben.
    /// </summary>
    public static ShareAccess AllowLevel(uint mask)
    {
        var m = FileAccessMask.MapGeneric(mask);
        if ((m & (FileAccessMask.WriteDac | FileAccessMask.WriteOwner)) != 0) return ShareAccess.Vollzugriff;
        const uint change = FileAccessMask.WriteData | FileAccessMask.AppendData | FileAccessMask.Delete | FileAccessMask.DeleteChild
            | FileAccessMask.WriteAttributes | FileAccessMask.WriteEa;
        if ((m & change) != 0) return ShareAccess.Aendern;
        // Über SMB lässt sich mit „Ausführen“ auch lesen (Paging von Programmen) – zählt deshalb als Lesen.
        if ((m & (FileAccessMask.ReadData | FileAccessMask.Execute)) != 0) return ShareAccess.Lesen;
        return ShareAccess.Keine;
    }

    /// <summary>
    /// Verweigern-Eintrag: niedrigste Stufe, die er sicher vollständig sperrt – „ab dieser Stufe verweigert“.
    /// Verweigern „Vollzugriff“ (0x1F01FF) sperrt also schon das Lesen und ergibt <see cref="ShareAccess.Lesen"/>,
    /// Verweigern „Ändern“ (0x10116) ergibt <see cref="ShareAccess.Aendern"/>. Teilweise Sperren (etwa nur Löschen)
    /// zählen nicht: Sonst würde der Bericht weniger Zugriff zeigen, als tatsächlich besteht.
    /// </summary>
    public static ShareAccess DenyLevel(uint mask)
    {
        var m = FileAccessMask.MapGeneric(mask);
        static bool All(uint value, uint bits) => (value & bits) == bits;
        if (All(m, FileAccessMask.ReadData | FileAccessMask.Execute)) return ShareAccess.Lesen;
        if (All(m, FileAccessMask.WriteData | FileAccessMask.AppendData | FileAccessMask.Delete)) return ShareAccess.Aendern;
        if (All(m, FileAccessMask.WriteDac | FileAccessMask.WriteOwner)) return ShareAccess.Vollzugriff;
        return ShareAccess.Keine;
    }
}
