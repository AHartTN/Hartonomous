#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Setup Hartonomous PostgreSQL database

.DESCRIPTION
    Idempotent database setup script:
    - Creates database if needed
    - Applies all schema files
    - Optionally seeds Unicode data
    - Runs consistency checks

.PARAMETER Clean
    Drop and recreate database (DESTRUCTIVE)

.PARAMETER Seed
    Seed ALL Unicode codepoints (1,114,112 codepoints)

.PARAMETER Test
    Run database tests after setup

.PARAMETER Host
    PostgreSQL host (default: localhost)

.PARAMETER Port
    PostgreSQL port (default: 5432)

.PARAMETER Database
    Database name (default: hypercube)

.PARAMETER User
    PostgreSQL user (default: postgres)

.PARAMETER Password
    PostgreSQL password (optional, uses PGPASSWORD env var if not specified)

.EXAMPLE
    .\setup-database.ps1
    .\setup-database.ps1 -Clean -Seed -Test
    .\setup-database.ps1 -Database mydb -User myuser
#>

param(
    [switch]$Clean,
    [switch]$Seed,
    [switch]$Test,
    [string]$PgHost = "localhost",
    [int]$PgPort = 5432,
    [string]$DbName = "hypercube",
    [string]$PgUser = "postgres",
    [string]$PgPassword = "postgres"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Hartonomous Database Setup ===" -ForegroundColor Cyan
Write-Host ""

# Find psql
$psqlPath = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psqlPath) {
    # Try common PostgreSQL installation paths
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
    Write-Host "✗ psql not found in PATH" -ForegroundColor Red
    Write-Host "Please install PostgreSQL or add it to PATH" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ psql found: $($psqlPath.Source)" -ForegroundColor Green

# Check PostgreSQL version
try {
    $pgVersion = psql --version | Select-String -Pattern "psql \(PostgreSQL\) (\d+\.\d+)"
    $versionNum = [decimal]$pgVersion.Matches.Groups[1].Value
    Write-Host "✓ PostgreSQL version: $versionNum" -ForegroundColor Green

    if ($versionNum -lt 16.0) {
        Write-Host "⚠ PostgreSQL 16+ recommended (found $versionNum)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠ Could not determine PostgreSQL version" -ForegroundColor Yellow
}

# Set PostgreSQL environment variables
$env:PGHOST = $PgHost
$env:PGPORT = $PgPort
$env:PGDATABASE = "postgres"
$env:PGUSER = $PgUser
$env:PGPASSWORD = $PgPassword

Write-Host ""
Write-Host "PostgreSQL Configuration:" -ForegroundColor Cyan
Write-Host "  Host:     $env:PGHOST"
Write-Host "  Port:     $env:PGPORT"
Write-Host "  Database: $DbName"
Write-Host "  User:     $env:PGUSER"
Write-Host ""

# Test connection
Write-Host "Testing connection..." -ForegroundColor Cyan
try {
    $null = psql -c "SELECT 1;" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Connection failed" }
    Write-Host "✓ Connected" -ForegroundColor Green
} catch {
    Write-Host "✗ Cannot connect" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Clean (drop database)
if ($Clean) {
    Write-Host "Dropping database '$DbName'..." -ForegroundColor Yellow
    $null = psql -c "DROP DATABASE IF EXISTS $DbName;" 2>&1
    Write-Host "✓ Database dropped" -ForegroundColor Green
}

# Create database
Write-Host "Creating database '$DbName'..." -ForegroundColor Cyan
$createResult = psql -c "CREATE DATABASE $DbName;" 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Database created" -ForegroundColor Green
} elseif ($createResult -match "already exists") {
    Write-Host "✓ Database already exists" -ForegroundColor Green
} else {
    Write-Host "✗ Failed" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Switch to database
$env:PGDATABASE = $DbName

# Apply schema
$schemaDir = Join-Path $PSScriptRoot ".." "schema"
$schemaFiles = @("00-foundation.sql", "01-core-tables.sql", "03-functions.sql")

Write-Host "Applying schema..." -ForegroundColor Cyan
foreach ($file in $schemaFiles) {
    $filePath = Join-Path $schemaDir $file
    if (-not (Test-Path $filePath)) {
        Write-Host "✗ Not found: $file" -ForegroundColor Red
        exit 1
    }
    Write-Host "  $file..." -ForegroundColor Gray
    $null = psql -f $filePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed: $file" -ForegroundColor Red
        exit 1
    }
}
Write-Host "✓ Schema applied" -ForegroundColor Green
Write-Host ""

# Consistency check
Write-Host "Running consistency checks..." -ForegroundColor Cyan
$null = psql -c "SELECT hartonomous.repair_inconsistencies();" 2>&1
Write-Host "✓ Complete" -ForegroundColor Green
Write-Host ""

# Seed Unicode
if ($Seed) {
    Write-Host "Seeding 1,114,112 Unicode codepoints..." -ForegroundColor Cyan

    $seedExe = $null
    $buildDirs = @("build\windows-release-threaded\Engine\tools", "build\windows-release-max-perf\Engine\tools")
    $repoRoot = Join-Path $PSScriptRoot ".."

    foreach ($dir in $buildDirs) {
        $exe = Join-Path $repoRoot $dir "seed_unicode.exe"
        if (Test-Path $exe) {
            $seedExe = $exe
            break
        }
    }

    if (-not $seedExe) {
        Write-Host "✗ seed_unicode.exe not found. Run .\build.ps1 first" -ForegroundColor Red
        exit 1
    }

    $seedStart = Get-Date
    & $seedExe

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Seeding failed" -ForegroundColor Red
        exit 1
    }

    $duration = ((Get-Date) - $seedStart).TotalSeconds
    Write-Host "✓ Seeding complete in $([math]::Round($duration, 1))s" -ForegroundColor Green

    $count = psql -t -c "SELECT COUNT(*) FROM hartonomous.atoms;"
    Write-Host "  Atoms: $($count.Trim())" -ForegroundColor Cyan
    Write-Host ""
}

# Tests
if ($Test) {
    Write-Host "Running tests..." -ForegroundColor Cyan

    Write-Host "  Version..." -ForegroundColor Gray
    $version = psql -t -c "SELECT hartonomous.version();"
    Write-Host "    $($version.Trim())" -ForegroundColor Gray

    Write-Host "  Statistics..." -ForegroundColor Gray
    psql -c "SELECT * FROM hartonomous.stats();"

    Write-Host "  Sample atoms..." -ForegroundColor Gray
    psql -c "SELECT codepoint, centroid_x, centroid_y FROM hartonomous.atoms LIMIT 3;"

    Write-Host "✓ Tests complete" -ForegroundColor Green
    Write-Host ""
}

Write-Host "=== Setup Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Database: postgresql://$env:PGHOST:$env:PGPORT/$DbName" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  .\scripts\setup-database.ps1 -Seed" -ForegroundColor Gray
Write-Host "  .\scripts\setup-database.ps1 -Test" -ForegroundColor Gray
Write-Host ""
