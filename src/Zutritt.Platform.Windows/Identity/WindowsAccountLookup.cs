// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Security.Principal;
using Zutritt.Core.Identity;
using Zutritt.Core.Model;

namespace Zutritt.Platform.Windows.Identity;

/// <summary>Antwort von <c>LookupAccountSid</c> mit den Windows-Angaben (Kontotyp, Fehlercode).</summary>
internal readonly record struct WindowsLookupResult(LookupStatus Status, string Domain, string Name, Native.SidNameUse Use, int Error)
{
    public string FullName => AccountName.Join(Domain, Name);

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

    /// <summary>Plattformneutrale Antwort für den Resolver: Fehlernummer als Text und Einordnung „Quelle ausgefallen“.</summary>
    public AccountLookupResult ToNeutral() => Status switch
    {
        LookupStatus.Gefunden => new AccountLookupResult(LookupStatus.Gefunden, Domain, Name, Kind),
        LookupStatus.NichtZugeordnet => AccountLookupResult.NotMapped,
        _ => AccountLookupResult.Failed(Native.Describe(Error), WindowsAccountLookup.IsSourceDown(Error)),
    };
}

/// <summary>
/// <c>LookupAccountSid</c> direkt statt <c>SecurityIdentifier.Translate</c>: Nur so kommt der genaue Fehlercode
/// an, und nur damit lässt sich „Konto existiert nicht“ (1332) von „Domäne nicht erreichbar“ unterscheiden.
/// </summary>
internal sealed class WindowsAccountLookup(string? server) : IAccountLookup
{
    // Fehler, die den ganzen Server betreffen: weitere Anfragen dort würden nur jeweils in die Zeitüberschreitung laufen.
    private static readonly HashSet<int> ServerDownErrors =
    [
        Native.ERROR_ACCESS_DENIED, Native.ERROR_BAD_NETPATH, 67 /* ERROR_BAD_NET_NAME */, 1203 /* ERROR_NO_NET_OR_BAD_PATH */,
        1231 /* ERROR_NETWORK_UNREACHABLE */, 1232 /* ERROR_HOST_UNREACHABLE */, Native.RPC_S_SERVER_UNAVAILABLE,
        Native.RPC_S_CALL_FAILED_DNE, 1753 /* EPT_S_NOT_REGISTERED */,
    ];

    public string? Server { get; } = MachineNames.Strip(server);

    /// <summary>Betrifft der Fehler die ganze Quelle (Server nicht erreichbar, Zugriff verweigert)?</summary>
    public static bool IsSourceDown(int error) => ServerDownErrors.Contains(error);

    /// <summary>Windows-Fehlercode als plattformneutrale Antwort (für Tests der Einordnung).</summary>
    public static AccountLookupResult FromError(int error)
        => new WindowsLookupResult(error == Native.ERROR_NONE_MAPPED ? LookupStatus.NichtZugeordnet : LookupStatus.Fehler,
            "", "", Native.SidNameUse.Unknown, error).ToNeutral();

    public AccountLookupResult Lookup(string sid) => LookupRaw(sid).ToNeutral();

    /// <summary>Wie <see cref="Lookup"/>, aber mit Kontotyp und Fehlercode von Windows (für die lokale Kontendatenbank).</summary>
    public unsafe WindowsLookupResult LookupRaw(string sid)
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
