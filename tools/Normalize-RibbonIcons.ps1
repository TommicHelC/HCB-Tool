param(
    [string]$Root = "D:\DEV-LOCAL\Revit_Codex_HCB",
    [switch]$Apply,
    [switch]$VerboseReport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore

function Get-IconFiles {
    param([string]$ProjectRoot)

    $patterns = @(
        (Join-Path $ProjectRoot "src\HCB.RevitAddin\Features"),
        (Join-Path $ProjectRoot "src\HCB.RevitAddin\Resources\Ribbon")
    )

    foreach ($path in $patterns) {
        if (-not (Test-Path $path)) {
            continue
        }

        Get-ChildItem $path -Recurse -File -Include icon16.png,icon32.png
    }
}

function Get-AlphaBounds {
    param([System.Windows.Media.Imaging.BitmapFrame]$Frame)

    $width = $Frame.PixelWidth
    $height = $Frame.PixelHeight
    $stride = $width * 4
    $pixels = New-Object byte[] ($stride * $height)
    $Frame.CopyPixels($pixels, $stride, 0)

    $minX = $width
    $minY = $height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $alpha = $pixels[$y * $stride + $x * 4 + 3]
            if ($alpha -le 0) {
                continue
            }

            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }

    if ($maxX -lt 0) {
        return $null
    }

    [PSCustomObject]@{
        X = $minX
        Y = $minY
        Width = $maxX - $minX + 1
        Height = $maxY - $minY + 1
        Left = $minX
        Top = $minY
        Right = $width - 1 - $maxX
        Bottom = $height - 1 - $maxY
    }
}

function Normalize-IconFile {
    param([System.IO.FileInfo]$File)

    $expectedSize = if ($File.Name -ieq "icon16.png") { 16 } else { 32 }
    $padding = if ($expectedSize -le 16) { 1 } else { 2 }

    $uri = [System.Uri]::new($File.FullName, [System.UriKind]::Absolute)
    $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create(
        $uri,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)

    $bounds = Get-AlphaBounds -Frame $frame
    if ($null -eq $bounds) {
        return [PSCustomObject]@{
            File = $File.FullName
            Changed = $false
            Reason = "Empty"
        }
    }

    $needsNormalization =
        ($frame.PixelWidth -ne $expectedSize) -or
        ($frame.PixelHeight -ne $expectedSize) -or
        ($bounds.Left -lt $padding) -or
        ($bounds.Top -lt $padding) -or
        ($bounds.Right -lt $padding) -or
        ($bounds.Bottom -lt $padding)

    if (-not $needsNormalization) {
        return [PSCustomObject]@{
            File = $File.FullName
            Changed = $false
            Reason = "AlreadySafe"
        }
    }

    $cropped = [System.Windows.Media.Imaging.CroppedBitmap]::new(
        $frame,
        [System.Windows.Int32Rect]::new($bounds.X, $bounds.Y, $bounds.Width, $bounds.Height))

    $available = [double]($expectedSize - ($padding * 2))
    $scale = [Math]::Min($available / $bounds.Width, $available / $bounds.Height)
    if ($scale -gt 1.0) {
        $scale = 1.0
    }

    $renderWidth = [Math]::Max(1.0, $bounds.Width * $scale)
    $renderHeight = [Math]::Max(1.0, $bounds.Height * $scale)
    $offsetX = ($expectedSize - $renderWidth) / 2.0
    $offsetY = ($expectedSize - $renderHeight) / 2.0

    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [System.Windows.Media.BitmapScalingMode]::HighQuality)

    $context = $visual.RenderOpen()
    try {
        $context.DrawImage(
            $cropped,
            [System.Windows.Rect]::new($offsetX, $offsetY, $renderWidth, $renderHeight))
    }
    finally {
        $context.Close()
    }

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $expectedSize,
        $expectedSize,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap.Freeze()

    $tempPath = "{0}.tmp.png" -f $File.FullName
    $stream = [System.IO.File]::Open($tempPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $encoder.Save($stream)
    }
    finally {
        $stream.Dispose()
    }

    if ($Apply) {
        Move-Item -LiteralPath $tempPath -Destination $File.FullName -Force
    }
    else {
        Remove-Item -LiteralPath $tempPath -Force
    }

    return [PSCustomObject]@{
        File = $File.FullName
        Changed = $Apply
        Reason = "Normalized"
        SourceWidth = $bounds.Width
        SourceHeight = $bounds.Height
    }
}

$files = @(Get-IconFiles -ProjectRoot $Root)
if ($files.Count -eq 0) {
    throw "Nie znaleziono ikon do analizy."
}

$results = foreach ($file in $files) {
    Normalize-IconFile -File $file
}

$normalized = @($results | Where-Object { $_.Reason -eq "Normalized" })
$alreadySafe = @($results | Where-Object { $_.Reason -eq "AlreadySafe" })
$empty = @($results | Where-Object { $_.Reason -eq "Empty" })

Write-Output ("Files: {0}" -f $results.Count)
Write-Output ("Needs normalization: {0}" -f $normalized.Count)
Write-Output ("Already safe: {0}" -f $alreadySafe.Count)
Write-Output ("Empty: {0}" -f $empty.Count)
Write-Output ("Apply mode: {0}" -f $Apply.IsPresent)

if ($VerboseReport -or $normalized.Count -gt 0) {
    $normalized | Select-Object File, SourceWidth, SourceHeight | Format-Table -AutoSize
}
