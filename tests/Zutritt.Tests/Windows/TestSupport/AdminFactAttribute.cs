// SPDX-License-Identifier: MIT
using System.Security.Principal;

namespace Zutritt.Tests.TestSupport;

/// <summary>Test, der Adminrechte braucht (Freigaben anlegen, lokale Konten). Ohne Rechte wird er übersprungen.</summary>
/// <remarks>Nur unter Windows übersetzt (Ordner Windows/), wie alle Tests, die echte Freigaben und Konten anlegen.</remarks>
public sealed class AdminFactAttribute : FactAttribute
{
    public AdminFactAttribute()
    {
        if (!IsAdmin) Skip = "Braucht Administratorrechte (Freigaben oder lokale Konten anlegen).";
    }

    public static bool IsAdmin { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
