// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;

namespace Zutritt.Core.Identity;

/// <summary>
/// SIDs in Textform („S-1-5-21-…“) prüfen und vereinheitlichen – ohne <c>SecurityIdentifier</c>, das es nur unter
/// Windows gibt. Liest dieselbe Schreibweise wie Windows (<c>ConvertStringSidToSid</c>): Revision 1, Kennungsautorität
/// dezimal oder hexadezimal („0x…“, höchstens 48 Bit), 1 bis 15 Unterautoritäten (dezimal oder „0x…“, führende Nullen
/// erlaubt, zu große Werte auf 4294967295 begrenzt), „s“ klein oder groß. SDDL-Kürzel wie „BA“ oder „SY“ kennt sie nicht –
/// in ACLs, in der Datenbank und in Verzeichnissen stehen SIDs immer ausgeschrieben.
/// </summary>
public static class SidText
{
    private const int MaxSubAuthorities = 15;
    private const ulong MaxAuthority = 0xFFFF_FFFF_FFFF;

    /// <summary>SID in die übliche Schreibweise bringen (S-1-…, Zahlen dezimal ohne führende Nullen).</summary>
    /// <returns>false, wenn <paramref name="text"/> keine gültige SID ist.</returns>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrEmpty(text) || text.Length < 2 || (text[0] != 'S' && text[0] != 's') || text[1] != '-') return false;
        var parts = text[2..].Split('-');
        // Revision, Autorität und mindestens eine Unterautorität
        if (parts.Length < 3 || parts.Length - 2 > MaxSubAuthorities) return false;
        if (!TryNumber(parts[0], out var revision, saturate: false) || revision != 1) return false;
        if (!TryNumber(parts[1], out var authority, saturate: false) || authority > MaxAuthority) return false;

        var sb = new StringBuilder(text.Length);
        sb.Append("S-1-").Append(authority.ToString(CultureInfo.InvariantCulture));
        for (int i = 2; i < parts.Length; i++)
        {
            if (!TryNumber(parts[i], out var sub, saturate: true)) return false;
            sb.Append('-').Append(Math.Min(sub, uint.MaxValue).ToString(CultureInfo.InvariantCulture));
        }
        normalized = sb.ToString();
        return true;
    }

    /// <summary>Vereinheitlichte SID; ungültige bleiben, wie sie sind (ohne Leerzeichen am Rand).</summary>
    public static string Normalize(string sid)
    {
        var s = sid.Trim();
        return TryNormalize(s, out var normalized) ? normalized : s;
    }

    /// <summary>Gültige SID in Textform?</summary>
    public static bool IsValid(string? sid) => TryNormalize(sid, out _);

    /// <summary>
    /// Domänenteil einer Konto-SID: „S-1-5-21-a-b-c-RID“ → „S-1-5-21-a-b-c“ (die Domänen-SID selbst ergibt sich selbst),
    /// null bei allen anderen SIDs.
    /// </summary>
    public static string? AccountDomainOf(string sid)
    {
        if (!TryNormalize(sid, out var normalized)) return null;
        var parts = normalized.Split('-');
        // S, 1, 5, 21, a, b, c[, RID …]
        if (parts.Length < 7 || parts[2] != "5" || parts[3] != "21") return null;
        return string.Join('-', parts, 0, 7);
    }

    /// <summary>Zahl wie <c>wcstoul</c> mit Basis 0: dezimal oder „0x…“ hexadezimal, nur Ziffern.</summary>
    private static bool TryNumber(string text, out ulong value, bool saturate)
    {
        value = 0;
        if (text.Length == 0) return false;
        bool hex = text.Length > 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X');
        var digits = hex ? text.AsSpan(2) : text.AsSpan();
        foreach (var c in digits)
        {
            int d = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' when hex => c - 'a' + 10,
                >= 'A' and <= 'F' when hex => c - 'A' + 10,
                _ => -1,
            };
            if (d < 0) return false;
            ulong next = value * (hex ? 16UL : 10UL) + (ulong)d;
            // Überlauf: Unterautoritäten wie Windows begrenzen, sonst ungültig
            if (next > MaxAuthority * 16 || next < value)
            {
                if (!saturate) return false;
                next = uint.MaxValue;
            }
            value = saturate ? Math.Min(next, uint.MaxValue) : next;
        }
        return true;
    }
}
