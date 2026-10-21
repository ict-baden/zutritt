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
- **Sprache:** Oberfläche, Befehlszeile und Berichte in fünf Sprachen (Richtlinie 13): Texte in `src/Zutritt.Core/Resources/Strings*.resx` (`Strings.resx` = Englisch, Rückfallsprache), Sprache folgt Windows, umstellbar auf der Seite Info (`%LocalAppData%\Zutritt\settings.json`), Vorgabe für alle in `%ProgramData%\Zutritt\settings.json`, erzwingbar mit `ZUTRITT_LANGUAGE`. Spanisch, Französisch und Chinesisch sind maschinell vorübersetzt und noch nicht gegengelesen (Kopfkommentar der Dateien; Windows-Kontonamen dort mit `TODO-übersetzen`). Fehler zeigen Codes ZUT-xxxx (`ErrorCodes`, Liste in `docs/error-codes.md`, nie umnummerieren). Tests laufen auf Deutsch (`TestCulture`). Code-Kommentare Deutsch, README und `docs/` Englisch; Protokoll und Diagnosepaket bleiben deutsch (für den Support).
- **Firmierung offen:** `ICT-Baden` in `LICENSE`, `Directory.Build.props` und MSI-`Manufacturer` wird gemeinsam „ICT-Baden GmbH“, sobald OFFEN.md „Firmierung“ entschieden ist.

## Regeln

- Commit-Nachrichten auf Deutsch.
- Jede `.cs`-Datei beginnt mit `// SPDX-License-Identifier: MIT` (Richtlinie 06, Test `SourceHeaderTests`).
- Kein Text fest im Code: jeden neuen Text in alle Sprachdateien eintragen (en, de, es, fr, zh-Hans), Fachbegriffe aus `ict-baden/richtlinien/sprachen/glossar.csv`. Unsichere Übersetzung: englischen Text eintragen, mit `TODO-übersetzen` markieren. Regeln in Richtlinie 13. Im Code `Strings.X` bzw. `Texts.Format(Strings.X, …)`, in XAML `{x:Static res:Strings.X}` – Platzhalter statt `+`, Mehrzahl über `Texts.Plural`.
- Nach Arbeit an diesem Projekt: Verlauf in `ict-baden-projekte/notizen/freigaben-berechtigungen.md` und Logbuch dort ergänzen.
- Vor jedem Release: Abschnitt „Unveröffentlicht“ im `CHANGELOG.md` pflegen.
