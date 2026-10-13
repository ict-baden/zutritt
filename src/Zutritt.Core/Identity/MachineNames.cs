using System.Net.NetworkInformation;
using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>
/// Welche Rechner ein Lauf betrifft. Wichtig für VORDEFINIERT-Gruppen und lokale Konten: Ihre Mitglieder
/// stehen in der Kontendatenbank des Servers, auf dem die Ordner liegen – nicht auf dem Rechner, der scannt.
/// </summary>
internal static class MachineNames
{
    private static readonly Lazy<HashSet<string>> LocalNames = new(BuildLocalNames);

    /// <summary>Servername in Großbuchstaben, null für diesen Rechner („.“, localhost, eigener Name).</summary>
    public static string? Normalize(string? server)
    {
        var s = Strip(server);
        if (s is null || LocalNames.Value.Contains(s)) return null;
        return s.ToUpperInvariant();
    }

    /// <summary>Servername ohne führende/abschließende Schrägstriche („\\FS01\“ → „FS01“), null wenn leer.</summary>
    public static string? Strip(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        var s = server.Trim().TrimStart('\\', '/').TrimEnd('\\', '/');
        return s.Length == 0 ? null : s;
    }

    /// <summary>Server eines Pfads (\\server\freigabe\… oder \\?\UNC\server\…), null für lokale Pfade.</summary>
    public static string? ServerOfPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim().Replace('/', '\\');
        if (p == ".") return null;
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;
        if (!p.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var rest = p[2..];
        var end = rest.IndexOf('\\');
        return Normalize(end < 0 ? rest : rest[..end]);
    }

    /// <summary>Alle Rechner eines Laufs in der Reihenfolge ihres Auftretens; null steht für diesen Rechner.</summary>
    public static IReadOnlyList<string?> Of(ScanResult result)
    {
        var list = new List<string?>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? machine)
        {
            if (seen.Add(machine ?? "")) list.Add(machine);
        }

        foreach (var target in result.Request.Targets) Add(ServerOfPath(target));
        foreach (var share in result.Shares) Add(Normalize(share.Server));
        foreach (var folder in result.Folders)
        {
            Add(ServerOfPath(folder.Path));
            if (folder.ShareUnc is not null) Add(ServerOfPath(folder.ShareUnc));
        }
        if (list.Count == 0) list.Add(null);
        return list;
    }

    /// <summary>Dieser Rechner unter allen Namen, unter denen er in Pfaden auftauchen kann.</summary>
    private static HashSet<string> BuildLocalNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".", "localhost", "127.0.0.1", "::1", "[::1]", "0:0:0:0:0:0:0:1", Environment.MachineName,
        };
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            if (!string.IsNullOrEmpty(props.HostName))
            {
                names.Add(props.HostName);
                if (!string.IsNullOrEmpty(props.DomainName)) names.Add($"{props.HostName}.{props.DomainName}");
            }
        }
        catch (NetworkInformationException)
        {
            // Ohne Netzwerkdaten bleibt es beim NetBIOS-Namen.
        }
        return names;
    }
}
