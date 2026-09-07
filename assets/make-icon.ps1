# Generates the PocketRoles launcher icon (original design): a red bean-shaped character peeking out of a teal pocket
# on an indigo rounded tile, with a gold role star. Output: PocketRoles.ico (16..256) + PocketRoles-256.png/512.png.
param([string]$OutDir = (Split-Path -Parent $MyInvocation.MyCommand.Path))
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-StarPath([float]$cx, [float]$cy, [float]$outer, [float]$inner) {
    $pts = New-Object System.Collections.Generic.List[System.Drawing.PointF]
    for ($i = 0; $i -lt 10; $i++) {
        $a = -90 + $i * 36
        $rad = if ($i % 2 -eq 0) { $outer } else { $inner }
        $pts.Add((New-Object System.Drawing.PointF(($cx + $rad * [Math]::Cos($a * [Math]::PI / 180)), ($cy + $rad * [Math]::Sin($a * [Math]::PI / 180)))))
    }
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddPolygon($pts.ToArray())
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    # tile
    $tile = New-RoundedPath 8 8 240 240 58
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 8)), (New-Object System.Drawing.PointF(0, 248)), [System.Drawing.Color]::FromArgb(255, 61, 66, 160), [System.Drawing.Color]::FromArgb(255, 22, 26, 74))
    $g.FillPath($grad, $tile)
    # soft highlight
    $hl = New-RoundedPath 20 18 216 90 44
    $hlBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 255, 255, 255))
    $g.FillPath($hlBrush, $hl)

    # character (red bean) — drawn before the pocket so the pocket covers its lower half
    $red = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 230, 57, 70))
    $redDark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 176, 34, 48))
    $g.FillEllipse($redDark, 66, 70, 34, 96)          # backpack
    $body = New-RoundedPath 84 52 96 150 44
    $g.FillPath($red, $body)
    $visor = New-RoundedPath 118 82 66 40 20
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 170, 226, 250))), $visor)
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 235, 250, 255))), (New-RoundedPath 126 88 34 16 8))
    $outline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 40, 14, 20), 6)
    $g.DrawPath($outline, $body)
    $g.DrawPath($outline, $visor)

    # pocket (teal) with stitch line
    $teal = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 46, 196, 182))
    $pocket = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pocket.AddLine(40, 138, 216, 138)
    $pocket.AddLine(216, 138, 216, 196)
    $pocket.AddArc(160, 176, 56, 56, 0, 90)
    $pocket.AddLine(188, 232, 68, 232)
    $pocket.AddArc(40, 176, 56, 56, 90, 90)
    $pocket.CloseFigure()
    $g.FillPath($teal, $pocket)
    $g.DrawPath($outline, $pocket)
    $stitch = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(160, 12, 90, 84), 4)
    $stitch.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
    $g.DrawLine($stitch, 56, 150, 200, 150)

    # gold role star
    $star = New-StarPath 204 58 30 13
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 209, 102))), $star)
    $g.DrawPath((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 120, 78, 0), 4)), $star)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$sz] = $ms.ToArray()
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir 'PocketRoles-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose(); $ms.Dispose()
}
$big = Draw-Icon 512; $big.Save((Join-Path $OutDir 'PocketRoles-512.png'), [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()

# ICO container with PNG entries
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $data = $pngs[$sz]
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($sz in $sizes) { $bw.Write($pngs[$sz]) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'PocketRoles.ico'), $ico.ToArray())
$bw.Dispose(); $ico.Dispose()
Write-Output ("icon written: " + (Join-Path $OutDir 'PocketRoles.ico') + " sizes=" + ($sizes -join ','))
