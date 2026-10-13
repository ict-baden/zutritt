using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Liest Besitzer, Vererbungsschutz und NTFS-Einträge eines Ordners oder einer Datei (SIDs, keine Namen).</summary>
public static class AclReader
{
    public static FolderNode Read(string path, int depth, bool isFile) => throw new NotImplementedException();
}
