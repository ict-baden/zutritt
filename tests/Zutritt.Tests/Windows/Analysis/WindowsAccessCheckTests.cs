using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Zutritt.Core.Analysis;
using Zutritt.Core.Model;
using Zutritt.Tests.TestSupport;
using Zutritt.Tests.Analysis;
using Zutritt.Tests.Identity;
using Zutritt.Tests.Scanning;
using Zutritt.Tests.Storage;
using Zutritt.Tests.Windows.Identity;
using Zutritt.Tests.Windows.Scanning;
using Principal = Zutritt.Core.Model.Principal;

namespace Zutritt.Tests.Windows.Analysis;

/// <summary>
/// Abgleich mit Windows: echte Ordner mit echten ACLs, ein echtes lokales Konto in einer echten lokalen Gruppe –
/// die von Zutritt berechneten Rechte müssen Bit für Bit dem entsprechen, was die Zugriffsprüfung von Windows
/// (AuthZ, MAXIMUM_ALLOWED) für dieses Konto ermittelt.
/// </summary>
public sealed class WindowsAccessCheckTests
{
    private const InheritanceFlags CIOI = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    [AdminFact]
    public void Berechnete_Rechte_entsprechen_der_Zugriffspruefung_von_Windows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        using var machine = new PrincipalContext(ContextType.Machine);
        var temp = new TempFolder();
        UserPrincipal? user = null;
        GroupPrincipal? group = null;
        try
        {
            user = new UserPrincipal(machine, "zt-test-u" + suffix, "Zt-" + Guid.NewGuid().ToString("N") + "!aA9", true);
            user.Save();
            group = new GroupPrincipal(machine, "zt-test-g" + suffix);
            group.Save();
            group.Members.Add(user);
            group.Save();

            var u = user.Sid;
            var g = group.Sid;
            using var authz = new AuthzContext(u);
            var scan = ScanFor(u, g, authz.GroupSids());

            var root = PrepareRoot(temp.Path);
            var cases = BuildTree(root, u, g);

            foreach (var (path, isFile) in cases)
            {
                var sd = ReadSecurityDescriptor(path);
                var node = ToNode(path, sd, isFile);
                var windows = authz.MaximumAllowed(sd);
                var ours = Evaluate(scan, node, u.Value);
                Assert.True(windows == ours,
                    $"{Rel(root, path)}: Windows {(uint)windows:X8} ({Rights.ToLevel(windows)}), Zutritt {(uint)ours:X8} ({Rights.ToLevel(ours)})");
            }

            // Nachgebildete Vererbung (für nicht gespeicherte Ordner) gegen das, was Windows tatsächlich vererbt hat
            foreach (var (ancestor, path, isFile) in InheritanceCases(root))
            {
                var anc = ToNode(ancestor, ReadSecurityDescriptor(ancestor), false);
                var distance = path[(ancestor.Length + 1)..].Count(c => c == '\\') + 1;
                var virtualNode = AccessContext.Inherit(anc, path, distance, isFile);
                var windows = authz.MaximumAllowed(ReadSecurityDescriptor(path));
                var ours = Evaluate(scan, virtualNode, u.Value);
                Assert.True(windows == ours,
                    $"geerbt {Rel(root, path)}: Windows {(uint)windows:X8} ({Rights.ToLevel(windows)}), Zutritt {(uint)ours:X8} ({Rights.ToLevel(ours)})");
            }

            // Stichprobe: Die Fälle decken wirklich verschiedene Stufen ab (sonst wäre der Abgleich wertlos)
            var levels = cases.Select(c => Rights.ToLevel(authz.MaximumAllowed(ReadSecurityDescriptor(c.Path)))).Distinct().ToList();
            Assert.Contains(AccessLevel.Keine, levels);
            Assert.Contains(AccessLevel.Lesen, levels);
            Assert.Contains(AccessLevel.Aendern, levels);
            Assert.Contains(AccessLevel.Vollzugriff, levels);
        }
        finally
        {
            Unlock(temp.Path);
            temp.Dispose();
            try { group?.Delete(); } catch { /* Aufräumen nach bestem Bemühen */ }
            try { user?.Delete(); } catch { }
            group?.Dispose();
            user?.Dispose();
        }

        // Nichts darf liegen bleiben – auch nicht der Ordner „v“, der nur dem (jetzt gelöschten) Testkonto Rechte gab
        Assert.False(Directory.Exists(temp.Path), $"Testordner nicht aufgeräumt: {temp.Path}");
        Assert.Null(FindLocal(machine, "zt-test-u" + suffix));
        Assert.Null(FindLocal(machine, "zt-test-g" + suffix));
    }

    private static string? FindLocal(PrincipalContext machine, string name)
    {
        using var found = System.DirectoryServices.AccountManagement.Principal.FindByIdentity(machine, IdentityType.SamAccountName, name);
        return found?.Name;
    }

    /// <summary>
    /// Gibt dem Aufrufer im ganzen Testbaum wieder Vollzugriff (Verweigerungen fallen weg). Nötig, weil z. B. „v“ nur
    /// dem Testkonto Rechte gibt: Die Aufzählung in <see cref="TempFolder.Dispose"/> öffnet jeden Unterordner sofort,
    /// bricht dort ab, und der ganze Baum bliebe im Temp-Ordner liegen. Besitzer ist der Test selbst (bzw. die
    /// Administratoren), er darf die DACL also immer neu schreiben.
    /// </summary>
    private static void Unlock(string root)
    {
        var me = WindowsIdentity.GetCurrent().User!;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                var ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(true, false);
                ds.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, CIOI, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(dir).SetAccessControl(ds);
                foreach (var file in Directory.GetFiles(dir))
                {
                    var fs = new FileSecurity();
                    fs.SetAccessRuleProtection(true, false);
                    fs.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
                    new FileInfo(file).SetAccessControl(fs);
                }
                foreach (var sub in Directory.GetDirectories(dir)) pending.Push(sub);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // Weiter mit dem Rest – was übrig bleibt, meldet die Prüfung am Ende des Tests
            }
        }
    }

    private static string Rel(string root, string path) => path.Length > root.Length ? path[(root.Length + 1)..] : ".";

    private static FileRights Evaluate(ScanResult scan, FolderNode node, string sid)
    {
        var ctx = new AccessContext(scan);
        return ctx.Evaluate(node, ctx.TokenOf(sid, AccessContext.LogonFor(node))).Granted;
    }

    /// <summary>Lauf mit Konto, Gruppe und allen Gruppen, die Windows dem Konto zuordnet.</summary>
    private static ScanResult ScanFor(SecurityIdentifier user, SecurityIdentifier group, IEnumerable<string> windowsGroups)
    {
        var scan = new ScanResult { Request = new ScanRequest { Targets = ["test"] } };
        scan.Principals[user.Value] = new Principal { Sid = user.Value, Name = "zt-test-u", Kind = PrincipalKind.Benutzer, IsLocal = true };
        foreach (var sid in windowsGroups.Append(group.Value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (sid is SidCatalog.Everyone or SidCatalog.AuthenticatedUsers) continue; // implizit
            var p = new Principal { Sid = sid, Name = sid, Kind = PrincipalKind.Gruppe };
            p.Members.Add(user.Value);
            scan.Principals[sid] = p;
        }
        return scan;
    }

    /// <summary>Testwurzel nur mit eigenen Rechten (ich, Administratoren, SYSTEM) – nichts von außen erben.</summary>
    private static string PrepareRoot(string tempPath)
    {
        var root = Path.Combine(tempPath, "w");
        Directory.CreateDirectory(root);
        var info = new DirectoryInfo(root);
        var sec = info.GetAccessControl();
        sec.SetAccessRuleProtection(true, false);
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, false, typeof(SecurityIdentifier))) sec.RemoveAccessRuleSpecific(r);
        sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, CIOI, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, CIOI, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, CIOI, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(sec);
        return root;
    }

    private static List<(string Path, bool IsFile)> BuildTree(string root, SecurityIdentifier u, SecurityIdentifier g)
    {
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var authenticated = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var cases = new List<(string, bool)>();

        string Dir(params string[] parts)
        {
            var p = Path.Combine([root, .. parts]);
            Directory.CreateDirectory(p);
            return p;
        }
        string File(string dir, string name)
        {
            var p = Path.Combine(dir, name);
            System.IO.File.WriteAllText(p, "x");
            return p;
        }
        void Rule(string path, IdentityReference who, FileSystemRights rights, AccessControlType type = AccessControlType.Allow,
            InheritanceFlags inh = CIOI, PropagationFlags prop = PropagationFlags.None)
        {
            if (System.IO.File.Exists(path))
            {
                var fi = new FileInfo(path);
                var fs = fi.GetAccessControl();
                fs.AddAccessRule(new FileSystemAccessRule(who, rights, type));
                fi.SetAccessControl(fs);
                return;
            }
            var di = new DirectoryInfo(path);
            var ds = di.GetAccessControl();
            ds.AddAccessRule(new FileSystemAccessRule(who, rights, inh, prop, type));
            di.SetAccessControl(ds);
        }

        // Gruppe darf ändern; Datei darin mit eigenem Leserecht für das Konto
        var a = Dir("a");
        Rule(a, g, FileSystemRights.Modify);
        var ax = File(a, "x.txt");
        Rule(ax, u, FileSystemRights.ReadAndExecute);
        File(a, "y.txt");
        cases.Add((a, false));
        cases.Add((ax, true));

        // Explizites Verweigern für das Konto, Erlauben für die Gruppe
        var b = Dir("b");
        Rule(b, g, FileSystemRights.Modify);
        Rule(b, u, FileSystemRights.Write, AccessControlType.Deny);
        cases.Add((b, false));

        // Geerbte Verweigerung gegen explizites Erlauben
        var c = Dir("c");
        Rule(c, g, FileSystemRights.Write, AccessControlType.Deny);
        var d = Dir("c", "d");
        Rule(d, u, FileSystemRights.Modify);
        Dir("c", "leer");
        cases.Add((c, false));
        cases.Add((d, false));

        // Zwei Vorfahren: Erlauben vom Elternordner vor Verweigern vom Großelternordner
        var e = Dir("e");
        Rule(e, g, FileSystemRights.Write, AccessControlType.Deny);
        var f = Dir("e", "f");
        Rule(f, u, FileSystemRights.Modify);
        var gg = Dir("e", "f", "g");
        Rule(gg, system, FileSystemRights.ReadData, inh: InheritanceFlags.None);
        Dir("e", "f", "rein");
        cases.Add((gg, false));

        // Nur vererbender Vollzugriff
        var h = Dir("h");
        Rule(h, u, FileSystemRights.FullControl, prop: PropagationFlags.InheritOnly);
        Dir("h", "i");
        cases.Add((h, false));

        // „Jeder“ darf nicht löschen
        var j = Dir("j");
        Rule(j, everyone, FileSystemRights.Delete, AccessControlType.Deny, InheritanceFlags.None);
        Rule(j, g, FileSystemRights.Modify, inh: InheritanceFlags.None);
        cases.Add((j, false));

        // Verweigern und Erlauben vom selben Vorfahren
        var k = Dir("k");
        Rule(k, g, FileSystemRights.Modify);
        Rule(k, u, FileSystemRights.WriteData, AccessControlType.Deny);
        var l = Dir("k", "l");
        Rule(l, system, FileSystemRights.ReadData, inh: InheritanceFlags.None);
        cases.Add((l, false));

        // Authentifizierte Benutzer lesen
        var m = Dir("m");
        Rule(m, authenticated, FileSystemRights.ReadAndExecute);
        cases.Add((m, false));

        // Nur eine Ebene weitergeben
        var n = Dir("n");
        Rule(n, g, FileSystemRights.Modify, prop: PropagationFlags.NoPropagateInherit);
        var o = Dir("n", "o");
        Dir("n", "o", "p");
        File(o, "q.txt");
        File(n, "r.txt");
        cases.Add((n, false));

        // Nur durchqueren und auflisten
        var s = Dir("s");
        Rule(s, u, FileSystemRights.ListDirectory | FileSystemRights.Traverse | FileSystemRights.Synchronize);
        cases.Add((s, false));

        // Berechtigungen ändern dürfen = Vollzugriff
        var t = Dir("t");
        Rule(t, g, FileSystemRights.ReadAndExecute | FileSystemRights.ChangePermissions);
        cases.Add((t, false));

        // Vererbung unterbrochen, nur ein eigener Eintrag
        var v = Dir("v");
        Rule(v, u, FileSystemRights.Read);
        var vi = new DirectoryInfo(v);
        var vs = vi.GetAccessControl();
        vs.SetAccessRuleProtection(true, false);
        vi.SetAccessControl(vs);
        cases.Add((v, false));

        // Nur Dateien erben (ObjectInherit über einen Zwischenordner hinweg)
        var w = Dir("w");
        Rule(w, g, FileSystemRights.Modify, inh: InheritanceFlags.ObjectInherit);
        var wx = Dir("w", "x");
        File(wx, "datei.txt");
        cases.Add((w, false));

        return cases;
    }

    private static IEnumerable<(string Ancestor, string Path, bool IsFile)> InheritanceCases(string root)
    {
        string P(params string[] parts) => Path.Combine([root, .. parts]);
        yield return (P("h"), P("h", "i"), false);
        yield return (P("e", "f"), P("e", "f", "rein"), false);
        yield return (P("k"), P("k", "l"), false);
        yield return (P("n"), P("n", "o"), false);
        yield return (P("n"), P("n", "o", "p"), false);
        yield return (P("n"), P("n", "o", "q.txt"), true);
        yield return (P("n"), P("n", "r.txt"), true);
        yield return (P("w"), P("w", "x"), false);
        yield return (P("w"), P("w", "x", "datei.txt"), true);
        yield return (P("a"), P("a", "y.txt"), true);
        yield return (P("c"), P("c", "leer"), false);
    }

    private static byte[] ReadSecurityDescriptor(string path)
    {
        const uint info = NativeMethods.OwnerSecurityInformation | NativeMethods.GroupSecurityInformation | NativeMethods.DaclSecurityInformation;
        NativeMethods.GetFileSecurity(path, info, null, 0, out var needed);
        var buffer = new byte[needed];
        if (!NativeMethods.GetFileSecurity(path, info, buffer, needed, out _)) throw new Win32Exception(Marshal.GetLastWin32Error(), path);
        return buffer;
    }

    /// <summary>Knoten aus der rohen Sicherheitsbeschreibung – Reihenfolge der Einträge genau wie auf dem Datenträger.</summary>
    private static FolderNode ToNode(string path, byte[] sd, bool isFile)
    {
        var raw = new RawSecurityDescriptor(sd, 0);
        var node = new FolderNode
        {
            Path = path,
            IsFile = isFile,
            OwnerSid = raw.Owner?.Value ?? "",
            InheritanceBroken = (raw.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0,
        };
        foreach (var ace in raw.DiscretionaryAcl!)
        {
            var common = Assert.IsType<CommonAce>(ace);
            node.Aces.Add(new AceEntry
            {
                Sid = common.SecurityIdentifier.Value,
                Rights = (FileRights)unchecked((uint)common.AccessMask),
                IsDeny = common.AceQualifier == AceQualifier.AccessDenied,
                IsInherited = common.IsInherited,
                Inheritance = (AceInheritance)common.InheritanceFlags,
                Propagation = (AcePropagation)common.PropagationFlags,
            });
        }
        return node;
    }

    /// <summary>AuthZ-Kontext für ein Konto (ohne Anmeldung, Gruppen ermittelt Windows selbst).</summary>
    private sealed class AuthzContext : IDisposable
    {
        private readonly IntPtr _manager;
        private readonly IntPtr _context;

        public AuthzContext(SecurityIdentifier user)
        {
            if (!NativeMethods.AuthzInitializeResourceManager(NativeMethods.AuthzRmFlagNoAudit, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, null, out _manager))
                throw new Win32Exception();
            var sid = new byte[user.BinaryLength];
            user.GetBinaryForm(sid, 0);
            if (!NativeMethods.AuthzInitializeContextFromSid(0, sid, _manager, IntPtr.Zero, default, IntPtr.Zero, out _context))
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.AuthzFreeResourceManager(_manager);
                throw new Win32Exception(error);
            }
        }

        public FileRights MaximumAllowed(byte[] securityDescriptor)
        {
            var request = new NativeMethods.AuthzAccessRequest { DesiredAccess = NativeMethods.MaximumAllowed };
            var granted = Marshal.AllocHGlobal(sizeof(uint));
            var error = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                var reply = new NativeMethods.AuthzAccessReply { ResultListLength = 1, GrantedAccessMask = granted, Error = error };
                if (!NativeMethods.AuthzAccessCheck(0, _context, ref request, IntPtr.Zero, securityDescriptor, IntPtr.Zero, 0, ref reply, IntPtr.Zero))
                    throw new Win32Exception();
                // Bei „kein Zugriff“ meldet AuthZ ERROR_ACCESS_DENIED und Maske 0
                return (FileRights)unchecked((uint)Marshal.ReadInt32(granted));
            }
            finally
            {
                Marshal.FreeHGlobal(granted);
                Marshal.FreeHGlobal(error);
            }
        }

        public List<string> GroupSids()
        {
            NativeMethods.AuthzGetInformationFromContext(_context, NativeMethods.AuthzContextInfoGroupsSids, 0, out var size, IntPtr.Zero);
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!NativeMethods.AuthzGetInformationFromContext(_context, NativeMethods.AuthzContextInfoGroupsSids, size, out _, buffer))
                    throw new Win32Exception();
                int count = Marshal.ReadInt32(buffer);
                int entrySize = Marshal.SizeOf<NativeMethods.SidAndAttributes>();
                var sids = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    var entry = Marshal.PtrToStructure<NativeMethods.SidAndAttributes>(buffer + IntPtr.Size + i * entrySize);
                    sids.Add(new SecurityIdentifier(entry.Sid).Value);
                }
                return sids;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            NativeMethods.AuthzFreeContext(_context);
            NativeMethods.AuthzFreeResourceManager(_manager);
        }
    }
}
