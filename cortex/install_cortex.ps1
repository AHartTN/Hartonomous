#!/usr/bin/env pwsh
# Install Cortex Extension (requires admin)
# Idempotent installation script

$ErrorActionPreference = "Stop"

Write-Host "=== Cortex Extension Installation ===" -ForegroundColor Cyan



# PostgreSQL paths
$PG_HOME = "D:/PostgreSQL/18"
$LIB_DIR = "$PG_HOME/lib"
$SHARE_DIR = "$PG_HOME/share/extension"

# Build cortex.dll if not exists or out of date
Write-Host "`nChecking Cortex build..." -ForegroundColor Yellow
$dllPath = "cortex/build/Release/cortex.dll"
$srcModified = (Get-Item cortex/cortex.c).LastWriteTime

if (-not (Test-Path $dllPath) -or (Get-Item $dllPath).LastWriteTime -lt $srcModified) {
    Write-Host "  → Building cortex.dll..." -ForegroundColor Gray
    Push-Location cortex
    if (Test-Path build) { Remove-Item -Recurse -Force build }
    cmake -B build -G "Visual Studio 18 2026" -DCMAKE_BUILD_TYPE=Release
    cmake --build build --config Release
    Pop-Location
    
    if (Test-Path $dllPath) {
        Write-Host "  ✓ Build successful" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Build failed" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  ✓ cortex.dll up to date" -ForegroundColor Green
}

# Install DLL
Write-Host "`nInstalling cortex.dll..." -ForegroundColor Yellow
Copy-Item -Force $dllPath $LIB_DIR/
if (Test-Path "$LIB_DIR/cortex.dll") {
    Write-Host "  ✓ Installed to $LIB_DIR" -ForegroundColor Green
} else {
    Write-Host "  ✗ Installation failed" -ForegroundColor Red
    exit 1
}

# Install control file
Write-Host "`nInstalling control file..." -ForegroundColor Yellow
Copy-Item -Force cortex/cortex.control $SHARE_DIR/
if (Test-Path "$SHARE_DIR/cortex.control") {
    Write-Host "  ✓ Installed to $SHARE_DIR" -ForegroundColor Green
} else {
    Write-Host "  ✗ Installation failed" -ForegroundColor Red
    exit 1
}

# Create SQL script if missing
$sqlPath = "$SHARE_DIR/cortex--1.0.sql"
if (-not (Test-Path $sqlPath)) {
    Write-Host "`nCreating extension SQL..." -ForegroundColor Yellow
    @"
-- Cortex Extension SQL
-- No database objects created - cortex runs as background worker

-- Placeholder function for extension validation
CREATE OR REPLACE FUNCTION cortex_version()
RETURNS TEXT AS `$`$
BEGIN
    RETURN '1.0';
END;
`$`$ LANGUAGE plpgsql IMMUTABLE;
"@ | Set-Content $sqlPath
    Write-Host "  ✓ Created $sqlPath" -ForegroundColor Green
}

# Verify installation
Write-Host "`nVerifying installation..." -ForegroundColor Yellow
$files = @(
    "$LIB_DIR/cortex.dll",
    "$SHARE_DIR/cortex.control",
    "$SHARE_DIR/cortex--1.0.sql"
)

$allExist = $true
foreach ($file in $files) {
    if (Test-Path $file) {
        Write-Host "  ✓ $(Split-Path $file -Leaf)" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $(Split-Path $file -Leaf) missing" -ForegroundColor Red
        $allExist = $false
    }
}

if ($allExist) {
    Write-Host "`n=== Installation Complete ===" -ForegroundColor Cyan
    Write-Host "Test with: psql -d hartonomous -c 'CREATE EXTENSION cortex;'" -ForegroundColor Gray
} else {
    Write-Host "`n=== Installation Incomplete ===" -ForegroundColor Red
    exit 1
}
