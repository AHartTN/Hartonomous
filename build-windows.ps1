# PowerShell build script for Windows
param(
    [string]$Preset = "windows-release-max-perf"
)

Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Building Hartonomous (Windows)" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Get repo root
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot

Write-Host "Repository: $RepoRoot" -ForegroundColor Cyan
Write-Host "Preset: $Preset" -ForegroundColor Cyan
Write-Host ""

# Check for Visual Studio
Write-Host "Detecting Visual Studio..." -ForegroundColor Cyan

$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $VsWhere)) {
    Write-Host "ERROR: Visual Studio not found" -ForegroundColor Red
    exit 1
}

$VsPath = & $VsWhere -latest -property installationPath
if (-not $VsPath) {
    Write-Host "ERROR: Could not find Visual Studio installation" -ForegroundColor Red
    exit 1
}

Write-Host "Found Visual Studio: $VsPath" -ForegroundColor Green

# Find vcvarsall.bat
$VcVarsAll = Join-Path $VsPath "VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path $VcVarsAll)) {
    Write-Host "ERROR: vcvarsall.bat not found at $VcVarsAll" -ForegroundColor Red
    exit 1
}

Write-Host "Found vcvarsall.bat" -ForegroundColor Green
Write-Host ""

# Setup VS environment and run cmake
Write-Host "Setting up Visual Studio environment..." -ForegroundColor Cyan

# Create a temporary batch file to setup environment and run cmake
$TempBatch = Join-Path $env:TEMP "hartonomous_build.bat"
@"
@echo off
call "$VcVarsAll" x64
cd /d "$RepoRoot"
cmake --preset $Preset
if errorlevel 1 exit /b 1
cmake --build build\$Preset --config Release -j %NUMBER_OF_PROCESSORS%
if errorlevel 1 exit /b 1
"@ | Out-File -FilePath $TempBatch -Encoding ASCII

# Run the batch file
& cmd.exe /c $TempBatch
$BuildResult = $LASTEXITCODE

Remove-Item $TempBatch -ErrorAction SilentlyContinue

if ($BuildResult -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Build failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build successful!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

# Check for output
$BuildDir = "build\$Preset"
$EngineLib = Join-Path $BuildDir "Engine\Release\engine.lib"

if (Test-Path $EngineLib) {
    Write-Host "Engine library: $EngineLib" -ForegroundColor Green
} else {
    Write-Host "WARNING: Engine library not found at $EngineLib" -ForegroundColor Yellow
    # Try other locations
    $AltLib = Join-Path $BuildDir "Engine\engine.lib"
    if (Test-Path $AltLib) {
        Write-Host "Found at: $AltLib" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Next step: Run database setup" -ForegroundColor Cyan
Write-Host ""
