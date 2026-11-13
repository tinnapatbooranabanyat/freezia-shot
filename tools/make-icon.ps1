# Generates src/SnapEdit/Assets/app.ico (multi-size, PNG-compressed entries).
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\SnapEdit\Assets\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [single]($s * 0.04)
    $bg = New-RoundedPath $m $m ($s - 2 * $m) ($s - 2 * $m) ([single]($s * 0.22))
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 77, 141, 255)), ([System.Drawing.Color]::FromArgb(255, 139, 92, 246)), 45
    $g.FillPath($brush, $bg)

    # viewfinder corners
    $penW = [single][Math]::Max(1.5, $s * 0.085)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penW
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $a = [single]($s * 0.25); $b = [single]($s * 0.75); $l = [single]($s * 0.16)
    $g.DrawLine($pen, $a, $a + $l, $a, $a); $g.DrawLine($pen, $a, $a, $a + $l, $a)
    $g.DrawLine($pen, $b - $l, $a, $b, $a); $g.DrawLine($pen, $b, $a, $b, $a + $l)
    $g.DrawLine($pen, $a, $b - $l, $a, $b); $g.DrawLine($pen, $a, $b, $a + $l, $b)
    $g.DrawLine($pen, $b - $l, $b, $b, $b); $g.DrawLine($pen, $b, $b, $b, $b - $l)

    # center dot
    $r = [single]($s * 0.075)
    $c = [single]($s / 2)
    $g.FillEllipse([System.Drawing.Brushes]::White, $c - $r, $c - $r, 2 * $r, 2 * $r)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = @()
foreach ($s in $sizes) { $images += , (New-IconPng $s) }

$fs = [System.IO.File]::Create((Resolve-Path (Split-Path $out)).Path + '\app.ico')
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
Write-Host "Wrote $out"
