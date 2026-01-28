#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Complete Hartonomous System Setup

.DESCRIPTION
    Master setup script that handles EVERYTHING:
    - PostgreSQL + PostGIS installation/verification
    - Database creation
    - Schema application
    - Unicode seeding (optional)
    - System testing

.PARAMETER SkipBuild
    Skip building C++ engine

.PARAMETER SkipPostGIS
    Skip PostGIS installation check

.PARAMETER Clean
    Drop and recreate database

.PARAMETER Seed
    Seed ALL 1,114,112 Unicode codepoints

.PARAMETER Test
    Run full system tests

.EXAMPLE
    .\setup.ps1
    .\setup.ps1 -Clean -Seed -Test
    .\setup.ps1 -SkipBuild
#>

param(
    [switch]$SkipBuild,
    [switch]$SkipPostGIS,
    [switch]$Clean,
    [switch]$Seed,
    [switch]$Test
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Hartonomous Complete System Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ==============================================================================
# STEP 1: Build C++ Engine
# ==============================================================================

if (-not $SkipBuild) {
    Write-Host "[1/5] Building C++ Engine..." -ForegroundColor Cyan
    Write-Host ""

    if (-not (Test-Path ".\build.ps1")) {
        Write-Host "✗ build.ps1 not found" -ForegroundColor Red
        exit 1
    }

    & .\build.ps1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "✓ Engine built successfully" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1/5] Skipping build (using existing binaries)" -ForegroundColor Yellow
    Write-Host ""
}

# ==============================================================================
# STEP 2: Check/Install PostgreSQL
# ==============================================================================

Write-Host "[2/5] Checking PostgreSQL..." -ForegroundColor Cyan
Write-Host ""

# Find psql
$psqlPath = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psqlPath) {
    $pgPaths = @(
        "C:\Program Files\PostgreSQL\18\bin",
        "C:\Program Files\PostgreSQL\17\bin",
        "C:\Program Files\PostgreSQL\16\bin"
    )

    foreach ($path in $pgPaths) {
        if (Test-Path "$path\psql.exe") {
            $env:Path = "$path;$env:Path"
            $psqlPath = Get-Command psql -ErrorAction SilentlyContinue
            break
        }
    }
}

if (-not $psqlPath) {
    Write-Host "✗ PostgreSQL not found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install PostgreSQL 16+:" -ForegroundColor Yellow
    Write-Host "  https://www.postgresql.org/download/windows/" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# Check version
$pgVersion = psql --version | Select-String -Pattern "psql \(PostgreSQL\) (\d+\.\d+)"
$versionNum = [decimal]$pgVersion.Matches.Groups[1].Value

Write-Host "✓ PostgreSQL found: $versionNum at $($psqlPath.Source)" -ForegroundColor Green

if ($versionNum -lt 16.0) {
    Write-Host "⚠ PostgreSQL 16+ recommended (found $versionNum)" -ForegroundColor Yellow
    Write-Host "  Consider upgrading for best performance" -ForegroundColor Yellow
}

Write-Host ""

# ==============================================================================
# STEP 3: Check/Install PostGIS
# ==============================================================================

if (-not $SkipPostGIS) {
    Write-Host "[3/5] Checking/Installing PostGIS..." -ForegroundColor Cyan
    Write-Host ""

    # Set credentials
    $env:PGHOST = "localhost"
    $env:PGPORT = 5432
    $env:PGUSER = "postgres"
    $env:PGDATABASE = "postgres"

    if (-not $env:PGPASSWORD) {
        $env:PGPASSWORD = "postgres"
    }

    # Test connection
    Write-Host "Testing PostgreSQL connection..." -ForegroundColor Gray
    $testConn = psql -c "SELECT version();" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Cannot connect to PostgreSQL" -ForegroundColor Red
        Write-Host "  Make sure PostgreSQL is running" -ForegroundColor Yellow
        Write-Host "  Set PGPASSWORD environment variable if needed" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "✓ Connected" -ForegroundColor Green

    # Check PostGIS by trying to create extension
    Write-Host "Checking PostGIS..." -ForegroundColor Gray

    # First check if available in extensions
    $postgisAvailable = psql -t -c "SELECT default_version FROM pg_available_extensions WHERE name = 'postgis';" 2>&1

    if ($postgisAvailable -match "\d+\.\d+") {
        $postgisVersion = $Matches[0]
        Write-Host "✓ PostGIS available: $postgisVersion" -ForegroundColor Green

        # Try to create extension to verify it works
        Write-Host "Verifying PostGIS installation..." -ForegroundColor Gray
        $testCreate = psql -c "CREATE EXTENSION IF NOT EXISTS postgis;" 2>&1

        if ($LASTEXITCODE -eq 0) {
            # Verify it actually works
            $testQuery = psql -t -c "SELECT PostGIS_Version();" 2>&1

            if ($testQuery -match "\d+\.\d+") {
                $installedVersion = $Matches[0]
                Write-Host "✓ PostGIS working: $installedVersion" -ForegroundColor Green

                # Check version
                $postgisVerNum = [decimal]$installedVersion
                if ($postgisVerNum -lt 3.3) {
                    Write-Host "⚠ PostGIS 3.3+ recommended (found $installedVersion)" -ForegroundColor Yellow
                }
            } else {
                Write-Host "✗ PostGIS extension exists but not functional" -ForegroundColor Red
                Write-Host "  Try reinstalling PostGIS" -ForegroundColor Yellow
                exit 1
            }
        } else {
            Write-Host "✗ PostGIS extension failed to install" -ForegroundColor Red
            Write-Host "$testCreate" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "✗ PostGIS NOT found in pg_available_extensions" -ForegroundColor Red
        Write-Host ""
        Write-Host "PostGIS is REQUIRED. Install it:" -ForegroundColor Yellow
        Write-Host "  1. Download: https://postgis.net/windows_downloads/" -ForegroundColor Yellow
        Write-Host "  2. Run installer for PostgreSQL $([math]::Floor($versionNum))" -ForegroundColor Yellow
        Write-Host "  3. Use StackBuilder or manual installer" -ForegroundColor Yellow
        Write-Host "  4. Re-run this script" -ForegroundColor Yellow
        Write-Host ""

        # Check if maybe PostGIS DLLs exist but not registered
        $pgLibDir = Split-Path -Parent (Split-Path -Parent $psqlPath.Source)
        $postgisLib = Join-Path $pgLibDir "lib\postgis-3.dll"

        if (Test-Path $postgisLib) {
            Write-Host "⚠ PostGIS DLLs found at $postgisLib but extension not registered" -ForegroundColor Yellow
            Write-Host "  This might be a permissions or configuration issue" -ForegroundColor Yellow
        }

        exit 1
    }

    Write-Host ""
} else {
    Write-Host "[3/5] Skipping PostGIS check" -ForegroundColor Yellow
    Write-Host ""
}

# ==============================================================================
# STEP 4: Install PostgreSQL Extension
# ==============================================================================

Write-Host "[4/5] Installing PostgreSQL extension..." -ForegroundColor Cyan
Write-Host ""

# Check if extension DLL exists
$extensionDll = Get-ChildItem -Path "build\windows-release-threaded\PostgresExtension" -Filter "hartonomous.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $extensionDll) {
    Write-Host "✗ Extension DLL not found. Build failed?" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Extension built: $($extensionDll.FullName)" -ForegroundColor Green

# Install extension (requires admin)
Write-Host ""
Write-Host "Installing extension to PostgreSQL (requires admin)..." -ForegroundColor Yellow
& .\scripts\install-extension.ps1

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Extension installation failed" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Extension installed" -ForegroundColor Green
Write-Host ""

# ==============================================================================
# STEP 5: Setup Database
# ==============================================================================

Write-Host "[5/5] Setting up database..." -ForegroundColor Cyan
Write-Host ""

$setupArgs = @()
if ($Clean) { $setupArgs += "-Clean" }
if ($Seed) { $setupArgs += "-Seed" }
if ($Test) { $setupArgs += "-Test" }

& .\scripts\setup-database.ps1 @setupArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Database setup failed" -ForegroundColor Red
    exit 1
}

# ==============================================================================
# STEP 6: Final Verification
# ==============================================================================

Write-Host "[6/6] Final verification..." -ForegroundColor Cyan
Write-Host ""

# Check engine library
$engineLib = Get-ChildItem -Path "build\windows-release-threaded\Engine" -Filter "engine.lib" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($engineLib) {
    Write-Host "✓ Engine library: $($engineLib.FullName)" -ForegroundColor Green
} else {
    Write-Host "⚠ Engine library not found" -ForegroundColor Yellow
}

# Check seed_unicode tool
$seedExe = Get-ChildItem -Path "build\windows-release-threaded\Engine\tools" -Filter "seed_unicode.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($seedExe) {
    Write-Host "✓ seed_unicode tool: $($seedExe.FullName)" -ForegroundColor Green
} else {
    Write-Host "⚠ seed_unicode tool not found (rebuild needed)" -ForegroundColor Yellow
}

# Check database
$env:PGDATABASE = "hypercube"
$atomCount = psql -t -c "SELECT COUNT(*) FROM hartonomous.atoms;" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Database ready: $($atomCount.Trim()) atoms" -ForegroundColor Green
} else {
    Write-Host "⚠ Database check failed" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

if (-not $Seed) {
    Write-Host "Next: Seed Unicode data" -ForegroundColor Cyan
    Write-Host "  .\setup.ps1 -Seed" -ForegroundColor Gray
    Write-Host ""
}

if (-not $Test) {
    Write-Host "Next: Run tests" -ForegroundColor Cyan
    Write-Host "  .\setup.ps1 -Test" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "Quick commands:" -ForegroundColor Cyan
Write-Host "  psql -d hypercube -c 'SELECT * FROM hartonomous.stats();'" -ForegroundColor Gray
Write-Host "  psql -d hypercube -c 'SELECT hartonomous.version();'" -ForegroundColor Gray
Write-Host ""
