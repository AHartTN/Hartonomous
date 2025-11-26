# Deploy Database Script
# Runs database schema migrations
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
$scriptRoot = $PSScriptRoot
. "$scriptRoot\..\common\logger.ps1"
. "$scriptRoot\..\common\config-loader.ps1"

Write-Step "Deploy Database"

# Load config
$config = Get-DeploymentConfig -Environment $Environment

# Set PostgreSQL environment variables
$env:PGHOST = $config.database.host
$env:PGPORT = $config.database.port
$env:PGDATABASE = $config.database.name
$env:PGUSER = $config.database.user

# Get password
if ($config.azure.key_vault_url -and $Environment -ne 'development') {
    $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
    $env:PGPASSWORD = az keyvault secret show --vault-name $kvName --name "PostgreSQL-$($config.database.name)-Password" --query "value" -o tsv
}

# Run migrations
$apiPath = $config.deployment.install_path
$migrationsPath = Join-Path $apiPath "database\migrations"

if (Test-Path $migrationsPath) {
    $sqlFiles = Get-ChildItem -Path $migrationsPath -Filter "*.sql" | Sort-Object Name
    
    foreach ($file in $sqlFiles) {
        Write-Log "Applying: $($file.Name)" -Level INFO
        & psql -f $file.FullName
        if ($LASTEXITCODE -ne 0) {
            Write-Failure "Migration failed: $($file.Name)"
        }
    }
    
    Write-Success "Database migrations completed ($($sqlFiles.Count) files)"
}
else {
    Write-Log "No migrations found at: $migrationsPath" -Level WARNING
}

exit 0
