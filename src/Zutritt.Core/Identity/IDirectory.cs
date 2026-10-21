// SPDX-License-Identifier: MIT
using Zutritt.Core.Model;

namespace Zutritt.Core.Identity;

/// <summary>Quelle für Konten und Gruppenmitgliedschaften (lokale SAM, Active Directory, Test-Attrappe).</summary>
public interface IDirectory
{
    /// <summary>Konto zu einer SID, null wenn diese Quelle es nicht kennt.</summary>
    Principal? Lookup(string sid);

    /// <summary>Direkte Mitglieder einer Gruppe als SIDs, null wenn diese Quelle die Gruppe nicht kennt oder nicht lesen kann.</summary>
    IReadOnlyList<string>? GetMembers(string groupSid);
}
