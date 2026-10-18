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

<!-- Abschnitte nach Bedarf: Neu, Geändert, Behoben, Entfernt, Sicherheit -->
