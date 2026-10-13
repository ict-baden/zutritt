using System.Runtime.InteropServices;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Eine gelesene Freigabe plus Hinweis, warum ihre Berechtigungen nicht (vollständig) lesbar sind.</summary>
internal sealed record ShareRead(ShareInfo Share, string? Note);

/// <summary>Alle Datenträger-Freigaben eines Servers oder der Grund, warum die Liste nicht lesbar war.</summary>
internal sealed record ShareListing(IReadOnlyList<ShareRead> Shares, string? Error, int ErrorCode);

/// <summary>Ergebnis der Suche nach einer einzelnen Freigabe.</summary>
/// <param name="NotFound">Der Server meldet ausdrücklich, dass es die Freigabe nicht gibt.</param>
internal sealed record ShareLookup(ShareRead? Read, string? Error, bool NotFound);

/// <summary>Liest SMB-Freigaben und ihre Freigabe-Berechtigungen (NetShareEnum / NetShareGetInfo Level 502).</summary>
/// <remarks>
/// Die Liste kommt über Level 1, das auch ohne Adminrechte funktioniert; Pfad und Berechtigungen dann je Freigabe
/// über Level 502 (braucht Adminrechte auf dem Server). Ohne diese Rechte bleibt die Freigabe in der Liste, aber mit
/// <see cref="ShareInfo.PermissionsReadable"/> = false und ohne lokalen Pfad.
/// </remarks>
public static class ShareEnumerator
{
    /// <summary>Alle Datenträger-Freigaben eines Servers. "." oder Environment.MachineName = dieser Rechner.</summary>
    /// <returns>Auch administrative Freigaben (markiert über <see cref="ShareInfo.IsAdminShare"/>); leer, wenn der Server nicht lesbar ist.</returns>
    public static IReadOnlyList<ShareInfo> GetShares(string server)
        => Enumerate(server).Shares.Select(s => s.Share).ToList();

    /// <summary>Eine einzelne Freigabe \server\name, null wenn es sie nicht gibt.</summary>
    /// <returns>
    /// null auch, wenn es keine Datenträger-Freigabe ist oder der Server nicht erreichbar ist. Existiert die Freigabe,
    /// sind aber ihre Berechtigungen nicht lesbar, kommt sie mit <see cref="ShareInfo.PermissionsReadable"/> = false.
    /// </returns>
    public static ShareInfo? GetShare(string server, string name) => Lookup(server, name).Read?.Share;

    internal static ShareListing Enumerate(string server)
    {
        var (api, display) = ServerNames(server);
        var entries = new List<(string Name, uint Type, string Remark)>();
        int status = EnumerateLevel1(api, entries);
        if (status != Native.ERROR_SUCCESS)
            return new([], $"Freigaben von „{display}“ nicht lesbar: {Win32Error.Describe(status)}", status);

        var list = new List<ShareRead>();
        foreach (var (name, type, remark) in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsDiskShare(type)) continue;
            var read = ReadDetails(api, display, name, type, remark, out var detailStatus);
            // Zwischen Liste und Detailabfrage gelöscht: einfach auslassen. Andere Fehler: Freigabe trotzdem nennen.
            if (read is null && detailStatus != Native.NERR_NetNameNotFound && detailStatus != -1)
                read = Basic(display, name, type, remark, "Freigabe-Berechtigungen nicht lesbar: " + Win32Error.Describe(detailStatus), out _);
            if (read is not null) list.Add(read);
        }
        return new(list, null, Native.ERROR_SUCCESS);
    }

    internal static ShareLookup Lookup(string server, string name)
    {
        var (api, display) = ServerNames(server);
        var unc = $@"\\{display}\{name}";
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\'))
            return new(null, $"Ungültiger Freigabename „{name}“", true);

        var read = ReadDetails(api, display, name, null, null, out int status);
        if (read is not null) return new(read, null, false);
        return status switch
        {
            Native.NERR_NetNameNotFound => new(null, $"Freigabe „{unc}“ nicht gefunden", true),
            -1 => new(null, $"„{unc}“ ist keine Datenträger-Freigabe", true),
            _ => new(null, $"Freigabe „{unc}“ nicht lesbar: {Win32Error.Describe(status)}", false),
        };
    }

    /// <summary>Details einer Freigabe über Level 502; ohne Adminrechte Rückfall auf Level 1 (ohne Pfad und Rechte).</summary>
    /// <param name="knownType">Typ aus der Liste, falls schon bekannt – spart beim Rückfall die Level-1-Abfrage.</param>
    /// <param name="knownRemark">Bemerkung aus der Liste, falls schon bekannt.</param>
    /// <param name="status">0 bei Erfolg, -1 wenn es keine Datenträger-Freigabe ist, sonst Win32-/NERR-Code.</param>
    private static unsafe ShareRead? ReadDetails(string? api, string display, string name, uint? knownType, string? knownRemark, out int status)
    {
        status = Native.NetShareGetInfo(api, name, 502, out nint buffer);
        if (status == Native.ERROR_SUCCESS)
        {
            try
            {
                var info = (Native.SHARE_INFO_502*)buffer;
                if (!IsDiskShare(info->shi502_type)) { status = -1; return null; }
                var actualName = Str(info->shi502_netname);
                if (actualName.Length == 0) actualName = name;
                bool admin = IsAdmin(info->shi502_type, actualName);
                var security = ReadSecurity(info->shi502_security_descriptor, (info->shi502_type & Native.STYPE_SPECIAL) != 0);
                var share = new ShareInfo
                {
                    Server = display,
                    Name = actualName,
                    LocalPath = Str(info->shi502_path),
                    Remark = Str(info->shi502_remark),
                    IsAdminShare = admin,
                    Permissions = security.Aces,
                    PermissionsReadable = security.Readable,
                };
                return new ShareRead(share, security.Note);
            }
            finally { Native.NetApiBufferFree(buffer); }
        }
        if (buffer != 0) Native.NetApiBufferFree(buffer);
        if (status != Native.ERROR_ACCESS_DENIED) return null;

        // Keine Adminrechte auf dem Server: Name, Typ und Bemerkung gibt es trotzdem.
        var deniedNote = $"Freigabe-Berechtigungen nicht lesbar: {Win32Error.Describe(Native.ERROR_ACCESS_DENIED)}";
        if (knownType is uint type)
            return Basic(display, name, type, knownRemark ?? "", deniedNote, out status);

        int level1 = Native.NetShareGetInfo(api, name, 1, out nint b1);
        try
        {
            if (level1 != Native.ERROR_SUCCESS) { status = level1 == Native.NERR_NetNameNotFound ? level1 : status; return null; }
            var info = (Native.SHARE_INFO_1*)b1;
            var actualName = Str(info->shi1_netname);
            return Basic(display, actualName.Length == 0 ? name : actualName, info->shi1_type, Str(info->shi1_remark), deniedNote, out status);
        }
        finally { if (b1 != 0) Native.NetApiBufferFree(b1); }
    }

    /// <summary>Freigabe nur mit Name, Typ und Bemerkung (Level 1) – Pfad und Berechtigungen fehlen.</summary>
    internal static ShareRead? Basic(string display, string name, uint type, string remark, string note, out int status)
    {
        if (!IsDiskShare(type)) { status = -1; return null; }
        status = Native.ERROR_SUCCESS;
        var share = new ShareInfo
        {
            Server = display,
            Name = name,
            Remark = remark,
            IsAdminShare = IsAdmin(type, name),
            PermissionsReadable = false,
        };
        return new ShareRead(share, note);
    }

    private static ShareSecurityResult ReadSecurity(nint descriptor, bool isSpecial)
    {
        if (descriptor == 0) return ShareSecurity.ForMissingDescriptor(isSpecial);
        if (!Native.IsValidSecurityDescriptor(descriptor))
            return ShareSecurityResult.NotReadable("Freigabe-Berechtigungen nicht lesbar: Sicherheitsbeschreibung ist ungültig");
        var length = (int)Native.GetSecurityDescriptorLength(descriptor);
        var bytes = new byte[length];
        Marshal.Copy(descriptor, bytes, 0, length);
        return ShareSecurity.FromBinary(bytes);
    }

    private static unsafe int EnumerateLevel1(string? api, List<(string Name, uint Type, string Remark)> into)
    {
        int resume = 0;
        while (true)
        {
            int status = Native.NetShareEnum(api, 1, out nint buffer, Native.MAX_PREFERRED_LENGTH, out int read, out _, ref resume);
            try
            {
                if (status != Native.ERROR_SUCCESS && status != Native.ERROR_MORE_DATA) return status;
                var items = (Native.SHARE_INFO_1*)buffer;
                for (int i = 0; i < read; i++)
                    into.Add((Str(items[i].shi1_netname), items[i].shi1_type, Str(items[i].shi1_remark)));
                // Ohne Fortschritt nicht endlos weiterfragen.
                if (status == Native.ERROR_SUCCESS || read == 0) return Native.ERROR_SUCCESS;
            }
            finally { if (buffer != 0) Native.NetApiBufferFree(buffer); }
        }
    }

    internal static bool IsDiskShare(uint type) => (type & Native.STYPE_MASK) == Native.STYPE_DISKTREE;

    /// <summary>Administrative bzw. versteckte Freigabe: Typ-Bit STYPE_SPECIAL (C$, ADMIN$) oder Name mit „$“ am Ende (print$, eigene versteckte).</summary>
    internal static bool IsAdmin(uint type, string name) => (type & Native.STYPE_SPECIAL) != 0 || name.EndsWith('$');

    /// <summary>
    /// Servername für die API (null = dieser Rechner, ohne Umweg über das Netz) und für die Anzeige im UNC-Pfad.
    /// „.“ wird zum Rechnernamen, sonst bleibt der Name so, wie er angegeben wurde – die UNC-Pfade der Ordner
    /// beginnen ebenfalls damit und müssen zur Freigabe passen.
    /// </summary>
    internal static (string? Api, string Display) ServerNames(string server)
    {
        var s = (server ?? "").Trim().TrimStart('\\').TrimEnd('\\');
        if (s.Length == 0 || s == ".") return (null, Environment.MachineName);
        if (IsLocalName(s)) return (null, s);
        return (@"\\" + s, s);
    }

    internal static bool IsLocalName(string server)
        => server == "." || server.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
           || server.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    private static string Str(nint p) => p == 0 ? "" : Marshal.PtrToStringUni(p) ?? "";
}
