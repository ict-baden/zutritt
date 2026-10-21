# Änderungen

Alle wichtigen Änderungen an Zutritt. Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

## [Unveröffentlicht]

### Neu
- Erste Version.
- Scan von Freigaben (`\\server\freigabe`), ganzen Servern (`\\server`), lokalen Ordnern und allen Freigaben dieses Rechners (`.`): Freigabe-Berechtigungen und NTFS-Rechte, nur lesend. Abzweigungen (Junctions) werden vermerkt, nicht verfolgt.
- Gruppen bis zu einzelnen Personen aufgelöst, verschachtelt und mit Schutz vor Zyklen: lokale Konten und Gruppen des gescannten Servers, auf Rechnern in einer Domäne auch Active Directory der eigenen Domäne.
- Effektive Rechte wie Windows sie berechnet: kanonische Reihenfolge, Verweigern-Einträge, Vererbung, *OWNER RIGHTS*, Freigabe und NTFS zusammen.
- 14 Prüfregeln ZT01–ZT14 mit Erklärung und Empfehlung, z. B. breite Gruppe darf schreiben, verwaiste SID, Recht direkt an eine Person, Rechte weichen von der Vererbung ab (verschobene Ordner).
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
- Impressum-Link in App, `--info`, README und SUPPORT.md führte ins Leere (404); er zeigt jetzt auf die erreichbare Seite `https://ict-baden.de/index.html?page=imprint`. Kontakt für den Supportvertrag sind bis zu einem eigenen Postfach die Kontaktangaben dort.
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

<!-- Abschnitte nach Bedarf: Neu, Geändert, Behoben, Entfernt, Sicherheit -->
