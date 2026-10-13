using System.ComponentModel;

namespace Zutritt.Core.Scanning;

/// <summary>Deutsche Kurztexte für Win32- und Netzwerkfehler, wie sie in Fehlerliste und Bericht erscheinen.</summary>
internal static class Win32Error
{
    public static string Describe(int code) => code switch
    {
        2 or 3 => "Pfad nicht gefunden",
        5 => "Zugriff verweigert",
        15 => "Laufwerk nicht gefunden",
        21 => "Gerät nicht bereit",
        32 => "Wird von einem anderen Prozess verwendet",
        50 => "Vorgang wird nicht unterstützt",
        51 => "Netzwerk-Server nicht erreichbar",
        53 => "Netzwerkpfad nicht gefunden",
        59 => "Unerwarteter Netzwerkfehler",
        64 => "Netzwerkname nicht mehr verfügbar",
        65 => "Netzwerkzugriff verweigert",
        67 => "Netzwerkname nicht gefunden",
        123 => "Ungültiger Name",
        161 => "Ungültiger Pfad",
        206 => "Pfad oder Name zu lang",
        267 => "Kein Ordner",
        1219 => "Für diesen Server sind bereits andere Anmeldedaten in Verwendung",
        1222 => "Netzwerk nicht vorhanden oder nicht gestartet",
        1231 => "Netzwerk nicht erreichbar",
        1311 => "Kein Anmeldeserver verfügbar",
        1326 => "Anmeldung fehlgeschlagen (Benutzername oder Kennwort falsch)",
        1722 => "RPC-Server nicht verfügbar",
        1920 => "Datei kann vom System nicht geöffnet werden",
        1921 => "Verknüpfung kann nicht aufgelöst werden",
        2114 => "Serverdienst ist nicht gestartet",
        2310 => "Freigabe nicht gefunden",
        _ => Fallback(code),
    };

    // Unbekannte Codes: Windows-Text (Sprache des Systems) plus Nummer, damit man nachschlagen kann.
    private static string Fallback(int code)
    {
        var text = new Win32Exception(code).Message.Trim().TrimEnd('.');
        return $"Windows-Fehler {code}: {text}";
    }
}
