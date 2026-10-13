using System.Runtime.InteropServices;
using System.Security.Principal;
using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

internal enum LookupStatus
{
    Gefunden,
    /// <summary>ERROR_NONE_MAPPED: Windows kennt zu dieser SID kein Konto.</summary>
    NichtZugeordnet,
    /// <summary>Jeder andere Fehler (Domäne/Server nicht erreichbar, Vertrauensstellung, Zugriff verweigert).</summary>
    Fehler,
}

internal readonly record struct AccountLookupResult(LookupStatus Status, string Domain, string Name, Native.SidNameUse Use, int Error)
{
    /// <summary>
    /// „DOMÄNE\name“ bzw. nur „name“, wenn Windows keine Domäne liefert (z. B. „Jeder“), oder nur die Domäne,
    /// wenn die SID die einer Domäne ist.
    /// </summary>
    public string FullName => string.IsNullOrEmpty(Domain) ? Name : string.IsNullOrEmpty(Name) ? Domain : $@"{Domain}\{Name}";

    public PrincipalKind Kind => KindOf(Use);

    public static PrincipalKind KindOf(Native.SidNameUse use) => use switch
    {
        Native.SidNameUse.User => PrincipalKind.Benutzer,
        Native.SidNameUse.Group or Native.SidNameUse.Alias => PrincipalKind.Gruppe,
        Native.SidNameUse.WellKnownGroup or Native.SidNameUse.Label or Native.SidNameUse.LogonSession => PrincipalKind.Bekannt,
        Native.SidNameUse.DeletedAccount => PrincipalKind.Verwaist,
        Native.SidNameUse.Computer => PrincipalKind.Computer,
        _ => PrincipalKind.Unbekannt,
    };
}

/// <summary>SID → Kontoname über die LSA eines Rechners (austauschbar für Tests).</summary>
internal interface IAccountLookup
{
    /// <summary>Server, dessen LSA gefragt wird, null = dieser Rechner.</summary>
    string? Server { get; }

    AccountLookupResult Lookup(string sid);
}

/// <summary>
/// <c>LookupAccountSid</c> direkt statt <c>SecurityIdentifier.Translate</c>: Nur so kommt der genaue Fehlercode
/// an, und nur damit lässt sich „Konto existiert nicht“ (1332) von „Domäne nicht erreichbar“ unterscheiden.
/// </summary>
internal sealed class WindowsAccountLookup(string? server) : IAccountLookup
{
    public string? Server { get; } = string.IsNullOrWhiteSpace(server) ? null : server.Trim().TrimStart('\\');

    public unsafe AccountLookupResult Lookup(string sid)
    {
        byte[] binary;
        try
        {
            var si = new SecurityIdentifier(sid);
            binary = new byte[si.BinaryLength];
            si.GetBinaryForm(binary, 0);
        }
        catch (ArgumentException)
        {
            return new(LookupStatus.Fehler, "", "", Native.SidNameUse.Invalid, Native.ERROR_INVALID_SID);
        }

        uint nameLength = 128, domainLength = 64;
        // Zweiter Versuch mit der von Windows gemeldeten Puffergröße; ein dritter fängt den seltenen Fall ab,
        // dass sich der Name zwischen beiden Aufrufen ändert.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var name = new char[Math.Max(nameLength, 1)];
            var domain = new char[Math.Max(domainLength, 1)];
            uint n = (uint)name.Length, d = (uint)domain.Length;
            bool ok;
            Native.SidNameUse use;
            fixed (byte* pSid = binary)
            fixed (char* pName = name)
            fixed (char* pDomain = domain)
            {
                ok = Native.LookupAccountSid(Server, pSid, pName, ref n, pDomain, ref d, out use);
            }
            if (ok)
                return new(LookupStatus.Gefunden, new string(domain, 0, (int)d), new string(name, 0, (int)n), use, 0);

            var error = Marshal.GetLastPInvokeError();
            if (error == Native.ERROR_INSUFFICIENT_BUFFER)
            {
                nameLength = Math.Max(n, nameLength * 2);
                domainLength = Math.Max(d, domainLength * 2);
                continue;
            }
            var status = error == Native.ERROR_NONE_MAPPED ? LookupStatus.NichtZugeordnet : LookupStatus.Fehler;
            return new(status, "", "", Native.SidNameUse.Unknown, error);
        }
        return new(LookupStatus.Fehler, "", "", Native.SidNameUse.Unknown, Native.ERROR_INSUFFICIENT_BUFFER);
    }
}
