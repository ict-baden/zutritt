using System.Security.AccessControl;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Ergebnis der Auswertung einer Freigabe-Sicherheitsbeschreibung.</summary>
/// <param name="Note">Warum die Rechte nicht (vollständig) lesbar sind, sonst null.</param>
internal sealed record ShareSecurityResult(List<ShareAce> Aces, bool Readable, string? Note)
{
    public static ShareSecurityResult NotReadable(string note) => new([], false, note);
}

/// <summary>
/// Übersetzt die Sicherheitsbeschreibung einer Freigabe in <see cref="ShareAce"/>-Einträge.
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
        ? ShareSecurityResult.NotReadable("Freigabe-Berechtigungen nicht lesbar: Administrative Freigabe ohne eigene Berechtigungen – es gilt die interne Windows-Vorgabe (in der Regel nur Administratoren)")
        : new([new ShareAce(Everyone, ShareAccess.Vollzugriff, false)], true, null);

    public static ShareSecurityResult FromBinary(byte[] descriptor)
    {
        RawSecurityDescriptor sd;
        try { sd = new RawSecurityDescriptor(descriptor, 0); }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        {
            return ShareSecurityResult.NotReadable("Freigabe-Berechtigungen nicht lesbar: Sicherheitsbeschreibung ist beschädigt");
        }
        return FromDescriptor(sd);
    }

    public static ShareSecurityResult FromDescriptor(RawSecurityDescriptor sd)
    {
        // NULL-DACL (keine Zugriffsliste): Windows prüft dann nichts, jeder hat Vollzugriff.
        if ((sd.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0 || sd.DiscretionaryAcl is null)
            return new([new ShareAce(Everyone, ShareAccess.Vollzugriff, false)], true, null);

        var aces = new List<ShareAce>();
        foreach (GenericAce ace in sd.DiscretionaryAcl)
        {
            if (ace is not QualifiedAce q)
            {
                // Eintragstyp ohne SID (veraltet/unbekannt) – lieber als „nicht lesbar“ melden als still weglassen,
                // dann rechnet die Auswertung nur mit NTFS (obere Grenze).
                return ShareSecurityResult.NotReadable($"Freigabe-Berechtigungen nicht vollständig auswertbar: unbekannter Eintragstyp {(int)ace.AceType}");
            }
            // Nur-vererben-Einträge wirken nie auf die Freigabe selbst.
            if ((ace.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;

            var sid = q.SecurityIdentifier.Value;
            switch (q.AceQualifier)
            {
                case AceQualifier.AccessAllowed:
                    // Bedingte Erlauben-Einträge zählen voll: lieber Zugriff zeigen, der nur unter Bedingungen gilt.
                    var allow = AllowLevel((uint)q.AccessMask);
                    if (allow != ShareAccess.Keine) aces.Add(new ShareAce(sid, allow, false));
                    break;
                case AceQualifier.AccessDenied:
                    // Bedingte Verweigerungen gelten nicht immer – sie zu zählen könnte Zugriff verstecken.
                    if (q.IsCallback) break;
                    var deny = DenyLevel((uint)q.AccessMask);
                    if (deny != ShareAccess.Keine) aces.Add(new ShareAce(sid, deny, true));
                    break;
            }
        }
        return new(aces, true, null);
    }

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
