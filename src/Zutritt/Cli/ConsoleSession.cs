// SPDX-License-Identifier: MIT
using System.Text;
using Zutritt.Interop;

namespace Zutritt.Cli;

/// <summary>
/// Verbindet das Fensterprogramm mit der Konsole des Aufrufers und wählt je Ausgabe die Kodierung, mit der das
/// Gegenüber Umlaute und „Anführungszeichen“ richtig liest.
/// </summary>
/// <remarks>
/// <para>Entscheidung je Ziel der Ausgabe (stdout und stderr getrennt):</para>
/// <list type="bullet">
/// <item><b>Konsole</b>: Unicode über WriteConsoleW – richtig in jeder Konsole, ohne deren Codepage umzustellen.</item>
/// <item><b>Datei</b> (<c>Zutritt.exe scan … &gt; log.txt 2&gt;&amp;1</c> in cmd, geplante Aufgaben): UTF-8 mit BOM, damit Editoren
/// und <c>Get-Content</c> die Kodierung sicher erkennen. Das BOM nur in eine leere Datei – beim Anhängen (<c>&gt;&gt;</c>) oder wenn
/// stdout und stderr in dieselbe Datei gehen, stünde es sonst mitten im Text.</item>
/// <item><b>Pipe</b> (<c>&amp; Zutritt.exe … | Out-String</c>, <c>$x = &amp; Zutritt.exe …</c>, cmd <c>| more</c>): in der Codepage der
/// Konsole des Aufrufers. Genau mit ihr (<c>[Console]::OutputEncoding</c>) dekodiert PowerShell, was ein Programm in die Pipe
/// schreibt; PowerShell 7 beachtet dabei kein BOM (ausprobiert: UTF-8 mit BOM kommt bei Codepage 437/850 als „Ã¼“ an,
/// bei 65001 mit einem unsichtbaren U+FEFF am Anfang). Windows PowerShell 5.1 erkennt das BOM zwar, liest aber die
/// Konsolen-Codepage ebenso richtig. Steht die Konsole auf UTF-8 (65001), wird also UTF-8 geschrieben, sonst OEM
/// (437/850: Umlaute und ß vorhanden, typografische Zeichen werden durch ähnliche ersetzt). Ohne Konsole beim
/// Aufrufer (selten, z. B. ein Dienst liest die Pipe): UTF-8 ohne BOM.</item>
/// </list>
/// <para>
/// Grenze: PowerShell 7.4+ reicht bei <c>&amp; Zutritt.exe … &gt; log.txt</c> die Bytes unverändert in die Datei – mit OEM-Konsole
/// steht dann OEM darin. Für Logdateien deshalb die Umleitung von cmd nehmen (siehe Hilfe) oder vorher
/// <c>[Console]::OutputEncoding = [Text.Encoding]::UTF8</c> setzen.
/// </para>
/// </remarks>
internal static class ConsoleSession
{
    private static bool _attached;

    /// <summary>Ausgabe läuft in eine echte Konsole (nicht umgeleitet) – dann lohnt sich eine Fortschrittszeile.</summary>
    public static bool IsInteractive { get; private set; }

    private enum Target { None, Console, File, Pipe }

    public static void Attach()
    {
        if (_attached) return;
        _attached = true;

        // Ziele vor AttachConsole bestimmen – danach zeigen die Standard-Handles auf die Konsole
        var outHandle = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
        var errHandle = Native.GetStdHandle(Native.STD_ERROR_HANDLE);
        var outTarget = TargetOf(outHandle);
        var errTarget = TargetOf(errHandle);

        // Auch bei zwei umgeleiteten Ausgaben anhängen: Nur die Konsole des Aufrufers verrät, wie er eine Pipe
        // dekodiert, und Strg+C erreicht so den laufenden Scan (sauberer Abbruch mit Exitcode 3)
        bool attached = Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        if (attached)
        {
            // AttachConsole setzt die Standard-Handles auf die Konsole – auch umgeleitete. Ohne das Zurücksetzen
            // ginge „> log.txt“ ins Leere (die Datei bliebe leer, die Ausgabe landete in der Konsole).
            if (outTarget is Target.File or Target.Pipe) Native.SetStdHandle(Native.STD_OUTPUT_HANDLE, outHandle);
            if (errTarget is Target.File or Target.Pipe) Native.SetStdHandle(Native.STD_ERROR_HANDLE, errHandle);
        }

        if (attached && outTarget is Target.None or Target.Console)
        {
            // Unicode → .NET schreibt mit WriteConsoleW und lässt die Codepage der Konsole in Ruhe
            Console.OutputEncoding = Encoding.Unicode;
            IsInteractive = true;
            // Die Eingabeaufforderung wartet nicht auf Fensterprogramme und hat ihren Prompt schon geschrieben
            Console.WriteLine();
        }

        if (outTarget is Target.File or Target.Pipe)
            Console.SetOut(CreateWriter(Console.OpenStandardOutput(), outHandle, outTarget, attached));
        if (errTarget is Target.File or Target.Pipe)
            Console.SetError(CreateWriter(Console.OpenStandardError(), errHandle, errTarget, attached));
    }

    private static Target TargetOf(nint handle)
    {
        if (handle is 0 or -1) return Target.None;
        return Native.GetFileType(handle) switch
        {
            Native.FILE_TYPE_DISK => Target.File,
            Native.FILE_TYPE_PIPE => Target.Pipe,
            // Zeichengerät: Konsole oder NUL – beides braucht keine eigene Kodierung
            _ => Target.Console,
        };
    }

    private static StreamWriter CreateWriter(Stream stream, nint handle, Target target, bool attached)
    {
        if (target == Target.File)
            return new StreamWriter(new BomOnFirstWrite(stream, handle), new UTF8Encoding(false)) { AutoFlush = true };
        return new StreamWriter(stream, PipeEncoding(attached)) { AutoFlush = true };
    }

    /// <summary>
    /// Schreibt das UTF-8-BOM erst mit der ersten Ausgabe – und nur, wenn die Datei dann noch leer ist. Sofort
    /// geschrieben, bekäme jede umgeleitete Datei drei Bytes, auch wenn nichts hineingeht (<c>2&gt; fehler.txt</c> wäre
    /// nie leer), und bei <c>&gt; log.txt 2&gt;&amp;1</c> ginge das BOM an die Ausgabe, die zuerst schreibt.
    /// </summary>
    private sealed class BomOnFirstWrite(Stream inner, nint handle) : Stream
    {
        private bool _started;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.IsEmpty) return;
            if (!_started)
            {
                _started = true;
                if (Native.GetFileSizeEx(handle, out var size) && size == 0) inner.Write(Encoding.UTF8.Preamble);
            }
            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Kodierung, mit der der Aufrufer die Pipe liest: die Codepage seiner Konsole.</summary>
    private static Encoding PipeEncoding(bool attached)
    {
        uint codePage = attached ? Native.GetConsoleOutputCP() : 0;
        if (codePage is 0 or 65001) return new UTF8Encoding(false);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)codePage, new AsciiFallback(), DecoderFallback.ReplacementFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new UTF8Encoding(false);
        }
    }

    /// <summary>Zeichen, die es in der Codepage nicht gibt (z. B. „ “ – …), lesbar ersetzen statt „?“.</summary>
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
