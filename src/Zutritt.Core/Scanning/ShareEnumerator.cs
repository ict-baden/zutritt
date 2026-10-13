using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Liest SMB-Freigaben und ihre Freigabe-Berechtigungen (NetShareEnum / NetShareGetInfo Level 502).</summary>
public static class ShareEnumerator
{
    /// <summary>Alle Datenträger-Freigaben eines Servers. "." oder Environment.MachineName = dieser Rechner.</summary>
    public static IReadOnlyList<ShareInfo> GetShares(string server) => throw new NotImplementedException();

    /// <summary>Eine einzelne Freigabe \server\name, null wenn es sie nicht gibt.</summary>
    public static ShareInfo? GetShare(string server, string name) => throw new NotImplementedException();
}
