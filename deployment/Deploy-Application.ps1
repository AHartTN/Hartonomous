# Deploy-Application.ps1
# Deploys application package to target environment
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment,
    
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath
)

$ErrorActionPreference = "Stop"

Write-Host "Deploying to $Environment..." -ForegroundColor Cyan

# Load config
$configPath = Join-Path $PSScriptRoot "config\$Environment.json"
$config = Get-Content $configPath | ConvertFrom-Json

$installPath = $config.deployment.install_path

Write-Host "Extracting artifact to: $installPath" -ForegroundColor Yellow

# Create install directory
if (-not (Test-Path $installPath)) {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
}

# Extract artifact
Expand-Archive -Path $ArtifactPath -DestinationPath $installPath -Force

# Install Python dependencies
Push-Location $installPath
try {
    Write-Host "Installing Python dependencies..." -ForegroundColor Yellow
    
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
    }
    
    & .venv\Scripts\pip.exe install --upgrade pip
    & .venv\Scripts\pip.exe install -r requirements.txt
    
    Write-Host "? Dependencies installed" -ForegroundColor Green
}
finally {
    Pop-Location
}

# Configure environment
Write-Host "Configuring environment..." -ForegroundColor Yellow

$envFile = Join-Path $installPath ".env"
$envContent = @"
DEPLOYMENT_ENVIRONMENT=$Environment
LOG_LEVEL=$($config.api.log_level)
PGHOST=$($config.database.host)
PGPORT=$($config.database.port)
PGDATABASE=$($config.database.name)
PGUSER=$($config.database.user)
API_HOST=$($config.api.host)
API_PORT=$($config.api.port)
"@

Set-Content -Path $envFile -Value $envContent -Force

# Start service
Write-Host "Starting service..." -ForegroundColor Yellow

# Kill existing process
$existingProcess = Get-NetTCPConnection -LocalPort $config.api.port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess | Get-Process -ErrorAction SilentlyContinue

if ($existingProcess) {
    Stop-Process -Id $existingProcess.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Start API
$pythonExe = Join-Path $installPath ".venv\Scripts\python.exe"
$proc = Start-Process -FilePath $pythonExe -ArgumentList "-m","uvicorn","main:app","--host",$config.api.host,"--port",$config.api.port -WorkingDirectory $installPath -WindowStyle Hidden -PassThru

Start-Sleep -Seconds 5

Write-Host "? Deployment complete (PID: $($proc.Id))" -ForegroundColor Green
