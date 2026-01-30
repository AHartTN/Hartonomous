#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Install Hartonomous PostgreSQL Extension (REQUIRES ADMIN)

.DESCRIPTION
    Installs the Hartonomous PostgreSQL extension files to the PostgreSQL installation directory.
    This script MUST run as Administrator to copy files to Program Files.

.PARAMETER PgVersion
    PostgreSQL version (default: 18)

.PARAMETER BuildDir
    Build directory containing the extension files (default: build\windows-release-threaded)

.EXAMPLE
    .\install-extension.ps1
    .\install-extension.ps1 -PgVersion 17
#>

param(
    [int]$PgVersion = 18,
    [string]$BuildDir = "build\windows-release-threaded"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Install Hartonomous Extension" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "✗ This script requires Administrator privileges" -ForegroundColor Red
    Write-Host ""
    Write-Host "Requesting elevation..." -ForegroundColor Yellow

    # Restart script as admin
    $scriptPath = $MyInvocation.MyCommand.Path
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -PgVersion $PgVersion -BuildDir `"$BuildDir`""

    Start-Process pwsh -Verb RunAs -ArgumentList $arguments -Wait

    exit $LASTEXITCODE
}

Write-Host "✓ Running as Administrator" -ForegroundColor Green
Write-Host ""

# Find PostgreSQL installation
$pgRoot = "C:\Program Files\PostgreSQL\$PgVersion"

if (-not (Test-Path $pgRoot)) {
    Write-Host "✗ PostgreSQL $PgVersion not found at $pgRoot" -ForegroundColor Red
    Write-Host ""
    Write-Host "Available versions:" -ForegroundColor Yellow
    Get-ChildItem "C:\Program Files\PostgreSQL" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Gray
    }
    exit 1
}

Write-Host "PostgreSQL Installation: $pgRoot" -ForegroundColor Cyan
Write-Host ""

# Define source and destination paths
$repoRoot = Split-Path -Parent $PSScriptRoot
$extensionSourceDir = Join-Path $repoRoot "PostgresExtension"
$buildPath = Join-Path $repoRoot $BuildDir

$sourceFiles = @{
    "hartonomous.dll" = Join-Path $buildPath "PostgresExtension\hartonomous.dll"
    "hartonomous.control" = Join-Path $extensionSourceDir "hartonomous.control"
    "hartonomous--0.1.0.sql" = Join-Path $extensionSourceDir "hartonomous--0.1.0.sql"
}

$destinations = @{
    "hartonomous.dll" = Join-Path $pgRoot "lib\hartonomous.dll"
    "hartonomous.control" = Join-Path $pgRoot "share\extension\hartonomous.control"
    "hartonomous--0.1.0.sql" = Join-Path $pgRoot "share\extension\hartonomous--0.1.0.sql"
}

# Verify source files exist
Write-Host "Checking source files..." -ForegroundColor Cyan
$missing = @()
foreach ($file in $sourceFiles.Keys) {
    $path = $sourceFiles[$file]
    if (Test-Path $path) {
        Write-Host "  ✓ $file" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $file (not found at $path)" -ForegroundColor Red
        $missing += $file
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "✗ Missing files. Run .\build.ps1 first to build the extension." -ForegroundColor Red
    exit 1
}
Write-Host ""

# Copy files
Write-Host "Installing extension files..." -ForegroundColor Cyan

foreach ($file in $sourceFiles.Keys) {
    $source = $sourceFiles[$file]
    $dest = $destinations[$file]

    Write-Host "  $file -> $dest" -ForegroundColor Gray

    try {
        # Create destination directory if needed
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }

        # Copy file
        Copy-Item -Path $source -Destination $dest -Force

        Write-Host "    ✓ Installed" -ForegroundColor Green
    } catch {
        Write-Host "    ✗ Failed: $_" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Extension files installed to PostgreSQL $PgVersion" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next: Create extension in database" -ForegroundColor Cyan
Write-Host "  psql -d hypercube -c 'CREATE EXTENSION hartonomous;'" -ForegroundColor Gray
Write-Host ""

# Pause if running in elevated window
if ($isAdmin -and -not $PSScriptRoot) {
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
