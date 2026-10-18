using System.Diagnostics;

namespace Zutritt.UI;

/// <summary>
/// Öffnet Links im Browser – nur <c>http</c> und <c>https</c>, nie Dateien oder Programme (Richtlinie 09). Auch eigene
/// Konstanten laufen hier durch, damit niemand später eine Adresse aus fremden Daten direkt an die Shell gibt.
/// </summary>
public static class Links
{
    /// <summary>Darf diese Adresse geöffnet werden? Nur absolute http(s)-Adressen mit Rechnernamen.</summary>
    public static bool IsAllowed(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return false;
        if (string.IsNullOrEmpty(parsed.Host) || parsed.IsUnc || parsed.IsFile) return false;
        uri = parsed;
        return true;
    }

    /// <summary>Im Standardbrowser öffnen.</summary>
    /// <returns>null bei Erfolg, sonst ein deutscher Hinweis für die Meldungsleiste.</returns>
    public static string? Open(string url)
    {
        if (!IsAllowed(url, out var uri)) return $"Der Link „{url}“ wurde nicht geöffnet: Erlaubt sind nur Adressen mit http oder https.";
        try
        {
            Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"Der Browser ließ sich nicht öffnen: {ex.Message}";
        }
    }
}
