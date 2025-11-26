# Database Backup Script (PowerShell)
# Creates PostgreSQL backup using pg_dump
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $false)]
    [string]$BackupPath = $null
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"

# Initialize logger
$logLevelName = if ($env:LOG_LEVEL) { $env:LOG_LEVEL } else { 'INFO' }
$logPath = "D:\Hartonomous\logs\backup-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Initialize-Logger -LogFilePath $logPath -LogLevelName $logLevelName

Write-Step "Database Backup"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment

# Construct backup directory
if (-not $BackupPath) {
    $BackupPath = "D:\Hartonomous\backups\database"
}

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    Write-Log "Created backup directory: $BackupPath" -Level INFO
}

# Generate backup filename with timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dbName = $config.database.name
$backupFile = Join-Path $BackupPath "$dbName-$Environment-$timestamp.dump"

Write-Log "Backup file: $backupFile" -Level INFO

# Get database credentials
$dbHost = $config.database.host
$dbPort = $config.database.port
$dbUser = $config.database.user

# Get password from Key Vault or environment
$dbPassword = $null
if ($config.azure.key_vault_url) {
    $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
    $machineName = $env:COMPUTERNAME
    $secretName = "PostgreSQL-${machineName}-${dbName}-Password"
    
    try {
        $dbPassword = az keyvault secret show --vault-name $kvName --name $secretName --query "value" -o tsv 2>$null
        if ($LASTEXITCODE -eq 0 -and $dbPassword) {
            Write-Log "Retrieved password from Key Vault" -Level DEBUG
        }
        else {
            # Try fallback secret name
            $secretName = "PostgreSQL-$($config.database.name)-Password"
            $dbPassword = az keyvault secret show --vault-name $kvName --name $secretName --query "value" -o tsv 2>$null
        }
    }
    catch {
        Write-Log "Key Vault retrieval failed, using PGPASSWORD" -Level WARNING
    }
}

# Fallback to environment variable
if (-not $dbPassword) {
    $dbPassword = $env:PGPASSWORD
    if (-not $dbPassword) {
        Write-Failure "PGPASSWORD environment variable not set and could not retrieve from Key Vault"
    }
}

# Set PostgreSQL environment variables
$env:PGHOST = $dbHost
$env:PGPORT = $dbPort
$env:PGDATABASE = $dbName
$env:PGUSER = $dbUser
$env:PGPASSWORD = $dbPassword

# Create backup using pg_dump
Write-Step "Running pg_dump"
try {
    Write-Log "Running pg_dump with custom format..." -Level DEBUG

    # Use custom format (-Fc) with no compression for performance
    $output = & pg_dump -Fc -Z0 -f $backupFile $dbName 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Log "pg_dump output: $output" -Level ERROR
        Write-Failure "Backup failed"
    }

    Write-Success "Backup created: $backupFile"
}
catch {
    Write-Failure "Backup error: $($_.Exception.Message)"
}

# Verify backup file
if (-not (Test-Path $backupFile)) {
    Write-Failure "Backup file not found after creation: $backupFile"
}

$fileSize = (Get-Item $backupFile).Length
$fileSizeMB = [math]::Round($fileSize / 1MB, 2)
Write-Success "Backup size: $fileSizeMB MB"

Write-Log "Database backup completed: $backupFile" -Level INFO
Write-Output "BACKUP_FILE=$backupFile" >> $env:GITHUB_OUTPUT
