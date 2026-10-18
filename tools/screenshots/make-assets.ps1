<#
.SYNOPSIS
    Erzeugt alle Bilder der Doku neu: Screenshots der echten App, Beispielseiten des PDF-Berichts und das Banner.

.DESCRIPTION
    1. Baut Zutritt (Release) und rendert mit „Zutritt.exe --screenshots“ jede Seite hell und dunkel
       (Demo-Daten, geht auch ohne sichtbaren Desktop).
    2. Schreibt mit dem versteckten Schalter „--demo-bericht“ einen Demo-Bericht mit Vergleich und rendert
       ausgewählte Seiten mit pypdfium2 (pip install pypdfium2 pillow; freizügige Lizenzen, kein PyMuPDF/AGPL).
    3. Rendert banner.html mit Chrome über playwright-core (npm install in diesem Ordner).

.EXAMPLE
    cd tools\screenshots; pip install pypdfium2 pillow; npm install; .\make-assets.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$assets = Join-Path $repo 'docs\assets'
$shots = Join-Path $assets 'screenshots'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("zutritt-assets-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $shots, $tmp | Out-Null

try {
    dotnet build (Join-Path $repo 'src\Zutritt\Zutritt.csproj') -c Release -nologo -v q
    $exe = Join-Path $repo 'src\Zutritt\bin\Release\net10.0-windows10.0.19041.0\Zutritt.exe'

    # 1. Screenshots – Dateinamen der App (deutsch) auf englische Doku-Namen abbilden
    & $exe --screenshots $tmp | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Screenshots fehlgeschlagen ($LASTEXITCODE)" }
    $names = @{ uebersicht = 'overview'; befunde = 'findings'; ordner = 'folders'; 'ordner-verschoben' = 'folders-moved';
                personen = 'people'; vergleich = 'comparison'; bericht = 'report'; info = 'about' }
    foreach ($file in Get-ChildItem $tmp -Filter *.png) {
        if ($file.BaseName -notmatch '^(?<page>.+)-(?<theme>hell|dunkel)$') { continue }
        $page = $names[$Matches.page]; if (-not $page) { continue }
        $theme = if ($Matches.theme -eq 'hell') { 'light' } else { 'dark' }
        Copy-Item $file.FullName (Join-Path $shots "$page-$theme.png") -Force
    }

    # 2. Beispielseiten des PDF-Berichts (Deckblatt, Zusammenfassung, Befunde, Abzeichnen)
    $pdf = Join-Path $tmp 'demo.pdf'
    & $exe --demo-bericht --vergleich --bericht $pdf | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Demo-Bericht fehlgeschlagen ($LASTEXITCODE)" }
    python (Join-Path $PSScriptRoot 'report-pages.py') $pdf $assets
    if ($LASTEXITCODE -ne 0) { throw "Berichtseiten fehlgeschlagen ($LASTEXITCODE)" }

    # 3. Banner (nutzt folders-light.png aus Schritt 1)
    node (Join-Path $PSScriptRoot 'banner.js')
    if ($LASTEXITCODE -ne 0) { throw "Banner fehlgeschlagen ($LASTEXITCODE)" }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
