<#
  Generate-DiscordLogo.ps1 — render the MedicK's Might "MK" monogram as a 512x512 Discord server
  icon (brand: red M/K, gold ring, dark warm badge). Square fill so it works as both a square
  preview and Discord's circle crop. Bold so it stays legible at ~48px in the server list.
#>
param([string]$Out = "_design/discord_icon_MK.png", [int]$Size = 512)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing

$bmp = New-Object System.Drawing.Bitmap $Size, $Size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# Colors (brand)
$bg     = [System.Drawing.Color]::FromArgb(0x15, 0x10, 0x0d)   # near-black warm
$badge  = [System.Drawing.Color]::FromArgb(0x23, 0x1a, 0x14)   # card brown
$gold   = [System.Drawing.Color]::FromArgb(0xd4, 0xaf, 0x37)
$goldDk = [System.Drawing.Color]::FromArgb(0x9c, 0x7e, 0x26)
$red    = [System.Drawing.Color]::FromArgb(0xe2, 0x2d, 0x24)

# Square background
$g.FillRectangle((New-Object System.Drawing.SolidBrush $bg), 0, 0, $Size, $Size)

$cx = $Size / 2.0; $cy = $Size / 2.0
$r  = $Size * 0.46

# Badge disc with a subtle radial sheen (gold-tinted center → dark edge)
$discRect = New-Object System.Drawing.RectangleF (($cx-$r), ($cy-$r), (2*$r), (2*$r))
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddEllipse($discRect)
$pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
$pgb.CenterPoint = New-Object System.Drawing.PointF $cx, ($cy - $r*0.12)
$pgb.CenterColor = [System.Drawing.Color]::FromArgb(0x2e, 0x22, 0x18)
$pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0x16, 0x10, 0x0c))
$g.FillPath($pgb, $path)

# Gold rings: a thick outer ring + a thin inner accent
$penOuter = New-Object System.Drawing.Pen $gold, ($Size*0.028)
$penInner = New-Object System.Drawing.Pen $goldDk, ($Size*0.010)
$ro = $r - $Size*0.018
$ri = $r - $Size*0.075
$g.DrawEllipse($penOuter, ($cx-$ro), ($cy-$ro), (2*$ro), (2*$ro))
$g.DrawEllipse($penInner, ($cx-$ri), ($cy-$ri), (2*$ri), (2*$ri))

# "MK" monogram — heavy, red, centered. Drop shadow for depth.
$fontFamily = "Arial Black"
try { $testFont = New-Object System.Drawing.Font($fontFamily, 10) ; $testFont.Dispose() } catch { $fontFamily = "Impact" }
$font = New-Object System.Drawing.Font($fontFamily, ($Size*0.40), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$rectTxt = New-Object System.Drawing.RectangleF 0, ($Size*0.02), $Size, $Size
# shadow
$g.DrawString("MK", $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120,0,0,0))),
    (New-Object System.Drawing.RectangleF ($Size*0.012), ($Size*0.032), $Size, $Size), $sf)
# red MK
$g.DrawString("MK", $font, (New-Object System.Drawing.SolidBrush $red), $rectTxt, $sf)

$g.Dispose()
$outPath = Join-Path $root $Out
$outDir = Split-Path -Parent $outPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Saved $outPath ($Size x $Size)"
