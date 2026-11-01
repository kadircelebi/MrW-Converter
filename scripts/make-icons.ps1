<#
.SYNOPSIS
    Builds every icon the app and the MSIX use from packaging\Logo\MrWConverter-logo.png.

.DESCRIPTION
    The logo is a square PNG with a transparent background. To change the logo, replace
    that file and run this script once. The results are checked in, so builds don't need it.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$logoPath = Join-Path $root 'packaging\Logo\MrWConverter-logo.png'
$assets = Join-Path $root 'packaging\Assets'
$appAssets = Join-Path $root 'src\MrWConverter.App\Assets'

$logo = [System.Drawing.Image]::FromFile($logoPath)

# Draws the logo centered on a transparent canvas, covering the given share of its height.
function New-Icon([int] $width, [int] $height, [double] $share) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $side = [int][Math]::Round($height * $share)
    $graphics.DrawImage($logo, [int](($width - $side) / 2), [int](($height - $side) / 2), $side, $side)
    $graphics.Dispose()
    $bitmap
}

function Save-Icon([string] $path, [int] $width, [int] $height, [double] $share) {
    $bitmap = New-Icon $width $height $share
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# Small sizes fill their square, because a margin there would cost pixels the logo needs.
function Get-IconShare([int] $size) { if ($size -le 24) { 1.0 } else { 0.9 } }

Get-ChildItem $assets -Filter *.png | Remove-Item

foreach ($size in 16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256) {
    $share = Get-IconShare $size
    Save-Icon (Join-Path $assets "Square44x44Logo.targetsize-$size.png") $size $size $share
    Save-Icon (Join-Path $assets "Square44x44Logo.targetsize-${size}_altform-unplated.png") $size $size $share
}

foreach ($scale in 100, 125, 150, 200, 400) {
    $factor = $scale / 100
    $small = [int][Math]::Ceiling(44 * $factor)
    Save-Icon (Join-Path $assets "Square44x44Logo.scale-$scale.png") $small $small 0.9
    $tile = [int][Math]::Ceiling(150 * $factor)
    Save-Icon (Join-Path $assets "Square150x150Logo.scale-$scale.png") $tile $tile 0.66
    Save-Icon (Join-Path $assets "Wide310x150Logo.scale-$scale.png") ([int][Math]::Ceiling(310 * $factor)) $tile 0.66
    $store = [int][Math]::Ceiling(50 * $factor)
    Save-Icon (Join-Path $assets "StoreLogo.scale-$scale.png") $store $store 1.0
}

# Partner Center asks for this one when the store listing is filled in.
Save-Icon (Join-Path $root 'packaging\Logo\StoreListing-300.png') 300 300 1.0

# The window's own logo.
Save-Icon (Join-Path $appAssets 'Logo.png') 256 256 1.0

# The .ico: PNG frames, which every supported Windows version reads.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) {
    $bitmap = New-Icon $size $size (Get-IconShare $size)
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $memory.ToArray()
}

$file = [System.IO.File]::Create((Join-Path $appAssets 'AppIcon.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $byteSize = if ($sizes[$index] -ge 256) { 0 } else { $sizes[$index] }
    $writer.Write([byte]$byteSize)
    $writer.Write([byte]$byteSize)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$index].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose()
$logo.Dispose()

Write-Host "Icons written to $assets and $appAssets"
