// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Security.Principal;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;
using Zutritt.Core.Platform;
using Zutritt.Core.Scanning;
using Zutritt.Platform.Windows.Identity;
using Zutritt.Platform.Windows.Scanning;

namespace Zutritt.Platform.Windows;

/// <summary>
/// Windows-Anbindung des Scanners: NTFS-Rechte (NtOpenFile, GetKernelObjectSecurity), Ordner (FindFirstFileEx),
/// SMB-Freigaben (NetShareEnum, NetShareGetInfo), Konten (LSA, lokale Kontendatenbank, Active Directory).
/// Die Logik dahinter steckt plattformneutral in Zutritt.Core.
/// </summary>
public sealed class WindowsPlatform : IScanPlatform
{
    /// <summary>Die Anbindung ist zustandslos – eine für alle Läufe.</summary>
    public static WindowsPlatform Instance { get; } = new();

    private WindowsPlatform() { }

    /// <summary>Scanner mit dieser Anbindung (Konten je Lauf frisch über <see cref="CreateIdentityResolver"/>).</summary>
    public static Scanner CreateScanner() => new(Instance);

    public string CurrentUserName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    /// <summary>Standard: lokales Verzeichnis dieses Rechners plus AD, falls der Rechner in einer Domäne ist.</summary>
    /// <remarks>
    /// Bei Läufen über entfernte Server werden zusätzlich deren Kontendatenbank (VORDEFINIERT-Gruppen, lokale
    /// Konten) und LSA gefragt – die SIDs einer ACL gelten so, wie der Server sie sieht.
    /// </remarks>
    public IdentityResolver CreateIdentityResolver()
    {
        var directories = new List<IDirectory> { new LocalDirectory() };
        var owned = new List<IDisposable>();
        var domain = ComputerDomain.Query();
        if (domain is not null)
        {
            var ad = new ActiveDirectoryDirectory(domain);
            directories.Add(ad);
            owned.Add(ad);
        }
        return new IdentityResolver(directories, LsaOf, machineDirectoryFactory: server => new LocalDirectory(server),
            askScannedServers: true, owned);
    }

    /// <summary>
    /// Resolver mit genau diesen Verzeichnissen und der LSA dieses Rechners als Rückfall. Für Läufe über entfernte
    /// Server <see cref="CreateIdentityResolver"/> verwenden – das fragt zusätzlich die Kontendatenbank und LSA jedes
    /// gescannten Servers. Ohne das bleiben SIDs, die kein Verzeichnis und nicht die LSA dieses Rechners kennt, bei
    /// entfernten Servern „Unbekannt“ statt „Verwaist“: Lokale Konten eines Servers kennt nur der Server selbst.
    /// </summary>
    public static IdentityResolver CreateIdentityResolver(IEnumerable<IDirectory> directories)
        => new(directories, LsaOf, machineDirectoryFactory: null, askScannedServers: false, owned: []);

    private static IAccountLookup LsaOf(string? server) => new WindowsAccountLookup(server);

    public ScanTarget? ParseTarget(string? raw, out string? error) => TargetParser.Parse(raw, out error);

    public string? FullLocalPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public ShareListing EnumerateShares(string server) => ShareEnumerator.Enumerate(server);

    public ShareLookup LookupShare(string server, string share) => ShareEnumerator.Lookup(server, share);

    public DfsTarget? ResolveDfs(string uncPath) => DfsResolver.Resolve(uncPath);

    public TargetProbe Probe(string path)
    {
        uint attributes = Scanning.Native.GetFileAttributes(LongPath.ToExtended(path));
        if (attributes != Scanning.Native.INVALID_FILE_ATTRIBUTES)
            return DirectoryLister.IsDirectory((FileAttributes)attributes) ? TargetProbe.Directory : TargetProbe.File;
        int error = Marshal.GetLastPInvokeError();
        // Ohne Recht auf Attribute (Elternordner nicht auflistbar) als Ordner weiterversuchen: Rechte lesen und
        // Auflisten melden ihre Fehler dann genauer.
        return error == Scanning.Native.ERROR_ACCESS_DENIED ? TargetProbe.Directory : TargetProbe.Unreachable(Win32Error.Describe(error));
    }

    public FolderNode ReadSecurity(string path, int depth, bool isFile, bool followLinks, byte[]? parentDescriptor, bool readParent,
        bool wantDescriptor, out byte[]? descriptor)
        => AclReader.Read(path, depth, isFile, followLinks, parentDescriptor, readParent, wantDescriptor, out descriptor);

    public string? ListDirectory(string directory, List<DirEntry> entries)
    {
        int error = DirectoryLister.List(directory, entries);
        return error == Scanning.Native.ERROR_SUCCESS ? null : Win32Error.Describe(error);
    }
}
