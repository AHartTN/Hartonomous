#!/usr/bin/env pwsh
# Hartonomous Cortex Build Script

$ErrorActionPreference = "Stop"

Write-Host "=== Building Cortex Extension ===" -ForegroundColor Cyan

# Check PostgreSQL dev headers
Write-Host "Checking PostgreSQL development headers..." -ForegroundColor Yellow
try {
    $pgConfig = pg_config --includedir
    Write-Host "  ✓ Headers found: $pgConfig" -ForegroundColor Green
} catch {
    Write-Host "  ✗ PostgreSQL dev headers not found" -ForegroundColor Red
    Write-Host "    Install postgresql-server-dev-16" -ForegroundColor Gray
    exit 1
}

# Navigate to cortex directory
Set-Location cortex

# Build extension
Write-Host "`nBuilding Cortex extension..." -ForegroundColor Yellow
if ($IsWindows) {
    Write-Host "  → Windows build requires Visual Studio" -ForegroundColor Yellow
    Write-Host "  → Run: nmake /f Makefile.win" -ForegroundColor Gray
} else {
    make
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Build successful" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Build failed" -ForegroundColor Red
        exit 1
    }
}

# Install extension
Write-Host "`nInstalling extension..." -ForegroundColor Yellow
if (!$IsWindows) {
    sudo make install
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Extension installed" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Installation failed" -ForegroundColor Red
        exit 1
    }
}

Set-Location ..

# Enable extension in database
Write-Host "`nEnabling Cortex in database..." -ForegroundColor Yellow
psql -U postgres -d hartonomous -c "CREATE EXTENSION IF NOT EXISTS cortex;"

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Extension enabled" -ForegroundColor Green
} else {
    Write-Host "  ✗ Failed to enable extension" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Cortex Build Complete ===" -ForegroundColor Cyan
Write-Host "Extension: cortex.so installed in PostgreSQL" -ForegroundColor Gray
