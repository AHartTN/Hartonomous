# Database Schema Deployment Script (PowerShell)
# Deploys PostgreSQL schema from schema/ directory
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun,

    [Parameter(Mandatory = $false)]
    [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"
. "$PSScriptRoot\..\common\azure-auth.ps1"

# Initialize logger
$logLevel = if ($env:LOG_LEVEL) { $env:LOG_LEVEL } else { 'INFO' }
Initialize-Logger -Level $logLevel

Write-Step "Database Schema Deployment"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
Write-Log "Loaded configuration for: $Environment" -Level INFO

# Construct connection string
$dbHost = $config.database.host
$dbPort = $config.database.port
$dbName = $config.database.name
$dbUser = $config.database.user

Write-Log "Database: ${dbHost}:${dbPort}/${dbName}" -Level INFO

# Get database password
$dbPassword = $null
if ($config.azure.key_vault_url -and $Environment -ne 'development') {
    Write-Step "Retrieving Database Credentials from Azure Key Vault"

    # Authenticate to Azure
    Connect-AzureWithServicePrincipal `
        -TenantId $env:AZURE_TENANT_ID `
        -ClientId $env:AZURE_CLIENT_ID `
        -ClientSecret $env:AZURE_CLIENT_SECRET `
        -SubscriptionId $env:AZURE_SUBSCRIPTION_ID

    # Get database password
    $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
    $secretName = "PostgreSQL-$($config.database.name)-Password"
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

# Backup database (unless skipped)
if (-not $SkipBackup) {
    Write-Step "Creating Pre-Deployment Backup"
    & "$PSScriptRoot\backup-database.ps1" -Environment $Environment
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Backup failed. Aborting deployment."
    }
}

# Test database connectivity
Write-Step "Testing Database Connectivity"
try {
    $query = "SELECT version();"
    $result = & psql -t -c $query 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "Database connection failed: $result"
    }
    Write-Success "Connected to PostgreSQL"
    Write-Log "PostgreSQL version: $($result.Trim())" -Level DEBUG
}
catch {
    Write-Failure "Database connection test failed: $($_.Exception.Message)"
}

# Find schema files
Write-Step "Discovering Schema Files"
$schemaRoot = Join-Path $PSScriptRoot "..\..\..\schema"

if (-not (Test-Path $schemaRoot)) {
    Write-Failure "Schema directory not found: $schemaRoot"
}

# Schema deployment order
$schemaOrder = @(
    "core\tables",
    "core\indexes",
    "core\triggers",
    "core\functions",
    "extensions"
)

$deployedFiles = @()

foreach ($subPath in $schemaOrder) {
    $schemaPath = Join-Path $schemaRoot $subPath

    if (-not (Test-Path $schemaPath)) {
        Write-Log "Schema path not found, skipping: $subPath" -Level WARNING
        continue
    }

    Write-Step "Deploying: $subPath"

    # Get SQL files in order
    $sqlFiles = Get-ChildItem -Path $schemaPath -Filter "*.sql" | Sort-Object Name

    if ($sqlFiles.Count -eq 0) {
        Write-Log "No SQL files found in: $subPath" -Level WARNING
        continue
    }

    foreach ($file in $sqlFiles) {
        Write-Log "Processing: $($file.Name)" -Level INFO

        if ($DryRun) {
            Write-Log "DRY RUN: Would deploy $($file.FullName)" -Level INFO
            $deployedFiles += $file.FullName
            continue
        }

        try {
            # Execute SQL file
            Write-Log "Executing: $($file.Name)" -Level DEBUG
            $output = & psql -f $file.FullName 2>&1

            if ($LASTEXITCODE -ne 0) {
                Write-Log "SQL Output: $output" -Level ERROR
                Write-Failure "Failed to deploy: $($file.Name)"
            }

            Write-Success "Deployed: $($file.Name)"
            $deployedFiles += $file.FullName
        }
        catch {
            Write-Failure "Error deploying $($file.Name): $($_.Exception.Message)"
        }
    }
}

# Verify deployment
Write-Step "Verifying Deployment"

$verifyQueries = @(
    @{
        Name = "Tables"
        Query = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';"
    },
    @{
        Name = "Functions"
        Query = "SELECT COUNT(*) FROM pg_proc WHERE pronamespace = 'public'::regnamespace;"
    },
    @{
        Name = "Triggers"
        Query = "SELECT COUNT(*) FROM pg_trigger WHERE tgisinternal = false;"
    }
)

foreach ($check in $verifyQueries) {
    try {
        $result = & psql -t -c $check.Query 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "$($check.Name): $($result.Trim())"
        }
        else {
            Write-Log "Verification failed for $($check.Name): $result" -Level WARNING
        }
    }
    catch {
        Write-Log "Verification error for $($check.Name): $($_.Exception.Message)" -Level WARNING
    }
}

# Summary
Write-Step "Deployment Summary"
Write-Success "Successfully deployed $($deployedFiles.Count) schema files"

if ($DryRun) {
    Write-Log "DRY RUN mode - no changes were made" -Level INFO
}

Write-Log "Database schema deployment completed" -Level INFO
