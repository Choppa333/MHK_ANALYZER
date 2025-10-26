# Build script for MGK Analyzer Installer
# This script builds the Release version of the installer

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

Write-Host "================================" -ForegroundColor Cyan
Write-Host "MGK Analyzer Installer Build" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDir = Split-Path -Parent $scriptDir

Write-Host "Script Directory: $scriptDir" -ForegroundColor Gray
Write-Host "Solution Directory: $solutionDir" -ForegroundColor Gray
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Platform: $Platform" -ForegroundColor Gray

# Check if WiX is installed
$wixPath = "C:\Program Files (x86)\WiX Toolset v3.14\bin"
if (-not (Test-Path $wixPath)) {
    Write-Host "ERROR: WiX Toolset not found at $wixPath" -ForegroundColor Red
    Write-Host "Please install WiX Toolset from: https://wixtoolset.org/releases/" -ForegroundColor Yellow
    exit 1
}

Write-Host "WiX Toolset found at: $wixPath" -ForegroundColor Green

# Build the main application first (Release configuration)
Write-Host "`nBuilding MGK_Analyzer project..." -ForegroundColor Cyan
$mgkProjectPath = Join-Path $solutionDir "MGK_Analyzer.csproj"
& dotnet build $mgkProjectPath -c Release -p:Platform=x64

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build MGK_Analyzer project" -ForegroundColor Red
    exit 1
}

Write-Host "MGK_Analyzer project built successfully" -ForegroundColor Green

# Common paths
$publishDir = Join-Path $solutionDir "publish\win-x64"
$objDir = Join-Path $scriptDir "obj\$Configuration"
$binDir = Join-Path $scriptDir "bin\$Configuration"

# Ensure directories
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $objDir | Out-Null
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

# Publish self-contained app
Write-Host "`nPublishing app (self-contained win-x64) ..." -ForegroundColor Cyan
& dotnet publish $mgkProjectPath -c $Configuration -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed" -ForegroundColor Red
    exit 1
}

# Manual WiX build pipeline using heat/candle/light (avoids MSBuild Wix.targets issues)

# 1) Heat: harvest published files into a ComponentGroup
$harvestWxs = Join-Path $objDir "HarvestedFiles.wxs"
Write-Host "Running heat.exe ..." -ForegroundColor Cyan
& "$wixPath\heat.exe" dir $publishDir -dr INSTALLFOLDER -cg HarvestedFiles -gg -sfrag -srd -var var.PublishDir -out $harvestWxs
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: heat.exe failed" -ForegroundColor Red
    exit 1
}

# 2) Candle: compile .wxs to .wixobj
$productWxs = Join-Path $scriptDir "Product.wxs"
Write-Host "Running candle.exe ..." -ForegroundColor Cyan
& "$wixPath\candle.exe" -nologo -arch x64 -ext WixUIExtension "-dPublishDir=$publishDir" "-dWixUILicenseRtf=$scriptDir\License.rtf" -out "$objDir\" $productWxs $harvestWxs
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: candle.exe failed" -ForegroundColor Red
    exit 1
}

# 3) Light: link to MSI
$productWixobj = Join-Path $objDir "Product.wixobj"
$harvestWixobj = Join-Path $objDir "HarvestedFiles.wixobj"
$msiOut = Join-Path $binDir "MGK_Analyzer.msi"
Write-Host "Running light.exe ..." -ForegroundColor Cyan
& "$wixPath\light.exe" -nologo -ext WixUIExtension -cultures:en-us -out $msiOut $productWixobj $harvestWixobj
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: light.exe failed" -ForegroundColor Red
    exit 1
}

# Locate the generated MSI
$msiFile = $msiOut

if (Test-Path $msiFile) {
    Write-Host "`n================================" -ForegroundColor Green
    Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "================================" -ForegroundColor Green
    Write-Host "Installer location: $msiFile" -ForegroundColor Cyan
    Write-Host "`nTo install, run: msiexec /i `"$msiFile`"" -ForegroundColor Yellow
    Write-Host "Or double-click the MSI file" -ForegroundColor Yellow
} else {
    Write-Host "`nERROR: MSI file not found at expected location" -ForegroundColor Red
    Write-Host "Expected: $msiFile" -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild completed at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
