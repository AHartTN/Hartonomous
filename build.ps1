#!/usr/bin/env pwsh
# Hartonomous Build Script (Windows/PowerShell)
# Usage: .\build.ps1 [preset] [target]
#
# Examples:
#   .\build.ps1                    # Default: release-native, all targets
#   .\build.ps1 debug              # Debug build
#   .\build.ps1 release-native engine  # Build only engine target
#   .\build.ps1 release-portable   # Portable build (no -march=native)

param(
    [string]$Preset = "windows-release-threaded",
    [string]$Target = "",
    [int]$Jobs = 0,
    [switch]$Clean,
    [switch]$Test,
    [switch]$Install,
    [switch]$Help
)

function Show-Help {
    Write-Host @"
Hartonomous Build Script

Usage: .\build.ps1 [OPTIONS]

Options:
  -Preset <name>    Build preset (default: windows-release-threaded)
                    Available:
                        windows-release-threaded
                        windows-release-max-perf
                        windows-release-portable
                        windows-debug
                        linux-release-max-perf
                        linux-release-portable
                        linux-debug
  -Target <name>    Specific target to build (default: all)
  -Jobs <N>         Number of parallel jobs (default: auto)
  -Clean            Clean build directory before building
  -Test             Run tests after building
  -Install          Install after building
  -Help             Show this help message

Examples:
  .\build.ps1                         # Build everything (release-native)
  .\build.ps1 -Preset debug -Test    # Debug build + run tests
  .\build.ps1 -Clean -Test            # Clean, build, test
  .\build.ps1 -Target engine          # Build only engine
  .\build.ps1 -Preset release-portable -Install  # Build + install portable version

"@
    exit 0
}

if ($Help) {
    Show-Help
}

$ErrorActionPreference = "Stop"

# Colors
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Error { Write-Host $args -ForegroundColor Red }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }

Write-Info "=== Hartonomous Build Script ==="
Write-Info ""

& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
# C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe
#Call MSVC environment
# If they have it, the VS installation path is found via vswhere 
$vswhere = Join-Path "${env:ProgramFiles(x86)}" "Microsoft Visual Studio" "Installer" "vswhere.exe"
$vspath = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcvarsPath = Join-Path $vspath "VC" "Auxiliary" "Build" "vcvarsall.bat"

#Attempt 1
#& $vcvarsPath arm64

#Attempt 2
# Import VS environment variables into PowerShell
# try {
#     cmd /c "`"$vcvarsPath`" -arch=amd64 -no_logo && set" | ForEach-Object {
#         if ($_ -match '^([^=]+)=(.*)$') {
#             [Environment]::SetEnvironmentVariable($matches[1], $matches[2], "Process")
#         }
#     }
#     Write-Verbose "Visual Studio environment initialized from: $vsPath"
# } catch {
#     Write-Warning "Failed to initialize Visual Studio environment (C++ builds may fail)"
# }

#Attempt 3
$devShellPath = Join-Path $vsPath "Common7" "Tools" "Microsoft.VisualStudio.DevShell.dll"

if (Test-Path $devShellPath) {
    Import-Module $devShellPath
    # This cmdlet loads the environment natively into PowerShell
    Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=amd64"
}



# Check CMake
Write-Info "Checking CMake dependencies..."
try {
    $cmakeVersion = cmake --version | Select-String -Pattern "version (\d+\.\d+\.\d+)"
    Write-Success "✓ CMake found: $($cmakeVersion.Matches.Groups[1].Value)"
}
catch {
    Write-Error "✗ CMake not found. Please install CMake 3.20+."
    exit 1
}

# Check Intel OneAPI
$oneapiRoot = Join-Path ${env:ProgramFiles(x86)} "Intel" "oneAPI"

$components = @{
    "MKL"      = Join-Path $oneapiRoot "mkl\latest"
    "Compiler" = Join-Path $oneapiRoot "compiler\latest"
    "TBB"      = Join-Path $oneapiRoot "tbb\latest"
    "IPP"      = Join-Path $oneapiRoot "ipp\latest"
    "MPI"      = Join-Path $oneapiRoot "mpi\latest"
}

foreach ($name in $components.Keys) {
    $path = $components[$name]
    if (Test-Path $path) {
        Write-Output "$name installed at $path"
    } else {
        Write-Output "$name NOT installed"
    }
}

$oneapiVarsPath = Join-Path $oneapiRoot "setvars.bat"

# try {
#     if (Test-Path $oneapiVarsPath) {
#         Write-Success "✓ Intel OneAPI found"

#         # Attempt 1
#         #& "'$oneapiVarsPath'"

#         #Attempt 2
#         # Source environment
#         # cmd /c "`"$oneapiVarsPath`" && set" | ForEach-Object {
#         #     if ($_ -match "^(.*?)=(.*)$") {
#         #         Set-Content "env:\$($matches[1])" $matches[2]
#         #     }
#         # }

#         # Attempt 3
#         . $oneapiVarsPath --force
#     }
#     else {
#         Write-Warning "⚠ Intel OneAPI not found (MKL will use defaults)"
#     }
# }
# catch {
#     Write-Warning "⚠ Could not initialize Intel OneAPI"
# }

# Attempt 4
# 2. Load Intel oneAPI
if (Test-Path $oneapiVarsPath) {
    Write-Info "Initializing Intel oneAPI environment..."
    
    # We must use cmd.exe to run the batch, then capture the result
    # We use a unique separator string to cleanly split the output
    $guid = [Guid]::NewGuid().ToString()
    
    # Command: Run setvars.bat, print separator, then print all variables
    $cmd = "`"$oneapiVarsPath`" --force > NUL && echo $guid && set"
    
    $output = cmd /c $cmd
    
    # Find where our variables start
    $startParams = $false
    foreach ($line in $output) {
        if ($line -eq $guid) {
            $startParams = $true
            continue
        }
        
        if ($startParams -and $line -match '^(?<name>[^=]+)=(?<value>.*)$') {
            $name = $matches['name']
            $value = $matches['value']
            Write-Info "Setting $name observed with value $value"
            # Filter out known garbage or read-only variables to avoid errors
            if ($name -match '^(?!(PROMPT|ERRORLEVEL|CMDLINE)$).*') {
                [Environment]::SetEnvironmentVariable($name, $value, "Process")
            }
        }
    }
    
    # Quick verify
    if (Get-Command icx.exe -ErrorAction SilentlyContinue) {
        Write-Success "✓ Intel Compiler (icx) is now in PATH"
    } else {
        Write-Warning "⚠ Failed to load Intel Compiler into PATH"
    }
} else {
    Write-Warning "⚠ Intel setvars.bat not found"
}

# Check submodules
Write-Info ""
Write-Info "Checking submodules..."
$submodules = @(
    "Engine/external/blake3",
    "Engine/external/eigen",
    "Engine/external/hnswlib",
    "Engine/external/spectra",
    "Engine/external/json")
$missingSubmodules = @()

foreach ($submodule in $submodules) {
    if (Test-Path $submodule) {
        Write-Success "✓ $submodule"
    }
    else {
        Write-Error "✗ $submodule (missing)"
        $missingSubmodules += $submodule
    }
}

if ($missingSubmodules.Count -gt 0) {
    Write-Warning ""
    Write-Warning "Missing submodules detected. Initializing..."
    git submodule update --init --recursive
}

# Clean if requested
if ($Clean) {
    Write-Info ""
    Write-Info "Cleaning build directory..."
    $buildDir = "build/$Preset"
    if (Test-Path $buildDir) {
        Remove-Item -Recurse -Force $buildDir
        Write-Success "✓ Cleaned $buildDir"
    }
}

# Configure
Write-Info ""
Write-Info "Configuring build (preset: $Preset)..."
try {
    cmake --preset $Preset
    Write-Success "✓ Configuration complete"
}
catch {
    Write-Error "✗ Configuration failed"
    exit 1
}

# Build
Write-Info ""
$buildDir = "build/$Preset"

if ($Target) {
    Write-Info "Building target: $Target..."
    $buildCmd = "cmake --build $buildDir --target $Target"
}
else {
    Write-Info "Building all targets..."
    $buildCmd = "cmake --build $buildDir"
}

if ($Jobs -gt 0) {
    $buildCmd += " -j $Jobs"
}
else {
    $buildCmd += " -j"  # Auto-detect
}

try {
    Invoke-Expression $buildCmd
    Write-Success "✓ Build complete"
}
catch {
    Write-Error "✗ Build failed"
    exit 1
}

# Test
if ($Test) {
    Write-Info ""
    Write-Info "Running tests..."
    try {
        Push-Location $buildDir
        ctest --output-on-failure
        Pop-Location
        Write-Success "✓ Tests passed"
    }
    catch {
        Pop-Location
        Write-Error "✗ Tests failed"
        exit 1
    }
}

# Install
if ($Install) {
    Write-Info ""
    Write-Info "Installing..."
    try {
        cmake --install $buildDir
        Write-Success "✓ Installation complete"
    }
    catch {
        Write-Error "✗ Installation failed"
        Write-Warning "Note: May require administrator privileges"
        exit 1
    }
}

Write-Info ""
Write-Success "=== Build Complete ==="
Write-Info ""
Write-Info "Build directory: $buildDir"
Write-Info "Preset: $Preset"
if ($Target) {
    Write-Info "Target: $Target"
}
Write-Info ""
Write-Info "Next steps:"
Write-Info "  1. Run tests: .\build.ps1 -Test"
Write-Info "  2. Install: .\build.ps1 -Install"
Write-Info "  3. Setup database: .\scripts\setup-database.ps1"
Write-Info ""
