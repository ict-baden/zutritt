# Änderungen

Alle wichtigen Änderungen an Zutritt. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

Jeder Versionsabschnitt endet mit „English“: Das ist der Text des Releases auf GitHub (`.github/scripts/release-notes.ps1`).

## [Unveröffentlicht]

## [1.0.0] – 2026-11-23

Erste öffentliche Version. Sie enthält, was als 0.1.3 vorgesehen war (nie veröffentlicht), und die Korrekturen aus dem Test gegen eine echte Domäne (Windows Server 2025, Domänencontroller mit DFS, Mitgliedsserver). Ab jetzt braucht eine Änderung, die Befehlszeile, Exit-Codes oder die Datenbank bricht, eine neue Hauptversion.

### Behoben
- **DFS-Pfade** (`\\domäne\namensraum\…`) bekamen die Freigabe-Rechte der Freigabe des Namensraums statt der Freigabe, auf der die Ordner wirklich liegen. Der Bericht zeigte dadurch weniger Zugriff, als bestand – im Test „Lesen“, obwohl der Benutzer über denselben Pfad schreiben konnte. Zutritt löst DFS-Wurzel und DFS-Verknüpfungen jetzt auf die Zielfreigabe auf (als Ziel, auf dem Weg zum Ziel und beim Durchlaufen des Namensraums); ist die Zielfreigabe nicht lesbar, zählt NTFS allein (ZT13).
- Einträge für **„Diese Organisation“** (`S-1-5-15`) zählten für niemanden: Ein Ordner, auf den jedes Konto schreiben durfte, erschien als „nur Administratoren“, ohne Befund. Die Identität gehört jetzt zum Token jedes Kontos und gilt als breite Gruppe (ZT01, ZT02).
- Seite *Personen*: Eine Mitgliedschaft, die Windows nur stillschweigend vergibt (Jeder, Authentifizierte Benutzer, Diese Organisation), erschien als bloße SID, wenn kein Eintrag im Lauf sie nannte. Sie trägt jetzt ihren Namen.
- Release einer Vorabversion (Tag wie `v1.0.0-rc1`): Der Schalter `--prerelease` kam beim Aufruf von `gh release create` in einzelnen Zeichen an, das Release wäre nicht entstanden. Getaggte Versionen ohne Bindestrich waren nicht betroffen.

### Geändert
- **Herausgeber:** Lizenz, Dateieigenschaften, MSI (*Hersteller*) und die Info-Seite nennen die Firmierung aus dem Impressum, „ICT Baden GmbH“ (bisher „ICT-Baden“). Der Registry-Schlüssel der MSI und der `UpgradeCode` bleiben gleich, ein Update über ältere Versionen funktioniert wie bisher.
- **Doku:** Zutritt ist gegen eine echte Domäne geprüft (Windows Server 2025, ein Domänencontroller, ein Mitgliedsserver, DFS); `docs/limitations.md` nennt, was dabei ungeprüft blieb (mehrere Domänen, Vertrauensstellungen, NAS). Neu beschrieben: DFS, die Identitäten im Zugriffstoken und Einträge für Anmeldearten, die für niemanden zählen. Die Einstufung „early version“ und „Pre-release“ entfällt, „nicht signiert“ bleibt. `docs/limitations.md` nannte für einen Lauf ohne ein einziges geprüftes Ziel Exitcode 0; richtig ist 3.
- README mit Abschnitt *Publisher*; `SUPPORT.md` verweist auf Kontaktseite sowie Support und Preise; Sicherheitslücken lassen sich auch ohne GitHub-Konto melden (Issue-Auswahl).
- Öffentliche Dateien nennen keine internen Repos mehr.

### English

First public version. Zutritt was checked by hand against a real domain (Windows Server 2025, a domain controller with DFS and a member server) and compared with the Windows access check and with real access over SMB; this release fixes the two cases in which it showed less access than there was. From now on, a change that breaks the command line, the exit codes or the database needs a new major version.

Not code-signed yet – check the files against `SHA256SUMS.txt`. What Zutritt cannot see or decide: [Limitations](https://github.com/ict-baden/zutritt/blob/main/docs/limitations.md).

#### Fixed
- **DFS paths** (`\\domain\namespace\…`) were rated with the share permissions of the namespace share instead of the share the folders really live on. The report then showed less access than there was – in the test *Lesen* (read) although the user could write through the same path. Zutritt now resolves the DFS root and DFS links to the target share (for the target, on the way to it and while walking the namespace); if the target share cannot be read, NTFS alone counts (ZT13).
- Entries for **“This Organization”** (`S-1-5-15`) counted for no one: a folder every account could write to appeared as “administrators only”, without a finding. The identity is now part of every account's token and counts as a broad group (ZT01, ZT02).
- *Personen* page: a membership that Windows grants implicitly (Everyone, Authenticated Users, This Organization) appeared as a bare SID when no entry in the run named it. It now shows its name.
- Releasing a pre-release version (a tag like `v1.0.0-rc1`): the switch `--prerelease` reached `gh release create` as single characters, so the release would not have been created. Tagged versions without a hyphen were not affected.

#### Changed
- **Publisher:** licence, file properties, the MSI (*Manufacturer*) and the *Info* page name the company as in the legal notice, ICT Baden GmbH (so far “ICT-Baden”). The registry key of the MSI and its `UpgradeCode` are unchanged; updating over older versions works as before.
- **Documentation:** tested against a real domain; `docs/limitations.md` says what was not tested (several domains, trusts, NAS). New: DFS, the identities in the access token, and entries for sign-in types that count for no one. `docs/limitations.md` gave exit code 0 for a run in which no target could be checked; it is 3.
- The README has a *Publisher* section; `SUPPORT.md` points to the contact page and to support and pricing; vulnerabilities can also be reported without a GitHub account.

## [0.1.2] – 2026-10-22

### Behoben
- MSI: Nach einem Update vom rollierenden Build `latest` (oder einem anderen Build von main) auf ein Tag-Release fehlte `Zutritt.exe`. Das Tag-Release trug die Dateiversion `x.y.z.0`, älter als die der Builds von main derselben Version (`x.y.z.<Lauf>`); Windows Installer kopierte die Exe deshalb nicht, entfernte aber die alte. Jetzt trägt die Dateiversion immer die Lauf-Nummer, auch beim Tag, und die MSI ersetzt jede Datei, deren Version abweicht (`REINSTALLMODE=dmus`). Betroffen waren v0.1.0 und v0.1.1, wenn sie über einen Build von `latest` derselben Version installiert wurden; die MSI 0.1.2 bringt `Zutritt.exe` zurück.

### Geändert
- Die CI installiert jede MSI über den Build von `latest`, über das letzte Tag-Release und über ein Paket derselben Version mit neuerer Exe und prüft danach alle Dateien, die Dateiversion und den Start von `Zutritt.exe`.
- Die CI prüft die Syntax aller PowerShell-Schritte der Workflows und aller Skripte, auch der Schritte, die nur bei Tags laufen; ein typografisches Anführungszeichen als String-Begrenzer gilt als Fehler. Den Release-Text erzeugt sie schon im Pull Request, sobald der CHANGELOG den Abschnitt der Version hat.

### English

Fixes an MSI update that could leave out `Zutritt.exe`.

#### Fixed
- MSI: after updating from the rolling `latest` build (or another build of `main`) to a tagged release, `Zutritt.exe` was missing. The tagged release had the file version `x.y.z.0`, older than that of `main` builds of the same version (`x.y.z.<run>`), so Windows Installer did not copy the exe but still removed the old one. The file version now always ends in the run number, for tagged releases too, and the MSI replaces every file whose version differs (`REINSTALLMODE=dmus`). This affected v0.1.0 and v0.1.1 when installed over a `latest` build of the same version; installing 0.1.2 restores `Zutritt.exe`.

#### Changed
- Every MSI is now tested in CI by installing it over the `latest` build, over the last tagged release and over a package of the same version with a newer exe; afterwards all files, the file version and the start of `Zutritt.exe` are checked.
- CI checks the syntax of every PowerShell step and script, including the steps that only run for tagged releases, and generates the release notes in the pull request already (when the changelog has the version's section).

## [0.1.1] – 2026-10-21

### Geändert
- Kopf der Navigation und erste Zeile von `--info` führen den Untertitel wie Fenstertitel und Seite *Info*: „Zutritt – Freigaben-Rechte“ (englisch „Share Permissions“).
- *Datenschutz* auf der Seite *Info* und in `--info` führt auf die Datenschutzerklärung von Zutritt auf der Webseite, `https://ict-baden.de/datenschutz/zutritt/`. Die Einzelheiten stehen weiter in `docs/data-and-privacy.md`.
- Releases einer Version tragen den englischen Abschnitt dieses CHANGELOG mit der Liste der Dateien als Text, statt automatisch erzeugter Notizen. Der Text des rollierenden Builds `latest` ist englisch.

### Behoben
- Die Bilder der Doku zeigen deutsche Daten- und Zahlenformate statt amerikanischer, und die Seite *Info* in den Bildern sagt nicht mehr, die Sprache sei durch `ZUTRITT_LANGUAGE` vorgegeben. Dafür formatieren `--screenshots` und `--demo-bericht` Daten und Zahlen in der Sprache der Bilder.
- Die Zahl der Befunde je Schweregrad auf der Seite *Befunde* folgt dem Gebietsschema von Windows (war fest deutsch, z. B. „1.234“ auch bei englischer Oberfläche).

### English

Maintenance release: the product name with its subtitle everywhere, the privacy link on the ICT-Baden website, release notes, number formats and documentation images.

#### Changed
- The header of the navigation and the first line of `--info` carry the subtitle, as the window title and the *Info* page already did: “Zutritt – Share Permissions” (German “Freigaben-Rechte”).
- *Privacy* on the *Info* page and in `--info` opens Zutritt's privacy notice on the ICT-Baden website, `https://ict-baden.de/datenschutz/zutritt/`. The details remain in `docs/data-and-privacy.md`.
- Releases of a version use the English part of the changelog with the list of files as their notes instead of generated notes. The notes of the rolling `latest` build are in English.

#### Fixed
- The documentation images show German date and number formats instead of US ones, and the *Info* page in the images no longer says that the language is set by `ZUTRITT_LANGUAGE`. For this, `--screenshots` and `--demo-bericht` format dates and numbers in the language of the images.
- The number of findings per severity on the *Findings* page follows the Windows regional format (it was always German, e.g. “1.234” in the English interface too).

## [0.1.0] – 2026-10-21

### Neu
- Erste Version.
- Scan von Freigaben (`\\server\freigabe`), ganzen Servern (`\\server`), lokalen Ordnern und allen Freigaben dieses Rechners (`.`): Freigabe-Berechtigungen und NTFS-Rechte, nur lesend. Abzweigungen (Junctions) werden vermerkt, nicht verfolgt.
- Gruppen bis zu einzelnen Personen aufgelöst, verschachtelt und mit Schutz vor Zyklen: lokale Konten und Gruppen des gescannten Servers, auf Rechnern in einer Domäne auch Active Directory der eigenen Domäne.
- Effektive Rechte wie Windows sie berechnet: Einträge in gespeicherter Reihenfolge (auch nicht kanonische Listen), Verweigern-Einträge, Vererbung, *OWNER RIGHTS*, Freigabe und NTFS zusammen – in der CI Bit für Bit gegen die Zugriffsprüfung von Windows (AuthZ) abgeglichen.
- ZT15 meldet Zugriffslisten, die nicht in kanonischer Reihenfolge stehen (Verweigern hinter Erlauben, explizit hinter geerbt) – dort gewährt Windows anderes, als die Einträge auf den ersten Blick versprechen.
- 15 Prüfregeln ZT01–ZT15 mit Erklärung und Empfehlung, z. B. breite Gruppe darf schreiben, verwaiste SID, Recht direkt an eine Person, Rechte weichen von der Vererbung ab (verschobene Ordner).
- Seiten *Übersicht*, *Befunde*, *Ordner* („Wer darf hier rein“), *Personen*, *Vergleich*, *Bericht* und *Info*, hell und dunkel.
- Vergleich mit dem vorigen Lauf: neue und behobene Befunde, geänderte Einträge und Gruppenmitgliedschaften, wer jetzt mehr oder weniger darf.
- Bericht als Excel-Datei mit Filtern und als PDF mit Deckblatt, Zusammenfassung, Befunden und Block zum Abzeichnen.
- Verlauf pro Benutzer in einer SQLite-Datei unter `%LocalAppData%\Zutritt`.
- Befehlszeile `scan`, `liste` und `bericht` mit den Exitcodes 0–3 für geplante Aufgaben und Monitoring.
- Demo-Modus `--demo` mit eingebauten Beispieldaten.
- MSI für die Verteilung per Intune oder GPO, Einzeldatei `Zutritt.exe` ohne Installation und ZIP für die installierte .NET 10 Desktop Runtime.
- Release `latest` mit festen Dateinamen (`Zutritt-x64.msi`, `Zutritt.exe`, `Zutritt-win-x64-framework.zip`), in jedem Release `SHA256SUMS.txt` und eine Stückliste der ausgelieferten Bestandteile samt .NET- und Windows-Desktop-Runtime-Paketen mit ihren Versionen (SBOM, CycloneDX). Die Dateien werden signiert, sobald Artifact Signing eingerichtet ist.
- MIT-Lizenz, Lizenzen der Fremdbestandteile in `THIRD-PARTY-NOTICES.txt`, Wege für Hilfe und Sicherheitsmeldungen in `SUPPORT.md` und `SECURITY.md`.
- Seite *Info* nach den Richtlinien: Version mit Build (Commit), © und MIT-Lizenz mit Lizenztext, Drittanbieter-Lizenzen, Links zu Hilfe & Support, Quellcode, Datenschutz, Impressum und Webseite, der Satz „Diese Software sendet keine Daten an ICT-Baden.“ Links öffnen nur http/https.
- Befehlszeile `--version` (`-v`), `--info` und `--lizenzen`.
- Protokoll für den Support unter `%LocalAppData%\Zutritt\logs`: eine Datei je Tag, 7 Tage, höchstens 1 MB je Tag, ohne Rechte, Mitglieder und Kontennamen (die häufigsten Fehlerarten eines Scans ohne Gruppen-, Domänen- und Pfadnamen); unerwartete Fehler werden mitgeschrieben.
- *Info* › *Diagnosepaket erstellen*: Version, Systemangaben und Protokoll als ZIP, standardmäßig ohne Namen, Pfade und Adressen.
- Zeitlimits: Ein Verzeichnis oder eine Kontenauflösung, die 60 Sekunden nicht antwortet, wird im Lauf nicht mehr gefragt; Freigabelisten und Freigaben bekommen 2 Minuten. Der Lauf hängt nicht mehr an einem Server, der nie antwortet.
- Demo-Daten mit Rollenkonten (z. B. `MUSTER\qc.leitung`, „Labor Mitarbeitende 01“) statt Personennamen.
- Neues App-Icon (Tür im Verlauf #22C3A6 → #3B6CF6), Banner und Screenshots der Doku neu erzeugt.
- Auswertung, Vergleich, Speicher und Berichte (`Zutritt.Core`) sind plattformneutral und werden in der CI auch unter Linux getestet; alles Windows-Eigene liegt in `Zutritt.Platform.Windows`. Zutritt selbst bleibt an Windows gebunden (NTFS, Freigaben, SAM, Active Directory).
- `--screenshots` bricht ab, statt in einer getrennten Windows-Sitzung leere Bilder zu schreiben.
- Oberfläche, Befehlszeile und Berichte in fünf Sprachen: Englisch, Deutsch, Spanisch, Französisch und Chinesisch (vereinfacht). Spanisch, Französisch und Chinesisch sind maschinell vorübersetzt und noch nicht von Muttersprachlern gegengelesen. Die Sprache folgt Windows (sonst Englisch), lässt sich auf der Seite *Info* umstellen (`%LocalAppData%\Zutritt\settings.json`, gilt ab dem nächsten Start), für alle Benutzer vorgeben (`%ProgramData%\Zutritt\settings.json`) und mit der Umgebungsvariable `ZUTRITT_LANGUAGE` erzwingen. Zahlen und Daten folgen dem Gebietsschema von Windows. Auch die eingebaute Demo spricht die eingestellte Sprache. Protokoll und Diagnosepaket bleiben deutsch und ersetzen Namen in den Meldungen jeder Sprache.
- Fehlermeldungen tragen einen Code `ZUT-xxxx` in jeder Sprache (Liste in `docs/error-codes.md`).
- Befehle und Optionen der Befehlszeile gibt es auch englisch, gleichwertig zu den deutschen: `list`, `report`, `--help`, `--licenses`, `--files`, `--depth`, `--deep-from`, `--no-groups`, `--include-admin-shares`, `--report`, `--compare`.

### Geändert
- Die CI prüft die MSI wieder mit den ICE-Regeln, installiert sie bei jedem Build auf dem Runner, startet `--info` und einen kleinen Scan, deinstalliert sie und sucht nach Resten.
- ZT08 heißt „Freigabe: alle dürfen ändern“ statt „Jeder Vollzugriff“ – die Regel meldet *Jeder* oder *Authentifizierte Benutzer* schon ab „Ändern“ auf der Freigabe.
- `scan` endet mit Exitcode 3, wenn nichts geprüft werden konnte (Server aus, einzige Freigabe nicht lesbar), damit eine geplante Aufgabe nicht sauber aussieht. Die Zusammenfassung *Achtung: N von M Zielen bzw. Freigaben konnten nicht geprüft werden* zählt jetzt auch Server, deren Freigabeliste nicht lesbar ist; ein Ordner mit unlesbaren Rechten zählt als geprüft, wenn darunter etwas lesbar war.
- Die Dateiversion von `Zutritt.exe` trägt die Build-Nummer als vierte Stelle (wie die MSI), statt bei jedem Build 0.1.0.0.

### Behoben
- Ein fertiger Scan geht nicht mehr verloren, wenn das Speichern in der Datenbank scheitert. Die Befehlszeile prüft Datenbank und Berichtsordner jetzt vor dem Scan und bricht ab, bevor gescannt wird; scheitert das Speichern trotzdem, schreibt sie die Berichte und endet mit Exitcode 3. Die Oberfläche lädt den Lauf in diesem Fall trotzdem und weist darauf hin, dass er nicht gespeichert ist.
- Der Vergleich zweier Läufe mit anderem Umfang (z. B. einmal mit Dateien, einmal nur bis Tiefe 1, mit und ohne administrative Freigaben oder mit anderer Ebene für versteckte Ausnahmen) meldet keine scheinbar entfallenen Rechte und „behobenen“ Befunde mehr: Verglichen wird nur, was beide Läufe erfasst haben, und Vergleichsseite, Befehlszeile, PDF und Excel weisen auf den abweichenden Umfang hin. Der automatische Vergleich nimmt bevorzugt den letzten Lauf mit denselben Einstellungen.
- `scan D:` und `scan D:\` gelten beim Vergleich als dasselbe Ziel; Läufe, die frühere Ausgaben mit `scan D:` gespeichert haben, findet der automatische Vergleich weiterhin. Die Warnung „andere Ziele“ auf der Vergleichsseite ordnet Ziele genauso zu wie der automatische Vergleich (abschließendes `\`, `/`).
- Die Kachel der Vergleichsseite heißt „ACL- und Ordneränderungen“, weil sie auch Ordner mit neuen oder entfallenen eigenen Rechten zählt (Befehlszeile und Berichte zählen die ACL-Änderungen allein).
- Impressum-Link in App, `--info`, README und SUPPORT.md zeigt auf `https://ict-baden.de/impressum/`, der Link *Webseite* auf die Produktseite `https://ict-baden.de/produkte/zutritt/`. Kontakt für Supportvertrag und Sicherheitsmeldungen ohne GitHub-Konto: die Kontaktseite `https://ict-baden.de/#kontakt`.
- Spanische, französische und chinesische Namen der Windows-Konten (*Tout le monde*, *Usuarios autentificados* …) wie in lokalisiertem Windows statt Englisch; die Sprachauswahl kennzeichnet maschinell übersetzte Sprachen.
- Fenstertitel und Seite *Info* führen den Untertitel „Zutritt – Freigaben-Rechte“ (englisch „Share Permissions“), damit der Name nicht mit Zutrittskontrolle verwechselt wird.
- Ein MSI-Update oder eine Deinstallation beendet eine laufende Zutritt-Befehlszeile mit Exitcode 3 (Fehler oder Abbruch) statt 1 – für eine geplante Aufgabe hieß 1 „Befunde Mittel“.
- Die Fortschrittszeile der Befehlszeile brach den Scan in Konsolen mit etwa 57 bis 75 Spalten Breite ab.
- Chinesische Schriftzeichen in Ordner- und Kontonamen erscheinen im PDF nicht mehr als leere Kästchen, sofern Windows eine Schrift mit ihnen hat (z. B. DengXian, SimHei); bei chinesischer Oberfläche ist der ganze Bericht in dieser Schrift gesetzt.
- Die Seite *Info* fragt die Datenbank nicht mehr im UI-Thread ab, sondern über die Warteschlange des Speichers.
- Ließ sich die Datenbank beim Start nicht öffnen, meldete die Übersicht nach einem Scan „Scan abgeschlossen“ ohne Hinweis, dass der Lauf nicht gespeichert ist; jetzt kommt dieselbe Warnung wie bei gescheitertem Speichern.
- Ein Vergleich zwischen einem Lauf mit und einem ohne aufgelöste Gruppen (`--ohne-gruppen`) listete ZT11 (leere Gruppe) unter „Behobene Befunde“.
- Antwortet ein Domänencontroller nicht, gibt das Zeitlimit die Abfrage auf – am Ende des Scans wartete das Schließen der AD-Verbindung aber trotzdem auf sie, und Oberfläche oder geplante Aufgabe wurden nicht fertig.
- Das anonymisierte Diagnosepaket ersetzt jetzt auch die DNS-Domäne des Rechners, Namen in LDAP-Pfaden (*CN=…*, *OU=…*, *DC=…*), Pfade mit Leerzeichen in der Mitte und Pfade in .NET-Meldungen (`…`).

### Entfernt
- Die Pakete enthalten die Windows-SDK-Projektion (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`, rund 25 MB, Lizenz des Windows SDK) und die PDFsharp-Zusatzbibliotheken `PdfSharp.WPFonts.dll` (Segoe-WP-Schriften), `PdfSharp.Snippets.dll` und `PdfSharp.Quality.dll` nicht mehr. Zutritt hat keine davon benutzt; Ziel ist jetzt `net10.0-windows` ohne Windows-SDK-Version, Mindestversion bleibt Windows 10 1809 / Server 2019.

### Sicherheit
- System.Security.Cryptography.Pkcs auf 10.0.13 festgelegt: PDFsharp brachte 8.0.1 mit bekannten Schwachstellen (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) mit.

### English

First version of Zutritt – Share Permissions: shows who can access what on Windows file shares, and through which group.

- **Scan** shares (`\\server\share`), whole servers (`\\server`), local folders and all shares of this PC (`.`): share and NTFS permissions, read-only.
- **Groups resolved to people**, nested and protected against cycles: local accounts and groups of the scanned server and, on domain-joined PCs, Active Directory of the PC's own domain.
- **Effective access computed like Windows**: entries in stored order (including non-canonical lists), deny entries, inheritance, *OWNER RIGHTS*, share and NTFS together – compared bit for bit with the Windows access check (AuthZ) in CI.
- **15 rules ZT01–ZT15** with explanation and recommendation, e.g. broad group can write, orphaned SID, rights granted to a person directly, inheritance differs from the parent (moved folders), entries not in canonical order.
- **Pages** *Overview*, *Findings*, *Folders* (who can get in here), *People*, *Comparison*, *Report* and *Info*, light and dark.
- **Comparison** with the previous run: new and resolved findings, changed entries and group memberships, who can now do more or less; runs with a different scope are compared only where both looked.
- **Reports** as an Excel workbook with filters and as a PDF with cover, summary, findings and a sign-off block.
- **History** per user in a SQLite file under `%LocalAppData%\Zutritt`.
- **Command line** `scan`, `list` and `report` (German spellings too) with exit codes 0–3 for scheduled tasks and monitoring; `--version`, `--info` and `--licenses`.
- **Five languages** for the window, the command line and the reports: English, German, and machine-translated Spanish, French and Chinese (simplified), following Windows and switchable on the *Info* page. Error messages carry a code `ZUT-xxxx`.
- **Support without telemetry**: a 7-day log without permissions or account names, and a diagnostics package that replaces names and addresses by default. The software sends no data to ICT-Baden.
- **Packages**: MSI for Intune or Group Policy, single-file `Zutritt.exe` and a ZIP for PCs with the .NET 10 Desktop Runtime, each release with `SHA256SUMS.txt` and a bill of materials (CycloneDX).

<!-- Abschnitte nach Bedarf: Neu, Geändert, Behoben, Entfernt, Sicherheit -->
