using Zutritt.Core.Model;

namespace Zutritt.Core.Storage;

/// <summary>Speichert Läufe in einer SQLite-Datei (Standard: %ProgramData%\Zutritt\zutritt.db, sonst %LocalAppData%).</summary>
public sealed class ScanStore : IDisposable
{
    public ScanStore(string databasePath) => throw new NotImplementedException();

    public static string DefaultPath => throw new NotImplementedException();

    public long Save(ScanResult scan) => throw new NotImplementedException();
    public ScanResult? Load(long id) => throw new NotImplementedException();
    public IReadOnlyList<ScanSummary> List() => throw new NotImplementedException();
    /// <summary>Der letzte Lauf vor <paramref name="scan"/> mit denselben Zielen, für den Vergleich.</summary>
    public ScanSummary? FindPrevious(ScanResult scan) => throw new NotImplementedException();
    public void Delete(long id) => throw new NotImplementedException();
    public void Dispose() { }
}
