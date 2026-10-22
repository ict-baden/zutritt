<#
.SYNOPSIS
    Prüft auf dem Windows-Runner, dass ein Update mit der neuen MSI alle Programmdateien in der neuen Version hinterlässt.

.DESCRIPTION
    Hintergrund (Befund 2026-10-21, zuerst bei Vitrine): Tag-Builds trugen die Dateiversion x.y.z.0, CI-Builds
    x.y.z.<Lauf>. Windows Installer entscheidet beim Costing – vor dem Entfernen der alten Version durch
    RemoveExistingProducts –, ob eine Datei kopiert wird; war die installierte neuer, kopierte er die neue nicht und
    entfernte danach die alte. Nach einem Update vom CI-Build „latest“ auf das Tag-Release fehlte Zutritt.exe.
    Abhilfe: Dateiversion immer mit Lauf-Nummer (build.yml) und REINSTALLMODE=dmus (installer/Package.wxs).

    Je Ausgangslage: alte MSI still installieren, neue MSI darüber, dann prüfen –
    - jede Datei des Publish-Ordners liegt im Programmordner,
    - Zutritt.exe hat die Dateiversion des neuen Pakets,
    - „Zutritt.exe --version“ startet und nennt die neue Version –,
    danach die neue MSI deinstallieren und nach Resten suchen. Ausgangslagen:
    1. das rollierende Release „latest“ (CI-Build von main),
    2. das neueste Tag-Release (ohne das gerade gebaute Tag),
    3. -HigherFileVersionMsi: dieselbe MSI-Version, aber eine Exe mit höherer Dateiversion (wie ein späterer CI-Build
       derselben Basisversion) – prüft REINSTALLMODE=dmus.
    Ein veröffentlichtes Paket mit höherer Version als das neue (Downgrade, z. B. ein Pull Request von einem alten Stand,
    oder 'latest' wurde gerade durch einen neueren main-Build ersetzt) wird übersprungen – maßgeblich ist die
    ProductVersion der heruntergeladenen MSI selbst, nicht der Titel des Releases. Ein Release, das es nicht gibt, ebenso.
    Andere Fehler von gh (Netz, Anmeldung, 5xx) brechen ab, damit keine Ausgangslage still entfällt.
    Braucht GH_TOKEN und GITHUB_REPOSITORY; jeder msiexec-Lauf schreibt ein ausführliches Protokoll nach -LogDir.

.EXAMPLE
    ./.github/scripts/msi-update-test.ps1 -Msi dist/Zutritt-x64.msi -PublishDir out/self-contained -Version 0.1.2-ci.57 -FileVersion 0.1.2.57 -HigherFileVersionMsi out/msi-highver/Zutritt.msi
#>
# SPDX-License-Identifier: MIT
param(
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $FileVersion,
    [string] $HigherFileVersionMsi,
    [string] $App = 'Zutritt',
    [string] $LogDir = 'msi-logs'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false # Exit-Codes von gh werden selbst geprüft

$repo = $env:GITHUB_REPOSITORY
if (-not $repo -or -not $env:GH_TOKEN) { throw 'GITHUB_REPOSITORY und GH_TOKEN müssen gesetzt sein.' }
$Msi = (Resolve-Path $Msi).Path
$PublishDir = (Resolve-Path $PublishDir).Path
$logs = (New-Item -ItemType Directory -Force $LogDir).FullName
$installDir = Join-Path $env:ProgramFiles $App
$exe = Join-Path $installDir "$App.exe"
$shortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\$App.lnk"
$productKey = "HKLM:\SOFTWARE\ICT-Baden\$App"
$core = [version]($Version -replace '[-+].*$', '')
$expected = @(Get-ChildItem $PublishDir -Recurse -File | Where-Object Extension -ne '.pdb' |
              ForEach-Object { [IO.Path]::GetRelativePath($PublishDir, $_.FullName) })
if ($expected.Count -eq 0) { throw "Publish-Ordner $PublishDir ist leer." }

function Invoke-Msiexec([string[]] $arguments, [string] $log) {
    $p = Start-Process msiexec.exe -ArgumentList ($arguments + @('/qn', '/norestart', '/l*v', "`"$log`"")) -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(480000)) { $p.Kill($true); throw "msiexec $($arguments -join ' ') hängt seit 8 Minuten (Protokoll: $log)" }
    if ($p.ExitCode -notin 0, 3010) { throw "msiexec $($arguments -join ' ') endete mit $($p.ExitCode) (Protokoll: $log)" }
}

# Lädt die MSI eines Releases; $null, wenn es das Release oder die Datei nicht gibt
function Get-ReleaseMsi([string] $tag, [string] $pattern) {
    $dir = Join-Path $env:RUNNER_TEMP "msi-$tag"
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    $out = & gh release download $tag -R $repo -p $pattern -D $dir 2>&1
    if ($LASTEXITCODE -ne 0) {
        if (($out -join "`n") -match 'release not found|no assets match|no assets to download|HTTP 404') { return $null }
        throw "gh release download $tag ist fehlgeschlagen: $($out -join ' ')"
    }
    @(Get-ChildItem $dir -Filter *.msi)[0].FullName
}

# ProductVersion aus der MSI selbst (Windows Installer per COM; PowerShell braucht dafür InvokeMember)
function Get-MsiVersion([string] $path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($path, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property = 'ProductVersion'"))
    [void]$view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $value = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    [void]$view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
    # Die Datenbank freigeben, bevor msiexec die Datei öffnet
    foreach ($o in $record, $view, $db, $installer) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o) }
    $value
}

# gh-Abfrage, bei der „gibt es nicht“ kein Fehler ist ($null); alles andere bricht ab
function Find-Gh {
    $out = & gh @args 2>&1
    if ($LASTEXITCODE -eq 0) { return @($out | Where-Object { $_ -isnot [Management.Automation.ErrorRecord] }) }
    if (($out -join "`n") -match 'release not found|HTTP 404') { return $null }
    throw "gh $($args -join ' ') ist fehlgeschlagen: $($out -join ' ')"
}

# Altes Paket herunterladen und prüfen, ob die neue MSI darüber ein Update ist (nicht neuer als die neue)
function Test-UpdateFrom([string] $name, [string] $tag, [string] $pattern) {
    $old = Get-ReleaseMsi $tag $pattern
    if (-not $old) { Write-Host "::notice::$name hat keine MSI ($pattern) – übersprungen"; return 0 }
    $oldVersion = Get-MsiVersion $old
    $oldCore = [version](($oldVersion -split '\.')[0..2] -join '.')
    if ($oldCore -gt $core) { Write-Host "::notice::$name ($oldVersion) ist neuer als $core – kein Update, übersprungen"; return 0 }
    Test-Update "$name ($oldVersion)" $old | Out-Null
    return 1
}

function Test-Update([string] $name, [string] $oldMsi) {
    Write-Host "== Update von $name"
    $slug = $name -replace '[^A-Za-z0-9.]+', '-'
    Invoke-Msiexec @('/i', "`"$oldMsi`"") (Join-Path $logs "update-$slug-alt.log")
    if (-not (Test-Path $exe)) { throw "$name`: $App.exe fehlt schon nach der Installation der alten MSI" }
    Write-Host "   alt: $((Get-Item $exe).VersionInfo.FileVersion)"
    Invoke-Msiexec @('/i', "`"$Msi`"") (Join-Path $logs "update-$slug-neu.log")

    $missing = @($expected | Where-Object { -not (Test-Path (Join-Path $installDir $_)) })
    if ($missing.Count -gt 0) { throw "$name`: nach dem Update fehlen $($missing.Count) Dateien, z. B. $($missing[0..([Math]::Min(4, $missing.Count - 1))] -join ', ')" }
    $actual = (Get-Item $exe).VersionInfo.FileVersion
    if ($actual -ne $FileVersion) { throw "$name`: $App.exe hat nach dem Update die Dateiversion $actual statt $FileVersion" }
    # Wie in der MSI-Probe: Zutritt.exe ist eine Fensteranwendung, die Ausgabe kommt über umgeleitete Handles
    $psi = [Diagnostics.ProcessStartInfo]::new($exe)
    $psi.ArgumentList.Add('--version')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEndAsync()
    $stderr = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(120000)) { $p.Kill($true); throw "$name`: '$App.exe --version' hängt seit 2 Minuten" }
    $text = ($stdout.Result + $stderr.Result).Trim()
    if ($p.ExitCode -ne 0 -or $text -notmatch [regex]::Escape($Version)) { throw "$name`: '$App.exe --version' nach dem Update: $text (Exit $($p.ExitCode))" }
    Write-Host "   neu: $actual, $($expected.Count) Dateien, $text"

    Invoke-Msiexec @('/x', "`"$Msi`"") (Join-Path $logs "update-$slug-entfernen.log")
    $left = @($installDir, $shortcut, $productKey) | Where-Object { Test-Path $_ }
    if ($left.Count -gt 0) { throw "$name`: Reste nach der Deinstallation: $($left -join ', ')" }
}

if (Test-Path $exe) { throw "$App ist schon installiert – der Test braucht einen Runner ohne $App." }
$ran = 0

# 1. latest: CI-Build von main
if (Find-Gh release view latest -R $repo --json tagName -q .tagName) { $ran += Test-UpdateFrom 'latest' 'latest' "$App-x64.msi" }
else { Write-Host '::notice::Kein Release latest – Update von latest übersprungen' }

# 2. Neuestes Tag-Release (nicht das gerade gebaute)
$tags = @(Find-Gh release list -R $repo --exclude-drafts --exclude-pre-releases --limit 50 --json tagName -q '.[].tagName' |
          Where-Object { $_ -match '^v\d+\.\d+\.\d+$' -and $_ -ne $env:GITHUB_REF_NAME } |
          Sort-Object { [version]$_.Substring(1) } -Descending)
if ($tags.Count -gt 0) { $ran += Test-UpdateFrom "Tag-Release $($tags[0])" $tags[0] "$App-*-x64.msi" }
else { Write-Host '::notice::Noch kein Tag-Release – Update davon übersprungen' }

# 3. Dieselbe Version mit höherer Dateiversion der Exe: muss trotzdem ersetzt werden (REINSTALLMODE=dmus)
if ($HigherFileVersionMsi) {
    Test-Update 'Exe mit höherer Dateiversion' (Resolve-Path $HigherFileVersionMsi).Path | Out-Null
    $ran++
}

Write-Host "Update-Probe bestanden: $ran Ausgangslagen, jeweils alle Dateien und $App.exe $FileVersion."
$global:LASTEXITCODE = 0
