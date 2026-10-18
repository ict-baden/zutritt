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
- Release `latest` mit festen Dateinamen (`Zutritt-x64.msi`, `Zutritt.exe`, `Zutritt-win-x64-framework.zip`), in jedem Release `SHA256SUMS.txt` und eine Stückliste der Bestandteile (SBOM, CycloneDX). Die Dateien werden signiert, sobald Artifact Signing eingerichtet ist.
- MIT-Lizenz, Lizenzen der Fremdbestandteile in `THIRD-PARTY-NOTICES.txt`, Wege für Hilfe und Sicherheitsmeldungen in `SUPPORT.md` und `SECURITY.md`.
- Seite *Info* nach den Richtlinien: Version mit Build (Commit), © und MIT-Lizenz mit Lizenztext, Drittanbieter-Lizenzen, Links zu Hilfe & Support, Quellcode, Datenschutz, Impressum und Webseite, der Satz „Diese Software sendet keine Daten an ICT-Baden.“ Links öffnen nur http/https.
- Befehlszeile `--version` (`-v`), `--info` und `--lizenzen`.
- Protokoll für den Support unter `%LocalAppData%\Zutritt\logs`: eine Datei je Tag, 7 Tage, höchstens 1 MB je Tag, ohne Rechte, Mitglieder und Kontennamen; unerwartete Fehler werden mitgeschrieben.
- *Info* › *Diagnosepaket erstellen*: Version, Systemangaben und Protokoll als ZIP, standardmäßig ohne Namen, Pfade und Adressen.
- Zeitlimits: Ein Verzeichnis oder eine Kontenauflösung, die 60 Sekunden nicht antwortet, wird im Lauf nicht mehr gefragt; Freigabelisten und Freigaben bekommen 2 Minuten. Der Lauf hängt nicht mehr an einem Server, der nie antwortet.
- Demo-Daten mit Rollenkonten (z. B. `MUSTER\qc.leitung`, „Labor Mitarbeitende 01“) statt Personennamen.
- Neues App-Icon (Tür im Verlauf #22C3A6 → #3B6CF6), Banner und Screenshots der Doku neu erzeugt.
- Auswertung, Vergleich, Speicher und Berichte (`Zutritt.Core`) sind plattformneutral und werden in der CI auch unter Linux getestet; alles Windows-Eigene liegt in `Zutritt.Platform.Windows`. Zutritt selbst bleibt an Windows gebunden (NTFS, Freigaben, SAM, Active Directory).
- `--screenshots` bricht ab, statt in einer getrennten Windows-Sitzung leere Bilder zu schreiben.

### Sicherheit
- System.Security.Cryptography.Pkcs auf 10.0.13 festgelegt: PDFsharp brachte 8.0.1 mit bekannten Schwachstellen (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) mit.

<!-- Abschnitte nach Bedarf: Neu, Geändert, Behoben, Entfernt, Sicherheit -->
