#!/usr/bin/env pwsh
# Hartonomous Database Setup Script

$ErrorActionPreference = "Stop"

Write-Host "=== Hartonomous Database Setup ===" -ForegroundColor Cyan

# Check PostgreSQL
Write-Host "Checking PostgreSQL installation..." -ForegroundColor Yellow
try {
    $pgVersion = psql --version
    Write-Host "  ✓ $pgVersion" -ForegroundColor Green
} catch {
    Write-Host "  ✗ PostgreSQL not found. Install PostgreSQL 16+" -ForegroundColor Red
    exit 1
}

# Check PostGIS
Write-Host "Checking PostGIS..." -ForegroundColor Yellow
$postgisCheck = psql -U postgres -d postgres -tAc "SELECT PostGIS_Version();" 2>$null
if ($postgisCheck) {
    Write-Host "  ✓ PostGIS installed: $postgisCheck" -ForegroundColor Green
} else {
    Write-Host "  ✗ PostGIS not found. Install PostGIS 3.4+" -ForegroundColor Red
    exit 1
}

# Create database (idempotent - force drop with connection termination)
Write-Host "`nSetting up Hartonomous database..." -ForegroundColor Yellow

# Connect to postgres database to avoid "currently open database" error
psql -U postgres -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'hartonomous';" | Out-Null
psql -U postgres -d postgres -c "DROP DATABASE IF EXISTS hartonomous WITH (FORCE);" | Out-Null
Start-Sleep -Milliseconds 200
psql -U postgres -d postgres -c "CREATE DATABASE hartonomous;"

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Database created" -ForegroundColor Green
} else {
    Write-Host "  ✗ Database creation failed" -ForegroundColor Red
    exit 1
}

# Execute schema
Write-Host "`nExecuting schema..." -ForegroundColor Yellow
psql -U postgres -d hartonomous -f database/schema.sql
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Schema created successfully" -ForegroundColor Green
} else {
    Write-Host "  ✗ Schema creation failed" -ForegroundColor Red
    exit 1
}

# Verify tables
Write-Host "`nVerifying tables..." -ForegroundColor Yellow
$tables = psql -U postgres -d hartonomous -tAc "\dt"
if ($tables -match "atom") {
    Write-Host "  ✓ atom table exists" -ForegroundColor Green
} else {
    Write-Host "  ✗ atom table not found" -ForegroundColor Red
    exit 1
}

# Verify indexes
Write-Host "`nVerifying indexes..." -ForegroundColor Yellow
$indexes = psql -U postgres -d hartonomous -tAc "\di"
if ($indexes -match "idx_atoms_geom_gist") {
    Write-Host "  ✓ GiST spatial index exists" -ForegroundColor Green
} else {
    Write-Host "  ✗ Spatial index not found" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Setup Complete ===" -ForegroundColor Cyan
Write-Host "Database: hartonomous" -ForegroundColor Gray
Write-Host "Connection: psql -U postgres -d hartonomous" -ForegroundColor Gray
