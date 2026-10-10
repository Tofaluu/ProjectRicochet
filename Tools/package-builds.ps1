<#
.SYNOPSIS
Zips Builds/Windows and Builds/Mac (each with the player README) into Builds/Ricochet-Windows.zip and
Builds/Ricochet-Mac.zip, ready to send to playtesters.

.NOTES
Uses Windows' built-in tar so zip entries use forward slashes (macOS can't unpack the backslash paths
some Windows zippers write). The Mac app loses its "executable" flag on Windows; the README tells Mac
players how to restore it.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$builds = Join-Path $root 'Builds'
$readme = Join-Path $PSScriptRoot 'PLAYERS-README.txt'
$staging = Join-Path $builds '_package'

function Package($name, $source, $copy) {
    if (-not (Test-Path $source)) { Write-Host "Skipping $name ($source not built)" -ForegroundColor Yellow; return }

    $dir = Join-Path $staging $name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    & $copy $source $dir
    Copy-Item $readme (Join-Path $dir 'README.txt')

    $zip = Join-Path $builds "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    tar -a -c -f $zip -C $staging $name
    if ($LASTEXITCODE -ne 0) { throw "Zipping $name failed" }
    Write-Host ("{0}  ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
}

# Unity puts debug-only folders next to the Windows player; they must not ship.
Package 'Ricochet-Windows' (Join-Path $builds 'Windows') {
    param($src, $dst)
    robocopy $src $dst /E /NFL /NDL /NJH /NJS /NP /XD '*_BackUpThisFolder_ButDontShipItWithYourGame' '*_BurstDebugInformation_DoNotShip' | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the Windows build failed" }
}

Package 'Ricochet-Mac' (Join-Path $builds 'Mac\Ricochet.app') {
    param($src, $dst)
    Copy-Item $src $dst -Recurse
}

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
