# Makes the installer's wizard images into -OutDir: large-<scale>.bmp, the panel at the left of
# the first and last pages (the mark over the wordmark), and small-<scale>.bmp, the corner image
# of the other pages (the mark alone). Each at 100 % to 250 % display scaling, at the sizes Inno
# Setup's modern style asks for, as 24-bit bitmaps on the wizard's white.
# build-installer.ps1 runs it on every build, so the images always follow assets/.
param([Parameter(Mandatory)][string]$OutDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$scales = @{ '100' = 1.0; '125' = 1.25; '150' = 1.5; '200' = 2.0; '250' = 2.5 }

# The columns (or rows) of $art in $within that hold any visible pixel.
function Get-Opaque([System.Drawing.Bitmap]$art, [System.Drawing.Rectangle]$within, [bool]$columns) {
    $data = $art.LockBits($within, 'ReadOnly', 'Format32bppArgb')
    $row = New-Object int[] $within.Width
    $hit = New-Object bool[] ($(if ($columns) { $within.Width } else { $within.Height }))
    for ($y = 0; $y -lt $within.Height; $y++) {
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $within.Width)
        for ($x = 0; $x -lt $within.Width; $x++) {
            if ((($row[$x] -shr 24) -band 0xFF) -gt 8) { if ($columns) { $hit[$x] = $true } else { $hit[$y] = $true } }
        }
    }
    $art.UnlockBits($data)
    , $hit
}

function Get-Trimmed([System.Drawing.Bitmap]$art, [System.Drawing.Rectangle]$within) {
    $cols = Get-Opaque $art $within $true
    $rows = Get-Opaque $art $within $false
    $left = [Array]::IndexOf($cols, $true); $right = [Array]::LastIndexOf($cols, $true)
    $top = [Array]::IndexOf($rows, $true); $bottom = [Array]::LastIndexOf($rows, $true)
    [System.Drawing.Rectangle]::FromLTRB($within.X + $left, $within.Y + $top, $within.X + $right + 1, $within.Y + $bottom + 1)
}

function Get-Graphics([System.Drawing.Bitmap]$canvas) {
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::White)
    $g.CompositingQuality = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g
}

# Draws $from of $art $width wide, its top left at ($x, $y); returns the height drawn.
function Add-Art($g, $art, [System.Drawing.Rectangle]$from, [double]$x, [double]$y, [double]$width) {
    $height = $width * $from.Height / $from.Width
    $wrap = New-Object System.Drawing.Imaging.ImageAttributes
    $wrap.SetWrapMode('TileFlipXY') # no grey fringe at the edges
    $to = New-Object System.Drawing.Rectangle ([int][Math]::Round($x)), ([int][Math]::Round($y)), ([int][Math]::Round($width)), ([int][Math]::Round($height))
    $g.DrawImage($art, $to, $from.X, $from.Y, $from.Width, $from.Height, 'Pixel', $wrap)
    $wrap.Dispose()
    $height
}

$mark = New-Object System.Drawing.Bitmap (Join-Path $assets 'logo-mark-color.png')
$markBounds = Get-Trimmed $mark (New-Object System.Drawing.Rectangle 0, 0, $mark.Width, $mark.Height)

# The wordmark is the logo's right part, past the widest empty gap after the mark.
$logo = New-Object System.Drawing.Bitmap (Join-Path $assets 'logo-black.png')
$cols = Get-Opaque $logo (New-Object System.Drawing.Rectangle 0, 0, $logo.Width, $logo.Height) $true
$bestStart = 0; $bestLength = 0; $start = -1
for ($x = [Array]::IndexOf($cols, $true); $x -lt $cols.Length; $x++) {
    if (-not $cols[$x]) { if ($start -lt 0) { $start = $x } }
    elseif ($start -ge 0) { if ($x - $start -gt $bestLength) { $bestStart = $start; $bestLength = $x - $start }; $start = -1 }
}
$wordBounds = Get-Trimmed $logo (New-Object System.Drawing.Rectangle ($bestStart + $bestLength), 0, ($logo.Width - $bestStart - $bestLength), $logo.Height)

New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($name in $scales.Keys) {
    $s = $scales[$name]

    $w = [int][Math]::Round(164 * $s); $h = [int][Math]::Round(314 * $s)
    $large = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = Get-Graphics $large
    $markWidth = $w * 0.62; $wordWidth = $w * 0.62; $gap = $h * 0.06
    $markHeight = $markWidth * $markBounds.Height / $markBounds.Width
    $wordHeight = $wordWidth * $wordBounds.Height / $wordBounds.Width
    $top = ($h * 0.9 - ($markHeight + $gap + $wordHeight)) / 2 # a little above centre
    Add-Art $g $mark $markBounds (($w - $markWidth) / 2) $top $markWidth | Out-Null
    Add-Art $g $logo $wordBounds (($w - $wordWidth) / 2) ($top + $markHeight + $gap) $wordWidth | Out-Null
    $g.Dispose()
    $large.Save((Join-Path $OutDir "large-$name.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $large.Dispose()

    $side = [int][Math]::Round(55 * $s)
    $small = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = Get-Graphics $small
    $fit = $side * 0.9 / [Math]::Max($markBounds.Width, $markBounds.Height)
    $markWidth = $markBounds.Width * $fit
    Add-Art $g $mark $markBounds (($side - $markWidth) / 2) (($side - $markBounds.Height * $fit) / 2) $markWidth | Out-Null
    $g.Dispose()
    $small.Save((Join-Path $OutDir "small-$name.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $small.Dispose()
}
$mark.Dispose(); $logo.Dispose()
