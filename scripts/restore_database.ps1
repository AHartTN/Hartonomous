# Hartonomous Database Restore Script
# Restores from backup with optional clean/create database

param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFile,
    [switch]$Clean,
    [switch]$DataOnly
)

$ErrorActionPreference = "Stop"

# Ensure PostgreSQL bin is in PATH
$pgBin = "D:\PostgreSQL\18\bin"
$env:PATH = "$pgBin;$env:PATH"

if (-not (Test-Path $BackupFile)) {
    Write-Host "❌ Backup file not found: $BackupFile" -ForegroundColor Red
    exit 1
}

Write-Host "🔄 Starting Hartonomous database restore..." -ForegroundColor Cyan
Write-Host "   Source: $BackupFile"

# If Clean flag is set, drop and recreate database
if ($Clean) {
    Write-Host "⚠️  Clean mode: Dropping existing database..." -ForegroundColor Yellow
    
    # Drop database (terminate existing connections)
    psql -h localhost -U postgres -d postgres -c "DROP DATABASE IF EXISTS hartonomous WITH (FORCE);" 2>$null
    
    # Recreate database
    psql -h localhost -U postgres -d postgres -c "CREATE DATABASE hartonomous OWNER hartonomous;" | Out-Null
    
    # Enable extensions
    psql -h localhost -U hartonomous -d hartonomous -c "CREATE EXTENSION IF NOT EXISTS postgis;" | Out-Null
    psql -h localhost -U hartonomous -d hartonomous -c "CREATE EXTENSION IF NOT EXISTS pgcrypto;" | Out-Null
    psql -h localhost -U hartonomous -d hartonomous -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;" | Out-Null
}

# Build pg_restore command
$restoreArgs = @(
    "-h", "localhost",
    "-U", "hartonomous",
    "-d", "hartonomous",
    "--no-owner",
    "--no-privileges"
)

if ($DataOnly) {
    $restoreArgs += "-a"
    Write-Host "   Mode: Data only"
}
else {
    Write-Host "   Mode: Full restore (schema + data)"
}

$restoreArgs += $BackupFile

# Execute restore
try {
    & pg_restore $restoreArgs 2>&1 | Out-Null
    
    # Verify atom count
    $count = psql -h localhost -U hartonomous -d hartonomous -t -c "SELECT COUNT(*) FROM atom;" 2>$null
    $count = $count.Trim()
    
    Write-Host "✅ Restore completed successfully" -ForegroundColor Green
    Write-Host "   Atoms: $count"
}
catch {
    Write-Host "❌ Restore failed: $_" -ForegroundColor Red
    exit 1
}
