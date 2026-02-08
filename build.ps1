# GlimpseAI Build & Package Script
# Usage: .\build.ps1 [-Configuration Release] [-Package] [-Clean]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Package,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ProjectDir = "$PSScriptRoot\src\GlimpseAI"
$ProjectFile = "$ProjectDir\GlimpseAI.csproj"
$OutputDir = "$ProjectDir\bin\$Configuration\net7.0-windows"
$PackageDir = "$PSScriptRoot\dist"

Write-Host "=== GlimpseAI Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"

# Clean
if ($Clean) {
    Write-Host "`nCleaning..." -ForegroundColor Yellow
    dotnet clean $ProjectFile -c $Configuration --nologo -v q
    if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }
}

# Build
Write-Host "`nBuilding..." -ForegroundColor Yellow
dotnet build $ProjectFile -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED." -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded." -ForegroundColor Green

# Package as Yak
if ($Package) {
    Write-Host "`nPackaging Yak..." -ForegroundColor Yellow

    # Create dist folder
    if (!(Test-Path $PackageDir)) { New-Item -ItemType Directory -Path $PackageDir | Out-Null }

    # Check if yak CLI is available
    $yakPath = Get-Command yak -ErrorAction SilentlyContinue
    if ($yakPath) {
        Push-Location $OutputDir
        yak build --platform win
        $yakFile = Get-ChildItem -Filter "*.yak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($yakFile) {
            Move-Item $yakFile.FullName "$PackageDir\$($yakFile.Name)" -Force
            Write-Host "Yak package created: dist\$($yakFile.Name)" -ForegroundColor Green
        }
        Pop-Location
    }
    else {
        Write-Host "Yak CLI not found. Creating manual zip package instead..." -ForegroundColor Yellow
        Write-Host "Install Yak: https://developer.rhino3d.com/guides/yak/creating-a-rhino-plugin-package/" -ForegroundColor Gray

        # Manual packaging: zip the output directory
        $version = (Select-Xml -Path $ProjectFile -XPath "//Version").Node.InnerText
        $zipName = "glimpseai-$version-rh8-win.zip"
        $filesToPackage = @(
            "$OutputDir\GlimpseAI.rhp",
            "$OutputDir\manifest.yml",
            "$OutputDir\GlimpseAI.deps.json",
            "$OutputDir\GlimpseAI.runtimeconfig.json"
        )
        # Include any dependency DLLs
        $depDlls = Get-ChildItem "$OutputDir\*.dll" -ErrorAction SilentlyContinue
        foreach ($dll in $depDlls) { $filesToPackage += $dll.FullName }

        Compress-Archive -Path $filesToPackage -DestinationPath "$PackageDir\$zipName" -Force
        Write-Host "Zip package created: dist\$zipName" -ForegroundColor Green
    }
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
Write-Host "Output: $OutputDir\GlimpseAI.rhp"
if ($Package) { Write-Host "Package: $PackageDir" }
