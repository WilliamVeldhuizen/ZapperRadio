<#
Puts the newest station list from https://zapperradio.com/stations/ into src\ZapperRadio\Assets\Stations, so the
release packages carry it: a fresh install then has its stations at once, also offline, and a list that cannot be
downloaded never leaves the app empty. Run by build-installer.ps1 and by the Store MSIX workflow before they build;
a build without it simply ships no list.

  .\scripts\fetch-station-list.ps1
#>
param(
    [string] $Destination = (Join-Path $PSScriptRoot '..\src\ZapperRadio\Assets\Stations')
)

$ErrorActionPreference = 'Stop'
$indexUri = 'https://zapperradio.com/stations/'

$index = (Invoke-WebRequest $indexUri -UseBasicParsing).Content
$name = [regex]::Match($index, 'stations-\d{4}-\d{2}-\d{2}\.txt').Value
if (-not $name) {
    throw "No station list is linked from $indexUri."
}

New-Item -ItemType Directory -Force $Destination | Out-Null
Get-ChildItem $Destination -Filter 'stations-*' | Remove-Item -Force
$target = Join-Path $Destination $name
Invoke-WebRequest "$indexUri$name" -OutFile $target -UseBasicParsing

# The same check the list builder makes: a partial list is worse than an older one.
$lines = [System.IO.File]::ReadAllLines($target).Count
if ($lines -lt 20000) {
    Remove-Item $target
    throw "$name has only $lines lines; not shipping it."
}

Write-Host "Station list $name ($($lines - 1) stations) is in $Destination" -ForegroundColor Cyan
