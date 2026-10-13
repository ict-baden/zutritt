using System.Runtime.InteropServices;

namespace Zutritt.Interop;

/// <summary>Die wenigen Windows-Aufrufe der Oberfläche: Fensterrahmen (DWM) und Konsole für den Befehlszeilenmodus.</summary>
internal static partial class Native
{
    // ---------- DWM ----------
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    // ---------- Fenstergröße ----------
    // Ohne sichtbaren Desktop (z. B. 1024×768 in einer Dienst-Sitzung) begrenzt Windows Fenster auf die
    // Bildschirmgröße. Für Screenshots in fester Größe hebt WM_GETMINMAXINFO diese Grenze auf.
    public const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // ---------- Konsole ----------
    // Zutritt.exe ist ein Fensterprogramm (WinExe). Für die Befehlszeile hängt es sich an die Konsole
    // des Aufrufers, statt eine eigene zu öffnen – ein Doppelklick zeigt so nie ein schwarzes Fenster.
    public const int ATTACH_PARENT_PROCESS = -1;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;
    public const uint FILE_TYPE_DISK = 0x0001;
    public const uint FILE_TYPE_CHAR = 0x0002;
    public const uint FILE_TYPE_PIPE = 0x0003;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetStdHandle(int stdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetFileType(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetOEMCP();
}
