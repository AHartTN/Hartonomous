# Install-Requirements.ps1
# Installs prerequisites and backs up database
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment
)

$ErrorActionPreference = "Stop"

Write-Host "Installing requirements for $Environment environment..." -ForegroundColor Cyan

# Load config
$configPath = Join-Path $PSScriptRoot "config\$Environment.json"
$config = Get-Content $configPath | ConvertFrom-Json

# Backup database
$backupDir = Join-Path $config.deployment.backup_path "database"
if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $backupDir "$($config.database.name)-$Environment-$timestamp.dump"

Write-Host "Backing up database to: $backupFile" -ForegroundColor Yellow

$env:PGHOST = $config.database.host
$env:PGPORT = $config.database.port
$env:PGDATABASE = $config.database.name
$env:PGUSER = $config.database.user

& pg_dump -Fc -Z0 -f $backupFile $config.database.name

if ($LASTEXITCODE -eq 0) {
    Write-Host "? Backup complete" -ForegroundColor Green
} else {
    Write-Host "? Backup failed" -ForegroundColor Red
    exit 1
}

Write-Host "? Requirements installed" -ForegroundColor Green
