<#
.SYNOPSIS
    Builds the Microsoft Store MSIX of ZapperRadio for x64, signs it with a local test certificate and installs it.
.DESCRIPTION
    For trying the Store version on this PC before it goes to the Store. The Store signs the real package itself;
    this script stands in for that with a self-signed certificate whose subject is exactly the Publisher in
    Package.appxmanifest, which Windows requires for the signature to match the package.

    The first run creates that certificate in CurrentUser\My and trusts it in LocalMachine\TrustedPeople,
    which asks for administrator rights once. Later runs reuse it.

    The package is built with Visual Studio's MSBuild, found with vswhere, into artifacts\msix-test.
    Installing over an earlier test install keeps its data. -Remove uninstalls the test package, which deletes
    the data the Store version stored, and removes the test certificate again.
.EXAMPLE
    .\scripts\test-msix.ps1
    .\scripts\test-msix.ps1 -Version 1.30.0
    .\scripts\test-msix.ps1 -Remove
#>
param(
    # The app version; defaults to the one in ZapperRadio.csproj. The package version is this with a fourth part of 0.
    [string] $Version,
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\ZapperRadio\ZapperRadio.csproj'
$outputDir = Join-Path $root 'artifacts\msix-test'

# Must be exactly the Publisher of the Identity in Package.appxmanifest.
$publisher = 'CN=3D06A4D8-69AB-4D79-ABEE-8C65C4A034AA'
$packageName = '60526williamve.ZapperRadio'
$friendlyName = 'ZapperRadio MSIX test signing'

function Find-TestCertificate {
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq $friendlyName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

# Runs a script block in an elevated PowerShell, for the steps that touch the machine's certificate store.
function Invoke-Elevated([string] $command) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        & ([scriptblock]::Create($command))
        return
    }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("`$ErrorActionPreference = 'Stop'; $command"))
    $process = Start-Process powershell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-EncodedCommand', $encoded
    if ($process.ExitCode -ne 0) { throw 'The elevated step failed or was cancelled.' }
}

if ($Remove) {
    $installed = Get-AppxPackage -Name $packageName
    if ($installed) {
        Write-Host "Uninstalling $($installed.PackageFullName)..." -ForegroundColor Cyan
        $installed | Remove-AppxPackage
    }
    $certificate = Find-TestCertificate
    if ($certificate) {
        Write-Host 'Removing the test certificate...' -ForegroundColor Cyan
        $thumbprint = $certificate.Thumbprint
        Invoke-Elevated "Remove-Item Cert:\LocalMachine\TrustedPeople\$thumbprint -ErrorAction SilentlyContinue"
        Remove-Item "Cert:\CurrentUser\My\$thumbprint"
    }
    Write-Host 'Done.' -ForegroundColor Green
    return
}

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not of the form major.minor.patch." }

# 1. The test certificate, trusted by this PC.
$certificate = Find-TestCertificate
if (-not $certificate) {
    Write-Host "Creating a self-signed certificate for $publisher..." -ForegroundColor Cyan
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher -FriendlyName $friendlyName `
        -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)")) {
    Write-Host 'Trusting the certificate in Trusted People (asks for administrator rights)...' -ForegroundColor Cyan
    $cer = Join-Path ([IO.Path]::GetTempPath()) "zapperradio-msix-test-$($certificate.Thumbprint).cer"
    Export-Certificate -Cert $certificate -FilePath $cer | Out-Null
    try {
        Invoke-Elevated "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    }
    finally {
        Remove-Item $cer -ErrorAction SilentlyContinue
    }
}

# 2. The package, built the way the Store build is, but for sideloading.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = if (Test-Path $vswhere) {
    # A release of Visual Studio first, a preview only when there is nothing else.
    @(& $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe') +
    @(& $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe') |
        Where-Object { $_ } | Select-Object -First 1
}
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio (Community is fine) with the .NET desktop workload.' }

if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
Write-Host "Building the MSIX of ZapperRadio $Version (x64)..." -ForegroundColor Cyan
& $msbuild $project -restore -nologo -verbosity:minimal `
    -p:Configuration=Release -p:Platform=x64 -p:StoreMsix=true -p:Version=$Version `
    -p:UapAppxPackageBuildMode=SideloadOnly "-p:AppxPackageDir=$outputDir\"
if ($LASTEXITCODE -ne 0) { throw 'The MSIX build failed.' }

$msix = Get-ChildItem $outputDir -Recurse -Filter '*.msix' | Select-Object -First 1
if (-not $msix) { throw "No .msix found in $outputDir." }

# 3. Signed with the test certificate, by the signtool the build already restored.
$signtool = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signtool) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}
if (-not $signtool) { throw 'signtool.exe not found.' }

Write-Host 'Signing...' -ForegroundColor Cyan
& $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $msix.FullName
if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }

# 4. Installed, over an earlier test install if there is one.
Write-Host 'Installing...' -ForegroundColor Cyan
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion

Write-Host "Installed $($msix.Name). Start ZapperRadio from the Start menu; .\scripts\test-msix.ps1 -Remove uninstalls it." -ForegroundColor Green
