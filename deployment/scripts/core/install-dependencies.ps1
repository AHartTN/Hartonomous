# Install Dependencies Script
# Installs Python dependencies in target environment
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

Write-Step "Install Dependencies"

# Load config
$config = Get-DeploymentConfig -Environment $Environment
$apiPath = $config.deployment.install_path

# Navigate to API directory
Push-Location $apiPath

try {
    # Create venv if needed
    $venvPath = if ($config.target.os -eq 'windows') { ".venv\Scripts\python.exe" } else { ".venv/bin/python" }
    
    if (-not (Test-Path $venvPath)) {
        Write-Log "Creating virtual environment..." -Level INFO
        & python -m venv .venv
        if ($LASTEXITCODE -ne 0) { throw "Failed to create venv" }
    }
    
    # Install requirements
    Write-Log "Installing requirements..." -Level INFO
    
    $pipPath = if ($config.target.os -eq 'windows') { ".venv\Scripts\pip.exe" } else { ".venv/bin/pip" }
    
    & $pipPath install --upgrade pip
    & $pipPath install -r requirements.txt
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install requirements"
    }
    
    Write-Success "Dependencies installed"
    exit 0
}
catch {
    Write-Failure "Dependency installation failed: $($_.Exception.Message)"
    exit 1
}
finally {
    Pop-Location
}
