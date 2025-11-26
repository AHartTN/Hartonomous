# Neo4j Worker Deployment Script (PowerShell)
# Deploys Neo4j provenance worker
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"
. "$PSScriptRoot\..\common\azure-auth.ps1"

# Initialize logger
$logLevelName = if ($env:LOG_LEVEL) { $env:LOG_LEVEL } else { 'INFO' }
$logPath = "D:\Hartonomous\logs\deploy-neo4j-worker-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Initialize-Logger -LogFilePath $logPath -LogLevelName $logLevelName

Write-Step "Neo4j Worker Deployment"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
Write-Log "Loaded configuration for: $Environment" -Level INFO

# Check if Neo4j is enabled
if (-not $config.features.neo4j_enabled) {
    Write-Log "Neo4j is disabled for environment: $Environment" -Level WARNING
    Write-Success "Neo4j worker deployment skipped (disabled in config)"
    exit 0
}

# Configure Neo4j connection
Write-Step "Configuring Neo4j Connection"
try {
    & "$PSScriptRoot\configure-neo4j.ps1" -Environment $Environment
}
catch {
    Write-Failure "Neo4j configuration failed: $($_.Exception.Message)"
}

# Test Neo4j connectivity
Write-Step "Testing Neo4j Connectivity"

$neo4jUri = $config.neo4j.uri
$neo4jUser = $config.neo4j.user
$neo4jDatabase = $config.neo4j.database

Write-Log "Neo4j URI: $neo4jUri" -Level INFO
Write-Log "Neo4j Database: $neo4jDatabase" -Level INFO

# Get Neo4j password
$neo4jPassword = $null
if ($config.azure.key_vault_url -and $Environment -ne 'development') {
    Write-Log "Retrieving Neo4j credentials from Azure Key Vault..." -Level INFO

    Connect-AzureWithServicePrincipal `
        -TenantId $env:AZURE_TENANT_ID `
        -ClientId $env:AZURE_CLIENT_ID `
        -ClientSecret $env:AZURE_CLIENT_SECRET `
        -SubscriptionId $env:AZURE_SUBSCRIPTION_ID

    $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
    $secretName = "Neo4j-$($config.target.machine)-Password"
    $neo4jPassword = Get-KeyVaultSecret -VaultName $kvName -SecretName $secretName
}
else {
    # Development: Use environment variable or default
    $neo4jPassword = $env:NEO4J_PASSWORD ?? "neo4jneo4j"
}

# Test connection using cypher-shell (if available)
try {
    Write-Log "Testing Neo4j connection..." -Level DEBUG

    # Create test cypher query
    $testQuery = "RETURN 'connected' as status"

    # Try to connect (cypher-shell may not be in PATH)
    $cypherShell = Get-Command cypher-shell -ErrorAction SilentlyContinue

    if ($cypherShell) {
        $env:NEO4J_PASSWORD = $neo4jPassword
        $result = & cypher-shell -a $neo4jUri -u $neo4jUser -d $neo4jDatabase $testQuery 2>&1

        if ($LASTEXITCODE -eq 0) {
            Write-Success "Neo4j connection successful"
        }
        else {
            Write-Log "Neo4j connection test output: $result" -Level WARNING
            Write-Log "Note: Worker will attempt connection at runtime" -Level INFO
        }
    }
    else {
        Write-Log "cypher-shell not found in PATH, skipping connectivity test" -Level INFO
        Write-Log "Worker will test connection at startup" -Level INFO
    }
}
catch {
    Write-Log "Neo4j connection test failed: $($_.Exception.Message)" -Level WARNING
    Write-Log "Worker will attempt connection at runtime" -Level INFO
}

# Verify Neo4j worker exists in API
$apiPath = Join-Path $PSScriptRoot "..\..\..\api"
$workerPath = Join-Path $apiPath "workers\neo4j_sync.py"

if (-not (Test-Path $workerPath)) {
    Write-Failure "Neo4j worker not found: $workerPath"
}

Write-Success "Neo4j worker found: workers\neo4j_sync.py"

# Update API configuration to ensure Neo4j worker is enabled
Write-Step "Verifying API Configuration"

$apiConfigPath = Join-Path $apiPath "config.py"
if (Test-Path $apiConfigPath) {
    $configContent = Get-Content $apiConfigPath -Raw

    if ($configContent -match 'NEO4J_ENABLED.*=.*True' -or $configContent -match 'neo4j_enabled.*=.*True') {
        Write-Success "Neo4j worker enabled in API configuration"
    }
    else {
        Write-Log "Warning: NEO4J_ENABLED may not be set in config.py" -Level WARNING
        Write-Log "Ensure .env has NEO4J_ENABLED=true" -Level INFO
    }
}

# Check if worker will start with API or separately
Write-Step "Worker Startup Mode"

if ($Environment -eq 'development') {
    Write-Success "Development mode: Worker starts with API automatically"
    Write-Log "The Neo4j worker will start when you run: uvicorn main:app --reload" -Level INFO
}
else {
    Write-Success "Production mode: Worker starts with API service"
    Write-Log "The Neo4j worker is part of the API systemd/Windows service" -Level INFO
}

# Create Neo4j constraints and indexes
Write-Step "Creating Neo4j Schema (Constraints & Indexes)"

# Python script to create constraints
$schemaScript = @'
import os
from neo4j import GraphDatabase

uri = os.getenv('NEO4J_URI')
user = os.getenv('NEO4J_USER')
password = os.getenv('NEO4J_PASSWORD')
database = os.getenv('NEO4J_DATABASE', 'neo4j')

driver = GraphDatabase.driver(uri, auth=(user, password))

try:
    with driver.session(database=database) as session:
        # Create constraint on Atom.atom_id (unique)
        print("Creating constraint on Atom.atom_id...")
        session.run("CREATE CONSTRAINT atom_id_unique IF NOT EXISTS FOR (a:Atom) REQUIRE a.atom_id IS UNIQUE")

        # Create index on Atom.content_hash
        print("Creating index on Atom.content_hash...")
        session.run("CREATE INDEX atom_content_hash IF NOT EXISTS FOR (a:Atom) ON (a.content_hash)")

        # Create index on DERIVED_FROM.created_at
        print("Creating index on DERIVED_FROM.created_at...")
        session.run("CREATE INDEX derived_from_created_at IF NOT EXISTS FOR ()-[r:DERIVED_FROM]-() ON (r.created_at)")

        print("Neo4j schema created successfully")

finally:
    driver.close()
'@

$tempScript = Join-Path $env:TEMP "neo4j-schema-setup.py"
Set-Content -Path $tempScript -Value $schemaScript

try {
    # Set environment variables for Python script
    $env:NEO4J_URI = $neo4jUri
    $env:NEO4J_USER = $neo4jUser
    $env:NEO4J_PASSWORD = $neo4jPassword
    $env:NEO4J_DATABASE = $neo4jDatabase

    # Run schema setup
    Push-Location $apiPath
    $output = & python $tempScript 2>&1
    Pop-Location

    if ($LASTEXITCODE -eq 0) {
        Write-Success "Neo4j schema created"
        Write-Log $output -Level DEBUG
    }
    else {
        Write-Log "Schema creation output: $output" -Level WARNING
        Write-Log "Schema may already exist or Neo4j may be unreachable" -Level INFO
    }
}
catch {
    Write-Log "Failed to create Neo4j schema: $($_.Exception.Message)" -Level WARNING
    Write-Log "You may need to create schema manually" -Level INFO
}
finally {
    Remove-Item $tempScript -Force -ErrorAction SilentlyContinue
}

# Summary
Write-Step "Deployment Summary"
Write-Success "Neo4j worker deployment completed"
Write-Log "Neo4j URI: $neo4jUri" -Level INFO
Write-Log "Neo4j Database: $neo4jDatabase" -Level INFO
Write-Log "Worker file: api\workers\neo4j_sync.py" -Level INFO

if ($Environment -eq 'development') {
    Write-Host ""
    Write-Host "To start the worker:" -ForegroundColor Yellow
    Write-Host "1. cd api" -ForegroundColor White
    Write-Host "2. python -m uvicorn main:app --reload" -ForegroundColor White
    Write-Host "3. Worker starts automatically and listens for PostgreSQL events" -ForegroundColor White
}

Write-Log "Neo4j worker deployment completed" -Level INFO
