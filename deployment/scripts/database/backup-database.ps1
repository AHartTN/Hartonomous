# Database Backup Script (PowerShell)
# Creates timestamped backup of PostgreSQL database
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
. "$PSScriptRoot\..\common\azure-auth.ps1"

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
    $BackupPath = Join-Path $PSScriptRoot "..\..\..\backups\database"
}

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    Write-Log "Created backup directory: $BackupPath" -Level INFO
}

# Generate backup filename with timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dbName = $config.database.name
$backupFile = Join-Path $BackupPath "$dbName-$Environment-$timestamp.sql"

Write-Log "Backup file: $backupFile" -Level INFO

# Get database credentials
$dbHost = $config.database.host
$dbPort = $config.database.port
$dbUser = $config.database.user

$dbPassword = $null
if ($config.azure.key_vault_url -and $Environment -ne 'development') {
    # Production/Staging: Get from Azure Key Vault
    Connect-AzureWithServicePrincipal `
        -TenantId $env:AZURE_TENANT_ID `
        -ClientId $env:AZURE_CLIENT_ID `
        -ClientSecret $env:AZURE_CLIENT_SECRET `
        -SubscriptionId $env:AZURE_SUBSCRIPTION_ID

    $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
    $secretName = "PostgreSQL-$dbName-Password"
    $dbPassword = Get-KeyVaultSecret -VaultName $kvName -SecretName $secretName
}
else {
    # Development: Use environment variable
    $dbPassword = $env:PGPASSWORD
    if (-not $dbPassword) {
        Write-Failure "PGPASSWORD environment variable not set"
    }
}

# Set PostgreSQL environment variables
$env:PGHOST = $dbHost
$env:PGPORT = $dbPort
$env:PGDATABASE = $dbName
$env:PGUSER = $dbUser
$env:PGPASSWORD = $dbPassword

# Create backup using pg_dump
Write-Step "Creating Backup"
try {
    Write-Log "Running pg_dump..." -Level DEBUG

    # Use pg_dump with custom format for better compression
    $dumpArgs = @(
        "-F", "c",  # Custom format (compressed)
        "-b",       # Include large objects
        "-v",       # Verbose
        "-f", $backupFile,
        $dbName
    )

    $output = & pg_dump @dumpArgs 2>&1

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

# Retention policy: Keep last 10 backups per environment
Write-Step "Applying Retention Policy"
$allBackups = Get-ChildItem -Path $BackupPath -Filter "$dbName-$Environment-*.sql" |
    Sort-Object LastWriteTime -Descending

$keepCount = 10
if ($allBackups.Count -gt $keepCount) {
    $toDelete = $allBackups | Select-Object -Skip $keepCount

    foreach ($old in $toDelete) {
        Write-Log "Removing old backup: $($old.Name)" -Level INFO
        Remove-Item $old.FullName -Force
    }

    Write-Success "Retained $keepCount most recent backups, deleted $($toDelete.Count) old backups"
}
else {
    Write-Log "Current backups: $($allBackups.Count) (retention: $keepCount)" -Level INFO
}

Write-Log "Database backup completed: $backupFile" -Level INFO
