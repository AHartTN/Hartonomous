#!/usr/bin/env pwsh
# Hartonomous Build Script (Windows)
# Usage: .\build.ps1 [options]

param(
    [string]$Preset = "windows-release-threaded",
    [string]$Target = "",
    [int]$Jobs = 0,
    [switch]$Clean,
    [switch]$Test,
    [switch]$Install,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# ==============================================================================
#  Helpers
# ==============================================================================

function Write-Success { Write-Host "✓ $args" -ForegroundColor Green }
function Write-ErrorMsg { Write-Host "✗ $args" -ForegroundColor Red }
function Write-Info { Write-Host "ℹ $args" -ForegroundColor Cyan }
function Write-Warning { Write-Host "⚠ $args" -ForegroundColor Yellow }
function Write-Step { Write-Host "`n=== $args ===" -ForegroundColor Magenta }

function Show-Help {
    Write-Host @"
Hartonomous Build Script (Windows)

Usage: .\build.ps1 [OPTIONS]

Options:
  -Preset <name>    CMake preset (default: windows-release-threaded)
                    Available: windows-release-threaded, windows-release-max-perf
  -Target <name>    Specific CMake target (default: all)
  -Jobs <N>         Parallel jobs (default: auto)
  -Clean            Clean build directories before building
  -Test             Run C++ and .NET tests
  -Install          Install C++ artifacts
  -Help             Show this message
"@
    exit 0
}

if ($Help) { Show-Help }

# ==============================================================================
#  Environment Setup
# ==============================================================================

Write-Step "Environment Setup"

# 1. Visual Studio (via vswhere)
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio" "Installer" "vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-ErrorMsg "vswhere.exe not found. Is Visual Studio installed?"
    exit 1
}

$vsPath = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    Write-ErrorMsg "No suitable Visual Studio installation found (requires C++ tools)."
    exit 1
}
Write-Success "Found Visual Studio: $vsPath"

# Load VS Environment via Microsoft.VisualStudio.DevShell.dll
$devShellPath = Join-Path $vsPath "Common7" "Tools" "Microsoft.VisualStudio.DevShell.dll"
if (Test-Path $devShellPath) {
    Import-Module $devShellPath
    Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=amd64"
    Write-Success "Visual Studio environment loaded (x64)"
} else {
    Write-Warning "Could not load DevShell. Falling back to vcvarsall.bat..."
    $vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvarsall.bat"
    cmd /c "`"$vcvars`" x64 > nul && set" | ForEach-Object {
        if ($_ -match '^(?<name>[^=]+)=(?<value>.*)$') {
            [Environment]::SetEnvironmentVariable($matches['name'], $matches['value'], "Process")
        }
    }
}

# 2. Intel OneAPI
$oneapiBase = "C:\Program Files (x86)\Intel\oneAPI"
$setvars = Join-Path $oneapiBase "setvars.bat"
if (Test-Path $setvars) {
    Write-Info "Initializing Intel OneAPI..."
    # Run setvars and capture environment diff
    $guid = [Guid]::NewGuid().ToString()
    $cmd = "`"$setvars`" --force > NUL && echo $guid && set"
    $output = cmd /c $cmd
    $capturing = $false
    foreach ($line in $output) {
        if ($line -eq $guid) { $capturing = $true; continue }
        if ($capturing -and $line -match '^(?<name>[^=]+)=(?<value>.*)$') {
            $name = $matches['name']
            if ($name -notmatch '^(PROMPT|ERRORLEVEL|CMDLINE)$') {
                [Environment]::SetEnvironmentVariable($name, $matches['value'], "Process")
            }
        }
    }
    Write-Success "Intel OneAPI environment loaded"
} else {
    Write-Warning "Intel OneAPI not found at default location. Building without MKL/ICC optimizations."
}

# 3. CMake
try {
    $cmakeVer = cmake --version | Select-String "version (\d+\.\d+\.\d+)"
    Write-Success "CMake found: $($cmakeVer.Matches.Groups[1].Value)"
} catch {
    Write-ErrorMsg "CMake not found. Please install CMake 3.20+."
    exit 1
}

# 4. .NET SDK
try {
    $dotnetVer = dotnet --version
    Write-Success ".NET SDK found: $dotnetVer"
} catch {
    Write-ErrorMsg ".NET SDK not found."
    exit 1
}

# ==============================================================================
#  Build Execution
# ==============================================================================

# --- Cleaning ---
if ($Clean) {
    Write-Step "Cleaning"
    $dirs = @("build/$Preset", "Hartonomous.API/bin", "Hartonomous.API/obj", "Hartonomous.Core/bin", "Hartonomous.Core/obj")
    foreach ($d in $dirs) {
        if (Test-Path $d) {
            Remove-Item -Recurse -Force $d
            Write-Success "Removed $d"
        }
    }
}

# --- C++ Build ---
Write-Step "Building C++ Engine ($Preset)"
$buildDir = "build/$Preset"

Write-Info "Configuring..."
cmake --preset $Preset
if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed" }

Write-Info "Compiling..."
$buildCmd = "cmake --build $buildDir"
if ($Target) { $buildCmd += " --target $Target" }
if ($Jobs -gt 0) { $buildCmd += " -j $Jobs" } else { $buildCmd += " -j" }

Invoke-Expression $buildCmd
if ($LASTEXITCODE -ne 0) { throw "C++ Build failed" }
Write-Success "C++ Build Complete"

# --- .NET Build ---
Write-Step "Building .NET Solution"
Write-Info "Restoring dependencies..."
dotnet restore Hartonomous.sln
if ($LASTEXITCODE -ne 0) { throw ".NET Restore failed" }

Write-Info "Building solution (Release)..."
dotnet build Hartonomous.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw ".NET Build failed" }
Write-Success ".NET Build Complete"

# ==============================================================================
#  Tests & Installation
# ==============================================================================

if ($Test) {
    Write-Step "Running Tests"
    
    # C++ Tests
    Write-Info "Running C++ Tests..."
    Push-Location $buildDir
    ctest --output-on-failure
    if ($LASTEXITCODE -ne 0) { Write-ErrorMsg "C++ Tests failed"; exit 1 }
    Pop-Location
    Write-Success "C++ Tests Passed"

    # .NET Tests (if any test projects exist)
    # Write-Info "Running .NET Tests..."
    # dotnet test Hartonomous.sln -c Release --no-build
}

if ($Install) {
    Write-Step "Installing"
    cmake --install $buildDir
    if ($LASTEXITCODE -ne 0) { throw "Installation failed" }
    Write-Success "Installation Complete"
}

Write-Step "All Tasks Completed Successfully"
