using Zutritt.Core.Model;

namespace Zutritt.Core.Analysis;

/// <summary>Prüfregeln (ZT01 …) und ihre Auswertung.</summary>
/// <remarks>
/// ACL-Befunde entstehen nur aus expliziten (nicht geerbten) Einträgen – sonst würde jeder gespeicherte
/// Unterordner denselben Befund wiederholen. Je (Regel, Pfad, SID) gibt es höchstens einen Befund.
/// <para>
/// Windows-eigene Konten und Ordner erzeugen keine Fehlalarme (geprüft auf C:\Program Files, C:\ProgramData, C:\Users,
/// C:\Windows\System32): virtuelle Konten und Sonder-Identitäten sind keine Personen, VORDEFINIERT-Gruppen sind nie
/// „vergessene leere Gruppen“, in von Windows verwalteten Ordnern (<see cref="IsWindowsManaged"/>) entfallen die
/// Ordnungsregeln, tiefe Ausnahmen nur für Administratoren und Windows verstecken keinen Zugriff, und die
/// Kompatibilitäts-Verknüpfungen („Anwendungsdaten“ …) sind kein Verweigern-Befund.
/// </para>
/// </remarks>
public static class FindingRules
{
    internal const string BroadWrite = "ZT01";
    internal const string BroadRead = "ZT02";
    internal const string DirectUser = "ZT03";
    internal const string Orphaned = "ZT04";
    internal const string FullControlNonAdmin = "ZT05";
    internal const string DeepBreak = "ZT06";
    internal const string DenyEntry = "ZT07";
    internal const string ShareEveryone = "ZT08";
    internal const string Unreadable = "ZT09";
    internal const string OwnerIsUser = "ZT10";
    internal const string EmptyGroup = "ZT11";
    internal const string DisabledAccount = "ZT12";
    internal const string ShareUnreadable = "ZT13";
    internal const string InheritanceDiffers = "ZT14";

    /// <summary>Alle Regeln in fester Reihenfolge (ZT01 … ZT14).</summary>
    public static IReadOnlyList<RuleInfo> All { get; } =
    [
        new(BroadWrite, Severity.Hoch, "Breite Gruppe darf schreiben",
            "Eine Gruppe, die praktisch alle Personen umfasst (z. B. „Jeder“, „Authentifizierte Benutzer“, „Domänen-Benutzer“), "
            + "darf hier Dateien anlegen oder ändern. Damit kann jede angemeldete Person Daten verfälschen, löschen oder – etwa "
            + "durch Schadsoftware – verschlüsseln.",
            "Schreibrecht der breiten Gruppe entfernen und nur einer fachlichen Gruppe geben, die genau die zuständigen Personen enthält."),
        new(BroadRead, Severity.Mittel, "Breite Gruppe darf lesen",
            "Eine Gruppe, die praktisch alle Personen umfasst, darf hier lesen oder den Inhalt auflisten. Vertrauliche Daten sind "
            + "damit für jede angemeldete Person sichtbar.",
            "Prüfen, ob die Daten wirklich für alle bestimmt sind; sonst die breite Gruppe durch eine fachliche Gruppe ersetzen."),
        new(DirectUser, Severity.Mittel, "Recht direkt an Person vergeben",
            "Das Recht ist einem einzelnen Benutzerkonto statt einer Gruppe gegeben. Solche Einträge werden bei Wechsel der "
            + "Abteilung oder beim Austritt leicht vergessen und machen die Rechtevergabe unübersichtlich.",
            "Recht über eine Gruppe (Rolle bzw. Abteilung) vergeben und den Eintrag für die Person entfernen."),
        new(Orphaned, Severity.Mittel, "Verwaiste SID",
            "Ein Eintrag oder der Besitzer verweist auf ein Konto, das es nicht mehr gibt. Die SID lässt sich keinem Namen "
            + "zuordnen; niemand kann ohne Weiteres sagen, wem das Recht einmal gegeben wurde.",
            "Verwaisten Eintrag entfernen; ist die SID Besitzer, einen gültigen Besitzer (z. B. „Administratoren“) eintragen."),
        new(FullControlNonAdmin, Severity.Hoch, "Vollzugriff für Nicht-Admins",
            "Ein Konto oder eine Gruppe ohne Administratorrolle hat Vollzugriff. Damit darf es Berechtigungen ändern und den "
            + "Besitz übernehmen – also sich und anderen beliebige Rechte geben, an jeder Prüfung vorbei.",
            "Auf „Ändern“ reduzieren; Vollzugriff nur Administratoren und SYSTEM geben."),
        new(DeepBreak, Severity.Niedrig, "Tief versteckte Ausnahme",
            "Tief in der Ordnerstruktur ist die Vererbung unterbrochen. Solche Ausnahmen sieht man von oben nicht; Änderungen "
            + "an übergeordneten Ordnern kommen hier nicht an.",
            "Prüfen, ob die Ausnahme noch nötig ist, und sonst die Vererbung wieder einschalten. Nötige Ausnahmen dokumentieren "
            + "oder weiter oben in der Struktur abbilden."),
        new(DenyEntry, Severity.Info, "Verweigern-Eintrag",
            "Ein expliziter Verweigern-Eintrag geht allen Erlauben-Einträgen vor. Verweigerungen sind schwer nachzuvollziehen "
            + "und treffen oft mehr Personen als beabsichtigt (z. B. alle Mitglieder einer Gruppe).",
            "Recht lieber gar nicht erst vergeben (Gruppen passend zuschneiden). Verweigerungen nur in begründeten Ausnahmen "
            + "nutzen und dokumentieren."),
        new(ShareEveryone, Severity.Info, "Freigabe: Jeder Vollzugriff",
            "Die Freigabe-Berechtigung erlaubt allen angemeldeten Personen mindestens „Ändern“. Der Schutz liegt damit allein "
            + "bei den NTFS-Rechten. Das ist von Microsoft empfohlene Praxis, sollte aber bewusst so gewählt sein.",
            "Nichts zu tun, wenn die NTFS-Rechte sauber sind. Sonst die Freigabe-Berechtigung zusätzlich einschränken."),
        new(Unreadable, Severity.Info, "Ordner nicht lesbar",
            "Der Ordner (oder die Datei) konnte nicht gelesen werden, z. B. weil der Zugriff verweigert wurde. Seine "
            + "Berechtigungen und alles darunter sind in diesem Lauf nicht geprüft.",
            "Ursache beheben oder mit einem Konto scannen, das die Berechtigungen lesen darf, und den Lauf wiederholen."),
        new(OwnerIsUser, Severity.Niedrig, "Besitzer ist ein Benutzerkonto",
            "Der Besitzer darf die Berechtigungen jederzeit ändern – unabhängig davon, was in der Liste steht. Ist eine "
            + "einzelne Person Besitzer, behält sie diese Möglichkeit auch nach einem Rollenwechsel.",
            "Besitz auf „Administratoren“ (bzw. eine Admin-Gruppe) übertragen."),
        new(EmptyGroup, Severity.Niedrig, "Leere Gruppe berechtigt",
            "Eine Gruppe ohne Mitglieder ist berechtigt. Der Eintrag wirkt heute nicht, wird aber unbemerkt wirksam, sobald "
            + "jemand in die Gruppe aufgenommen wird.",
            "Eintrag entfernen, wenn die Gruppe nicht mehr gebraucht wird; sonst den Zweck dokumentieren."),
        new(DisabledAccount, Severity.Mittel, "Deaktiviertes Konto berechtigt",
            "Ein deaktiviertes Benutzerkonto hat hier ein eigenes Recht. Wird das Konto wieder aktiviert, gilt das Recht sofort.",
            "Eintrag entfernen; ausgeschiedene Konten nach Ablauf der Frist löschen."),
        new(ShareUnreadable, Severity.Info, "Freigabe-Berechtigungen nicht lesbar",
            "Die Berechtigungen der Freigabe konnten nicht gelesen werden. Die Rechte über das Netz sind deshalb allein aus "
            + "den NTFS-Rechten berechnet und können in Wirklichkeit geringer sein.",
            "Mit Administratorrechten auf dem Server erneut scannen."),
        new(InheritanceDiffers, Severity.Niedrig, "Rechte weichen von der Vererbung ab",
            "Der Ordner (oder die Datei) hat geerbte Einträge, die nicht zum übergeordneten Ordner passen. Typisch sind "
            + "verschobene Ordner: Beim Verschieben innerhalb eines Laufwerks nimmt Windows die alten geerbten Rechte mit, "
            + "statt die des neuen Ortes zu übernehmen. Hier gelten dann womöglich noch Rechte vom früheren Ort – auch für "
            + "Personen, die am neuen Ort keinen Zugriff haben sollen –, und Änderungen am übergeordneten Ordner kommen "
            + "erst nach der nächsten Weitergabe der Rechte an.",
            "Prüfen, ob die abweichenden Rechte noch gebraucht werden. Sonst die Vererbung neu anwenden: in den erweiterten "
            + "Sicherheitseinstellungen die Vererbung deaktivieren, dabei die geerbten Einträge entfernen und sie gleich "
            + "wieder aktivieren – eigene Einträge bleiben erhalten."),
    ];

    private static readonly Dictionary<string, RuleInfo> ById = All.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Regel zur ID (z. B. „ZT01“), null wenn unbekannt.</summary>
    public static RuleInfo? Get(string ruleId) => ById.GetValueOrDefault(ruleId);

    /// <summary>Wertet alle Regeln aus. Sortiert nach Schwere (hoch zuerst), Regel, Pfad.</summary>
    public static IReadOnlyList<Finding> Evaluate(ScanResult scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var ctx = new AccessContext(scan);
        var sink = new FindingSink(ctx);

        foreach (var folder in scan.Folders)
        {
            if (folder.Error is not null)
            {
                sink.Add(Unreadable, folder.Path, null,
                    $"{(folder.IsFile ? "Datei" : "Ordner")} nicht lesbar: {folder.Error.TrimEnd('.')} – Berechtigungen hier und darunter nicht geprüft.");
                continue;
            }
            // Ordner, deren Rechte Windows selbst setzt und pflegt: dort nur Befunde, die eine echte Gefahr zeigen
            bool managed = IsWindowsManaged(folder);
            EvaluateAces(ctx, sink, folder, managed);
            EvaluateOwner(ctx, sink, folder, managed);

            int threshold = Math.Max(1, scan.Request.DeepInheritanceBreakDepth);
            if (folder.InheritanceBroken && folder.Depth >= threshold && !managed && GrantsBeyondAdmins(folder))
                sink.Add(DeepBreak, folder.Path, null,
                    $"Vererbung in Tiefe {folder.Depth} unterbrochen (ab Tiefe {threshold} gilt eine Ausnahme als versteckt).");

            // Auch geerbte Windows-Kennungen zählen: Unter App- und Programmordnern legen Installationsprogramme
            // Rechte oft ohne Weitergabe an – das ist kein verschobener Ordner.
            if (folder.InheritanceDiffers && !folder.InheritanceBroken && !managed
                && !folder.Aces.Any(a => SidCatalog.IsWindowsInternal(a.Sid)))
                sink.Add(InheritanceDiffers, folder.Path, null, DescribeInheritanceDiffers(ctx, folder));
        }

        EvaluateShares(ctx, sink);
        EvaluateScanErrors(scan, sink);
        return sink.Sorted();
    }

    /// <param name="managed">Von Windows verwalteter Ordner (siehe <see cref="IsWindowsManaged"/>): nur Befunde, die eine echte Gefahr zeigen.</param>
    private static void EvaluateAces(AccessContext ctx, FindingSink sink, FolderNode folder, bool managed)
    {
        var scan = ctx.Scan;
        var explicitSids = folder.Aces.Where(a => !a.IsInherited).Select(a => a.Sid)
            .Where(s => !SidCatalog.IsCreatorPlaceholder(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var sid in explicitSids)
        {
            var kind = ctx.KindOf(sid);
            scan.Principals.TryGetValue(sid, out var principal);
            var who = ctx.Label(sid);
            var scopes = ScopeLevels.For(ctx, folder, sid);
            var suffix = scopes.OnlyForChildren ? " (gilt für Unterordner bzw. Dateien)" : "";

            if (scopes.HasAllow && SidCatalog.IsBroad(sid))
            {
                if (scopes.Net >= AccessLevel.Schreiben)
                    sink.Add(BroadWrite, folder.Path, sid,
                        $"„{who}“ hat „{Rights.Label(scopes.Net)}“{suffix} – praktisch jede angemeldete Person darf hier schreiben.");
                // Erst ab „Lesen“ (Inhalt auflisten bzw. Datei lesen): Bloßes Durchqueren oder Attribute lesen – etwa
                // „Jeder: Ordner durchsuchen“ über Freigaben oder die gesperrten Kompatibilitäts-Verknüpfungen von
                // Windows – zeigt niemandem Daten.
                else if (scopes.Net >= AccessLevel.Lesen && !managed)
                    sink.Add(BroadRead, folder.Path, sid,
                        $"„{who}“ hat „{Rights.Label(scopes.Net)}“{suffix} – praktisch jede angemeldete Person darf hier lesen.");
            }

            bool person = kind == PrincipalKind.Benutzer && IsPerson(sid);
            if (person && scopes.Raw > AccessLevel.Keine)
            {
                if (!SidCatalog.IsBuiltinAdministrator(sid) && !managed)
                    sink.Add(DirectUser, folder.Path, sid,
                        $"„{who}“ hat „{Rights.Label(scopes.Raw)}“ direkt als Person statt über eine Gruppe.");
                if (principal?.IsDisabled == true)
                    sink.Add(DisabledAccount, folder.Path, sid,
                        $"Das deaktivierte Konto „{who}“ hat hier „{Rights.Label(scopes.Raw)}“.");
            }

            if (kind == PrincipalKind.Verwaist)
            {
                bool alsoOwner = string.Equals(folder.OwnerSid, sid, StringComparison.OrdinalIgnoreCase);
                var entry = !scopes.HasAllow ? "einen Verweigern-Eintrag"
                    : scopes.Raw > AccessLevel.Keine ? $"einen Eintrag „{Rights.Label(scopes.Raw)}“"
                    : $"einen Eintrag {Describe(scopes.AllowMask)}";
                sink.Add(Orphaned, folder.Path, sid, alsoOwner
                    ? $"Die verwaiste SID {sid} ist Besitzer und hat {entry} – das Konto gibt es nicht mehr."
                    : $"Die verwaiste SID {sid} hat {entry} – das Konto gibt es nicht mehr.");
            }

            // In Windows-Ordnern gibt Windows dem Besitzer selbst Vollzugriff (z. B. auf seine Fehlerberichte) –
            // ändern könnte er die Rechte als Besitzer ohnehin. Vollzugriff für andere bleibt ein Befund.
            if (scopes.Net == AccessLevel.Vollzugriff && IsNonAdminAccountOrGroup(sid, kind)
                && !(managed && sid.Equals(folder.OwnerSid, StringComparison.OrdinalIgnoreCase)))
                sink.Add(FullControlNonAdmin, folder.Path, sid,
                    $"„{who}“ hat Vollzugriff{suffix} und darf damit Berechtigungen ändern und den Besitz übernehmen.");

            if (scopes.HasDeny && !IsCompatibilityLinkDeny(folder, sid))
                sink.Add(DenyEntry, folder.Path, sid,
                    $"Verweigern-Eintrag für „{who}“ ({Describe(scopes.DeniedMask)} verweigert).");

            // VORDEFINIERT-Gruppen (Hauptbenutzer, Sicherungs-Operatoren …) trägt Windows selbst in Systemordner ein;
            // dass sie leer sind, ist der Normalfall und keine vergessene Gruppe.
            if (kind == PrincipalKind.Gruppe && principal is { Members.Count: 0, MembersUnknown: false }
                && !SidCatalog.IsBuiltinGroup(sid)
                && scan.Request.ResolveGroups && scopes.Raw > AccessLevel.Keine)
                sink.Add(EmptyGroup, folder.Path, sid,
                    $"Die Gruppe „{who}“ hat keine Mitglieder, ist aber berechtigt („{Rights.Label(scopes.Raw)}“).");
        }
    }

    /// <summary>
    /// „Jeder: Ordner auflisten verweigert“, nur für diesen Ordner, sonst nichts: So schützt Windows seine
    /// Kompatibilitäts-Verknüpfungen („Anwendungsdaten“, „Eigene Dateien“, „All Users“ …) gegen Endlosschleifen beim
    /// Durchsuchen. Die Sperre nimmt niemandem Daten weg – als Verweigern-Befund wäre sie nur Rauschen.
    /// </summary>
    internal static bool IsCompatibilityLinkDeny(FolderNode folder, string sid)
    {
        if (!sid.Equals(SidCatalog.Everyone, StringComparison.OrdinalIgnoreCase)) return false;
        bool any = false;
        foreach (var ace in folder.Aces)
        {
            if (ace.IsInherited || !ace.IsDeny || !ace.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase)) continue;
            var mask = Rights.MapGeneric(ace.Rights) & ~FileRights.Synchronize;
            if (mask != FileRights.ListDirectory || ace.Inheritance != AceInheritance.None || ace.InheritOnly) return false;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Für ZT05: Benutzer, Gruppen und nicht auflösbare Konten ohne Administratorrolle. Breite Gruppen deckt ZT01 ab,
    /// Sonder-Identitäten (Dienste, BESITZERRECHTE …) sind keine Konten im Sinne der Regel.
    /// </summary>
    private static bool IsNonAdminAccountOrGroup(string sid, PrincipalKind kind) =>
        kind != PrincipalKind.Bekannt
        && !SidCatalog.IsSpecialIdentity(sid)
        && !SidCatalog.IsAdminLike(sid)
        && !SidCatalog.IsBuiltinAdministrator(sid)
        && !SidCatalog.IsBroad(sid);

    /// <param name="managed">Von Windows verwalteter Ordner: Den Besitz setzt Windows selbst (etwa für Fehlerberichte des Benutzers).</param>
    private static void EvaluateOwner(AccessContext ctx, FindingSink sink, FolderNode folder, bool managed)
    {
        var owner = folder.OwnerSid;
        if (string.IsNullOrEmpty(owner)) return;
        switch (ctx.KindOf(owner))
        {
            case PrincipalKind.Verwaist:
                // Steht die SID auch als Eintrag in der Liste, nennt der Befund aus EvaluateAces beides
                sink.Add(Orphaned, folder.Path, owner, $"Besitzer ist die verwaiste SID {owner} – das Konto gibt es nicht mehr.");
                break;
            case PrincipalKind.Benutzer when !managed && IsPerson(owner) && !SidCatalog.IsBuiltinAdministrator(owner):
                sink.Add(OwnerIsUser, folder.Path, owner, $"Besitzer ist das Benutzerkonto „{ctx.Label(owner)}“.");
                break;
        }
    }

    /// <summary>
    /// Benutzerkonto einer Person: Virtuelle Konten (Window Manager\DWM-1, Dienst- und Aufgabenkonten …) meldet Windows
    /// zwar teils als „Benutzer“, sie gehören aber zum System und nie einer Person.
    /// </summary>
    private static bool IsPerson(string sid) => !SidCatalog.IsSpecialIdentity(sid);

    /// <summary>
    /// Ordner, dessen Rechte Windows selbst setzt und pflegt: Besitzer TrustedInstaller oder ein ausdrücklicher Eintrag für
    /// eine Windows-interne Kennung (App-Pakete und -Fähigkeiten, SCHREIBGESCHÜTZT, EINGESCHRÄNKT …), die niemand auf
    /// Datenordnern vergibt. Dort entfallen die Ordnungsregeln (breite Gruppe darf lesen, Recht direkt an Person, Besitzer,
    /// versteckte Ausnahme, abweichende Vererbung) – „Benutzer dürfen C:\Programme lesen“ ist so gewollt, und ändern
    /// sollte man dort ohnehin nichts. Was eine echte Gefahr zeigt, bleibt: Schreibrecht breiter Gruppen, Vollzugriff
    /// für andere als den Besitzer, verwaiste SIDs, Verweigerungen, deaktivierte Konten.
    /// </summary>
    internal static bool IsWindowsManaged(FolderNode folder)
    {
        if (folder.OwnerSid.Equals(SidCatalog.TrustedInstaller, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var ace in folder.Aces)
            if (!ace.IsInherited && SidCatalog.IsWindowsInternal(ace.Sid)) return true;
        return false;
    }

    /// <summary>
    /// Für ZT06: Lässt die Ausnahme jemanden außer Administratoren und Windows selbst zu? Eine Ausnahme, die nur
    /// SYSTEM, Administratoren, TrustedInstaller oder Dienste zulässt (so legt Windows z. B. Paket- und Fehlerordner
    /// unter C:\ProgramData an), schottet ab und versteckt keinen Zugriff.
    /// </summary>
    internal static bool GrantsBeyondAdmins(FolderNode folder)
    {
        foreach (var ace in folder.Aces)
        {
            if (ace.IsDeny || ace.Rights == 0) continue;
            var sid = ace.Sid;
            if (SidCatalog.IsAdminLike(sid) || SidCatalog.IsBuiltinAdministrator(sid) || SidCatalog.IsCreatorPlaceholder(sid)) continue;
            // Sonder-Identitäten zählen, wenn sie für Personen stehen (Jeder, ANONYMOUS-ANMELDUNG, Diese Organisation …)
            if (SidCatalog.IsWindowsOrService(sid)) continue;
            return true;
        }
        return false;
    }

    /// <summary>Meldung für ZT14, möglichst mit den Konten, deren geerbte Einträge nicht vom übergeordneten Ordner kommen.</summary>
    private static string DescribeInheritanceDiffers(AccessContext ctx, FolderNode folder)
    {
        // Vererbung eingeschaltet, aber nichts geerbt: Ein Programm hat den Ordner mit eigenen Rechten angelegt
        if (!folder.Aces.Any(a => a.IsInherited))
            return "Erbt nichts vom übergeordneten Ordner, obwohl die Vererbung eingeschaltet ist – meist hat ein Programm den "
                + "Ordner mit eigenen Rechten angelegt. Die Rechte von oben gelten hier erst nach der nächsten Weitergabe.";
        const string Generic = "Geerbte Einträge passen nicht zum übergeordneten Ordner – vermutlich verschoben; es gelten womöglich noch Rechte vom früheren Ort.";

        // Quelle der Vererbung: der nächste gespeicherte Ordner darüber – der Elternordner selbst oder ein Vorfahr, von dem
        // alles dazwischen unverändert erbt. Dessen Einträge, nicht ein nachgebildeter Elternordner: Der kennt weder
        // ERSTELLER-BESITZER noch „nur Dateien“, die als „nur vererben“ trotzdem bei Unterordnern und Dateien ankommen.
        FolderNode? source = null;
        int distance = 0;
        for (var p = PathOrder.Parent(folder.Path); p is not null && source is null; p = PathOrder.Parent(p))
        {
            distance++;
            if (ctx.StoredAt(p) is { IsFile: false } stored) source = stored;
        }
        if (source is null || source.Error is not null) return Generic;

        // Was dort ankommt (je SID, ohne Rechte – für eine verständliche Aufzählung): Dateien erben „Objekte erben“,
        // Ordner auch das (als „nur vererben“); „nicht weitergeben“ reicht nur eine Ebene.
        var needed = folder.IsFile ? AceInheritance.ObjectInherit : AceInheritance.ContainerInherit | AceInheritance.ObjectInherit;
        var passed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // ERSTELLER-BESITZER wird beim Erben zum Besitzer – erkennbar am Platzhalter oben oder am eigenen „nur vererben“-Eintrag.
        bool creator = folder.Aces.Any(a => a.IsInherited && SidCatalog.IsCreatorPlaceholder(a.Sid));
        foreach (var ace in source.Aces)
        {
            if ((ace.Inheritance & needed) == 0) continue;
            if (distance > 1 && (ace.Propagation & AcePropagation.NoPropagateInherit) != 0) continue;
            passed.Add(ace.Sid);
            creator |= SidCatalog.IsCreatorPlaceholder(ace.Sid);
        }
        var foreign = folder.Aces
            .Where(a => a.IsInherited && !passed.Contains(a.Sid) && !SidCatalog.IsCreatorPlaceholder(a.Sid)
                        && !(creator && a.Sid.Equals(folder.OwnerSid, StringComparison.OrdinalIgnoreCase)))
            .Select(a => a.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => $"„{ctx.Label(s)}“")
            .ToList();
        if (foreign.Count == 0) return Generic;
        var names = foreign.Count == 1 ? foreign[0] : string.Join(", ", foreign.Take(foreign.Count - 1)) + " und " + foreign[^1];
        return $"Geerbte Einträge passen nicht zum übergeordneten Ordner – vermutlich verschoben. {names} "
            + (foreign.Count == 1 ? "kommt" : "kommen") + " nicht von dort, sondern vom früheren Ort.";
    }

    private static void EvaluateShares(AccessContext ctx, FindingSink sink)
    {
        foreach (var share in ctx.Scan.Shares)
        {
            if (!share.PermissionsReadable)
            {
                sink.Add(ShareUnreadable, share.UncPath, null,
                    $"Die Freigabe-Berechtigungen von {share.UncPath} konnten nicht gelesen werden.");
                continue;
            }
            foreach (var ace in share.Permissions)
            {
                if (!ace.IsDeny && ace.Access >= ShareAccess.Aendern
                    && (ace.Sid.Equals(SidCatalog.Everyone, StringComparison.OrdinalIgnoreCase)
                        || ace.Sid.Equals(SidCatalog.AuthenticatedUsers, StringComparison.OrdinalIgnoreCase)))
                    sink.Add(ShareEveryone, share.UncPath, ace.Sid,
                        $"Freigabe-Berechtigung „{ctx.Label(ace.Sid)}“: „{Rights.Label(ace.Access)}“ – der Schutz liegt allein bei NTFS.");

                if (ctx.KindOf(ace.Sid) == PrincipalKind.Verwaist)
                    sink.Add(Orphaned, share.UncPath, ace.Sid,
                        $"Die Freigabe-Berechtigung nennt die verwaiste SID {ace.Sid} – das Konto gibt es nicht mehr.");
            }
        }
    }

    /// <summary>
    /// Fehler ohne gespeicherten Knoten (z. B. Server nicht erreichbar) – damit der Bericht zeigt, dass nicht alles
    /// geprüft ist. Fehler zu gespeicherten Knoten und nicht lesbare Freigabe-Rechte haben eigene Befunde.
    /// </summary>
    private static void EvaluateScanErrors(ScanResult scan, FindingSink sink)
    {
        var known = new HashSet<string>(scan.Folders.Select(f => PathOrder.Normalize(f.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var share in scan.Shares.Where(s => !s.PermissionsReadable)) known.Add(PathOrder.Normalize(share.UncPath));
        foreach (var error in scan.Errors)
        {
            if (string.IsNullOrEmpty(error.Path) || known.Contains(PathOrder.Normalize(error.Path))) continue;
            sink.Add(Unreadable, error.Path, null, $"Nicht geprüft: {error.Message.TrimEnd('.')}.");
        }
    }

    /// <summary>
    /// Rechte einer SID aus den expliziten Einträgen eines Knotens, getrennt nach Geltungsbereich (Ordner selbst,
    /// Unterordner, Dateien). So zählt auch ein Eintrag, der nur an Inhalte vererbt wird, ohne dass Rechte aus
    /// verschiedenen Bereichen zu einem Recht verschmelzen, das es nirgends gibt.
    /// </summary>
    /// <param name="AllowMask">Alle eigenen expliziten Erlauben-Rechte der SID (für Texte).</param>
    /// <param name="DeniedMask">Alle eigenen expliziten Verweigern-Rechte der SID (für Texte).</param>
    private readonly record struct ScopeLevels(AccessLevel Raw, AccessLevel Net, FileRights AllowMask, FileRights DeniedMask,
        bool HasAllow, bool HasDeny, bool OnlyForChildren)
    {
        public static ScopeLevels For(AccessContext ctx, FolderNode folder, string sid)
        {
            // Explizite Verweigerungen stehen kanonisch vor expliziten Erlaubnissen – sie treffen auch die Mitglieder,
            // geerbte Verweigerungen können explizite Erlaubnisse dagegen nicht aufheben.
            var token = ctx.TokenOf(sid, AccessContext.LogonFor(folder));
            Span<FileRights> allow = stackalloc FileRights[3];
            Span<FileRights> deny = stackalloc FileRights[3];
            FileRights ownAllow = 0, ownDeny = 0;
            bool hasAllow = false, hasDeny = false;

            foreach (var ace in folder.Aces)
            {
                if (ace.IsInherited) continue;
                bool own = ace.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase);
                if (ace.IsDeny ? !token.Contains(ace.Sid) : !own) continue;

                var mask = Rights.MapGeneric(ace.Rights);
                var target = ace.IsDeny ? deny : allow;
                if (!ace.InheritOnly) target[0] |= mask;
                if (!folder.IsFile && (ace.Inheritance & AceInheritance.ContainerInherit) != 0) target[1] |= mask;
                if (!folder.IsFile && (ace.Inheritance & AceInheritance.ObjectInherit) != 0) target[2] |= mask;

                if (own && ace.IsDeny) { hasDeny = true; ownDeny |= mask; }
                if (own && !ace.IsDeny) { hasAllow = true; ownAllow |= mask; }
            }

            AccessLevel raw = AccessLevel.Keine, net = AccessLevel.Keine;
            for (int i = 0; i < 3; i++)
            {
                raw = Max(raw, Rights.ToLevel(allow[i]));
                net = Max(net, Rights.ToLevel(allow[i] & ~deny[i]));
            }
            var self = Rights.ToLevel(allow[0] & ~deny[0]);
            return new ScopeLevels(raw, net, ownAllow, ownDeny, hasAllow, hasDeny, net > self);
        }

        private static AccessLevel Max(AccessLevel a, AccessLevel b) => a > b ? a : b;
    }

    /// <summary>
    /// Rechte als Text, z. B. „„Löschen“ und „Berechtigungen ändern““. Für Verweigerungen taugt
    /// <see cref="Rights.ToLevel"/> nicht: Es beschreibt, was jemand höchstens kann – „Löschen verweigert“ hieße dann
    /// „Kein Zugriff verweigert“, „Berechtigungen ändern verweigert“ hieße „Vollzugriff verweigert“. Ganze Stufen
    /// heißen deshalb wie in der Windows-Oberfläche, sonst werden die enthaltenen Teile genannt.
    /// </summary>
    internal static string Describe(FileRights rights)
    {
        var quoted = Rights.Parts(rights).Select(p => $"„{p}“").ToList();
        return quoted.Count == 1 ? quoted[0] : string.Join(", ", quoted.Take(quoted.Count - 1)) + " und " + quoted[^1];
    }

    /// <summary>Sammelt Befunde eindeutig je (Regel, Pfad, SID); der erste gewinnt.</summary>
    private sealed class FindingSink(AccessContext ctx)
    {
        private readonly Dictionary<string, Finding> _findings = new(StringComparer.Ordinal);

        public void Add(string ruleId, string path, string? sid, string message)
        {
            var finding = new Finding { RuleId = ruleId, Severity = ById[ruleId].Severity, Path = path, Sid = sid, Message = message };
            _findings.TryAdd(finding.Key, finding);
        }

        public IReadOnlyList<Finding> Sorted()
        {
            var list = _findings.Values.ToList();
            list.Sort((a, b) =>
            {
                int c = b.Severity.CompareTo(a.Severity);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.RuleId, b.RuleId);
                if (c != 0) return c;
                c = PathOrder.Instance.Compare(a.Path, b.Path);
                if (c != 0) return c;
                return StringComparer.OrdinalIgnoreCase.Compare(a.Sid is null ? "" : ctx.Label(a.Sid), b.Sid is null ? "" : ctx.Label(b.Sid));
            });
            return list;
        }
    }
}
