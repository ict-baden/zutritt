<#
.SYNOPSIS
    Prüft die Syntax aller PowerShell-Skripte und aller PowerShell-Schritte der Workflows.
.DESCRIPTION
    Eingabe: JSON aus ps-syntax.py (Name und Code je Schritt). ${{ … }}-Ausdrücke werden vor dem Parsen ersetzt.
    Hintergrund: Schritte, die nur bei Tags laufen (Release), brachen zweimal an einem ParserError ab – PowerShell liest
    die typografischen Anführungszeichen U+201C, U+201D, U+201E (und U+2018, U+2019, U+201A, U+201B) als Begrenzer. Deshalb
    zusätzlich: Ein String, den solch ein Zeichen begrenzt, ist ein Fehler, auch wenn er sich parsen lässt.
    Exitcode = Anzahl der Fehler (höchstens 255).
#>
# SPDX-License-Identifier: MIT
param([Parameter(Mandatory)][string] $Blocks, [string[]] $Root = @('.github', 'tools'))

$fail = 0
$typographic = [char[]]@(0x201C, 0x201D, 0x201E, 0x2018, 0x2019, 0x201A, 0x201B)
function Test-Code($tokens, $errors, [string] $where) {
    foreach ($e in $errors) { $script:fail++; Write-Host "::error::$where, Zeile $($e.Extent.StartLineNumber): $($e.Message)" }
    foreach ($t in $tokens) {
        if ($t.Kind -notin 'StringLiteral', 'StringExpandable', 'HereStringLiteral', 'HereStringExpandable') { continue }
        $text = $t.Extent.Text
        if ($text.Length -gt 0 -and ($typographic -contains $text[0] -or $typographic -contains $text[-1])) {
            $script:fail++
            Write-Host "::error::$where, Zeile $($t.Extent.StartLineNumber): typografisches Anführungszeichen begrenzt den String $text"
        }
    }
}

$items = @(Get-Content $Blocks -Raw | ConvertFrom-Json)
foreach ($b in $items) {
    $code = $b.code -replace '\$\{\{.*?\}\}', 'X' # auch mit } im Ausdruck, z. B. format('{0}')
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    Test-Code $tokens $errors $b.name
}
$files = @($Root | Where-Object { Test-Path $_ } | ForEach-Object { Get-ChildItem -Recurse -Path $_ -Filter *.ps1 } |
           Where-Object { $_.FullName -notmatch '[\\/](node_modules|bin|obj)[\\/]' })
foreach ($f in $files) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors)
    Test-Code $tokens $errors $f.FullName
}
Write-Host "PowerShell-Syntax: $($items.Count) Workflow-Schritte, $($files.Count) Skripte, $fail Fehler"
exit [Math]::Min($fail, 255)
