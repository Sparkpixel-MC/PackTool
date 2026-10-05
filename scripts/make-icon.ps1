# Renders the PackTool icon (dark rounded tile + gradient cube + padlock) and
# packs a multi-size .ico. Re-run after design tweaks: powershell scripts\make-icon.ps1
# Preview: scripts\icon-preview.png
param([string]$OutIco = "$PSScriptRoot\..\PackTool\icon.ico")

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $u = $size / 256.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # --- rounded tile ---
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 56 * $u; $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 0x1C, 0x2B, 0x3C),
        [System.Drawing.Color]::FromArgb(255, 0x0C, 0x11, 0x17))
    $g.FillPath($bg, $path)
    if ($size -ge 32) {
        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0x2E, 0x3C, 0x4E), (1.5 * $u))
        $g.DrawPath($borderPen, $path)
        $borderPen.Dispose()
    }

    # --- isometric cube ---
    $pt = { param($x, $y) New-Object System.Drawing.PointF(($x * $u), ($y * $u)) }
    $top    = @(& $pt 128 64;  & $pt 196 98;  & $pt 128 132; & $pt 60 98)
    $left   = @(& $pt 60 98;   & $pt 128 132; & $pt 128 204; & $pt 60 170)
    $right  = @(& $pt 196 98;  & $pt 128 132; & $pt 128 204; & $pt 196 170)

    function Face([object[]]$pts, [System.Drawing.Brush]$brush) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddPolygon($pts)
        $g.FillPath($brush, $p)
        $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0x0C, 0x11, 0x17), (1.2 * $u))
        $p2 = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p2.AddPolygon($pts)
        $g.DrawPath($edge, $p2)
        $edge.Dispose()
    }

    $topBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (& $pt 60 98), (& $pt 196 98),
        [System.Drawing.Color]::FromArgb(255, 0x7C, 0xC3, 0xF8),
        [System.Drawing.Color]::FromArgb(255, 0x3E, 0x92, 0xE8))
    $leftBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (& $pt 60 98), (& $pt 128 204),
        [System.Drawing.Color]::FromArgb(255, 0x2E, 0x86, 0xDE),
        [System.Drawing.Color]::FromArgb(255, 0x17, 0x55, 0x92))
    $rightBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (& $pt 128 204), (& $pt 196 98),
        [System.Drawing.Color]::FromArgb(255, 0x27, 0xC5, 0xA8),
        [System.Drawing.Color]::FromArgb(255, 0x11, 0x82, 0x6C))
    Face $top $topBrush
    Face $left $leftBrush
    Face $right $rightBrush

    # --- padlock on the box front (skipped on tiny sizes) ---
    if ($size -ge 32) {
        $lockBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xF2, 0xF8, 0xFC))
        $shackle = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0xF2, 0xF8, 0xFC), (6 * $u))
        $shackle.StartCap = 'Round'; $shackle.EndCap = 'Round'
        $g.DrawArc($shackle, (112 * $u), (146 * $u), (32 * $u), (30 * $u), 180, 180)
        $bodyPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $br = 5 * $u; $bd = $br * 2
        $bx = 105 * $u; $by = 157 * $u; $bw2 = 46 * $u; $bh = 30 * $u
        $bodyPath.AddArc($bx, $by, $bd, $bd, 180, 90)
        $bodyPath.AddArc($bx + $bw2 - $bd, $by, $bd, $bd, 270, 90)
        $bodyPath.AddArc($bx + $bw2 - $bd, $by + $bh - $bd, $bd, $bd, 0, 90)
        $bodyPath.AddArc($bx, $by + $bh - $bd, $bd, $bd, 90, 90)
        $bodyPath.CloseFigure()
        $g.FillPath($lockBrush, $bodyPath)
        # keyhole
        $holeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x10, 0x4A, 0x6E))
        $holeR = 5 * $u
        $g.FillEllipse($holeBrush, (128 * $u - $holeR), (164 * $u), ($holeR * 2), ($holeR * 2))
        $g.FillRectangle($holeBrush, (128 * $u - 2 * $u), (168 * $u), (4 * $u), (9 * $u))
    }

    $g.Dispose()
    return $bmp
}

# preview
$preview = New-IconBitmap 256
$preview.Save("$PSScriptRoot\icon-preview.png", [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

# pack .ico (PNG-compressed entries, sizes 256..16)
$sizes = 256, 128, 64, 48, 32, 24, 16
$blobs = @()
foreach ($s in $sizes) {
    $b = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $ms.ToArray()
    $b.Dispose(); $ms.Dispose()
}
$bw = New-Object System.IO.BinaryWriter([System.IO.File]::Create($OutIco))
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = $(if ($s -eq 256) { 0 } else { $s })
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$blobs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($b in $blobs) { $bw.Write($b) }
$bw.Close()
Write-Host "icon written: $OutIco"
