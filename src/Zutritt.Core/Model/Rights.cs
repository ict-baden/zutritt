using System.Security.AccessControl;

namespace Zutritt.Core.Model;

/// <summary>
/// Vereinfachte Rechtestufe, wie sie Menschen lesen: aufsteigend geordnet, damit sich
/// „mindestens Ändern“ als Vergleich schreiben lässt.
/// </summary>
public enum AccessLevel
{
    Keine = 0,
    /// <summary>Nur Ordner auflisten bzw. Attribute lesen, kein Dateiinhalt.</summary>
    Auflisten = 1,
    Lesen = 2,
    /// <summary>Lesen plus Schreiben, aber kein Löschen (z. B. Ablage „nur hinzufügen“).</summary>
    Schreiben = 3,
    /// <summary>Lesen, Schreiben, Löschen.</summary>
    Aendern = 4,
    /// <summary>Darf zusätzlich Berechtigungen ändern oder Besitz übernehmen.</summary>
    Vollzugriff = 5,
}

/// <summary>Freigabe-Berechtigung (SMB), unabhängig von den NTFS-Rechten.</summary>
public enum ShareAccess
{
    Keine = 0,
    Lesen = 1,
    Aendern = 2,
    Vollzugriff = 3,
}

public static class Rights
{
    private const FileSystemRights ReadMask =
        FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadAttributes | FileSystemRights.ReadPermissions;
    private const FileSystemRights WriteMask =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes;

    /// <summary>
    /// Ordnet eine NTFS-Rechtemaske einer <see cref="AccessLevel"/> zu. Generische Rechte
    /// (GENERIC_ALL usw., z. B. bei CREATOR OWNER) werden vorher in Dateirechte übersetzt.
    /// </summary>
    public static AccessLevel ToLevel(FileSystemRights rights)
    {
        rights = MapGeneric(rights);
        if ((rights & (FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0) return AccessLevel.Vollzugriff;
        bool write = (rights & (FileSystemRights.WriteData | FileSystemRights.AppendData)) != 0;
        bool delete = (rights & (FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles)) != 0;
        if (write && delete) return AccessLevel.Aendern;
        if (write) return AccessLevel.Schreiben;
        if ((rights & FileSystemRights.ReadData) != 0) return AccessLevel.Lesen;
        if ((rights & ReadMask) != 0 || (rights & FileSystemRights.Traverse) != 0) return AccessLevel.Auflisten;
        return AccessLevel.Keine;
    }

    /// <summary>GENERIC_*-Bits (oberes Byte) in konkrete Dateirechte übersetzen.</summary>
    public static FileSystemRights MapGeneric(FileSystemRights rights)
    {
        var raw = (uint)rights;
        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, GenericExecute = 0x20000000, GenericAll = 0x10000000;
        var mapped = rights & (FileSystemRights)0x0FFFFFFF;
        if ((raw & GenericAll) != 0) mapped |= FileSystemRights.FullControl;
        if ((raw & GenericRead) != 0) mapped |= FileSystemRights.Read;
        if ((raw & GenericWrite) != 0) mapped |= FileSystemRights.Write;
        if ((raw & GenericExecute) != 0) mapped |= FileSystemRights.ExecuteFile | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
        return mapped;
    }

    public static string Label(AccessLevel level) => level switch
    {
        AccessLevel.Vollzugriff => "Vollzugriff",
        AccessLevel.Aendern => "Ändern",
        AccessLevel.Schreiben => "Schreiben",
        AccessLevel.Lesen => "Lesen",
        AccessLevel.Auflisten => "Auflisten",
        _ => "Kein Zugriff",
    };

    public static string Label(ShareAccess access) => access switch
    {
        ShareAccess.Vollzugriff => "Vollzugriff",
        ShareAccess.Aendern => "Ändern",
        ShareAccess.Lesen => "Lesen",
        _ => "Kein Zugriff",
    };

    /// <summary>
    /// Recht eines NTFS-Eintrags für Menschen. Erlauben: die Stufe, also was jemand höchstens kann. Verweigern: was
    /// gesperrt ist (<see cref="Parts"/>) – die Stufe taugt dafür nicht, „nur Löschen verweigert“ hieße sonst „Kein Zugriff“,
    /// „Berechtigungen ändern verweigert“ hieße „Vollzugriff“. Erlauben-Einträge ohne ganze Stufe nennen ihre Einzelrechte.
    /// </summary>
    public static string EntryLabel(FileSystemRights rights, bool isDeny)
    {
        var level = ToLevel(rights);
        return !isDeny && level != AccessLevel.Keine ? Label(level) : string.Join(", ", Parts(rights));
    }

    /// <summary>
    /// Die Rechte einer Maske in Worten: ganze Stufen wie in der Windows-Oberfläche („Vollzugriff“, „Ändern“), sonst die
    /// enthaltenen Teile („Lesen“, „Schreiben“, „Löschen“, „Berechtigungen ändern“ …), bei reinen Kleinigkeiten genau diese.
    /// </summary>
    public static IReadOnlyList<string> Parts(FileSystemRights rights)
    {
        const FileSystemRights Full = FileSystemRights.FullControl & ~FileSystemRights.Synchronize;
        const FileSystemRights Modify = FileSystemRights.Modify & ~FileSystemRights.Synchronize;
        var mask = MapGeneric(rights) & ~FileSystemRights.Synchronize;
        bool Has(FileSystemRights r) => (mask & r) != 0;

        if ((mask & Full) == Full) return ["Vollzugriff"];
        var parts = new List<string>();
        if ((mask & Modify) == Modify) parts.Add("Ändern");
        else
        {
            if (Has(FileSystemRights.ReadData)) parts.Add("Lesen");
            if (Has(FileSystemRights.WriteData | FileSystemRights.AppendData)) parts.Add("Schreiben");
            if (Has(FileSystemRights.Delete)) parts.Add("Löschen");
        }
        if (Has(FileSystemRights.DeleteSubdirectoriesAndFiles)) parts.Add("Unterordner und Dateien löschen");
        if (Has(FileSystemRights.ChangePermissions)) parts.Add("Berechtigungen ändern");
        if (Has(FileSystemRights.TakeOwnership)) parts.Add("Besitz übernehmen");
        if (parts.Count == 0)
        {
            // Nur Kleinigkeiten – dann genau diese nennen
            if (Has(FileSystemRights.ExecuteFile)) parts.Add("Ordner durchsuchen bzw. Datei ausführen");
            if (Has(FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes)) parts.Add("Attribute lesen");
            if (Has(FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes)) parts.Add("Attribute schreiben");
            if (Has(FileSystemRights.ReadPermissions)) parts.Add("Berechtigungen lesen");
        }
        if (parts.Count == 0) parts.Add("Synchronisieren");
        return parts;
    }

    /// <summary>
    /// Was ein Verweigern-Eintrag der Freigabe sperrt. <see cref="ShareAce.Access"/> nennt bei Verweigerungen die Stufe,
    /// <b>ab der</b> gesperrt ist – „Verweigern Lesen“ läse sich wie „nur Lesen gesperrt“, gemeint ist aber alles.
    /// Deshalb wird ausgeschrieben, was nicht mehr geht: „Verweigert: alles (ab Lesen)“, „Verweigert: Ändern und
    /// Vollzugriff (ab Ändern)“ (Lesen bleibt möglich), „Verweigert: Vollzugriff“ (Ändern bleibt möglich).
    /// </summary>
    public static string DenyLabel(ShareAccess deniedFrom) => deniedFrom switch
    {
        ShareAccess.Lesen => "Verweigert: alles (ab Lesen)",
        ShareAccess.Aendern => "Verweigert: Ändern und Vollzugriff (ab Ändern)",
        ShareAccess.Vollzugriff => "Verweigert: Vollzugriff",
        // Legt der Scanner nie an; die Auswertung lässt einen solchen Eintrag wirkungslos
        _ => "Verweigert: nichts (ohne Wirkung)",
    };

    /// <summary>Recht eines Freigabe-Eintrags für Menschen: Erlauben wie <see cref="Label(ShareAccess)"/>, Verweigern wie <see cref="DenyLabel"/>.</summary>
    public static string Label(ShareAce ace)
    {
        ArgumentNullException.ThrowIfNull(ace);
        return ace.IsDeny ? DenyLabel(ace.Access) : Label(ace.Access);
    }

    /// <summary>Über das Netz gilt die strengere von Freigabe- und NTFS-Berechtigung.</summary>
    public static AccessLevel CapByShare(AccessLevel ntfs, ShareAccess share) => share switch
    {
        ShareAccess.Vollzugriff => ntfs,
        ShareAccess.Aendern => ntfs > AccessLevel.Aendern ? AccessLevel.Aendern : ntfs,
        ShareAccess.Lesen => ntfs > AccessLevel.Lesen ? AccessLevel.Lesen : ntfs,
        _ => AccessLevel.Keine,
    };
}
