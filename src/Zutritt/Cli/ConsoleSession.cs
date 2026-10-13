using System.Text;
using Zutritt.Interop;

namespace Zutritt.Cli;

/// <summary>
/// Verbindet das Fensterprogramm mit der Konsole des Aufrufers. In eine echte Konsole wird Unicode geschrieben
/// (Umlaute und „Anführungszeichen“ erscheinen richtig, ohne die Codepage des Aufrufers umzustellen); bei
/// Umleitung in Datei oder Pipe die OEM-Codepage, wie sie cmd und PowerShell dort erwarten.
/// </summary>
internal static class ConsoleSession
{
    private static bool _attached;

    /// <summary>Ausgabe läuft in eine echte Konsole (nicht umgeleitet) – dann lohnt sich eine Fortschrittszeile.</summary>
    public static bool IsInteractive { get; private set; }

    public static void Attach()
    {
        if (_attached) return;
        _attached = true;

        bool outRedirected = IsRedirected(Native.STD_OUTPUT_HANDLE);
        bool errRedirected = IsRedirected(Native.STD_ERROR_HANDLE);
        bool attached = false;
        if (!outRedirected || !errRedirected) attached = Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);

        if (attached && !outRedirected)
        {
            // Unicode → .NET schreibt mit WriteConsoleW und lässt die Codepage der Konsole in Ruhe
            Console.OutputEncoding = Encoding.Unicode;
            IsInteractive = true;
            // Die Eingabeaufforderung wartet nicht auf Fensterprogramme und hat ihren Prompt schon geschrieben
            Console.WriteLine();
        }

        var oem = OemEncoding();
        if (outRedirected) Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), oem) { AutoFlush = true });
        if (errRedirected) Console.SetError(new StreamWriter(Console.OpenStandardError(), oem) { AutoFlush = true });
    }

    private static bool IsRedirected(int std)
    {
        var handle = Native.GetStdHandle(std);
        if (handle is 0 or -1) return false;
        var type = Native.GetFileType(handle);
        return type is Native.FILE_TYPE_DISK or Native.FILE_TYPE_PIPE;
    }

    private static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)Native.GetOEMCP(), new AsciiFallback(), DecoderFallback.ReplacementFallback);
        }
        catch
        {
            return new UTF8Encoding(false);
        }
    }

    /// <summary>Zeichen, die es in der OEM-Codepage nicht gibt (z. B. „ “ – …), lesbar ersetzen statt „?“.</summary>
    private sealed class AsciiFallback : EncoderFallback
    {
        public override int MaxCharCount => 3;
        public override EncoderFallbackBuffer CreateFallbackBuffer() => new Buffer();

        private sealed class Buffer : EncoderFallbackBuffer
        {
            private string _text = "";
            private int _pos;

            public override int Remaining => _text.Length - _pos;

            public override bool Fallback(char unknown, int index)
            {
                _text = unknown switch
                {
                    '„' or '“' or '”' or '»' or '«' => "\"",
                    '‚' or '‘' or '’' or '›' or '‹' => "'",
                    '–' or '—' or '−' => "-",
                    '…' => "...",
                    '→' => "->",
                    '←' => "<-",
                    '•' or '·' => "*",
                    '\u00A0' or '\u202F' => " ",
                    _ => "?",
                };
                _pos = 0;
                return true;
            }

            public override bool Fallback(char high, char low, int index)
            {
                _text = "?";
                _pos = 0;
                return true;
            }

            public override char GetNextChar() => _pos < _text.Length ? _text[_pos++] : '\0';

            public override bool MovePrevious()
            {
                if (_pos == 0) return false;
                _pos--;
                return true;
            }

            public override void Reset()
            {
                _text = "";
                _pos = 0;
            }
        }
    }
}
