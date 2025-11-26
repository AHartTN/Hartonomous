# Core Deployment Orchestrator
# Single entry point for all deployments
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import core modules
$scriptRoot = $PSScriptRoot
. "$scriptRoot\..\common\logger.ps1"
. "$scriptRoot\..\common\config-loader.ps1"

# Initialize
$logLevel = if ($env:LOG_LEVEL) { $env:LOG_LEVEL } else { 'INFO' }
Initialize-Logger -LogFilePath "D:\Hartonomous\logs\deploy-$(Get-Date -Format 'yyyyMMdd-HHmmss').log" -LogLevelName $logLevel

Write-Step "Hartonomous Deployment - $Environment"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set"
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
Write-Log "Loaded configuration for: $Environment" -Level INFO

# Execute deployment steps
try {
    # 1. Copy artifacts
    Write-Step "Step 1: Copy Artifacts"
    & "$scriptRoot\copy-artifacts.ps1" -Environment $Environment
    if ($LASTEXITCODE -ne 0) { throw "Artifact copy failed" }

    # 2. Install dependencies
    Write-Step "Step 2: Install Dependencies"
    & "$scriptRoot\install-dependencies.ps1" -Environment $Environment
    if ($LASTEXITCODE -ne 0) { throw "Dependency installation failed" }

    # 3. Deploy database
    Write-Step "Step 3: Deploy Database"
    & "$scriptRoot\deploy-database.ps1" -Environment $Environment
    if ($LASTEXITCODE -ne 0) { throw "Database deployment failed" }

    # 4. Configure services
    Write-Step "Step 4: Configure Services"
    & "$scriptRoot\configure-service.ps1" -Environment $Environment -Component api
    if ($LASTEXITCODE -ne 0) { throw "Service configuration failed" }

    if ($config.features.neo4j_enabled) {
        & "$scriptRoot\configure-service.ps1" -Environment $Environment -Component neo4j
        if ($LASTEXITCODE -ne 0) { throw "Neo4j configuration failed" }
    }

    # 5. Start services
    Write-Step "Step 5: Start Services"
    & "$scriptRoot\start-service.ps1" -Environment $Environment -Component api
    if ($LASTEXITCODE -ne 0) { throw "Service start failed" }

    if ($config.features.neo4j_enabled) {
        & "$scriptRoot\start-service.ps1" -Environment $Environment -Component neo4j-worker
        if ($LASTEXITCODE -ne 0) { throw "Neo4j worker start failed" }
    }

    Write-Success "Deployment completed successfully"
    exit 0
}
catch {
    Write-Failure "Deployment failed: $($_.Exception.Message)"
    exit 1
}
