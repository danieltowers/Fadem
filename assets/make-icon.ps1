# Makes fadem.ico from logo-mark-color.png, the logo's mark: the exe's icon, the installer's,
# and every Fadem window's.
# The art's transparent margin is trimmed and the rest centred on a square, then scaled to each
# size Windows asks for. Up to 64 px each image is a 32-bit bitmap, which every reader of icons
# takes (.NET Framework's System.Drawing among them); 256 px is PNG, as Windows expects there.
# Run it again whenever the mark changes, and commit both.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$source = Join-Path $PSScriptRoot 'logo-mark-color.png'
$target = Join-Path $PSScriptRoot 'fadem.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Get-Trimmed([System.Drawing.Bitmap]$art) {
    $left = $art.Width; $top = $art.Height; $right = -1; $bottom = -1
    for ($y = 0; $y -lt $art.Height; $y++) {
        for ($x = 0; $x -lt $art.Width; $x++) {
            if ($art.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $left) { $left = $x }; if ($x -gt $right) { $right = $x }
                if ($y -lt $top) { $top = $y }; if ($y -gt $bottom) { $bottom = $y }
            }
        }
    }
    [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)
}

function New-Square([System.Drawing.Bitmap]$art, [System.Drawing.Rectangle]$bounds, [int]$size) {
    # A margin of 2 % a side, so the art never touches the icon's edge.
    $side = [Math]::Max($bounds.Width, $bounds.Height) * 1.04
    $scale = $size / $side
    $w = $bounds.Width * $scale; $h = $bounds.Height * $scale
    $square = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($square)
    $g.CompositingQuality = 'HighQuality'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'HighQuality'
    $wrap = New-Object System.Drawing.Imaging.ImageAttributes
    $wrap.SetWrapMode('TileFlipXY') # no grey fringe at the edges
    $destination = New-Object System.Drawing.Rectangle ([int][Math]::Round(($size - $w) / 2)), ([int][Math]::Round(($size - $h) / 2)), ([int][Math]::Round($w)), ([int][Math]::Round($h))
    $g.DrawImage($art, $destination, $bounds.X, $bounds.Y, $bounds.Width, $bounds.Height, 'Pixel', $wrap)
    $g.Dispose(); $wrap.Dispose()
    $square
}

function Get-Bitmap32([System.Drawing.Bitmap]$image) {
    # A BITMAPINFOHEADER of twice the height (the image, then its mask), the BGRA rows bottom
    # up, then the 1-bit AND mask, also bottom up: set where the pixel is fully transparent.
    $size = $image.Width
    $maskStride = [int]([Math]::Ceiling($size / 32) * 4)
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    $mask = New-Object byte[] ($maskStride * $size)
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = $size - 1 - $y
        for ($x = 0; $x -lt $size; $x++) {
            $p = $image.GetPixel($x, $y)
            $writer.Write([byte]$p.B); $writer.Write([byte]$p.G); $writer.Write([byte]$p.R); $writer.Write([byte]$p.A)
            if ($p.A -eq 0) { $mask[$row * $maskStride + [Math]::Floor($x / 8)] = $mask[$row * $maskStride + [Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
    }
    $writer.Write($mask)
    $writer.Flush()
    , $stream.ToArray() # the comma keeps the array whole through the pipeline
}

$art = New-Object System.Drawing.Bitmap $source
$bounds = Get-Trimmed $art
$images = foreach ($size in $sizes) {
    $square = New-Square $art $bounds $size
    if ($size -ge 256) {
        $png = New-Object System.IO.MemoryStream
        $square.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        , $png.ToArray()
    } else {
        , [byte[]](Get-Bitmap32 $square)
    }
    $square.Dispose()
}
$art.Dispose()

$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] } # 0 means 256
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$images[$i].Length); $writer.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($target, $file.ToArray())
"Wrote $target ($($sizes -join ', ') px)"
