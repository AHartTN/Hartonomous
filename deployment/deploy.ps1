# deploy.ps1
# Platform-agnostic deployment script for Windows
# Uses Azure App Config + Key Vault with Arc managed identity
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment,
    
    [Parameter(Mandatory=$true)]
    [string]$ArtifactPath
)

$ErrorActionPreference = "Stop"

Write-Host "?? Deploying to $Environment environment..." -ForegroundColor Cyan

# Get configuration from Azure App Configuration
$appConfigName = $env:APP_CONFIG_NAME
$resourceGroup = $env:RESOURCE_GROUP

if (-not $appConfigName -or -not $resourceGroup) {
    Write-Host "? APP_CONFIG_NAME and RESOURCE_GROUP environment variables required" -ForegroundColor Red
    exit 1
}

Write-Host "?? Loading configuration from Azure App Configuration..." -ForegroundColor Yellow

# Get configuration values
$installPath = az appconfig kv show --name $appConfigName --key "deployment:${Environment}:install_path" --query value -o tsv
$apiHost = az appconfig kv show --name $appConfigName --key "api:${Environment}:host" --query value -o tsv
$apiPort = az appconfig kv show --name $appConfigName --key "api:${Environment}:port" --query value -o tsv
$dbUrl = az appconfig kv show --name $appConfigName --key "database:${Environment}:connection_string" --query value -o tsv

Write-Host "?? Installing to: $installPath" -ForegroundColor Green
Write-Host "?? API: ${apiHost}:${apiPort}" -ForegroundColor Green

# Create installation directory
if (-not (Test-Path $installPath)) {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
}

# Extract artifact
Write-Host "?? Extracting application..." -ForegroundColor Yellow
Expand-Archive -Path $ArtifactPath -DestinationPath $installPath -Force

# Setup Python environment
Set-Location $installPath

$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) {
    $pythonCmd = Get-Command python3 -ErrorAction SilentlyContinue
}

if (-not $pythonCmd) {
    Write-Host "? Python not found" -ForegroundColor Red
    exit 1
}

Write-Host "?? Setting up Python environment..." -ForegroundColor Yellow

# Create venv if doesn't exist
if (-not (Test-Path ".venv")) {
    & $pythonCmd.Source -m venv .venv
}

# Activate venv
$activateScript = Join-Path $installPath ".venv\Scripts\Activate.ps1"
if (Test-Path $activateScript) {
    & $activateScript
} else {
    Write-Host "? Cannot activate virtual environment" -ForegroundColor Red
    exit 1
}

# Install dependencies
Write-Host "?? Installing dependencies..." -ForegroundColor Yellow
pip install --quiet --upgrade pip
pip install --quiet -r requirements.txt

# Create .env file
Write-Host "?? Configuring environment..." -ForegroundColor Yellow
@"
DATABASE_URL=$dbUrl
API_HOST=$apiHost
API_PORT=$apiPort
ENVIRONMENT=$Environment
"@ | Set-Content -Path ".env" -Force

# Run database migrations
Write-Host "??? Running database migrations..." -ForegroundColor Yellow
if (Test-Path "alembic.ini") {
    alembic upgrade head
}

# Restart service
Write-Host "?? Restarting service..." -ForegroundColor Yellow

# Kill existing process
Get-Process | Where-Object { $_.CommandLine -like "*uvicorn*main:app*" } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Start new process
$pythonExe = Join-Path $installPath ".venv\Scripts\python.exe"
$proc = Start-Process -FilePath $pythonExe -ArgumentList "-m","uvicorn","main:app","--host",$apiHost,"--port",$apiPort -WorkingDirectory $installPath -WindowStyle Hidden -PassThru

Write-Host "? Service started (PID: $($proc.Id))" -ForegroundColor Green

# Verify deployment
Write-Host "?? Verifying deployment..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

$healthUrl = "http://${apiHost}:${apiPort}/health"
try {
    $response = Invoke-WebRequest -Uri $healthUrl -TimeoutSec 10 -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        Write-Host "? Health check passed" -ForegroundColor Green
        Write-Host "?? Deployment to $Environment complete!" -ForegroundColor Cyan
        exit 0
    }
} catch {
    Write-Host "?? Health check failed - service may still be starting" -ForegroundColor Yellow
    exit 1
}
