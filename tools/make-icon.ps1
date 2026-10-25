# Antilag simgesini üretir: koyu yuvarlak kare + çentikli kadran (3/4'ü yanık) + ortada düz daire.
# Kullanım:  powershell -File tools\make-icon.ps1 -Out src\CS2Hazirla\Assets\app.ico [-PreviewPng önizleme.png]
param([string]$Out = "$PSScriptRoot\..\src\CS2Hazirla\Assets\app.ico", [string]$PreviewPng)
Add-Type -AssemblyName System.Drawing

$tile   = [System.Drawing.Color]::FromArgb(255, 0x17, 0x19, 0x20)     # RailColor
$accent = [System.Drawing.Color]::FromArgb(255, 0xA9, 0xB3, 0xFF)     # AccentColor
$dim    = [System.Drawing.Color]::FromArgb(255, 0x4A, 0x51, 0x63)     # sönük çentik

function New-MarkPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # yuvarlak kare zemin
    $r = $size * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $path.AddArc(0, $size - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $tile), $path)

    # kadran: küçük boyutta daha az ve daha kalın çentik
    $n = if ($size -le 24) { 12 } elseif ($size -le 48) { 18 } else { 24 }
    $lit = [int][Math]::Round($n * 0.75)
    $cx = $size / 2.0; $cy = $size / 2.0
    $outer = $size * 0.40
    $len = $size * 0.115
    $thick = [Math]::Max(1.3, $size * 0.045)
    for ($i = 0; $i -lt $n; $i++) {
        $a = (-90.0 + $i * 360.0 / $n) * [Math]::PI / 180.0
        $color = if ($i -lt $lit) { $accent } else { $dim }
        $pen = New-Object System.Drawing.Pen $color, $thick
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $x1 = $cx + [Math]::Cos($a) * ($outer - $len); $y1 = $cy + [Math]::Sin($a) * ($outer - $len)
        $x2 = $cx + [Math]::Cos($a) * $outer;         $y2 = $cy + [Math]::Sin($a) * $outer
        $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
        $pen.Dispose()
    }

    # ortadaki düz daire (ana düğme)
    $dot = $size * 0.30
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $accent), [single](($size - $dot) / 2.0), [single](($size - $dot) / 2.0), [single]$dot, [single]$dot)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = @{}
foreach ($s in $sizes) { $images[$s] = New-MarkPng $s }

$Out = [System.IO.Path]::GetFullPath($Out)
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $images[$s]
    $dim2 = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim2); $bw.Write([byte]$dim2); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($images[$s]) }
$bw.Flush(); $fs.Close()

if ($PreviewPng) {
    # iki zemin üzerinde hızlı bakış: koyu görev çubuğu ve açık Gezgin
    $sheet = New-Object System.Drawing.Bitmap 600, 200
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 18, 19, 24))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 242, 246))), 0, 100, 600, 100)
    $x = 14
    foreach ($s in 16, 24, 32, 48, 64, 128) {
        $ms = New-Object System.IO.MemoryStream (, $images[$s])
        $img = [System.Drawing.Image]::FromStream($ms)
        $w = [Math]::Min($s, 76)
        $g.DrawImage($img, $x, 12 + (76 - $w) / 2, $w, $w)
        $g.DrawImage($img, $x, 112 + (76 - $w) / 2, $w, $w)
        $x += 96
    }
    $g.Dispose(); $sheet.Save($PreviewPng)
}
"yazıldı: $Out ($((Get-Item $Out).Length) bayt, $($sizes.Count) boyut)"
