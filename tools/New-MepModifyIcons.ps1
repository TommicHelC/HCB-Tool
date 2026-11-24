param([string]$FeaturesDir, [string]$PreviewDir)
Add-Type -AssemblyName System.Drawing

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

# palette
$steelFill = C 255 176 192 210; $steelLine = C 255 62 85 115; $steelHi = C 255 228 236 244
$blueFill = C 255 52 150 222;   $blueLine = C 255 22 88 150;  $blueHi = C 255 150 206 245
$orange = C 255 245 160 35;     $orangeLine = C 255 190 110 10
$green = C 255 60 180 90;       $greenLine = C 255 30 120 55

function P([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }

function RoundPen($color, [float]$w) {
    $pen = New-Object System.Drawing.Pen $color, $w
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    return $pen
}

function FlatPen($color, [float]$w) {
    $pen = New-Object System.Drawing.Pen $color, $w
    $pen.StartCap = 'Flat'; $pen.EndCap = 'Flat'; $pen.LineJoin = 'Round'
    return $pen
}

# Tube along a path: outline, fill, highlight. Ends are flat.
function Draw-Tube($g, $path, [float]$w, $fill, $line, $hi, [int]$alpha = 255) {
    $o = FlatPen (C $alpha $line.R $line.G $line.B) ($w + 2.2)
    $f = FlatPen (C $alpha $fill.R $fill.G $fill.B) $w
    $h = FlatPen (C ([int]($alpha * 0.9)) $hi.R $hi.G $hi.B) ($w * 0.28)
    $g.DrawPath($o, $path); $g.DrawPath($f, $path)
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate(-$w * 0.18, -$w * 0.18)
    $hp = $path.Clone(); $hp.Transform($m)
    $g.DrawPath($h, $hp)
}

# Flange: short bar perpendicular to the tube end.
function Draw-Flange($g, [float]$x1, [float]$y1, [float]$x2, [float]$y2, $fill, $line, [int]$alpha = 255) {
    $o = FlatPen (C $alpha $line.R $line.G $line.B) 4.4
    $f = FlatPen (C $alpha $fill.R $fill.G $fill.B) 2.2
    $g.DrawLine($o, $x1, $y1, $x2, $y2)
    $g.DrawLine($f, $x1 + ($x2 - $x1) * 0.04, $y1 + ($y2 - $y1) * 0.04, $x2 - ($x2 - $x1) * 0.04, $y2 - ($y2 - $y1) * 0.04)
}

function Elbow-Path([float]$x, [float]$yBottom, [float]$yBendStart, [float]$r, [float]$xEnd, [int]$dir) {
    # vertical leg at $x going up to $yBendStart, then a 90 deg bend towards $dir (+1 right, -1 left)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddLine($x, $yBottom, $x, $yBendStart)
    if ($dir -gt 0) {
        $path.AddArc($x, $yBendStart - $r, 2 * $r, 2 * $r, 180, 90)
    } else {
        $path.AddArc($x - 2 * $r, $yBendStart - $r, 2 * $r, 2 * $r, 0, -90)
    }
    $path.AddLine($x + $dir * $r, $yBendStart - $r, $xEnd, $yBendStart - $r)
    return $path
}

function Arrow-Head($g, $brush, $pen, [float]$tipX, [float]$tipY, [float]$dx, [float]$dy, [float]$size) {
    $len = [Math]::Sqrt($dx * $dx + $dy * $dy); $dx /= $len; $dy /= $len
    $bx = $tipX - $dx * $size; $by = $tipY - $dy * $size
    $px = -$dy * $size * 0.62; $py = $dx * $size * 0.62
    $pts = [System.Drawing.PointF[]]@((P $tipX $tipY), (P ($bx + $px) ($by + $py)), (P ($bx - $px) ($by - $py)))
    $g.FillPolygon($brush, $pts)
    if ($pen) { $g.DrawPolygon($pen, $pts) }
}

function Plus-Badge($g, [float]$cx, [float]$cy, [float]$r) {
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $green), $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $g.DrawEllipse((New-Object System.Drawing.Pen $greenLine, 1.0), $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $w = RoundPen ([System.Drawing.Color]::White) ($r * 0.42)
    $g.DrawLine($w, $cx - $r * 0.5, $cy, $cx + $r * 0.5, $cy)
    $g.DrawLine($w, $cx, $cy - $r * 0.5, $cx, $cy + $r * 0.5)
}

# ---------- Rotate Fittings: elbow turned from right (ghost) to left, curved arrow over the top
function Draw-Rotate($g, [bool]$small) {
    $w = if ($small) { 6.5 } else { 5.5 }
    $elbow = Elbow-Path 7 31 25 6 22 1
    Draw-Tube $g $elbow $w $blueFill $blueLine $blueHi
    Draw-Flange $g 22.5 (19 - $w / 2 - 1.5) 22.5 (19 + $w / 2 + 1.5) $blueFill $blueLine

    # classic rotate symbol: open circle with an arrowhead, top-right
    $cx = 21.5; $cy = 8.5; $r = if ($small) { 6.8 } else { 6.2 }
    $arcW = if ($small) { 2.8 } else { 2.8 }
    $ao = FlatPen $orangeLine ($arcW + 1.8); $af = FlatPen $orange $arcW
    $g.DrawArc($ao, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 300, 270)
    $g.DrawArc($af, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 300, 270)
    # arrowhead at the arc end (upper left), pointing along the sweep: classic clockwise rotate symbol
    $a = 210 * [Math]::PI / 180
    $tipX = $cx + $r * [Math]::Cos($a); $tipY = $cy + $r * [Math]::Sin($a)
    $head = if ($small) { 8.5 } else { 6.0 }
    $dx = -[Math]::Sin($a); $dy = [Math]::Cos($a)
    Arrow-Head $g (New-Object System.Drawing.SolidBrush $orange) (New-Object System.Drawing.Pen $orangeLine, 1.0) ($tipX + $dx * $head * 0.75) ($tipY + $dy * $head * 0.75) $dx $dy $head
}

# ---------- Connect: fixed elbow (steel) + blue segment pushed in by a green arrow, orange connection point
function Draw-Connect($g, [bool]$small) {
    $w = if ($small) { 6.5 } else { 5.5 }
    $fixed = Elbow-Path 6 31 17 5 12 1
    Draw-Tube $g $fixed $w $steelFill $steelLine $steelHi
    Draw-Flange $g 12.5 (12 - $w / 2 - 1.5) 12.5 (12 + $w / 2 + 1.5) $steelFill $steelLine

    $moving = New-Object System.Drawing.Drawing2D.GraphicsPath
    $moving.AddLine(19.5, 12, 31, 12)
    Draw-Tube $g $moving $w $blueFill $blueLine $blueHi
    Draw-Flange $g 19 (12 - $w / 2 - 1.5) 19 (12 + $w / 2 + 1.5) $blueFill $blueLine

    $dot = if ($small) { 3.2 } else { 2.6 }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $orange), 15.75 - $dot, 12 - $dot, 2 * $dot, 2 * $dot)
    $g.DrawEllipse((New-Object System.Drawing.Pen $orangeLine, 1.0), 15.75 - $dot, 12 - $dot, 2 * $dot, 2 * $dot)

    $aw = if ($small) { 4.2 } else { 3.4 }
    $g.DrawLine((FlatPen $greenLine ($aw + 1.6)), 30, 24.5, 22, 24.5)
    $g.DrawLine((FlatPen $green $aw), 30.2, 24.5, 22, 24.5)
    Arrow-Head $g (New-Object System.Drawing.SolidBrush $green) (New-Object System.Drawing.Pen $greenLine, 1.0) 14.5 24.5 -1 0 9
}

# ---------- Extend: steel elbow + new blue straight segment with length dimension and green plus
function Draw-Extend($g, [bool]$small) {
    $w = if ($small) { 6.5 } else { 5.5 }
    $fixed = Elbow-Path 6 31 20 5 12 1
    Draw-Tube $g $fixed $w $steelFill $steelLine $steelHi
    Draw-Flange $g 12.5 (15 - $w / 2 - 1.5) 12.5 (15 + $w / 2 + 1.5) $steelFill $steelLine

    $new = New-Object System.Drawing.Drawing2D.GraphicsPath
    $new.AddLine(13.5, 15, 30, 15)
    Draw-Tube $g $new $w $blueFill $blueLine $blueHi

    if (-not $small) {
        $dim = New-Object System.Drawing.Pen $blueLine, 1.3
        $g.DrawLine($dim, 13.5, 3, 13.5, 9)
        $g.DrawLine($dim, 30, 3, 30, 9)
        $g.DrawLine($dim, 15, 6, 28.5, 6)
        $b = New-Object System.Drawing.SolidBrush $blueLine
        Arrow-Head $g $b $null 14 6 -1 0 3.6
        Arrow-Head $g $b $null 29.5 6 1 0 3.6
    }

    $r = if ($small) { 7.5 } else { 6.5 }
    Plus-Badge $g (31.5 - $r) (31.5 - $r) $r
}

# ---------- Align To Axis: steel reference pipe with orange centre line, blue pipe moved up onto that axis
function Draw-Align($g, [bool]$small) {
    $w = if ($small) { 6.5 } else { 5.5 }
    $axisPen = New-Object System.Drawing.Pen $orange, ($(if ($small) { 2.2 } else { 1.6 }))
    $axisPen.DashPattern = if ($small) { [float[]]@(3, 1.5) } else { [float[]]@(4, 1.5, 1, 1.5) }

    $ghost = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ghost.AddLine(18, 26, 31, 26)
    Draw-Tube $g $ghost $w $blueFill $blueLine $blueHi 80

    $reference = New-Object System.Drawing.Drawing2D.GraphicsPath
    $reference.AddLine(1, 9, 14, 9)
    Draw-Tube $g $reference $w $steelFill $steelLine $steelHi

    $moved = New-Object System.Drawing.Drawing2D.GraphicsPath
    $moved.AddLine(18, 9, 31, 9)
    Draw-Tube $g $moved $w $blueFill $blueLine $blueHi

    $g.DrawLine($axisPen, 0.5, 9, 31.5, 9)

    $aw = if ($small) { 3.6 } else { 3.0 }
    $g.DrawLine((FlatPen $greenLine ($aw + 1.6)), 9, 27, 9, 20)
    $g.DrawLine((FlatPen $green $aw), 9, 27.2, 9, 20)
    Arrow-Head $g (New-Object System.Drawing.SolidBrush $green) (New-Object System.Drawing.Pen $greenLine, 1.0) 9 13.5 0 -1 7.5
}

# ---------- Elbow Up: existing steel pipe, new blue elbow turning up with straight segment, green up arrow
function Draw-ElbowUp($g, [bool]$small) {
    $w = if ($small) { 6.5 } else { 5.5 }
    $existing = New-Object System.Drawing.Drawing2D.GraphicsPath
    $existing.AddLine(1, 25, 11, 25)
    Draw-Tube $g $existing $w $steelFill $steelLine $steelHi

    $new = New-Object System.Drawing.Drawing2D.GraphicsPath
    $new.AddLine(12.5, 25, 13, 25)
    $new.AddArc(7, 13, 12, 12, 90, -90)
    $new.AddLine(19, 19, 19, 3)
    Draw-Tube $g $new $w $blueFill $blueLine $blueHi
    Draw-Flange $g 12 (25 - $w / 2 - 1.5) 12 (25 + $w / 2 + 1.5) $blueFill $blueLine

    $aw = if ($small) { 3.6 } else { 3.0 }
    $g.DrawLine((FlatPen $greenLine ($aw + 1.6)), 28, 22, 28, 12)
    $g.DrawLine((FlatPen $green $aw), 28, 22.2, 28, 12)
    Arrow-Head $g (New-Object System.Drawing.SolidBrush $green) (New-Object System.Drawing.Pen $greenLine, 1.0) 28 4.5 0 -1 8.5
}

# ---------- Elbow 45: plan view, existing steel pipe, new blue 45 degree elbow with straight segment, green "45" badge
function Draw-Elbow45($g, [bool]$small) {
    $w = if ($small) { 6.5 } else { 5.5 }
    $existing = New-Object System.Drawing.Drawing2D.GraphicsPath
    $existing.AddLine(1, 8, 11, 8)
    Draw-Tube $g $existing $w $steelFill $steelLine $steelHi

    # arc centre (13, 16), radius 8: from straight right (270) turning 45 degrees clockwise
    $new = New-Object System.Drawing.Drawing2D.GraphicsPath
    $new.AddLine(12.5, 8, 13, 8)
    $new.AddArc(5, 8, 16, 16, 270, 45)
    $new.AddLine(18.66, 10.34, 29, 20.7)
    Draw-Tube $g $new $w $blueFill $blueLine $blueHi
    Draw-Flange $g 12 (8 - $w / 2 - 1.5) 12 (8 + $w / 2 + 1.5) $blueFill $blueLine

    $r = if ($small) { 8.0 } else { 7.0 }
    $cx = $r + 0.5; $cy = 31.5 - $r
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $green), $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $g.DrawEllipse((New-Object System.Drawing.Pen $greenLine, 1.0), $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $fontSize = if ($small) { 9.5 } else { 8.0 }
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
    $g.DrawString('45', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF ($cx - $r), ($cy - $r + 0.4), (2 * $r), (2 * $r)), $format)
}

function Render($drawer, [int]$size, [bool]$small) {
    $hi = 256
    $big = New-Object System.Drawing.Bitmap $hi, $hi
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($hi / 32.0, $hi / 32.0)
    & $drawer $g $small
    $g.Dispose()

    $out = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'; $g2.SmoothingMode = 'AntiAlias'
    $g2.Clear([System.Drawing.Color]::Transparent)
    $g2.DrawImage($big, 0, 0, $size, $size)
    $g2.Dispose(); $big.Dispose()
    return $out
}

$tools = [ordered]@{
    'RotateFittings\Resources'  = ${function:Draw-Rotate}
    'ConnectElements\Resources' = ${function:Draw-Connect}
    'ConnectorExtend\Resources' = ${function:Draw-Extend}
    'AlignToAxis\Resources'     = ${function:Draw-Align}
    'ElbowBranch\Resources\Up'   = ${function:Draw-ElbowUp}
    'ElbowBranch\Resources\Side45' = ${function:Draw-Elbow45}
}
foreach ($name in $tools.Keys) {
    $dir = Join-Path $FeaturesDir $name
    New-Item -ItemType Directory -Force $dir | Out-Null
    $b32 = Render $tools[$name] 32 $false
    $b32.Save((Join-Path $dir 'icon32.png'), [System.Drawing.Imaging.ImageFormat]::Png); $b32.Dispose()
    $b16 = Render $tools[$name] 16 $true
    $b16.Save((Join-Path $dir 'icon16.png'), [System.Drawing.Imaging.ImageFormat]::Png); $b16.Dispose()
    if ($PreviewDir) {
        $p = Render $tools[$name] 128 $false
        $p.Save((Join-Path $PreviewDir (($name -replace '\\', '-') + '-128.png')), [System.Drawing.Imaging.ImageFormat]::Png); $p.Dispose()
    }
}
