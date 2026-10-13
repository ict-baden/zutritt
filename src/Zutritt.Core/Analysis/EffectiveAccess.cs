using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Berechnet effektive Rechte aus ACL, Gruppenmitgliedschaft, Verweigerungen und Freigabe.</summary>
public static class EffectiveAccess
{
    /// <summary>„Wer darf hier rein?“ – je Konto aus der ACL (Gruppen nicht aufgelöst) das Recht auf diesen Ordner.</summary>
    public static IReadOnlyList<AccessGrant> ForFolder(ScanResult scan, FolderNode folder) => throw new NotImplementedException();

    /// <summary>„Wer darf hier rein?“ – aufgelöst bis zur Person (nur Benutzerkonten).</summary>
    public static IReadOnlyList<AccessGrant> PeopleForFolder(ScanResult scan, FolderNode folder) => throw new NotImplementedException();

    /// <summary>„Wo darf Max überall hin?“ – alle gespeicherten Ordner, auf die die Person mindestens Auflisten hat.</summary>
    public static IReadOnlyList<AccessGrant> ForPerson(ScanResult scan, string personSid) => throw new NotImplementedException();

    /// <summary>Alle Gruppen (transitiv, inkl. bekannter Prinzipale wie Jeder), in denen das Konto Mitglied ist.</summary>
    public static IReadOnlySet<string> GroupsOf(ScanResult scan, string sid) => throw new NotImplementedException();
}
