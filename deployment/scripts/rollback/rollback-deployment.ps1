# Rollback Deployment Script (PowerShell)
# Rolls back to previous deployment using backups
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $false)]
    [string]$DatabaseBackup = $null,

    [Parameter(Mandatory = $false)]
    [string]$ApplicationBackup = $null,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"
. "$PSScriptRoot\..\common\azure-auth.ps1"

# Initialize logger
Initialize-Logger -Level $env:LOG_LEVEL ?? 'INFO'

Write-Step "Deployment Rollback"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
Write-Log "Rolling back deployment for: $Environment" -Level INFO

# Safety check for production
if ($Environment -eq 'production' -and -not $Force) {
    Write-Log "PRODUCTION rollback requires -Force flag" -Level ERROR
    Write-Log "This will restore database and application from backup" -Level WARNING
    Write-Failure "Add -Force flag to confirm production rollback"
}

# Get backup directories
$backupRoot = Join-Path $PSScriptRoot "..\..\..\..\backups"
$dbBackupPath = Join-Path $backupRoot "database"
$appBackupPath = Join-Path $backupRoot "application"

# Find latest backups if not specified
if (-not $DatabaseBackup) {
    Write-Step "Finding Latest Database Backup"

    $dbName = $config.database.name
    $latestDbBackup = Get-ChildItem -Path $dbBackupPath -Filter "$dbName-$Environment-*.sql" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latestDbBackup) {
        $DatabaseBackup = $latestDbBackup.FullName
        Write-Log "Found database backup: $($latestDbBackup.Name)" -Level INFO
    }
    else {
        Write-Log "No database backup found for: $dbName-$Environment" -Level WARNING
    }
}

if (-not $ApplicationBackup) {
    Write-Step "Finding Latest Application Backup"

    $latestAppBackup = Get-ChildItem -Path $appBackupPath -Filter "api-$Environment-*.zip" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latestAppBackup) {
        $ApplicationBackup = $latestAppBackup.FullName
        Write-Log "Found application backup: $($latestAppBackup.Name)" -Level INFO
    }
    else {
        Write-Log "No application backup found for: api-$Environment" -Level WARNING
    }
}

# Confirm rollback
Write-Step "Rollback Confirmation"
Write-Host "Environment: $Environment" -ForegroundColor Yellow
Write-Host "Database Backup: $DatabaseBackup" -ForegroundColor Yellow
Write-Host "Application Backup: $ApplicationBackup" -ForegroundColor Yellow
Write-Host ""

if (-not $Force) {
    $confirmation = Read-Host "Proceed with rollback? (yes/no)"
    if ($confirmation -ne 'yes') {
        Write-Log "Rollback cancelled by user" -Level INFO
        exit 0
    }
}

# Stop application
Write-Step "Stopping Application"
$serviceName = "hartonomous-api-$Environment"

try {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Write-Log "Stopping service: $serviceName" -Level INFO
        Stop-Service -Name $serviceName -Force
        Start-Sleep -Seconds 2
        Write-Success "Service stopped"
    }
}
catch {
    Write-Log "Could not stop service: $($_.Exception.Message)" -Level WARNING
}

# Rollback Database
if ($DatabaseBackup -and (Test-Path $DatabaseBackup)) {
    Write-Step "Rolling Back Database"

    # Get database credentials
    $dbHost = $config.database.host
    $dbPort = $config.database.port
    $dbName = $config.database.name
    $dbUser = $config.database.user

    $dbPassword = $null
    if ($config.azure.key_vault_url -and $Environment -ne 'development') {
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
        $dbPassword = $env:PGPASSWORD
    }

    # Set PostgreSQL environment variables
    $env:PGHOST = $dbHost
    $env:PGPORT = $dbPort
    $env:PGDATABASE = $dbName
    $env:PGUSER = $dbUser
    $env:PGPASSWORD = $dbPassword

    Write-Log "Restoring database from: $DatabaseBackup" -Level INFO

    # Restore database using pg_restore
    try {
        & pg_restore -c -d $dbName $DatabaseBackup 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Success "Database restored successfully"
        }
        else {
            Write-Log "Database restore had warnings (this may be normal)" -Level WARNING
        }
    }
    catch {
        Write-Failure "Database restore failed: $($_.Exception.Message)"
    }
}
else {
    Write-Log "No database backup to restore" -Level WARNING
}

# Rollback Application
if ($ApplicationBackup -and (Test-Path $ApplicationBackup)) {
    Write-Step "Rolling Back Application"

    $apiPath = Join-Path $PSScriptRoot "..\..\..\api"
    $tempExtractPath = Join-Path $env:TEMP "hartonomous-rollback-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

    try {
        # Extract backup
        Write-Log "Extracting backup: $ApplicationBackup" -Level INFO
        Expand-Archive -Path $ApplicationBackup -DestinationPath $tempExtractPath -Force

        # Stop any running processes
        Get-Process | Where-Object { $_.Path -like "*$apiPath*" } | Stop-Process -Force -ErrorAction SilentlyContinue

        # Remove current API directory (keep .venv)
        Write-Log "Removing current application files..." -Level INFO
        Get-ChildItem -Path $apiPath -Exclude ".venv", "__pycache__" | Remove-Item -Recurse -Force

        # Copy backup files
        Write-Log "Restoring application files..." -Level INFO
        Copy-Item -Path "$tempExtractPath\*" -Destination $apiPath -Recurse -Force

        # Clean up temp directory
        Remove-Item -Path $tempExtractPath -Recurse -Force

        Write-Success "Application restored successfully"
    }
    catch {
        Write-Failure "Application restore failed: $($_.Exception.Message)"
    }
}
else {
    Write-Log "No application backup to restore" -Level WARNING
}

# Restart application
Write-Step "Restarting Application"
try {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Log "Starting service: $serviceName" -Level INFO
        Start-Service -Name $serviceName
        Start-Sleep -Seconds 3

        if ((Get-Service -Name $serviceName).Status -eq 'Running') {
            Write-Success "Service started successfully"
        }
        else {
            Write-Log "Service did not start successfully" -Level WARNING
        }
    }
    else {
        Write-Log "Service not installed: $serviceName" -Level INFO
        Write-Log "For development, start manually: cd api && python -m uvicorn main:app --reload" -Level INFO
    }
}
catch {
    Write-Log "Could not restart service: $($_.Exception.Message)" -Level WARNING
}

# Run health checks
Write-Step "Running Health Checks"
& "$PSScriptRoot\..\validation\health-check.ps1" -Environment $Environment

# Summary
Write-Step "Rollback Summary"
Write-Success "Deployment rollback completed"
Write-Log "Environment: $Environment" -Level INFO
Write-Log "Database rolled back: $(if($DatabaseBackup){'Yes'}else{'No'})" -Level INFO
Write-Log "Application rolled back: $(if($ApplicationBackup){'Yes'}else{'No'})" -Level INFO

Write-Log "Deployment rollback completed" -Level INFO
