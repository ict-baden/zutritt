# Mitmachen / Contributing

Danke für Ihr Interesse! Thank you for your interest!

## Vor größeren Änderungen / Before larger changes

Bitte erst ein Issue anlegen und den Plan kurz beschreiben. So vermeiden wir doppelte Arbeit.
Please open an issue first and describe your plan.

## Pull Requests

- Ein Thema pro Pull Request, mit Tests.
- `dotnet build` und `dotnet test` müssen grün sein. Viele Tests legen echte Freigaben und lokale Konten an (`[AdminFact]`) und werden ohne Administratorrechte übersprungen – `dotnet test Zutritt.sln` deshalb in einem Terminal mit Administratorrechten ausführen. Die CI führt sie aus.
- Keine neuen Abhängigkeiten ohne Rücksprache. Nur freizügige Lizenzen (MIT, BSD, Apache-2.0, ISC), keine GPL/AGPL. Jede neue oder aktualisierte Abhängigkeit gehört mit Version, Quelle, Copyright und Lizenztext in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
- Keine Passwörter, internen Hostnamen oder Kundendaten in Code, Tests oder Screenshots. Demo-Daten und Screenshots ohne echte Personennamen.
- Bilder der Doku nicht von Hand bearbeiten, sondern mit `tools/screenshots/make-assets.ps1` neu erzeugen.

## Developer Certificate of Origin (DCO)

Mit jedem Commit bestätigen Sie, dass Sie den Beitrag unter der Lizenz dieses Projekts (MIT) einbringen dürfen ([developercertificate.org](https://developercertificate.org/)). Dafür jeden Commit mit `-s` erstellen:

By contributing you certify the [DCO](https://developercertificate.org/). Sign off every commit:

```
git commit -s -m "Fehler beim Lesen leerer Dateien behoben"
```

Das fügt `Signed-off-by: Name <E-Mail>` hinzu.
