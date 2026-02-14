# Resize image to 256x256 for Yak icon
# Usage: .\resize-icon.ps1 input.png
# Output: assets\icon.png

param([Parameter(Mandatory)][string]$InputFile)

if (!(Test-Path $InputFile)) { Write-Host "File not found: $InputFile" -ForegroundColor Red; exit 1 }

$outDir = "$PSScriptRoot\assets"
if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile((Resolve-Path $InputFile))
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.DrawImage($img, 0, 0, 256, 256)
$g.Dispose()
$img.Dispose()

$outPath = "$outDir\icon.png"
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "Saved: $outPath (256x256)" -ForegroundColor Green
