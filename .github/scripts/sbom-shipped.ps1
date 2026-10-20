<#
.SYNOPSIS
    Gleicht die CycloneDX-Stückliste an das an, was die Pakete wirklich enthalten (richtlinien/06).

.DESCRIPTION
    CycloneDX liest die Paketgraphen aus NuGet und nennt dabei auch Pakete, die NuGet für net10.0-windows weglässt
    („pruning“), weil die .NET- oder Windows-Desktop-Runtime sie schon enthält – etwa System.IO.Packaging 8.0.1 oder
    System.DirectoryServices 10.0.x. Ausgeliefert werden diese Versionen nie; ein Schwachstellen-Scan der Stückliste
    prüfte die falschen. Maßgeblich ist die deps.json des Publish (dieselbe Quelle wie der Test
    ThirdPartyNoticesTests):
    1. Komponenten, die dort nicht als Paket stehen, fliegen raus (samt ihrer Kanten in „dependencies“).
    2. Fehlt ein ausgeliefertes Paket in der Stückliste, bricht das Skript ab – lieber kein Release als eine
       unvollständige Stückliste.
    3. Die Runtime-Pakete (.NET, Windows Desktop) kommen mit ihrer echten Version dazu. Ein unbekanntes Runtime-Paket
       (etwa wieder die Windows-SDK-Projektion) bricht das Skript ab.

.PARAMETER Sbom
    Die von dotnet-CycloneDX erzeugte JSON-Datei; wird überschrieben.

.PARAMETER DepsJson
    deps.json eines self-contained Publish (enthält Pakete und Runtime-Pakete).

.EXAMPLE
    ./.github/scripts/sbom-shipped.ps1 -Sbom dist/Zutritt-sbom.cdx.json -DepsJson src/Zutritt/obj/Release/net10.0-windows/win-x64/Zutritt.deps.json
#>
param(
    [Parameter(Mandatory)] [string] $Sbom,
    [Parameter(Mandatory)] [string] $DepsJson
)
$ErrorActionPreference = 'Stop'

$deps = Get-Content $DepsJson -Raw | ConvertFrom-Json
$libraries = @($deps.libraries.PSObject.Properties | ForEach-Object {
    $id, $version = $_.Name -split '/', 2
    # Runtime-Pakete heißen in der deps.json „runtimepack.<Paket-Id>“
    [pscustomobject]@{ Id = $id -replace '^runtimepack\.', ''; Version = $version; Type = $_.Value.type }
})
$packages = @($libraries | Where-Object Type -eq 'package')
$runtimePacks = @($libraries | Where-Object Type -eq 'runtimepack')
if ($packages.Count -eq 0) { throw "$DepsJson nennt keine Pakete." }
if (-not ($runtimePacks | Where-Object Id -like 'Microsoft.NETCore.App.Runtime.*')) {
    throw "$DepsJson stammt nicht aus einem self-contained Publish (keine .NET-Runtime darin)."
}

function PackageUrl([string] $id, [string] $version) { "pkg:nuget/$id@$version" }
$shipped = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($p in $packages) { [void]$shipped.Add((PackageUrl $p.Id $p.Version)) }

$bom = Get-Content $Sbom -Raw | ConvertFrom-Json -Depth 100
$all = @($bom.components)
$kept = @($all | Where-Object { $shipped.Contains($_.purl) })
$dropped = @($all | Where-Object { -not $shipped.Contains($_.purl) })
$missing = @($shipped | Where-Object { $url = $_; -not ($kept | Where-Object purl -eq $url) })
if ($missing.Count -gt 0) { throw "In der Stückliste fehlen ausgelieferte Pakete: $($missing -join ', ')" }

# Runtime-Pakete: welche Dateien in welchem Paket stecken, steht in THIRD-PARTY-NOTICES.txt (Eintrag 13)
$runtimeInfo = @{
    'Microsoft.NETCore.App.Runtime'      = @{ Text = '.NET runtime, only in the self-contained packages (MSI and single-file exe)'; Licence = @{ id = 'MIT' } }
    'Microsoft.WindowsDesktop.App.Runtime' = @{ Text = 'Windows Desktop runtime (WPF), only in the self-contained packages (MSI and single-file exe)'; Licence = @{ id = 'MIT' } }
}
$added = foreach ($r in $runtimePacks) {
    $key = $runtimeInfo.Keys | Where-Object { $r.Id -eq $_ -or $r.Id -like "$_.*" } | Select-Object -First 1
    if (-not $key) { throw "Unbekanntes Runtime-Paket $($r.Id) – in sbom-shipped.ps1 und THIRD-PARTY-NOTICES.txt eintragen." }
    $url = PackageUrl $r.Id $r.Version
    [pscustomobject][ordered]@{
        type        = 'framework'
        'bom-ref'   = $url
        publisher   = 'Microsoft'
        name        = $r.Id
        version     = $r.Version
        description = $runtimeInfo[$key].Text
        scope       = 'required'
        licenses    = @(@{ license = $runtimeInfo[$key].Licence })
        purl        = $url
    }
}

$droppedRefs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($c in $dropped) { [void]$droppedRefs.Add($c.'bom-ref') }
$root = $bom.metadata.component.'bom-ref'
$dependencies = foreach ($d in @($bom.dependencies)) {
    if ($droppedRefs.Contains($d.ref)) { continue }
    $on = @($d.dependsOn | Where-Object { -not $droppedRefs.Contains($_) })
    if ($d.ref -eq $root) { $on += @($added | ForEach-Object { $_.'bom-ref' }) }
    [pscustomobject][ordered]@{ ref = $d.ref; dependsOn = $on }
}
$dependencies = @($dependencies) + @($added | ForEach-Object { [pscustomobject][ordered]@{ ref = $_.'bom-ref'; dependsOn = @() } })

$bom.components = @($kept) + @($added)
$bom.dependencies = $dependencies
[IO.File]::WriteAllText((Resolve-Path $Sbom), ($bom | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

Write-Host "Stückliste: $($kept.Count) Pakete, $(@($added).Count) Runtime-Pakete"
if ($dropped.Count -gt 0) {
    Write-Host "Entfernt (nicht ausgeliefert, kommen aus der Runtime): $(($dropped | ForEach-Object { "$($_.name) $($_.version)" }) -join ', ')"
}
