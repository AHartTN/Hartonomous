# ============================================================================
# Hartonomous Database Reset Script (PowerShell)
# 
# WARNING: This will DROP and recreate the database, destroying all data!
#
# Usage:
#   .\scripts\reset-database.ps1 [-Force]
#
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# Load .env file
$envFile = Join-Path $projectRoot '.env'
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^([^#][^=]+)=(.*)$') {
            $key = $matches[1].Trim()
            $value = $matches[2].Trim()
            [Environment]::SetEnvironmentVariable($key, $value, 'Process')
        }
    }
}

# Database connection
$env:PGHOST = if ($env:PGHOST) { $env:PGHOST } else { 'localhost' }
$env:PGPORT = if ($env:PGPORT) { $env:PGPORT } else { '5432' }
$env:PGUSER = if ($env:PGUSER) { $env:PGUSER } else { 'hartonomous' }
$env:PGDATABASE = if ($env:PGDATABASE) { $env:PGDATABASE } else { 'hartonomous' }

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Red
Write-Host "  DATABASE RESET WARNING" -ForegroundColor Red
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Red
Write-Host ""
Write-Host "  This will:" -ForegroundColor Yellow
Write-Host "    1. DROP database: $env:PGDATABASE" -ForegroundColor Yellow
Write-Host "    2. CREATE new database" -ForegroundColor Yellow
Write-Host "    3. Run initialization scripts" -ForegroundColor Yellow
Write-Host ""
Write-Host "  ALL DATA WILL BE LOST!" -ForegroundColor Red
Write-Host ""

if (-not $Force) {
    $confirmation = Read-Host "Type 'YES' to continue"
    if ($confirmation -ne 'YES') {
        Write-Host "`n? Cancelled" -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "`n? Dropping database: $env:PGDATABASE" -ForegroundColor Yellow

try {
    # Connect to postgres database to drop target
    $dropCmd = "DROP DATABASE IF EXISTS $env:PGDATABASE;"
    & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -c $dropCmd 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to drop database"
    }
    
    Write-Host "? Database dropped" -ForegroundColor Green
}
catch {
    Write-Host "? Failed to drop database: $_" -ForegroundColor Red
    exit 1
}

Write-Host "? Creating database: $env:PGDATABASE" -ForegroundColor Yellow

try {
    $createCmd = "CREATE DATABASE $env:PGDATABASE WITH OWNER $env:PGUSER ENCODING 'UTF8';"
    & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d postgres -c $createCmd 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create database"
    }
    
    Write-Host "? Database created" -ForegroundColor Green
}
catch {
    Write-Host "? Failed to create database: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n? Running initialization script..." -ForegroundColor Yellow
Write-Host ""

# Run initialization script
$initScript = Join-Path $scriptDir 'Initialize-Database.ps1'
& $initScript -Environment localhost

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Green
Write-Host "?  Database reset complete!" -ForegroundColor Green
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Green
