# Renders the logos of the made-up stations of the demo mode (ZapperRadio.exe --demo) into src\ZapperRadio\Assets\Demo.
# The stations do not exist, so the Store screenshots show the real app without anyone else's brand.
# Run it again after changing a logo here: .\render-demo-logos.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\..\..\src\ZapperRadio\Assets\Demo'
New-Item -ItemType Directory -Force $out | Out-Null
$size = 256

# Name of the file, the two background colors from top to bottom, the text color, the word(s) and the emblem.
$logos = @(
    @{ File = 'nightwave'; From = '#1B1446'; To = '#3A2A8C'; Ink = '#F4F1FF'; Text = 'NIGHT', 'WAVE'; Emblem = 'moon' }
    @{ File = 'sunrise'; From = '#FF7A2F'; To = '#FFC23D'; Ink = '#FFFFFF'; Text = 'SUNRISE', 'FM'; Emblem = 'sun' }
    @{ File = 'meridian'; From = '#1F3B57'; To = '#2F5F86'; Ink = '#FFFFFF'; Text = 'MERIDIAN', 'TALK'; Emblem = 'bubble' }
    @{ File = 'velvet'; From = '#5A1026'; To = '#8E1F3F'; Ink = '#F8E3C8'; Text = 'VELVET', 'HOUR'; Emblem = 'note' }
    @{ File = 'kestrel'; From = '#0B6E6E'; To = '#14A3A3'; Ink = '#FFFFFF'; Text = 'KESTREL', '88 FM'; Emblem = 'waves' }
    @{ File = 'polar'; From = '#7FD3FF'; To = '#FF8FD1'; Ink = '#FFFFFF'; Text = 'POLAR', 'POP'; Emblem = 'star' }
    @{ File = 'oldoak'; From = '#23452A'; To = '#3E6B45'; Ink = '#EADFC4'; Text = 'OLD OAK', 'CLASSICS'; Emblem = 'leaf' }
    @{ File = 'neon'; From = '#2A0845'; To = '#6441A5'; Ink = '#FF4FD8'; Text = 'NEON', 'COAST'; Emblem = 'grid' }
    @{ File = 'driftwood'; From = '#8A6A4B'; To = '#C9A77C'; Ink = '#FFFFFF'; Text = 'DRIFT', 'WOOD'; Emblem = 'wave' }
    @{ File = 'lumen'; From = '#161616'; To = '#2B2B2B'; Ink = '#FFD83D'; Text = 'LUMEN', 'FM'; Emblem = 'bulb' }
)

function Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $path
}

function Draw-Emblem($g, [string]$emblem, $ink) {
    $brush = [System.Drawing.SolidBrush]::new($ink)
    $pen = [System.Drawing.Pen]::new($ink, 9)
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cx = 128; $cy = 78
    switch ($emblem) {
        'moon' {
            $moon = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $moon.AddEllipse(98, 44, 64, 64)
            $region = [System.Drawing.Region]::new($moon)
            $cut = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $cut.AddEllipse(118, 34, 60, 60)
            $region.Exclude($cut)
            $g.FillRegion($brush, $region)
        }
        'sun' {
            $g.FillPie($brush, 88, 50, 80, 80, 180, 180)
            foreach ($angle in 200, 235, 270, 305, 340) {
                $rad = $angle * [Math]::PI / 180
                $g.DrawLine($pen, [float]($cx + 50 * [Math]::Cos($rad)), [float](90 + 50 * [Math]::Sin($rad)), [float]($cx + 66 * [Math]::Cos($rad)), [float](90 + 66 * [Math]::Sin($rad)))
            }
        }
        'bubble' {
            $bubble = RoundedRect 90 42 76 50 16
            $g.FillPath($brush, $bubble)
            $g.FillPolygon($brush, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(108, 88), [System.Drawing.PointF]::new(104, 110), [System.Drawing.PointF]::new(128, 90)))
        }
        'note' {
            $g.FillEllipse($brush, 100, 86, 30, 24)
            $g.FillRectangle($brush, 122, 40, 8, 58)
            $g.FillPolygon($brush, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(122, 40), [System.Drawing.PointF]::new(158, 52), [System.Drawing.PointF]::new(158, 66), [System.Drawing.PointF]::new(130, 56)))
        }
        'waves' {
            $g.FillEllipse($brush, 119, 95, 18, 18)
            foreach ($r in 26, 44, 62) { $g.DrawArc($pen, $cx - $r, 104 - $r, 2 * $r, 2 * $r, 225, 90) }
        }
        'star' {
            $points = for ($i = 0; $i -lt 10; $i++) {
                $r = if ($i % 2 -eq 0) { 38 } else { 16 }
                $a = (-90 + 36 * $i) * [Math]::PI / 180
                [System.Drawing.PointF]::new([float]($cx + $r * [Math]::Cos($a)), [float]($cy + $r * [Math]::Sin($a)))
            }
            $g.FillPolygon($brush, [System.Drawing.PointF[]]$points)
        }
        'leaf' {
            $g.FillEllipse($brush, 104, 36, 48, 70)
            $vein = [System.Drawing.Pen]::new((Color '#23452A'), 5)
            $g.DrawLine($vein, 128, 44, 128, 112)
            $g.DrawLine($pen, 128, 100, 128, 118)
        }
        'grid' {
            $thin = [System.Drawing.Pen]::new($ink, 4)
            $g.FillPie($brush, 96, 44, 64, 64, 180, 180)
            foreach ($y in 84, 94, 104) { $g.DrawLine($thin, 70, $y, 186, $y) }
        }
        'wave' {
            $wave = [System.Drawing.PointF[]]@(
                [System.Drawing.PointF]::new(70, 84), [System.Drawing.PointF]::new(99, 56), [System.Drawing.PointF]::new(128, 84),
                [System.Drawing.PointF]::new(157, 112), [System.Drawing.PointF]::new(186, 84))
            $g.DrawCurve($pen, $wave, 0.6)
        }
        'bulb' {
            $g.FillEllipse($brush, 100, 38, 56, 56)
            $g.FillRectangle($brush, 114, 88, 28, 18)
            $dark = [System.Drawing.Pen]::new((Color '#161616'), 4)
            $g.DrawLine($dark, 114, 96, 142, 96)
        }
    }
}

foreach ($logo in $logos) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Point]::new(0, 0), [System.Drawing.Point]::new(0, $size), (Color $logo.From), (Color $logo.To))
    $g.FillPath($background, (RoundedRect 0 0 $size $size 48))

    $ink = Color $logo.Ink
    $inkBrush = [System.Drawing.SolidBrush]::new($ink)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    Draw-Emblem $g $logo.Emblem $ink
    $main = [System.Drawing.Font]::new('Segoe UI Black', 40, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $sub = [System.Drawing.Font]::new('Segoe UI Semibold', 26, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    # Long words are narrowed to fit the tile instead of being cut off.
    $fit = [Math]::Min(1.0, 224 / $g.MeasureString($logo.Text[0], $main).Width)
    if ($fit -lt 1.0) { $main = [System.Drawing.Font]::new('Segoe UI Black', [float](40 * $fit), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel) }
    $g.DrawString($logo.Text[0], $main, $inkBrush, [System.Drawing.RectangleF]::new(0, 126, $size, 52), $format)
    $g.DrawString($logo.Text[1], $sub, $inkBrush, [System.Drawing.RectangleF]::new(0, 176, $size, 40), $format)

    $path = Join-Path $out "$($logo.File).png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose()
    Write-Host "Rendered $path"
}
