using System.Globalization;
using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>
/// Berechnet effektive Rechte aus ACL, Gruppenmitgliedschaft, Verweigerungen und Freigabe.
/// </summary>
/// <remarks>
/// <para>
/// Auswertung wie die Zugriffsprüfung von Windows: Einträge in kanonischer Reihenfolge (explizit verweigern,
/// explizit erlauben, dann geerbte Einträge in gespeicherter Reihenfolge); ein Recht gilt, wenn es erlaubt
/// wurde, bevor ein passender Verweigern-Eintrag es sperrt. Auf derselben Ebene gewinnt damit Verweigern, ein
/// expliziter Erlauben-Eintrag schlägt aber eine geerbte Verweigerung – genau wie bei Windows.
/// </para>
/// <para>
/// Einträge für „INHERIT_ONLY“ zählen nicht für den Ordner selbst, ERSTELLER-BESITZER wird ignoriert,
/// BESITZERRECHTE gelten für Personen, die (selbst oder über eine Gruppe) Besitzer sind. Die impliziten
/// Rechte des Besitzers (Berechtigungen lesen und ändern) zählen nicht – dafür gibt es die Regel ZT10.
/// Über die Freigabe steht NETZWERK im Token, am Rechner selbst INTERAKTIV.
/// </para>
/// <para>
/// Gespeichert sind nur Ordner mit eigenen Rechten (und Wurzeln, Fehler). Alle anderen erben unverändert vom
/// nächsthöheren gespeicherten Ordner; deshalb arbeiten die Methoden nur auf gespeicherten Ordnern.
/// </para>
/// </remarks>
public static class EffectiveAccess
{
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), CompareOptions.IgnoreCase);

    /// <summary>
    /// „Wer darf hier rein?“ – je Konto aus der ACL (Gruppen nicht aufgelöst) das Recht auf diesen Ordner.
    /// </summary>
    /// <remarks>
    /// Je Eintrag zählen nur die Erlauben-Einträge genau dieser SID, aber alle Verweigerungen, die seine Mitglieder
    /// treffen (auch z. B. „Jeder verweigert“). Ohne ERSTELLER-BESITZER, ohne Konten mit nur Verweigerungen und ohne
    /// Einträge, die nur an Unterordner vererbt werden. Sortiert nach Recht absteigend, dann Name.
    /// </remarks>
    public static IReadOnlyList<AccessGrant> ForFolder(ScanResult scan, FolderNode folder)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Error is not null) return [];

        var ctx = new AccessContext(scan);
        var logon = AccessContext.LogonFor(folder);
        var grants = new List<AccessGrant>();
        foreach (var trustee in AllowTrustees(folder))
        {
            var token = ctx.TokenOf(trustee, logon);
            var result = ctx.EvaluateEntry(folder, trustee, token);
            // Nur Synchronisieren o. Ä. ist kein Zugriff
            if (Rights.ToLevel(result.Allowed) == AccessLevel.Keine) continue;
            grants.Add(ToGrant(ctx, folder, trustee, token, result, via: []));
        }
        return Sort(ctx, grants);
    }

    /// <summary>„Wer darf hier rein?“ – aufgelöst bis zur Person (nur Benutzerkonten).</summary>
    /// <remarks>
    /// Alle Benutzer aus <see cref="ScanResult.Principals"/> mit effektivem Recht über „Kein Zugriff“, auch
    /// deaktivierte (sie werden über <see cref="Principal.IsDisabled"/> erkennbar). Hat eine breite Gruppe
    /// (Jeder, Authentifizierte Benutzer, Domänen-Benutzer …) – direkt oder als Mitglied einer berechtigten
    /// Gruppe – Zugriff, kommt zusätzlich ein Eintrag für ihre SID dazu, damit die Oberfläche „alle angemeldeten
    /// Benutzer“ zeigen kann (deren Personen kennt der Lauf nicht vollständig). Sortiert nach Recht absteigend, Name.
    /// </remarks>
    public static IReadOnlyList<AccessGrant> PeopleForFolder(ScanResult scan, FolderNode folder)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Error is not null) return [];

        var ctx = new AccessContext(scan);
        var logon = AccessContext.LogonFor(folder);
        var grants = new List<AccessGrant>();

        foreach (var person in scan.Principals.Values.Where(p => p.Kind == PrincipalKind.Benutzer))
        {
            var grant = ForToken(ctx, folder, person.Sid, logon);
            if (grant.Effective > AccessLevel.Keine) grants.Add(grant);
        }

        // Breite Gruppen: direkt eingetragen oder (transitiv) Mitglied einer eingetragenen Gruppe
        var trustees = new HashSet<string>(AllowTrustees(folder), StringComparer.OrdinalIgnoreCase);
        var otherLogon = logon == LogonKind.Network ? SidCatalog.Interactive : SidCatalog.Network;
        var candidates = scan.Principals.Keys.Concat(trustees)
            .Where(SidCatalog.IsBroad)
            .Where(sid => !sid.Equals(otherLogon, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var broad in candidates)
        {
            if (!ctx.GroupsOf(broad).Sids.Any(trustees.Contains)) continue;
            var grant = ForToken(ctx, folder, broad, logon);
            if (grant.Effective > AccessLevel.Keine) grants.Add(grant);
        }
        return Sort(ctx, grants);
    }

    /// <summary>„Wo darf Max überall hin?“ – alle gespeicherten Ordner, auf die die Person mindestens Auflisten hat.</summary>
    /// <remarks>Nicht lesbare Ordner fehlen (siehe Regel ZT09). Sortiert nach Pfad wie ein Baum.</remarks>
    public static IReadOnlyList<AccessGrant> ForPerson(ScanResult scan, string personSid)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentException.ThrowIfNullOrEmpty(personSid);
        var ctx = new AccessContext(scan);
        return ForPerson(ctx, personSid);
    }

    /// <summary>Alle Gruppen (transitiv, inkl. bekannter Prinzipale wie Jeder), in denen das Konto Mitglied ist.</summary>
    /// <remarks>
    /// Enthält die SID selbst. Benutzer und Computer (ebenso verwaiste und nicht auflösbare SIDs, hinter denen ein
    /// Konto stehen kann) sind implizit Mitglied von Jeder und Authentifizierte Benutzer – und damit indirekt
    /// z. B. von VORDEFINIERT\Benutzer, wenn Authentifizierte Benutzer dort Mitglied ist. Gruppen und bekannte
    /// Prinzipale bekommen nichts implizit dazu, nur ihre eingetragenen Mitgliedschaften. Zyklen sind unschädlich.
    /// </remarks>
    public static IReadOnlySet<string> GroupsOf(ScanResult scan, string sid)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentException.ThrowIfNullOrEmpty(sid);
        return new HashSet<string>(new AccessContext(scan).GroupsOf(sid).Sids, StringComparer.OrdinalIgnoreCase);
    }

    // ---------- intern, auch für den Vergleich ----------

    internal static IReadOnlyList<AccessGrant> ForPerson(AccessContext ctx, string personSid)
    {
        var grants = new List<AccessGrant>();
        foreach (var folder in ctx.Scan.Folders)
        {
            if (folder.Error is not null) continue;
            var grant = ForToken(ctx, folder, personSid, AccessContext.LogonFor(folder));
            if (grant.Effective >= AccessLevel.Auflisten) grants.Add(grant);
        }
        grants.Sort((a, b) => PathOrder.Instance.Compare(a.Path, b.Path));
        return grants;
    }

    /// <summary>Effektives Recht eines Kontos (bzw. der Mitglieder einer Gruppe) auf einen Knoten.</summary>
    internal static AccessGrant ForToken(AccessContext ctx, FolderNode folder, string sid, LogonKind logon)
    {
        var token = ctx.TokenOf(sid, logon);
        var result = ctx.Evaluate(folder, token);
        return ToGrant(ctx, folder, sid, token, result, ctx.Via(folder, token, result.BestAceSid));
    }

    /// <summary>Nur die effektive Stufe (ohne Erklärung) – für Massenvergleiche.</summary>
    internal static AccessLevel EffectiveLevel(AccessContext ctx, FolderNode folder, string sid)
    {
        var token = ctx.TokenOf(sid, AccessContext.LogonFor(folder));
        var ntfs = ctx.Evaluate(folder, token).Level;
        return ctx.ShareAccessFor(folder, token) is { } share ? Rights.CapByShare(ntfs, share) : ntfs;
    }

    private static AccessGrant ToGrant(AccessContext ctx, FolderNode folder, string sid, Membership token, AccessResult result,
        IReadOnlyList<string> via)
    {
        var ntfs = result.Level;
        var share = ctx.ShareAccessFor(folder, token);
        return new AccessGrant
        {
            Path = folder.Path,
            Sid = sid,
            Ntfs = ntfs,
            ViaShare = share is { } s ? Rights.CapByShare(ntfs, s) : null,
            Via = via,
            ReducedByDeny = result.ReducedByDeny,
        };
    }

    /// <summary>SIDs mit Erlauben-Einträgen, die auf den Ordner selbst wirken, in ACL-Reihenfolge.</summary>
    private static IEnumerable<string> AllowTrustees(FolderNode folder) =>
        folder.Aces
            .Where(a => !a.IsDeny && !a.InheritOnly && !SidCatalog.IsCreatorPlaceholder(a.Sid))
            .Select(a => a.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static List<AccessGrant> Sort(AccessContext ctx, List<AccessGrant> grants)
    {
        grants.Sort((a, b) =>
        {
            int c = b.Effective.CompareTo(a.Effective);
            if (c != 0) return c;
            c = NameOrder.Compare(ctx.Label(a.Sid), ctx.Label(b.Sid));
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Sid, b.Sid);
        });
        return grants;
    }
}
