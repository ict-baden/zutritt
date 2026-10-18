# CLAUDE.md – Zutritt

Zutritt: Zeigt, wer auf Dateifreigaben worauf zugreifen darf – mit Befunden, Vergleich und Bericht

Projekt von ICT-Baden. Es gelten die Richtlinien in [`ict-baden/richtlinien`](https://github.com/ict-baden/richtlinien). Stand und Zuständigkeit stehen in `ict-baden/ict-baden-projekte` (`projekte.json`, Eintrag `freigaben-berechtigungen`, `notizen/freigaben-berechtigungen.md`, `in-arbeit.md`).

## Bauen und testen

```powershell
dotnet build Zutritt.sln
dotnet test Zutritt.sln                          # als Administrator, sonst werden die [AdminFact]-Tests übersprungen
dotnet test tests/Zutritt.Tests -f net10.0       # nur der plattformneutrale Teil (so läuft es in der CI unter Linux)
dotnet run --project src/Zutritt -- --demo
```

## Aufbau

- `src/Zutritt` – Oberfläche (WPF) und Befehlszeile
- `src/Zutritt.Core` – Logik ohne Oberfläche und ohne Systemaufrufe (`net10.0`): Auswertung, Regeln, Vergleich, Speicher, Berichte
- `src/Zutritt.Platform.Windows` – alles Windows-Eigene: NTFS-Rechte, SMB-Freigaben, lokale Konten, Active Directory
- `tests/Zutritt.Tests`
- `installer/` – MSI (WiX 5)
- `docs/` – Doku (Englisch), Bilder in `docs/assets/`
- `tools/screenshots/` – erzeugt Screenshots, Berichtsseiten und Banner (`make-assets.ps1`)
- `.github/` – CI (`workflows/build.yml`), Release „latest“ (`scripts/release-latest.ps1`), Stückliste auf das Ausgelieferte zuschneiden (`scripts/sbom-shipped.ps1`), Issue-Formulare, Dependabot

## Besonderheiten

- **Windows-gebunden:** Zutritt liest NTFS-Rechte und SMB-Freigaben und löst Konten über SAM und Active Directory auf. Das gibt es nur unter Windows, deshalb WPF statt Avalonia (Ausnahme nach richtlinien/01 und 02). `Zutritt.Core` bleibt trotzdem plattformneutral, Windows-Code nur in `Zutritt.Platform.Windows`. Die CI testet den neutralen Teil zusätzlich unter Linux.
- **Viele Tests brauchen Administratorrechte** (`[AdminFact]`): Sie legen echte Ordner mit ACLs, SMB-Freigaben und lokale Konten an und räumen danach auf. Ohne Rechte werden sie übersprungen – ein grüner Lauf ohne Admin sagt wenig.
- **Active Directory** ist nur gegen ein nachgebautes Verzeichnis (`FakeDirectory`) getestet, nicht gegen eine echte Domäne.
- **Daten:** Datenbank pro Benutzer unter `%LocalAppData%\Zutritt\zutritt.db`. Berichte und Datenbank sind eine Landkarte der Berechtigungen – vertraulich, nie in Issues oder ins Repo.
- **MSI:** `UpgradeCode` (075F2980-6E82-4EF1-ABB5-936F52A88839) und die GUID der Startmenü-Komponente nie ändern.
- **Keine Personennamen** in Oberfläche, Demo-Daten und Screenshots. Bilder der Doku nur mit `tools/screenshots/make-assets.ps1` neu erzeugen – in einer angemeldeten, verbundenen Windows-Sitzung (in einer getrennten Sitzung zeichnet WPF nichts, `--screenshots` bricht dann mit Exitcode 3 ab). Ohne eine solche Sitzung auf einem `windows-latest`-Runner erzeugen.
- **Abhängigkeiten:** jede neue oder aktualisierte in `THIRD-PARTY-NOTICES.txt` eintragen. PDFsharp/MigraDoc bleibt bei 6.2.x (MIT) – ab 6.5.0 AGPL-3.0. SixLabors.Fonts (über ClosedXML) bei 1.x (Apache-2.0) – ab 2.0 Six Labors Split License. WiX bei 5.x (MS-RL) – ab 6.0 OSMF-EULA. Dependabot ignoriert diese Sprünge, siehe `.github/dependabot.yml`.
- **Sprache:** Oberfläche, Befehlszeile und Berichte Deutsch, Code-Kommentare Deutsch, README und `docs/` Englisch. Richtlinie 13 (fünf Sprachen en, de, es, fr, zh-Hans, Texte in `.resx`) gilt für Zutritt erst mit der Umstellung bis Version 1.0 (Übergangsregel in 13); dann auch `NeutralLanguage` `en` und `SatelliteResourceLanguages` `en;de;es;fr;zh-Hans` in `Directory.Build.props` wie in der Vorlage.
- **Firmierung offen:** `ICT-Baden` in `LICENSE`, `Directory.Build.props` und MSI-`Manufacturer` wird gemeinsam „ICT-Baden GmbH“, sobald OFFEN.md „Firmierung“ entschieden ist.

## Regeln

- Commit-Nachrichten auf Deutsch.
- Kein Text fest im Code: jeden neuen Text in alle Sprachdateien eintragen (en, de, es, fr, zh-Hans), Fachbegriffe aus `ict-baden/richtlinien/sprachen/glossar.csv`. Unsichere Übersetzung: englischen Text eintragen, mit `TODO-übersetzen` markieren. Regeln in Richtlinie 13. Für Zutritt aufgeschoben bis zur Umstellung auf `.resx` (spätestens 1.0, siehe „Sprache“ oben); bis dahin neue Texte so schreiben, dass sie sich ohne Zusammenstückeln in `.resx` übernehmen lassen (Platzhalter statt `+`).
- Nach Arbeit an diesem Projekt: Verlauf in `ict-baden-projekte/notizen/freigaben-berechtigungen.md` und Logbuch dort ergänzen.
- Vor jedem Release: Abschnitt „Unveröffentlicht“ im `CHANGELOG.md` pflegen.
