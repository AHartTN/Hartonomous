# Configure Service Script
# Configures service settings and environment
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $true)]
    [ValidateSet('api', 'neo4j')]
    [string]$Component
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
$scriptRoot = $PSScriptRoot
. "$scriptRoot\..\common\logger.ps1"
. "$scriptRoot\..\common\config-loader.ps1"

Write-Step "Configure $Component Service"

# Load config
$config = Get-DeploymentConfig -Environment $Environment
$apiPath = $config.deployment.install_path

# Configure API
if ($Component -eq 'api') {
    $envFile = Join-Path $apiPath ".env"
    
    $envContent = @"
DEPLOYMENT_ENVIRONMENT=$Environment
LOG_LEVEL=$($config.api.log_level)
PGHOST=$($config.database.host)
PGPORT=$($config.database.port)
PGDATABASE=$($config.database.name)
PGUSER=$($config.database.user)
API_HOST=$($config.api.host)
API_PORT=$($config.api.port)
API_WORKERS=$($config.api.workers)
API_RELOAD=$($config.api.reload.ToString().ToLower())
NEO4J_ENABLED=$($config.features.neo4j_enabled.ToString().ToLower())
"@

    if ($config.features.neo4j_enabled) {
        $envContent += "`nNEO4J_URI=$($config.neo4j.uri)`nNEO4J_USER=$($config.neo4j.user)`nNEO4J_DATABASE=$($config.neo4j.database)"
    }
    
    # Add secrets from Key Vault
    if ($config.azure.key_vault_url -and $Environment -ne 'development') {
        $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
        
        $dbPass = az keyvault secret show --vault-name $kvName --name "PostgreSQL-$($config.database.name)-Password" --query "value" -o tsv
        $envContent += "`nPGPASSWORD=$dbPass"
        
        if ($config.features.neo4j_enabled) {
            $neo4jPass = az keyvault secret show --vault-name $kvName --name "Neo4j-$($config.target.machine)-Password" --query "value" -o tsv
            $envContent += "`nNEO4J_PASSWORD=$neo4jPass"
        }
    }
    
    Set-Content -Path $envFile -Value $envContent -Force
    Write-Success "API configuration created: $envFile"
}

# Configure Neo4j
if ($Component -eq 'neo4j') {
    Write-Log "Neo4j configuration handled in API .env" -Level INFO
}

exit 0
