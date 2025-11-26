# Start Service Script  
# Starts application services
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $true)]
    [ValidateSet('api', 'neo4j-worker')]
    [string]$Component
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
$scriptRoot = $PSScriptRoot
. "$scriptRoot\..\common\logger.ps1"
. "$scriptRoot\..\common\config-loader.ps1"

Write-Step "Start $Component"

# Load config
$config = Get-DeploymentConfig -Environment $Environment
$apiPath = $config.deployment.install_path

# Start API
if ($Component -eq 'api') {
    # Kill existing process on port
    $existingProcess = Get-NetTCPConnection -LocalPort $config.api.port -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess | Get-Process -ErrorAction SilentlyContinue
    
    if ($existingProcess) {
        Write-Log "Stopping existing process on port $($config.api.port)" -Level INFO
        Stop-Process -Id $existingProcess.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    
    # Start uvicorn
    $pythonExe = if ($config.target.os -eq 'windows') { Join-Path $apiPath ".venv\Scripts\python.exe" } else { Join-Path $apiPath ".venv/bin/python" }
    
    $startArgs = @(
        "-m", "uvicorn",
        "main:app",
        "--host", $config.api.host,
        "--port", $config.api.port,
        "--workers", $config.api.workers,
        "--log-level", $config.api.log_level.ToLower()
    )
    
    if ($config.api.reload) {
        $startArgs += "--reload"
    }
    
    $proc = Start-Process -FilePath $pythonExe -ArgumentList $startArgs -WorkingDirectory $apiPath -WindowStyle Hidden -PassThru
    
    Write-Success "API started (PID: $($proc.Id))"
    
    # Wait and verify
    Start-Sleep -Seconds 5
    
    $listening = Test-NetConnection -ComputerName localhost -Port $config.api.port -InformationLevel Quiet -WarningAction SilentlyContinue
    if ($listening) {
        Write-Success "API is listening on port $($config.api.port)"
    }
    else {
        Write-Failure "API failed to start on port $($config.api.port)"
    }
}

# Neo4j worker starts automatically with API
if ($Component -eq 'neo4j-worker') {
    Write-Log "Neo4j worker starts automatically with API" -Level INFO
}

exit 0
