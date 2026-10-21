// SPDX-License-Identifier: MIT
using Microsoft.Win32;

namespace Zutritt.Interop;

/// <summary>Hell oder dunkel, wie das System es für Apps vorgibt.</summary>
/// <remarks>
/// Windows-Eigenes steckt hinter dieser Schnittstelle (Richtlinie 02). Die WPF-App ist ohnehin an Windows gebunden
/// (NTFS, Freigaben, AD); eine andere Oberfläche bekäme hier ihr Gegenstück.
/// </remarks>
public interface ISystemTheme
{
    /// <summary>Apps sollen hell dargestellt werden.</summary>
    bool AppsUseLightTheme { get; }
}

/// <summary>Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize, Wert AppsUseLightTheme.</summary>
public sealed class WindowsSystemTheme : ISystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public bool AppsUseLightTheme
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is int i && i == 1;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }
}
