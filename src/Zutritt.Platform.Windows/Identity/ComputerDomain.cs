// SPDX-License-Identifier: MIT
using System.Security.Principal;

namespace Zutritt.Platform.Windows.Identity;

/// <summary>Domäne, in der dieser Rechner Mitglied ist.</summary>
/// <param name="NetBiosName">Kurzname, wie er in „DOMÄNE\name“ steht.</param>
/// <param name="DnsName">DNS-Name (z. B. lab.local), leer bei sehr alten Domänen.</param>
/// <param name="Sid">SID der Domäne (S-1-5-21-…), Präfix aller Domänenkonten.</param>
public sealed record ComputerDomainInfo(string NetBiosName, string DnsName, string Sid);

/// <summary>
/// Domänenmitgliedschaft aus der lokalen LSA-Richtlinie. Anders als <c>Domain.GetComputerDomain()</c> braucht das
/// keinen Domänencontroller: auch ein Notebook ohne Netz weiß, zu welcher Domäne es gehört.
/// </summary>
public static class ComputerDomain
{
    /// <summary>Die Domäne dieses Rechners, null in einer Arbeitsgruppe (oder wenn die Richtlinie nicht lesbar ist).</summary>
    public static unsafe ComputerDomainInfo? Query()
    {
        var attributes = new Native.LSA_OBJECT_ATTRIBUTES { Length = (uint)sizeof(Native.LSA_OBJECT_ATTRIBUTES) };
        if (Native.LsaOpenPolicy(null, ref attributes, Native.POLICY_VIEW_LOCAL_INFORMATION, out var policy) != 0) return null;
        try
        {
            if (Native.LsaQueryInformationPolicy(policy, Native.PolicyDnsDomainInformation, out var buffer) != 0 || buffer == 0) return null;
            try
            {
                var info = (Native.POLICY_DNS_DOMAIN_INFO*)buffer;
                // Arbeitsgruppe: kein Domänen-SID, Name ist dann der Arbeitsgruppenname.
                if (info->Sid == 0) return null;
                return new ComputerDomainInfo(info->Name.Value, info->DnsDomainName.Value, new SecurityIdentifier(info->Sid).Value);
            }
            finally
            {
                Native.LsaFreeMemory(buffer);
            }
        }
        finally
        {
            Native.LsaClose(policy);
        }
    }
}
