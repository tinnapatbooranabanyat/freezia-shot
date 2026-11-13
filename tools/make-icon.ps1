# Generates src/Freezia/Assets/app.ico (multi-size, PNG entries) — design "C7 cream / orange":
# a freesia bloom inside viewfinder corners on a cream tile, flat colors only.
# Drawn with WPF so the path data is the same as the SVG mock-up (100 x 100 design grid).
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$PreviewPng = '')

Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'

$out = Join-Path $PSScriptRoot '..\src\Freezia\Assets\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

# ---- palette ----
function Hex($h) { [System.Windows.Media.ColorConverter]::ConvertFromString($h) }
$tile   = Hex '#FFF1DC'   # cream
$edge   = Hex '#E9D3B2'   # hairline so the light tile stays visible on light taskbars
$corner = Hex '#2B2B2B'   # viewfinder corners
$flower = Hex '#FF8A3D'   # freesia orange
$center = Hex '#FFF1DC'   # flower center = tile color

function Brush($c) { $b = New-Object System.Windows.Media.SolidColorBrush $c; $b.Freeze(); $b }

$petal = [System.Windows.Media.Geometry]::Parse('M0,-5 C-8,-12 -8,-28 0,-34 C8,-28 8,-12 0,-5 Z')
$corners = [System.Windows.Media.Geometry]::Parse('M22,37 V22 H37 M63,22 H78 V37 M78,63 V78 H63 M37,78 H22 V63')

function New-IconPng([int]$s) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $k = $s / 100.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $k, $k))

    # tile (+ hairline edge)
    $edgePen = New-Object System.Windows.Media.Pen (Brush $edge), ([Math]::Max(1.0, 1.2 / $k))
    $dc.DrawRoundedRectangle((Brush $tile), $edgePen, (New-Object System.Windows.Rect 4, 4, 92, 92), 22, 22)

    # viewfinder corners (a bit bolder at tiny sizes so they survive pixel snapping)
    $w = if ($s -le 24) { 7.5 } else { 6 }
    $pen = New-Object System.Windows.Media.Pen (Brush $corner), $w
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
    $dc.DrawGeometry($null, $pen, $corners)

    # six-petal freesia bloom (larger at tiny sizes so it still reads as a flower)
    $fs = if ($s -le 24) { 0.74 } elseif ($s -le 32) { 0.64 } else { 0.58 }
    $fb = Brush $flower
    for ($a = 0; $a -lt 360; $a += 60) {
        $g = New-Object System.Windows.Media.TransformGroup
        $g.Children.Add((New-Object System.Windows.Media.RotateTransform $a))
        $g.Children.Add((New-Object System.Windows.Media.ScaleTransform $fs, $fs))
        $g.Children.Add((New-Object System.Windows.Media.TranslateTransform 50, 50))
        $p = $petal.Clone(); $p.Transform = $g
        $dc.DrawGeometry($fb, $null, $p)
    }
    $cr = if ($s -le 24) { 5.5 } else { 4.5 }
    $dc.DrawEllipse((Brush $center), $null, (New-Object System.Windows.Point 50, 50), $cr, $cr)

    $dc.Pop()
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $s, $s, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$images = @()
foreach ($s in $sizes) { $images += , (New-IconPng $s) }

if ($PreviewPng) { [System.IO.File]::WriteAllBytes($PreviewPng, $images[-1]) }

# ---- write the .ico (PNG-compressed entries) ----
$fs = [System.IO.File]::Create([System.IO.Path]::GetFullPath($out))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Wrote $out"
