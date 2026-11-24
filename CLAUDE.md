# CLAUDE.md – Zutritt

Zutritt: Zeigt, wer auf Dateifreigaben worauf zugreifen darf – mit Befunden, Vergleich und Bericht

Projekt der ICT Baden GmbH. Diese Datei ist die Arbeitsanleitung für Claude Code. Regeln für Beiträge stehen in [`CONTRIBUTING.md`](CONTRIBUTING.md), Bauen, Testen, CI und Releases in [`docs/development.md`](docs/development.md). „Richtlinie NN“ meint die internen, nicht öffentlichen Richtlinien der ICT Baden GmbH; alles Nötige steht in den beiden Dateien.

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

- **Windows-gebunden:** Zutritt liest NTFS-Rechte und SMB-Freigaben und löst Konten über SAM und Active Directory auf. Das gibt es nur unter Windows, deshalb WPF statt Avalonia (Ausnahme nach Richtlinie 01 und 02). `Zutritt.Core` bleibt trotzdem plattformneutral, Windows-Code nur in `Zutritt.Platform.Windows`. Die CI testet den neutralen Teil zusätzlich unter Linux.
- **Viele Tests brauchen Administratorrechte** (`[AdminFact]`): Sie legen echte Ordner mit ACLs, SMB-Freigaben und lokale Konten an und räumen danach auf. Ohne Rechte werden sie übersprungen – ein grüner Lauf ohne Admin sagt wenig.
- **Active Directory** ist in den automatischen Tests nur gegen ein nachgebautes Verzeichnis (`FakeDirectory`) geprüft. Gegen eine echte Domäne (Windows Server 2025, ein Domänencontroller, ein Mitgliedsserver, DFS) lief am 2026-11-20 ein Praxistest von Hand; mehrere Domänen und Vertrauensstellungen sind ungeprüft.
- **Daten:** Datenbank pro Benutzer unter `%LocalAppData%\Zutritt\zutritt.db`. Berichte und Datenbank sind eine Landkarte der Berechtigungen – vertraulich, nie in Issues oder ins Repo.
- **MSI:** `UpgradeCode` (075F2980-6E82-4EF1-ABB5-936F52A88839) und die GUID der Startmenü-Komponente nie ändern.
- **Keine Personennamen** in Oberfläche, Demo-Daten und Screenshots. Bilder der Doku nur mit `tools/screenshots/make-assets.ps1` neu erzeugen – in einer angemeldeten, verbundenen Windows-Sitzung (in einer getrennten Sitzung zeichnet WPF nichts, `--screenshots` bricht dann mit Exitcode 3 ab). Ohne eine solche Sitzung auf einem `windows-latest`-Runner erzeugen.
- **Abhängigkeiten:** jede neue oder aktualisierte in `THIRD-PARTY-NOTICES.txt` eintragen. PDFsharp/MigraDoc bleibt bei 6.2.x (MIT) – ab 6.5.0 AGPL-3.0. SixLabors.Fonts (über ClosedXML) bei 1.x (Apache-2.0) – ab 2.0 Six Labors Split License. WiX bei 5.x (MS-RL) – ab 6.0 OSMF-EULA. Dependabot ignoriert diese Sprünge, siehe `.github/dependabot.yml`.
- **Sprache:** Oberfläche, Befehlszeile und Berichte in fünf Sprachen (Richtlinie 13): Texte in `src/Zutritt.Core/Resources/Strings*.resx` (`Strings.resx` = Englisch, Rückfallsprache), Sprache folgt Windows, umstellbar auf der Seite Info (`%LocalAppData%\Zutritt\settings.json`), Vorgabe für alle in `%ProgramData%\Zutritt\settings.json`, erzwingbar mit `ZUTRITT_LANGUAGE`. Spanisch, Französisch und Chinesisch sind maschinell vorübersetzt und noch nicht gegengelesen (Kopfkommentar der Dateien, Sprachauswahl „maschinelle Übersetzung“); offene `TODO-übersetzen` gibt es nicht mehr (Test `Keine_offenen_Uebersetzungen`), Windows-Kontonamen (`Sid_*`) heißen wie in lokalisiertem Windows (zh-Hans dort teils 帐户, der eigene Text 账户); nicht an echter Windows-Ausgabe belegte tragen den Kommentar „nicht an echter Ausgabe belegt“. Untertitel „Zutritt – Freigaben-Rechte“ überall (Fenstertitel, Info, Navigation, `--info`; Test prüft `App_Title` gegen `Ui_AppSubtitle`). Fehler zeigen Codes ZUT-xxxx (`ErrorCodes`, Liste in `docs/error-codes.md`, nie umnummerieren). Tests laufen auf Deutsch (`TestCulture`). Code-Kommentare Deutsch, README und `docs/` Englisch; Protokoll und Diagnosepaket bleiben deutsch (für den Support).
- **Herausgeber:** `LICENSE`, `Directory.Build.props`, MSI-`Manufacturer` und die Info-Seite nennen die Firmierung aus dem Impressum, „ICT Baden GmbH“. Im Fließtext „ICT Baden“ ohne Bindestrich; „ICT-Baden“ bleibt in technischen Kennungen. Der Registry-Schlüssel `HKLM\SOFTWARE\ICT-Baden\Zutritt` der MSI bleibt unverändert. Die Programmtexte `App_NoDataSentence` und `Info_WebsiteDescription` (`Strings*.resx`, dazu `AppInfoTests`) tragen den Bindestrich noch.

## Regeln

- Commit-Nachrichten auf Deutsch.
- Jede `.cs`-Datei beginnt mit `// SPDX-License-Identifier: MIT` (Richtlinie 06, Test `SourceHeaderTests`).
- Kein Text fest im Code: jeden neuen Text in alle Sprachdateien eintragen (en, de, es, fr, zh-Hans), Fachbegriffe nach dem internen Glossar. Unsichere Übersetzung: bestmöglich übersetzen (es/fr/zh-Hans sind ohnehin als maschinell übersetzt gekennzeichnet); `TODO-übersetzen` höchstens vorübergehend auf einem Branch – in main lässt der Test `Keine_offenen_Uebersetzungen` keine zu. Regeln in Richtlinie 13. Im Code `Strings.X` bzw. `Texts.Format(Strings.X, …)`, in XAML `{x:Static res:Strings.X}` – Platzhalter statt `+`, Mehrzahl über `Texts.Plural`.
- Vor jedem Release: Abschnitt „Unveröffentlicht“ im `CHANGELOG.md` pflegen; jeder Versionsabschnitt endet mit `### English` (Text des GitHub-Releases, `.github/scripts/release-notes.ps1`, Test `ChangelogTests`). Nach dem Tag `<Version>` auf main gleich auf die nächste Patch-Version heben.
