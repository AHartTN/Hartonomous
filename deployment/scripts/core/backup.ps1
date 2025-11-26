# Unified Backup Script
# Single backup script for all components
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $true)]
    [ValidateSet('database', 'application', 'all')]
    [string]$Component
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
$scriptRoot = $PSScriptRoot
. "$scriptRoot\..\common\logger.ps1"
. "$scriptRoot\..\common\config-loader.ps1"

# Initialize
$logLevel = if ($env:LOG_LEVEL) { $env:LOG_LEVEL } else { 'INFO' }
Initialize-Logger -LogFilePath "D:\Hartonomous\logs\backup-$(Get-Date -Format 'yyyyMMdd-HHmmss').log" -LogLevelName $logLevel

Write-Step "Backup - $Component"

# Load config
$config = Get-DeploymentConfig -Environment $Environment
$backupPath = $config.deployment.backup_path
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

# Create backup directory
if (-not (Test-Path $backupPath)) {
    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
}

# Backup database
if ($Component -in @('database', 'all')) {
    Write-Log "Backing up database..." -Level INFO
    
    $dbName = $config.database.name
    $backupFile = Join-Path $backupPath "database\$dbName-$Environment-$timestamp.dump"
    
    $env:PGHOST = $config.database.host
    $env:PGPORT = $config.database.port
    $env:PGDATABASE = $dbName
    $env:PGUSER = $config.database.user
    
    # Get password from Key Vault or env
    if ($config.azure.key_vault_url -and $Environment -ne 'development') {
        $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
        $env:PGPASSWORD = az keyvault secret show --vault-name $kvName --name "PostgreSQL-$dbName-Password" --query "value" -o tsv
    }
    else {
        $env:PGPASSWORD = $env:PGPASSWORD
    }
    
    # Run pg_dump
    $backupDir = Split-Path $backupFile -Parent
    if (-not (Test-Path $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    }
    
    & pg_dump -Fc -Z0 -f $backupFile $dbName
    
    if ($LASTEXITCODE -eq 0) {
        $fileSize = (Get-Item $backupFile).Length / 1MB
        Write-Success "Database backup created: $backupFile ($([math]::Round($fileSize, 2)) MB)"
        
        # Output for GitHub Actions
        Write-Output "BACKUP_FILE=$backupFile" >> $env:GITHUB_OUTPUT
        Write-Output "backup_path=$backupDir" >> $env:GITHUB_OUTPUT
    }
    else {
        Write-Failure "Database backup failed"
    }
}

exit 0
