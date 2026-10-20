# Hilfe und Support / Help and support

Zutritt ist kostenlos und Open Source. Für Hilfe gibt es zwei Wege.
Zutritt is free and open source. There are two ways to get help.

## Kostenlos: GitHub / Free: GitHub

- **Fehler, Wünsche und Fragen:** [Issue anlegen](https://github.com/ict-baden/zutritt/issues/new/choose)
<!-- ⚠ Zeile „Fragen: Discussions“ aus der Vorlage wieder aufnehmen, sobald Discussions im Repo eingeschaltet ist. -->

Wir lesen alles, können aber keine Reaktionszeit zusagen. Alles dort ist öffentlich – bitte keine internen Daten.
We read everything but cannot promise response times. Everything there is public.

## Verbindlich: Supportvertrag / Guaranteed: support contract

Mit einem Supportvertrag von ICT-Baden bekommen Sie:

- zugesicherte Reaktionszeiten
- Hilfe bei Einrichtung, Verteilung und Betrieb in Ihrer Umgebung, per E-Mail, Telefon oder Fernsitzung
- vertrauliche Fehleranalyse (mit Auftragsverarbeitungsvertrag)
- Vorrang bei Fehlerbehebungen und Sicherheitsinformationen vorab
- Anpassungen und Schulungen nach Absprache

Kontakt: über die Kontaktangaben im [Impressum von ICT-Baden](https://ict-baden.de/index.html?page=imprint).
Contact: see the contact details in the [ICT-Baden imprint](https://ict-baden.de/index.html?page=imprint).
<!-- ⚠ Eigene Support-Adresse eintragen, sobald das Postfach existiert (OFFEN.md); bis dahin die Kontaktangaben im Impressum. -->

## Was Sie mitschicken / What to include

- **Version:** steht auf der Seite *Info* und kommt von `Zutritt.exe --version` (z. B. `Zutritt 0.1.0-ci.12 (Build abc1234)`).
- **Variante:** MSI, Einzeldatei `Zutritt.exe` oder ZIP.
- **Umgebung:** Domäne oder Arbeitsgruppe, Windows-Version des Rechners und des Servers, Zutritt als Administrator gestartet oder nicht.
- **Diagnosepaket:** *Info* › *Diagnosepaket erstellen* speichert Version, Systemangaben und das Protokoll der letzten 7 Tage als ZIP – mit dem Schalter *Ohne Namen und Adressen* (Standard) ohne Rechner-, Konto- und Servernamen, Pfade, SIDs und Adressen. Bitte vor dem Anhängen hineinschauen. Was drin ist: [Daten und Datenschutz](docs/data-and-privacy.md#diagnostics-package-and-support).
- **Ausgabe der Befehlszeile** (z. B. mit `> zutritt.log 2>&1`, siehe [Befehlszeile](docs/command-line.md#logs-and-encoding)), mit ersetzten Server-, Freigabe-, Pfad- und Kontonamen.

**English:** include the version (`Zutritt.exe --version`), the package (MSI, single exe, ZIP), your environment, and the anonymised diagnostics package from *Info* › *Diagnosepaket erstellen*.

Berichte (Excel, PDF) und die Datenbank `zutritt.db` sind eine Landkarte der Berechtigungen. Bitte **nie öffentlich** in ein Issue stellen – zur vertraulichen Analyse gibt es den Supportvertrag mit Auftragsverarbeitungsvertrag.
Reports and the database `zutritt.db` map who can access what. Never attach them to a public issue.
