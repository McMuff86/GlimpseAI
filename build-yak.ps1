# GlimpseAI Yak Package Builder
# Usage: .\build-yak.ps1
# Creates a .yak package ready for: yak push glimpseai-x.x.x-rh8_18-win.yak
#
# Prerequisites:
#   - Rhino 8 installed (includes yak.exe)
#   - dotnet SDK

$ErrorActionPreference = "Stop"

# --- Config ---
$ProjectDir   = "$PSScriptRoot\src\GlimpseAI"
$ProjectFile  = "$ProjectDir\GlimpseAI.csproj"
$ManifestFile = "$ProjectDir\manifest.yml"
$OutputDir    = "$ProjectDir\bin\Release\net7.0-windows"
$StagingDir   = "$PSScriptRoot\dist\yak-staging"
$DistDir      = "$PSScriptRoot\dist"

# --- Find Yak ---
$yakExe = $null
$rhinoPaths = @(
    "${env:ProgramFiles}\Rhino 8\System\yak.exe",
    "${env:ProgramFiles}\Rhino 7\System\yak.exe"
)
foreach ($p in $rhinoPaths) {
    if (Test-Path $p) { $yakExe = $p; break }
}
if (-not $yakExe) {
    $yakExe = (Get-Command yak -ErrorAction SilentlyContinue)?.Source
}
if (-not $yakExe) {
    Write-Host "ERROR: yak.exe not found. Install Rhino 8 or add yak to PATH." -ForegroundColor Red
    Write-Host "Expected: $($rhinoPaths[0])" -ForegroundColor Gray
    exit 1
}
Write-Host "Using yak: $yakExe" -ForegroundColor Gray

# --- Read version from manifest ---
$manifestContent = Get-Content $ManifestFile -Raw
if ($manifestContent -match 'version:\s*(.+)') {
    $version = $Matches[1].Trim()
} else {
    Write-Host "ERROR: Could not read version from manifest.yml" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== GlimpseAI Yak Package Builder ===" -ForegroundColor Cyan
Write-Host "Version: $version"
Write-Host ""

# --- Step 1: Build Release ---
Write-Host "[1/4] Building Release..." -ForegroundColor Yellow
dotnet build $ProjectFile -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED." -ForegroundColor Red
    exit 1
}
Write-Host "  Build OK" -ForegroundColor Green

# --- Step 2: Prepare staging directory ---
Write-Host "[2/4] Staging files..." -ForegroundColor Yellow
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir | Out-Null
New-Item -ItemType Directory -Path "$StagingDir\misc" -ErrorAction SilentlyContinue | Out-Null

# Copy manifest
Copy-Item $ManifestFile "$StagingDir\manifest.yml"

# Copy plugin + dependencies
Copy-Item "$OutputDir\GlimpseAI.rhp" $StagingDir
Copy-Item "$OutputDir\*.dll" $StagingDir -ErrorAction SilentlyContinue
Copy-Item "$OutputDir\GlimpseAI.deps.json" $StagingDir -ErrorAction SilentlyContinue

# Copy icon if exists
$iconPaths = @(
    "$PSScriptRoot\assets\icon.png",
    "$PSScriptRoot\icon.png",
    "$ProjectDir\icon.png"
)
foreach ($ip in $iconPaths) {
    if (Test-Path $ip) {
        Copy-Item $ip "$StagingDir\misc\icon.png"
        Write-Host "  Icon: $ip" -ForegroundColor Gray
        break
    }
}

# List staged files
$stagedFiles = Get-ChildItem $StagingDir -Recurse -File
Write-Host "  Staged $($stagedFiles.Count) files:" -ForegroundColor Gray
foreach ($f in $stagedFiles) {
    $rel = $f.FullName.Replace("$StagingDir\", "")
    $sizeKB = [math]::Round($f.Length / 1KB, 1)
    Write-Host "    $rel ($sizeKB KB)" -ForegroundColor DarkGray
}

# --- Step 3: Build Yak package ---
Write-Host "[3/4] Building .yak package..." -ForegroundColor Yellow
if (!(Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }

Push-Location $StagingDir
try {
    & $yakExe build --platform win
    if ($LASTEXITCODE -ne 0) {
        Write-Host "YAK BUILD FAILED." -ForegroundColor Red
        exit 1
    }
    
    # Move .yak to dist/
    $yakFile = Get-ChildItem -Filter "*.yak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($yakFile) {
        $destPath = "$DistDir\$($yakFile.Name)"
        Move-Item $yakFile.FullName $destPath -Force
        Write-Host "  Package: dist\$($yakFile.Name)" -ForegroundColor Green
    } else {
        Write-Host "  ERROR: No .yak file generated" -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

# --- Step 4: Cleanup staging ---
Write-Host "[4/4] Cleanup..." -ForegroundColor Yellow
Remove-Item $StagingDir -Recurse -Force
Write-Host "  Done" -ForegroundColor Green

# --- Summary ---
$yakSize = [math]::Round((Get-Item $destPath).Length / 1MB, 2)
Write-Host ""
Write-Host "=== Package Ready ===" -ForegroundColor Cyan
Write-Host "  File:    dist\$($yakFile.Name)"
Write-Host "  Size:    $yakSize MB"
Write-Host "  Version: $version"
Write-Host ""
Write-Host "Install locally:" -ForegroundColor Gray
Write-Host "  yak install `"$destPath`"" -ForegroundColor White
Write-Host ""
Write-Host "Publish to food4rhino:" -ForegroundColor Gray
Write-Host "  yak login" -ForegroundColor White
Write-Host "  yak push `"$destPath`"" -ForegroundColor White
Write-Host ""
