<#
.SYNOPSIS
    Schreibt den Text eines Tag-Releases: den englischen Teil des Versionsabschnitts aus CHANGELOG.md und die Dateiliste.

.DESCRIPTION
    Statt „--generate-notes“: Das listete deutsche PR-Titel, nannte Beitragende als „New Contributor“, die längst
    mitarbeiten, und verglich mit dem rollierenden Tag „latest“ (leerer Vergleich, wenn beide auf denselben Commit zeigen).
    CHANGELOG.md bleibt deutsch (nach der Vorlage der internen Richtlinien), Release-Texte sind englisch: Jeder Versionsabschnitt
    endet deshalb mit „### English“, und das ist der Release-Text. Fehlt er oder ist er leer, bricht das Skript ab – die CI
    ruft es vor dem Bauen auf, damit kein Release ohne Text entsteht. Der Test „Jede_Version_hat_einen_englischen_
    Release_Text“ prüft dasselbe schon im Pull Request.

.EXAMPLE
    ./.github/scripts/release-notes.ps1 -Version 0.1.1 > notes.md
#>
# SPDX-License-Identifier: MIT
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Changelog = (Join-Path $PSScriptRoot '../../CHANGELOG.md'),
    [string] $App = 'Zutritt',
    [string] $Repository = 'ict-baden/zutritt',
    [switch] $Signed
)
$ErrorActionPreference = 'Stop'

$lines = @(Get-Content -LiteralPath $Changelog -Encoding utf8)

# Abschnitt „## [0.1.1] – 2026-10-21“ bis zum nächsten „## “; -1, wenn es ihn nicht gibt
function Find-Section([string] $heading) {
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^## \[$([regex]::Escape($heading))\]") { return $i }
    }
    return -1
}

# Vorabversion (v1.0.0-rc1): eigener Abschnitt, sonst der der Version selbst, sonst ein kurzer Hinweis – ein Testlauf
# soll nicht an einem fehlenden Text scheitern. Eine richtige Version braucht ihren Abschnitt.
$isPrerelease = $Version -match '-'
$start = Find-Section $Version
if ($start -lt 0 -and $isPrerelease) { $start = Find-Section ($Version -replace '-.*$', '') }

if ($start -lt 0 -and $isPrerelease) {
    $body = "Pre-release of $App $Version for testing before the final version – not for production. " +
            "Changes so far: section *Unveröffentlicht* in CHANGELOG.md (German)."
}
else {
    if ($start -lt 0) { throw "CHANGELOG.md hat keinen Abschnitt '## [$Version]'." }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^## ') { $end = $i; break }
    }
    $english = -1
    for ($i = $start + 1; $i -lt $end; $i++) {
        if ($lines[$i] -match '^### English\s*$') { $english = $i; break }
    }
    if ($english -lt 0) { throw "Im CHANGELOG-Abschnitt $Version fehlt '### English' (der englische Release-Text)." }
    # Der englische Teil reicht bis zur nächsten Überschrift „### “ (eigene Unterüberschriften: „#### Changed“)
    for ($i = $english + 1; $i -lt $end; $i++) {
        if ($lines[$i] -match '^### ') { $end = $i; break }
    }
    # Ohne HTML-Kommentare (z. B. der Hinweis auf die Abschnitte am Ende der Datei, auch über mehrere Zeilen) und
    # Leerzeilen am Rand. Den Bereich nur, wenn nach der Überschrift noch Zeilen kommen – 5..4 zählte rückwärts.
    $body = if ($english + 1 -lt $end) { $lines[($english + 1)..($end - 1)] -join "`n" } else { '' }
    $body = ($body -replace '(?s)<!--.*?-->', '').Trim()
    if (-not $body) { throw "Der Abschnitt '### English' von $Version ist leer." }
}

$tag = "v$Version"
$blob = "https://github.com/$Repository/blob/$tag"
$check = if ($Signed) { 'The programs and the MSI are code-signed.' }
         else { "The files are not code-signed yet – check them against SHA256SUMS.txt ([how]($blob/docs/installation.md#verifying-the-download))." }
$notes = @"
$body

#### Downloads

- **$App-$Version-x64.msi** – installer for all users of a PC, also through Intune or Group Policy; includes the .NET runtime
- **$App.exe** – a single file that runs without installation and without a .NET runtime
- **$App-$Version-win-x64-framework.zip** – small variant for PCs with the .NET 10 Desktop Runtime
- **SHA256SUMS.txt** – SHA-256 checksums of all files
- **$App-$Version-sbom.cdx.json** – bill of materials (CycloneDX)

$check All changes: [CHANGELOG.md]($blob/CHANGELOG.md) (German).
"@
# Here-Strings übernehmen die Zeilenenden der Datei (*.ps1 wird mit CRLF ausgecheckt): einheitlich LF
$notes -replace "`r`n", "`n"
