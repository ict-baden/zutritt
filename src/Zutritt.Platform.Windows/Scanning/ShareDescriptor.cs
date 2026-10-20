// SPDX-License-Identifier: MIT
using System.Security.AccessControl;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;
using Zutritt.Core.Resources;

namespace Zutritt.Platform.Windows.Scanning;

/// <summary>
/// Übersetzt die Sicherheitsbeschreibung einer Freigabe (binär, von NetShareGetInfo) in <see cref="ShareAce"/>-Einträge.
/// Die Stufen selbst berechnet <see cref="ShareSecurity"/> in Core.
/// Leitlinie: Im Zweifel lieber zu viel Zugriff zeigen als zu wenig – ein Audit darf nichts übersehen.
/// </summary>
internal static class ShareDescriptor
{
    public const string Everyone = ShareSecurity.Everyone;

    public static ShareSecurityResult FromBinary(byte[] descriptor)
    {
        RawSecurityDescriptor sd;
        try { sd = new RawSecurityDescriptor(descriptor, 0); }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        {
            return ShareSecurityResult.NotReadable(Strings.Share_PermissionsCorrupt);
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
                return ShareSecurityResult.NotReadable(Texts.Format(Strings.Share_UnknownAceType, (int)ace.AceType));
            }
            // Nur-vererben-Einträge wirken nie auf die Freigabe selbst.
            if ((ace.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;

            var sid = q.SecurityIdentifier.Value;
            switch (q.AceQualifier)
            {
                case AceQualifier.AccessAllowed:
                    // Bedingte Erlauben-Einträge zählen voll: lieber Zugriff zeigen, der nur unter Bedingungen gilt.
                    var allow = ShareSecurity.AllowLevel((uint)q.AccessMask);
                    if (allow != ShareAccess.Keine) aces.Add(new ShareAce(sid, allow, false));
                    break;
                case AceQualifier.AccessDenied:
                    // Bedingte Verweigerungen gelten nicht immer – sie zu zählen könnte Zugriff verstecken.
                    if (q.IsCallback) break;
                    var deny = ShareSecurity.DenyLevel((uint)q.AccessMask);
                    if (deny != ShareAccess.Keine) aces.Add(new ShareAce(sid, deny, true));
                    break;
            }
        }
        return new(aces, true, null);
    }
}
