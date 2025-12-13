#!/usr/bin/env pwsh
# Hartonomous Database Repair Script
# Idempotent repair - creates missing objects without destroying existing data

$ErrorActionPreference = "Stop"

Write-Host "=== Hartonomous Database Repair ===" -ForegroundColor Cyan

# Check database exists
Write-Host "`nChecking database..." -ForegroundColor Yellow
$dbExists = psql -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'hartonomous';"
if (-not $dbExists) {
    Write-Host "  ✗ Database 'hartonomous' does not exist. Run setup_database.ps1 first." -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Database exists" -ForegroundColor Green

# Execute repair schema
Write-Host "`nRepairing schema..." -ForegroundColor Yellow
psql -U postgres -d hartonomous -f database/schema_repair.sql

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Repair completed successfully" -ForegroundColor Green
} else {
    Write-Host "  ✗ Repair failed" -ForegroundColor Red
    exit 1
}

# Run migrations
Write-Host "`nApplying migrations..." -ForegroundColor Yellow
Get-ChildItem -Path database/migrations -Filter *.sql | Sort-Object Name | ForEach-Object {
    Write-Host "  → $($_.Name)" -ForegroundColor Gray
    psql -U postgres -d hartonomous -f $_.FullName -q
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    ✓ Applied" -ForegroundColor Green
    } else {
        Write-Host "    ✗ Failed" -ForegroundColor Yellow
    }
}

# Verify schema health
Write-Host "`nVerifying schema health..." -ForegroundColor Yellow

# Check atom table
$atomExists = psql -U postgres -d hartonomous -tAc "SELECT to_regclass('public.atom');"
if ($atomExists -eq "atom") {
    Write-Host "  ✓ atom table exists" -ForegroundColor Green
    
    # Check row count
    $rowCount = psql -U postgres -d hartonomous -tAc "SELECT COUNT(*) FROM atom;"
    Write-Host "    → $rowCount atoms" -ForegroundColor Gray
} else {
    Write-Host "  ✗ atom table missing" -ForegroundColor Red
}

# Check indexes
$gistExists = psql -U postgres -d hartonomous -tAc "SELECT to_regclass('public.idx_atoms_geom_gist');"
if ($gistExists -eq "idx_atoms_geom_gist") {
    Write-Host "  ✓ GiST spatial index exists" -ForegroundColor Green
} else {
    Write-Host "  ✗ GiST index missing" -ForegroundColor Yellow
}

# Check extensions
$postgisVersion = psql -U postgres -d hartonomous -tAc "SELECT PostGIS_Version();" 2>$null
if ($postgisVersion) {
    Write-Host "  ✓ PostGIS: $postgisVersion" -ForegroundColor Green
} else {
    Write-Host "  ✗ PostGIS not installed" -ForegroundColor Red
}

Write-Host "`n=== Repair Complete ===" -ForegroundColor Cyan
