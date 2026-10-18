using System.Runtime.InteropServices;
using Zutritt.Core.Model;
using Zutritt.Core.Scanning;

namespace Zutritt.Platform.Windows.Scanning;

/// <summary>Listet Ordnerinhalte über FindFirstFileEx (mit „\\?\“-Pfaden, also ohne Längengrenze).</summary>
internal static class DirectoryLister
{
    /// <summary>Inhalt von <paramref name="directory"/> an <paramref name="entries"/> anhängen.</summary>
    /// <returns>0, wenn vollständig gelesen; sonst der Win32-Fehler (bis dahin gelesene Einträge bleiben erhalten).</returns>
    public static unsafe int List(string directory, List<DirEntry> entries)
    {
        var pattern = LongPath.ToExtended(WindowsPath.Join(directory, "*"));
        Native.WIN32_FIND_DATAW data;
        using var handle = Native.FindFirstFileEx(pattern, Native.FindExInfoBasic, &data, Native.FindExSearchNameMatch, 0,
            Native.FIND_FIRST_EX_LARGE_FETCH);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            // Ganz leerer Ordner (Laufwerkswurzel ohne „.“/„..“) meldet „nicht gefunden“.
            return error == Native.ERROR_FILE_NOT_FOUND ? Native.ERROR_SUCCESS : error;
        }

        do
        {
            var name = new string(data.cFileName);
            if (name is "." or "..") continue;
            var attributes = (FileAttributes)data.dwFileAttributes;
            entries.Add(new DirEntry(name, IsDirectory(attributes), IsLink(attributes, data.dwReserved0)));
        }
        while (Native.FindNextFile(handle, &data));

        int last = Marshal.GetLastPInvokeError();
        return last == Native.ERROR_NO_MORE_FILES ? Native.ERROR_SUCCESS : last;
    }

    internal static bool IsDirectory(FileAttributes attributes) => (attributes & FileAttributes.Directory) != 0;

    /// <summary>
    /// Verweis auf etwas anderes (Junction, symbolische Verknüpfung, Bereitstellungspunkt) – wird nicht verfolgt.
    /// Andere Reparse-Punkte (z. B. OneDrive-Platzhalter) sind echte Ordner und werden normal durchlaufen.
    /// </summary>
    internal static bool IsLink(FileAttributes attributes, uint reparseTag)
        => (attributes & FileAttributes.ReparsePoint) != 0 && (reparseTag & Native.IO_REPARSE_TAG_NAME_SURROGATE) != 0;
}
