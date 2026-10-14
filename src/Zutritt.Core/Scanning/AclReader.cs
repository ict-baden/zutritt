using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using Zutritt.Core.Model;

namespace Zutritt.Core.Scanning;

/// <summary>Liest Besitzer, Vererbungsschutz und NTFS-Einträge eines Ordners oder einer Datei (SIDs, keine Namen).</summary>
/// <remarks>
/// Fehler (Zugriff verweigert, Pfad nicht gefunden, Netzwerkfehler) kommen nicht als Ausnahme, sondern in
/// <see cref="FolderNode.Error"/>. Die Einträge stehen in der Reihenfolge der Zugriffsliste, generische Rechte
/// (GENERIC_ALL usw.) bleiben unverändert in <see cref="AceEntry.Rights"/>.
/// <para>
/// Zugriffslisten im NT4-Stil (ohne Kennzeichen „automatisch geerbt“, z. B. nach Kopien mit alten Werkzeugen oder
/// unter Ordnern wie %TEMP%) tragen auf der Platte keine Geerbt-Markierung. Windows (Explorer, icacls, Get-Acl) ermittelt
/// sie beim Lesen aus dem Elternordner – genau so macht es der Leser auch. Sonst sähe jeder Unterordner wie einer mit
/// eigenen Rechten aus.
/// </para>
/// </remarks>
public static class AclReader
{
    internal const string EveryoneSid = "S-1-1-0";

    // Puffer je Thread wiederverwenden: Bei großen Bäumen sonst eine Allokation pro Ordner.
    [ThreadStatic] private static byte[]? t_buffer;

    private static readonly Native.GENERIC_MAPPING FileMapping = new()
    {
        GenericRead = FileAccessMask.FileGenericRead,
        GenericWrite = FileAccessMask.FileGenericWrite,
        GenericExecute = FileAccessMask.FileGenericExecute,
        GenericAll = FileAccessMask.FileAllAccess,
    };

    /// <summary>
    /// Rechte von <paramref name="path"/> lesen. Verknüpfungen (Junctions, symbolische Links) werden nicht verfolgt:
    /// Gelesen wird die Zugriffsliste des Verweises selbst.
    /// </summary>
    /// <param name="path">Vollständiger lokaler oder UNC-Pfad; relative Pfade werden zum aktuellen Verzeichnis aufgelöst.</param>
    /// <param name="depth">Tiefe unter der Scan-Wurzel (wird nur übernommen).</param>
    /// <param name="isFile">Datei statt Ordner (wird nur übernommen).</param>
    public static FolderNode Read(string path, int depth, bool isFile) => Read(path, depth, isFile, followLinks: false);

    internal static FolderNode Read(string path, int depth, bool isFile, bool followLinks)
        => Read(path, depth, isFile, followLinks, parentDescriptor: null, readParent: true, wantDescriptor: false, out _);

    /// <param name="parentDescriptor">Rohe Sicherheitsbeschreibung des Elternordners, wenn schon gelesen (Scanner).</param>
    /// <param name="readParent">Ohne <paramref name="parentDescriptor"/> den Elternordner bei Bedarf selbst lesen.</param>
    /// <param name="wantDescriptor">Rohe Beschreibung dieses Objekts zurückgeben (für seine Kinder).</param>
    /// <param name="descriptor">Rohe Beschreibung, null bei Fehler oder wenn nicht gewünscht.</param>
    internal static FolderNode Read(string path, int depth, bool isFile, bool followLinks, byte[]? parentDescriptor, bool readParent,
        bool wantDescriptor, out byte[]? descriptor)
    {
        ArgumentNullException.ThrowIfNull(path);
        descriptor = null;
        string full;
        try
        {
            if (path.Contains('\0')) return Failed(path, depth, isFile, "Ungültiger Pfad");
            // Vollständige Pfade nur normalisieren, wenn nötig – GetFullPath kürzt Leerzeichen und Punkte am Namensende.
            full = LongPath.FromExtended(path);
            // „\\?\Volume{…}\x“ (Bereitstellungspunkt ohne Laufwerk) unverändert lassen: Ohne Präfix wäre es ein
            // relativer Pfad, und gelesen würde ein Ordner unter dem aktuellen Verzeichnis.
            if (LongPath.IsDevicePath(path, full)) full = path;
            else if (!Path.IsPathFullyQualified(full) || LongPath.NeedsNormalization(full)) full = Path.GetFullPath(full);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(path, depth, isFile, "Ungültiger Pfad");
        }

        int error = ReadRaw(full, followLinks, out var raw, out int length, out var finalPath);
        if (error != Native.ERROR_SUCCESS)
            return Failed(full, depth, isFile, "Berechtigungen nicht lesbar: " + Win32Error.Describe(error));

        RawSecurityDescriptor sd;
        try { sd = new RawSecurityDescriptor(raw, 0); }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        {
            return Failed(full, depth, isFile, "Berechtigungen nicht lesbar: Sicherheitsbeschreibung ist beschädigt");
        }
        if (wantDescriptor) descriptor = raw.AsSpan(0, length).ToArray();

        if (NeedsInheritanceCheck(sd))
        {
            // Erst kopieren: Das Lesen des Elternordners benutzt denselben Puffer.
            var current = descriptor ?? raw.AsSpan(0, length).ToArray();
            // Bei verfolgten Verknüpfungen zählt der Elternordner des Ziels, nicht der des Verweises.
            var parent = parentDescriptor ?? (readParent ? ReadParentDescriptor(finalPath ?? full) : null);
            if (parent is not null && ConvertToAutoInherit(parent, current, !isFile) is { } converted) sd = converted;
        }
        return FromDescriptor(full, depth, isFile, sd);
    }

    /// <summary>Knoten aus einer bereits gelesenen Sicherheitsbeschreibung bauen (auch für Tests mit SDDL).</summary>
    internal static FolderNode FromDescriptor(string path, int depth, bool isFile, RawSecurityDescriptor sd)
    {
        var node = new FolderNode
        {
            Path = path,
            Depth = depth,
            IsFile = isFile,
            InheritanceBroken = (sd.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0,
            OwnerSid = sd.Owner?.Value ?? "",
        };

        // NULL-DACL: Windows prüft nichts, jeder darf alles. Als expliziter Eintrag „Jeder: Vollzugriff“ abbilden –
        // eine leere Liste hieße das Gegenteil (niemand hat Zugriff).
        if ((sd.ControlFlags & ControlFlags.DiscretionaryAclPresent) == 0 || sd.DiscretionaryAcl is null)
        {
            node.Aces.Add(new AceEntry { Sid = EveryoneSid, Rights = FileSystemRights.FullControl });
            return node;
        }

        List<int>? unknownTypes = null;
        foreach (GenericAce ace in sd.DiscretionaryAcl)
        {
            if (ace is not QualifiedAce q)
            {
                (unknownTypes ??= []).Add((int)ace.AceType);
                continue;
            }
            bool deny;
            switch (q.AceQualifier)
            {
                case AceQualifier.AccessAllowed:
                    // Bedingte Erlauben-Einträge (dynamische Zugriffssteuerung) zählen als Erlauben: lieber zu viel zeigen.
                    deny = false;
                    break;
                case AceQualifier.AccessDenied:
                    // Bedingte bzw. objektspezifische Verweigerungen gelten nicht immer – sie zu zählen könnte Zugriff verstecken.
                    if (q.IsCallback) continue;
                    if (q is ObjectAce oa && (oa.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0) continue;
                    deny = true;
                    break;
                default:
                    continue; // Überwachungs-/Alarmeinträge regeln keinen Zugriff
            }
            node.Aces.Add(new AceEntry
            {
                Sid = q.SecurityIdentifier.Value,
                Rights = (FileSystemRights)q.AccessMask,
                IsDeny = deny,
                IsInherited = q.IsInherited,
                Inheritance = q.InheritanceFlags,
                Propagation = q.PropagationFlags,
            });
        }

        if (unknownTypes is not null)
            node.Error = "Berechtigungen nicht vollständig auswertbar: unbekannter Eintragstyp " + string.Join(", ", unknownTypes.Distinct());
        return node;
    }

    /// <summary>Zugriffsliste im NT4-Stil: weder „automatisch geerbt“ noch geschützt, aber mit Einträgen.</summary>
    internal static bool NeedsInheritanceCheck(RawSecurityDescriptor sd)
        => (sd.ControlFlags & (ControlFlags.DiscretionaryAclAutoInherited | ControlFlags.DiscretionaryAclProtected)) == 0
           && (sd.ControlFlags & ControlFlags.DiscretionaryAclPresent) != 0
           && sd.DiscretionaryAcl is { Count: > 0 };

    /// <summary>
    /// Wie Windows beim Anzeigen: Einträge, die dem entsprechen, was der Elternordner vererben würde, als geerbt
    /// kennzeichnen. Passt die Liste nicht zum Elternordner, setzt Windows dabei den Vererbungsschutz.
    /// </summary>
    /// <returns>null, wenn die Umwandlung scheitert – dann gilt die Liste wie gelesen.</returns>
    internal static unsafe RawSecurityDescriptor? ConvertToAutoInherit(byte[] parent, byte[] current, bool isDirectory)
    {
        nint converted = 0;
        try
        {
            var mapping = FileMapping;
            bool ok;
            fixed (byte* p = parent)
            fixed (byte* c = current)
                ok = Native.ConvertToAutoInheritPrivateObjectSecurity(p, c, out converted, 0, isDirectory ? (byte)1 : (byte)0, ref mapping);
            if (!ok || converted == 0) return null;
            var length = (int)Native.GetSecurityDescriptorLength(converted);
            var bytes = new byte[length];
            Marshal.Copy(converted, bytes, 0, length);
            return new RawSecurityDescriptor(bytes, 0);
        }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        {
            return null;
        }
        finally
        {
            if (converted != 0) Native.DestroyPrivateObjectSecurity(ref converted);
        }
    }

    private static byte[]? ReadParentDescriptor(string fullPath)
    {
        var parent = ParentOf(fullPath);
        if (parent is null) return null;
        return ReadRaw(parent, followLinks: true, out var raw, out int length, out _) == Native.ERROR_SUCCESS
            ? raw.AsSpan(0, length).ToArray()
            : null;
    }

    /// <summary>Elternpfad ohne Normalisierung (Namen mit Leerzeichen am Ende bleiben erhalten); null an Wurzeln.</summary>
    internal static string? ParentOf(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('\\');
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            // Gerätepfad („\\?\Volume{…}\x“): Die Wurzel ist der Gerätename mit „\“ – ohne ihn öffnete man das Gerät.
            int root = trimmed.IndexOf('\\', 4);
            if (root < 0) return null;
            int last = trimmed.LastIndexOf('\\');
            return last == root ? trimmed[..(root + 1)] : trimmed[..last];
        }
        int cut = trimmed.LastIndexOf('\\');
        if (cut < 0) return null;
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\freigabe ist die Wurzel – darüber gibt es keinen Ordner.
            int share = trimmed.IndexOf('\\', 2);
            if (share < 0 || cut <= share) return null;
            return trimmed[..cut];
        }
        if (cut == 2 && trimmed[1] == ':') return trimmed.Length > 3 ? trimmed[..3] : null; // „C:\x“ → „C:\“
        return cut < 2 ? null : trimmed[..cut];
    }

    /// <summary>Rohe Beschreibung (Besitzer, Gruppe, DACL) in den Puffer dieses Threads lesen.</summary>
    /// <param name="finalPath">Bei <paramref name="followLinks"/>: tatsächlicher Pfad nach Auflösen aller Verknüpfungen.</param>
    private static int ReadRaw(string fullPath, bool followLinks, out byte[] buffer, out int length, out string? finalPath)
    {
        buffer = t_buffer ??= new byte[4096];
        length = 0;
        finalPath = null;
        int error = OpenForReadControl(fullPath, followLinks, out var handle);
        if (error != Native.ERROR_SUCCESS) return error;
        using (handle)
        {
            if (followLinks) finalPath = FinalPath(handle);
            return ReadDescriptor(handle, ref buffer, out length);
        }
    }

    /// <summary>
    /// Öffnet nur mit READ_CONTROL (über NtOpenFile, siehe <see cref="Native"/>). Mit Sicherungsabsicht, damit ein
    /// eingeschaltetes Sicherungsrecht (SeBackupPrivilege) greift; Verknüpfungen auf Wunsch nicht verfolgen.
    /// </summary>
    private static unsafe int OpenForReadControl(string fullPath, bool followLinks, out SafeFileHandle handle)
    {
        // „\\?\C:\x“ → „\??\C:\x“, „\\?\UNC\s\f“ → „\??\UNC\s\f“: dieselben Namen im NT-Namensraum.
        var ntPath = @"\??\" + LongPath.ToExtended(fullPath)[4..];
        if (ntPath.Length > short.MaxValue)
        {
            handle = new SafeFileHandle();
            return 206; // ERROR_FILENAME_EXCED_RANGE
        }
        fixed (char* p = ntPath)
        {
            var name = new Native.UNICODE_STRING { Length = (ushort)(ntPath.Length * 2), MaximumLength = (ushort)(ntPath.Length * 2), Buffer = p };
            var attributes = new Native.OBJECT_ATTRIBUTES
            {
                Length = (uint)sizeof(Native.OBJECT_ATTRIBUTES),
                ObjectName = &name,
                Attributes = Native.OBJ_CASE_INSENSITIVE,
            };
            Native.IO_STATUS_BLOCK io;
            uint options = Native.FILE_OPEN_FOR_BACKUP_INTENT | (followLinks ? 0 : Native.FILE_OPEN_REPARSE_POINT);
            int status = Native.NtOpenFile(out handle, Native.READ_CONTROL, &attributes, &io, Native.FILE_SHARE_READ_WRITE_DELETE, options);
            if (status >= 0) return Native.ERROR_SUCCESS;
            handle.Dispose();
            return (int)Native.RtlNtStatusToDosError(status);
        }
    }

    private static unsafe string? FinalPath(SafeFileHandle handle)
    {
        Span<char> small = stackalloc char[512];
        fixed (char* p = small)
        {
            uint n = Native.GetFinalPathNameByHandle(handle, p, (uint)small.Length, Native.FILE_NAME_NORMALIZED | Native.VOLUME_NAME_DOS);
            if (n == 0) return null;
            if (n < small.Length) return LongPath.FromExtended(new string(p, 0, (int)n));
            var large = new char[n];
            fixed (char* q = large)
            {
                uint m = Native.GetFinalPathNameByHandle(handle, q, n, Native.FILE_NAME_NORMALIZED | Native.VOLUME_NAME_DOS);
                return m == 0 || m >= n ? null : LongPath.FromExtended(new string(q, 0, (int)m));
            }
        }
    }

    private static unsafe int ReadDescriptor(SafeFileHandle handle, ref byte[] buffer, out int length)
    {
        const uint what = Native.OWNER_SECURITY_INFORMATION | Native.GROUP_SECURITY_INFORMATION | Native.DACL_SECURITY_INFORMATION;
        while (true)
        {
            bool ok;
            uint needed;
            fixed (byte* p = buffer)
            {
                ok = Native.GetKernelObjectSecurity(handle, what, p, (uint)buffer.Length, out needed);
                length = ok ? (int)Native.GetSecurityDescriptorLength((nint)p) : 0;
            }
            if (ok) return Native.ERROR_SUCCESS;

            int error = Marshal.GetLastPInvokeError();
            if (error != Native.ERROR_INSUFFICIENT_BUFFER || needed <= buffer.Length) return error;
            buffer = t_buffer = new byte[needed];
        }
    }

    private static FolderNode Failed(string path, int depth, bool isFile, string error)
        => new() { Path = path, Depth = depth, IsFile = isFile, Error = error };
}
