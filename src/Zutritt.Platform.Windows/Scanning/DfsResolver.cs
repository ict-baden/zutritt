// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Zutritt.Core.Scanning;

namespace Zutritt.Platform.Windows.Scanning;

/// <summary>
/// Löst DFS-Wurzeln und DFS-Verknüpfungen auf die Freigabe auf, auf der die Daten wirklich liegen
/// (NetDfsGetClientInfo – die Sicht dieses Rechners, also das Ziel, das er beim Zugriff benutzt).
/// </summary>
internal static class DfsResolver
{
    /// <returns>
    /// Zielfreigabe, wenn <paramref name="uncPath"/> genau eine DFS-Wurzel oder -Verknüpfung ist; sonst null
    /// (gewöhnliche Freigabe, Ordner unterhalb einer Verknüpfung, DFS-Client nicht verfügbar).
    /// </returns>
    public static unsafe DfsTarget? Resolve(string uncPath)
    {
        int status;
        nint buffer;
        try
        {
            // NetDfsGetClientInfo liest nur den Verweis-Zwischenspeicher dieses Rechners. Der füllt sich erst beim Zugriff
            // (Praxistest 2026-11-20: der erste Scan nach dem Start fand keine Verknüpfung, der zweite alle) – deshalb den
            // Pfad zuerst anfassen. Ob er erreichbar ist, entscheidet später die Prüfung des Ziels.
            _ = Native.GetFileAttributes(LongPath.ToExtended(uncPath));
            status = Native.NetDfsGetClientInfo(uncPath, null, null, 3, out buffer);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        if (status != Native.ERROR_SUCCESS || buffer == 0) return null;
        try
        {
            var info = (Native.DFS_INFO_3*)buffer;
            // Für einen gewöhnlichen Ordner im Namensraum liefert Windows den Eintrag, der ihn enthält (die Wurzel oder
            // eine Verknüpfung weiter oben). Gefragt ist aber, ob genau dieser Pfad ein Eintrag ist.
            if (!IsSameEntry(Marshal.PtrToStringUni(info->EntryPath), uncPath)) return null;
            var storages = (Native.DFS_STORAGE_INFO*)info->Storage;
            if (storages is null || info->NumberOfStorages == 0) return null;

            // Das aktive Ziel ist das, über das dieser Rechner gerade zugreift; sonst das erste erreichbare, sonst das erste.
            DfsTarget? online = null, first = null;
            for (uint i = 0; i < info->NumberOfStorages; i++)
            {
                var target = ToTarget(storages[i]);
                if (target is null) continue;
                if ((storages[i].State & Native.DFS_STORAGE_STATE_ACTIVE) != 0) return target;
                if ((storages[i].State & Native.DFS_STORAGE_STATE_ONLINE) != 0) online ??= target;
                first ??= target;
            }
            return online ?? first;
        }
        finally
        {
            _ = Native.NetApiBufferFree(buffer);
        }
    }

    /// <summary>
    /// Ob der gelieferte Eintrag der gefragte Pfad ist. Verglichen wird die Zahl der Pfadteile, nicht der Text:
    /// Windows nennt den Server im Eintrag teils anders als in der Frage (kurzer Name, voller Name, Domäne).
    /// </summary>
    internal static bool IsSameEntry(string? entryPath, string queried)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) return true; // nichts zu vergleichen – dem Erfolg der Abfrage glauben
        return Segments(entryPath) == Segments(queried);

        static int Segments(string path) => path.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static DfsTarget? ToTarget(Native.DFS_STORAGE_INFO storage)
        => Parse(Marshal.PtrToStringUni(storage.ServerName), Marshal.PtrToStringUni(storage.ShareName));

    /// <summary>
    /// Server und Freigabe eines DFS-Ziels. Der „Freigabename“ eines Ziels kann einen Unterordner enthalten
    /// (Ziel \\server\freigabe\ordner) – die Freigabe ist dann der erste Teil.
    /// </summary>
    internal static DfsTarget? Parse(string? server, string? share)
    {
        server = server?.Trim('\\');
        var name = share?.Trim('\\').Split('\\')[0];
        return string.IsNullOrEmpty(server) || string.IsNullOrEmpty(name) ? null : new DfsTarget(server, name);
    }
}
