<#
.SYNOPSIS
    Builds the ZapperRadio installer and the packages the installed app updates itself from.
.DESCRIPTION
    Publishes the app self-contained (no .NET or Windows App SDK install needed on the target PC)
    and packs it with Velopack into artifacts\velopack. That folder gets, per architecture:
      - ZapperRadio-<arch>-Setup.exe, the installer people download;
      - a full package (and, when the earlier releases are in the folder, a delta package) plus the
        releases.<channel>.json feed that the installed app reads to find updates.
    Needs the vpk tool, in the version of the Velopack package in ZapperRadio.csproj:
        dotnet tool install -g vpk --version 1.2.0
.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.1.0 -Arch arm64
#>
param(
    [string] $Version = '1.1.2',
    [ValidateSet('x64', 'arm64')]
    [string] $Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\$Arch\"
$outputDir = Join-Path $root 'artifacts\velopack'

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk not found. Install it with: dotnet tool install -g vpk --version 1.2.0'
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "Publishing ZapperRadio $Version ($Arch)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\ZapperRadio\ZapperRadio.csproj') `
    -c Release -r "win-$Arch" --self-contained true `
    -p:Platform=$Arch -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# The pack id names the folder the app is installed in (%LOCALAPPDATA%\ZapperRadioApp). It is not ZapperRadio,
# because uninstalling deletes that whole folder and %LOCALAPPDATA%\ZapperRadio holds the favorites and settings.
# Each architecture has its own channel: an installed app only follows the releases of the channel it came from.
Write-Host 'Packing with Velopack...' -ForegroundColor Cyan
vpk pack `
    --packId ZapperRadioApp `
    --packTitle ZapperRadio `
    --packAuthors 'William Veldhuizen' `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ZapperRadio.exe `
    --icon (Join-Path $root 'src\ZapperRadio\Assets\ZapperRadio.ico') `
    --runtime "win-$Arch" `
    --channel "win-$Arch" `
    --shortcuts StartMenuRoot `
    --outputDir $outputDir
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

$setup = Get-ChildItem $outputDir -Filter "*-win-$Arch-Setup.exe" | Select-Object -First 1
Write-Host "Installer: $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 1)) MB)" -ForegroundColor Green
