<#
.SYNOPSIS
    Renders the MSIX logos in src\ZapperRadio\Assets\Store from the app icon masters.
.DESCRIPTION
    Uses headless Microsoft Edge, like the .ico frames, so there is no ImageMagick dependency.
    Square44x44Logo is the icon itself, as the taskbar and Start show it: the target sizes use the same
    size-specific masters as the .ico. The tiles, the Store logo and the splash screen put the full icon
    in the middle of a transparent canvas, so Windows can show them on its own background.
.EXAMPLE
    .\design\logos\app-icon\render-store-logos.ps1
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$out = Join-Path $here '..\..\..\src\ZapperRadio\Assets\Store' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $out) {
    $out = New-Item -ItemType Directory -Force (Join-Path $here '..\..\..\src\ZapperRadio\Assets\Store')
}
$out = "$out"

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

$work = Join-Path ([IO.Path]::GetTempPath()) "zapperradio-logos-$PID"
New-Item -ItemType Directory -Force $work | Out-Null

# Renders $svg at $iconSize pixels, centered on a transparent $width x $height canvas.
function Render([string] $svg, [int] $width, [int] $height, [int] $iconSize, [string] $name) {
    $svgPath = (Join-Path $here $svg) -replace '\\', '/'
    $html = Join-Path $work "$name.html"
    @"
<!DOCTYPE html><html><head><style>
html,body{margin:0;width:${width}px;height:${height}px;background:transparent;overflow:hidden}
body{display:flex;align-items:center;justify-content:center}
img{width:${iconSize}px;height:${iconSize}px}
</style></head><body><img src="file:///$svgPath"></body></html>
"@ | Set-Content $html -Encoding utf8
    $png = Join-Path $out "$name.png"
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --default-background-color=00000000 --allow-file-access-from-files `
        "--user-data-dir=$work\profile" "--window-size=$width,$height" "--screenshot=$png" "file:///$($html -replace '\\', '/')" 2>$null | Out-Null
    if (-not (Test-Path $png)) { throw "Rendering $name failed." }
    Write-Host "$name.png ($width x $height)"
}

function MasterFor([int] $size) {
    if ($size -le 24) { 'zapperradio-icon-tiny.svg' } elseif ($size -le 32) { 'zapperradio-icon-mid.svg' } else { 'zapperradio-icon-full.svg' }
}

$scales = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }

# The app list, taskbar and title bar icon: full bleed, like the .ico.
foreach ($scale in $scales.Keys) {
    $size = [int][math]::Round(44 * $scales[$scale])
    Render (MasterFor $size) $size $size $size "Square44x44Logo.scale-$scale"
}
foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    Render (MasterFor $size) $size $size $size "Square44x44Logo.targetsize-$size"
    Copy-Item (Join-Path $out "Square44x44Logo.targetsize-$size.png") (Join-Path $out "Square44x44Logo.targetsize-${size}_altform-unplated.png")
    Copy-Item (Join-Path $out "Square44x44Logo.targetsize-$size.png") (Join-Path $out "Square44x44Logo.targetsize-${size}_altform-lightunplated.png")
}

# Tiles, Store logo and splash screen: the icon with room around it.
$canvases = @(
    @{ Name = 'SmallTile'; Width = 71; Height = 71; Icon = 0.66 },
    @{ Name = 'Square150x150Logo'; Width = 150; Height = 150; Icon = 0.56 },
    @{ Name = 'Wide310x150Logo'; Width = 310; Height = 150; Icon = 0.56 },
    @{ Name = 'LargeTile'; Width = 310; Height = 310; Icon = 0.5 },
    @{ Name = 'StoreLogo'; Width = 50; Height = 50; Icon = 1.0 },
    @{ Name = 'SplashScreen'; Width = 620; Height = 300; Icon = 0.5 }
)
foreach ($canvas in $canvases) {
    foreach ($scale in $scales.Keys) {
        $factor = $scales[$scale]
        $width = [int][math]::Round($canvas.Width * $factor)
        $height = [int][math]::Round($canvas.Height * $factor)
        $icon = [int][math]::Round([math]::Min($canvas.Width, $canvas.Height) * $canvas.Icon * $factor)
        Render (MasterFor $icon) $width $height $icon "$($canvas.Name).scale-$scale"
    }
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
