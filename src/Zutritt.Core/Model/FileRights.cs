namespace Zutritt.Core.Model;

/// <summary>
/// Rechte eines NTFS-Eintrags (Zugriffsmaske). Eigener Typ statt <c>System.Security.AccessControl.FileSystemRights</c>,
/// der nur unter Windows zur Verfügung steht – Auswertung, Vergleich, Speicher und Berichte laufen so auf jedem System.
/// </summary>
/// <remarks>
/// Die Werte sind genau die der Windows-Zugriffsmaske (winnt.h) und von <c>FileSystemRights</c>: Die Datenbank speichert
/// die Zahl, gespeicherte Läufe bleiben deshalb ohne Umstellung lesbar. Generische Rechte (GENERIC_ALL usw.) stehen
/// unverändert im oberen Byte, <see cref="Rights.MapGeneric"/> löst sie auf.
/// </remarks>
[Flags]
public enum FileRights : uint
{
    ReadData = 0x0001,
    ListDirectory = ReadData,
    WriteData = 0x0002,
    CreateFiles = WriteData,
    AppendData = 0x0004,
    CreateDirectories = AppendData,
    ReadExtendedAttributes = 0x0008,
    WriteExtendedAttributes = 0x0010,
    ExecuteFile = 0x0020,
    Traverse = ExecuteFile,
    DeleteSubdirectoriesAndFiles = 0x0040,
    ReadAttributes = 0x0080,
    WriteAttributes = 0x0100,
    Delete = 0x0001_0000,
    ReadPermissions = 0x0002_0000,
    ChangePermissions = 0x0004_0000,
    TakeOwnership = 0x0008_0000,
    Synchronize = 0x0010_0000,

    /// <summary>Alle Dateirechte (0x1F01FF), wie „Vollzugriff“ in Windows.</summary>
    FullControl = 0x001F_01FF,
    /// <summary>0x20089 – ohne SYNCHRONIZE, wie bei Windows.</summary>
    Read = ReadData | ReadExtendedAttributes | ReadAttributes | ReadPermissions,
    ReadAndExecute = Read | ExecuteFile,
    Write = WriteData | AppendData | WriteExtendedAttributes | WriteAttributes,
    Modify = Write | ReadAndExecute | Delete,
}

/// <summary>Wohin ein Eintrag vererbt wird (Werte wie <c>InheritanceFlags</c> von Windows).</summary>
[Flags]
public enum AceInheritance
{
    None = 0,
    /// <summary>Unterordner erben den Eintrag („Container erben“).</summary>
    ContainerInherit = 1,
    /// <summary>Dateien erben den Eintrag („Objekte erben“).</summary>
    ObjectInherit = 2,
}

/// <summary>Wie ein Eintrag weitergegeben wird (Werte wie <c>PropagationFlags</c> von Windows).</summary>
[Flags]
public enum AcePropagation
{
    None = 0,
    /// <summary>Nur eine Ebene weit vererben.</summary>
    NoPropagateInherit = 1,
    /// <summary>Gilt nur für Unterordner bzw. Dateien, nicht für das Objekt selbst.</summary>
    InheritOnly = 2,
}
