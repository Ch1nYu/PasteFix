<#
產生 PasteFix 的托盤圖示 app.ico。

記號是「一條左邊界豎線 + 三條長短不一的橫線全部靠上去」——直接畫出 Dedent 做的
事。全部是矩形，沒有曲線也沒有細節，所以 16px 縮下去不會糊。

白色記號挖空在圓角方塊底上：托盤圖示是靜態點陣圖，不會跟著淺色／深色工作列變，
填滿的底色是唯一能保證兩種工作列都看得見的做法。

小尺寸（16/24/32）關掉反鋸齒、座標對齊整數像素，讓線條銳利；大尺寸才開。

用法：
  pwsh -File tools\make-icon.ps1
  pwsh -File tools\make-icon.ps1 -Preview   # 另外輸出放大的 PNG 方便肉眼檢查
#>

[CmdletBinding()]
param(
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outIco = Join-Path $PSScriptRoot '..\PasteFix\dotnet\src\PasteFix\app.ico'
$tile   = [System.Drawing.Color]::FromArgb(255, 76, 110, 245)   # indigo，深淺工作列都夠對比
$ink    = [System.Drawing.Color]::White

# 16 格為基準的版面；其餘尺寸等比放大。
#   豎線 x=3、y=3..11
#   橫線起點 x=5，各自的 y 與寬度如下
$RuleX = 3; $RuleY = 4; $RuleH = 8; $RuleW = 1
$Bars = @(
    @{ Y = 4;  W = 8 }
    @{ Y = 7;  W = 4 }
    @{ Y = 10; W = 7 }
)
$BarX = 5; $BarH = 2

function New-IconBitmap([int]$size) {
    $s = $size / 16.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)

        # 底的圓角一律開反鋸齒——16px 下沒開的話弧線會變成缺角。
        # 記號則永遠對齊整數像素，不受這個設定影響。
        $g.SmoothingMode = 'AntiAlias'

        # 圓角方塊底
        $r = [Math]::Max(2, [int][Math]::Round($size * 0.2))
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $path.AddArc(0, 0, $d, $d, 180, 90)
        $path.AddArc($size - $d, 0, $d, $d, 270, 90)
        $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
        $path.AddArc(0, $size - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $brush = New-Object System.Drawing.SolidBrush($tile)
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()

        # 記號：整數像素、不吃反鋸齒，線條才銳利
        $g.SmoothingMode = 'None'
        $pen = New-Object System.Drawing.SolidBrush($ink)
        function Rect([double]$x, [double]$y, [double]$w, [double]$h) {
            $g.FillRectangle($pen,
                [int][Math]::Round($x * $s), [int][Math]::Round($y * $s),
                [Math]::Max(1, [int][Math]::Round($w * $s)),
                [Math]::Max(1, [int][Math]::Round($h * $s)))
        }
        Rect $RuleX $RuleY $RuleW $RuleH
        foreach ($b in $Bars) { Rect $BarX $b.Y $b.W $BarH }
        $pen.Dispose()
    }
    finally { $g.Dispose() }
    return $bmp
}

# --- 手工組出多尺寸 .ico（System.Drawing 不提供寫入多張影格的 API）---
$sizes = @(16, 24, 32, 48, 64, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $sz; Bytes = $ms.ToArray() }
    $ms.Dispose(); $bmp.Dispose()
}

$fs = [System.IO.File]::Create((New-Item -ItemType File -Path $outIco -Force).FullName)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)   # ICONDIR
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $bw.Write([byte]$(if ($p.Size -ge 256) { 0 } else { $p.Size }))          # width
        $bw.Write([byte]$(if ($p.Size -ge 256) { 0 } else { $p.Size }))          # height
        $bw.Write([byte]0); $bw.Write([byte]0)                                   # palette, reserved
        $bw.Write([uint16]1); $bw.Write([uint16]32)                              # planes, bpp
        $bw.Write([uint32]$p.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $p.Bytes.Length
    }
    foreach ($p in $pngs) { $bw.Write($p.Bytes) }
}
finally { $bw.Dispose(); $fs.Dispose() }

Write-Host "已產生 $((Resolve-Path $outIco).Path)（$($sizes -join ', ') px）" -ForegroundColor Green

if ($Preview) {
    foreach ($sz in @(16, 32)) {
        $src = New-IconBitmap $sz
        $scale = 16
        $out = New-Object System.Drawing.Bitmap(($sz * $scale), ($sz * $scale))
        $g = [System.Drawing.Graphics]::FromImage($out)
        $g.InterpolationMode = 'NearestNeighbor'
        $g.PixelOffsetMode = 'Half'
        $g.DrawImage($src, 0, 0, $sz * $scale, $sz * $scale)
        $g.Dispose()
        $path = Join-Path $PSScriptRoot "..\icon-preview-$sz.png"
        $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $out.Dispose(); $src.Dispose()
        Write-Host "預覽：$path" -ForegroundColor DarkGray
    }
}
