# Renders the LionGrep app icon as PNGs at every size WinUI 3 expects in Assets/.
# The icon is the 🦁 emoji (U+1F981) rendered via WPF FormattedText (DirectWrite,
# color-glyph-aware) on a warm radial gradient that suggests the lion's mane. The
# `targetsize-24_altform-unplated` variant is rendered transparent (no plate) per
# Windows guidelines for unplated tiles in the taskbar / Start.

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$lion = [System.Char]::ConvertFromUtf32(0x1F981)
$assetsDir = Join-Path (Split-Path -Parent $PSCommandPath) 'LionGrep\Assets'

function Render-IconPngBytes {
    param(
        [int]$Width,
        [int]$Height,
        [bool]$Plated = $true,           # if false, transparent background (for unplated icon variant)
        [double]$LionScale = 0.78        # fraction of the shorter side the emoji should occupy
    )
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Width, $Height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $dv = New-Object System.Windows.Media.DrawingVisual
    $ctx = $dv.RenderOpen()

    if ($Plated) {
        # Warm orange radial gradient — evokes a lion's mane / fire.
        $g = New-Object System.Windows.Media.RadialGradientBrush
        $g.GradientOrigin = New-Object System.Windows.Point 0.5, 0.45
        $g.Center         = New-Object System.Windows.Point 0.5, 0.5
        $g.RadiusX = 0.6; $g.RadiusY = 0.6
        [void]$g.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(255,180,60)), 0.0))
        [void]$g.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(220,90, 30)), 1.0))
        $rect = New-Object System.Windows.Rect 0, 0, $Width, $Height
        $ctx.DrawRectangle($g, $null, $rect)
    }

    $glyphSize = [Math]::Min($Width, $Height) * $LionScale
    $tf = New-Object System.Windows.Media.Typeface ([System.Windows.Media.FontFamily]::new("Segoe UI Emoji"))
    $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Colors]::Black)
    $ft = New-Object System.Windows.Media.FormattedText $lion,
        ([System.Globalization.CultureInfo]::InvariantCulture),
        'LeftToRight', $tf, $glyphSize, $brush, 1.0
    $origin = New-Object System.Windows.Point (($Width - $ft.Width)/2), (($Height - $ft.Height)/2)
    $ctx.DrawText($ft, $origin)
    $ctx.Close()
    $rtb.Render($dv)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    [void]$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return ,$ms.ToArray()
}

function Render-IconPng {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path,
        [bool]$Plated = $true,
        [double]$LionScale = 0.78
    )
    $bytes = Render-IconPngBytes -Width $Width -Height $Height -Plated $Plated -LionScale $LionScale
    [System.IO.File]::WriteAllBytes($Path, $bytes)
    "{0,-50} {1}x{2}" -f (Split-Path -Leaf $Path), $Width, $Height
}

# Builds a multi-resolution .ico with PNG-compressed payloads (supported by Windows Vista+).
# Embedding PNGs keeps the file small and preserves alpha at every size.
function Build-IcoFile {
    param(
        [string]$Path,
        [int[]]$Sizes,
        [double]$LionScale = 0.78
    )
    $frames = foreach ($sz in $Sizes) {
        [PSCustomObject]@{
            Size  = $sz
            Bytes = Render-IconPngBytes -Width $sz -Height $sz -LionScale $LionScale
        }
    }

    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try {
        $bw = New-Object System.IO.BinaryWriter $fs
        # ICONDIR header
        $bw.Write([uint16]0)                  # reserved
        $bw.Write([uint16]1)                  # type = 1 (ICO)
        $bw.Write([uint16]$frames.Count)

        # ICONDIRENTRY for each frame — 16 bytes each
        $offset = 6 + 16 * $frames.Count
        foreach ($f in $frames) {
            $dim = if ($f.Size -ge 256) { [byte]0 } else { [byte]$f.Size }  # 0 means 256
            $bw.Write([byte]$dim)             # width
            $bw.Write([byte]$dim)             # height
            $bw.Write([byte]0)                # color palette count (0 = no palette)
            $bw.Write([byte]0)                # reserved
            $bw.Write([uint16]1)              # color planes
            $bw.Write([uint16]32)             # bits per pixel
            $bw.Write([uint32]$f.Bytes.Length) # image data size
            $bw.Write([uint32]$offset)         # image data offset
            $offset += $f.Bytes.Length
        }

        # Image payloads
        foreach ($f in $frames) { $bw.Write($f.Bytes) }
        $bw.Flush()
    } finally { $fs.Close() }
    "{0,-50} {1} frames" -f (Split-Path -Leaf $Path), $frames.Count
}

Render-IconPng -Width   48 -Height  48 -Path (Join-Path $assetsDir 'LockScreenLogo.scale-200.png')
Render-IconPng -Width 1240 -Height 600 -Path (Join-Path $assetsDir 'SplashScreen.scale-200.png')      -LionScale 0.65
Render-IconPng -Width  300 -Height 300 -Path (Join-Path $assetsDir 'Square150x150Logo.scale-200.png')
Render-IconPng -Width   88 -Height  88 -Path (Join-Path $assetsDir 'Square44x44Logo.scale-200.png')
Render-IconPng -Width   24 -Height  24 -Path (Join-Path $assetsDir 'Square44x44Logo.targetsize-24_altform-unplated.png') -Plated $false
Render-IconPng -Width   50 -Height  50 -Path (Join-Path $assetsDir 'StoreLogo.png')
Render-IconPng -Width  620 -Height 300 -Path (Join-Path $assetsDir 'Wide310x150Logo.scale-200.png')   -LionScale 0.65

# Multi-resolution .ico used for the unpackaged window/taskbar icon (AppWindow.SetIcon)
# and for embedding in the .exe via <ApplicationIcon>.
Build-IcoFile -Path (Join-Path $assetsDir 'AppIcon.ico') -Sizes 16,20,24,32,40,48,64,96,128,256
