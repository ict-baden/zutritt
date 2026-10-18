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

<!-- ⚠ Kontakt: Support-Adresse und Impressum-Link folgen, sobald E-Mail-Adressen und Domain feststehen (OFFEN.md). -->

## Was Sie mitschicken / What to include

- **Version:** steht auf der Seite *Info* und in der ersten Zeile von `Zutritt.exe --hilfe`.
- **Variante:** MSI, Einzeldatei `Zutritt.exe` oder ZIP.
- **Umgebung:** Domäne oder Arbeitsgruppe, Windows-Version des Rechners und des Servers, Zutritt als Administrator gestartet oder nicht.
- **Ausgabe der Befehlszeile** (z. B. mit `> zutritt.log 2>&1`, siehe [Befehlszeile](docs/command-line.md#logs-and-encoding)), mit ersetzten Server-, Freigabe-, Pfad- und Kontonamen.

Berichte (Excel, PDF) und die Datenbank `zutritt.db` sind eine Landkarte der Berechtigungen. Bitte **nie öffentlich** in ein Issue stellen – zur vertraulichen Analyse gibt es den Supportvertrag mit Auftragsverarbeitungsvertrag.
Reports and the database `zutritt.db` map who can access what. Never attach them to a public issue.
