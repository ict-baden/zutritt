// SPDX-License-Identifier: MIT
namespace Zutritt.Core.Reports;

/// <summary>
/// Der Bericht konnte nicht gespeichert werden (Datei in Excel geöffnet, keine Schreibrechte, Datenträger
/// voll …). Die Meldung ist in der Sprache der Oberfläche und für Menschen gedacht; die ursprüngliche Ausnahme steckt in
/// <see cref="Exception.InnerException"/>. Leitet von <see cref="IOException"/> ab, damit vorhandene
/// Fehlerbehandlung für Dateizugriffe greift.
/// </summary>
public sealed class ReportWriteException : IOException, IHasErrorCode
{
    /// <summary>Fehlercode für den Support (<see cref="ErrorCodes.ReportWrite"/>).</summary>
    public string ErrorCode => ErrorCodes.ReportWrite;

    public ReportWriteException(string path, string message, Exception? inner = null) : base(message, inner)
    {
        Path = path;
    }

    /// <summary>Zieldatei des Berichts.</summary>
    public string Path { get; }
}
