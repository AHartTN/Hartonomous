# API Application Deployment Script (PowerShell)
# Deploys Hartonomous FastAPI application
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $false)]
    [switch]$SkipBackup,

    [Parameter(Mandatory = $false)]
    [switch]$SkipDependencies
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"
. "$PSScriptRoot\..\common\azure-auth.ps1"

# Initialize logger
$logLevelName = if ($env:LOG_LEVEL) { $env:LOG_LEVEL } else { 'INFO' }
$logPath = "D:\Hartonomous\logs\api-deploy-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Initialize-Logger -LogFilePath $logPath -LogLevelName $logLevelName

Write-Step "API Application Deployment"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
Write-Log "Loaded configuration for: $Environment" -Level INFO

# Determine paths
$repoRoot = Join-Path $PSScriptRoot "..\..\..\"
$sourceApiPath = Join-Path $repoRoot "api"
$targetApiPath = $config.deployment.install_path

Write-Log "Source API path: $sourceApiPath" -Level INFO
Write-Log "Target install path: $targetApiPath" -Level INFO

# Validate source exists
if (-not (Test-Path $sourceApiPath)) {
    Write-Failure "Source API directory not found: $sourceApiPath"
}

# Create target directory if it doesn't exist
if (-not (Test-Path $targetApiPath)) {
    Write-Log "Creating target directory: $targetApiPath" -Level INFO
    New-Item -ItemType Directory -Path $targetApiPath -Force | Out-Null
}

# Copy source to target (excluding venv and cache)
Write-Step "Copying Application Files"
$excludePatterns = @(".venv", "__pycache__", "*.pyc", ".pytest_cache", ".env")

Write-Log "Syncing from source to target..." -Level INFO
robocopy $sourceApiPath $targetApiPath /MIR /XD .venv __pycache__ .pytest_cache /XF *.pyc .env /NFL /NDL /NJH /NJS /NC /NS /NP
if ($LASTEXITCODE -gt 7) {
    Write-Failure "Failed to copy application files (robocopy exit code: $LASTEXITCODE)"
}
Write-Success "Application files copied to: $targetApiPath"

# Now work in the target path
$apiPath = $targetApiPath

# Backup existing deployment (unless skipped)
if (-not $SkipBackup) {
    Write-Step "Creating Pre-Deployment Backup"
    & "$PSScriptRoot\backup-application.ps1" -Environment $Environment
    if ($LASTEXITCODE -ne 0) {
        Write-Log "Backup failed, but continuing..." -Level WARNING
    }
}

# Install/Update dependencies
if (-not $SkipDependencies) {
    Write-Step "Installing Python Dependencies"

    Push-Location $apiPath
    try {
        # Check if virtual environment exists
        $venvPath = if ($config.target.os -eq 'windows') { ".venv\Scripts\python.exe" } else { ".venv/bin/python" }

        if (-not (Test-Path $venvPath)) {
            Write-Log "Creating virtual environment..." -Level INFO
            python -m venv .venv
        }

        # Activate virtual environment
        $activateScript = if ($config.target.os -eq 'windows') {
            ".venv\Scripts\Activate.ps1"
        } else {
            ".venv/bin/activate"
        }

        if (Test-Path $activateScript) {
            Write-Log "Activating virtual environment..." -Level DEBUG
            if ($config.target.os -eq 'windows') {
                & $activateScript
            }
        }

        # Install requirements
        Write-Log "Installing requirements..." -Level INFO
        & python -m pip install --upgrade pip
        & python -m pip install -r requirements.txt

        if ($LASTEXITCODE -ne 0) {
            Write-Failure "Failed to install dependencies"
        }

        Write-Success "Dependencies installed"
    }
    catch {
        Write-Failure "Dependency installation failed: $($_.Exception.Message)"
    }
    finally {
        Pop-Location
    }
}

# Create .env file for environment
Write-Step "Configuring Environment Variables"

$envFile = Join-Path $apiPath ".env"
$envTemplate = @"
# Auto-generated .env file for $Environment
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

DEPLOYMENT_ENVIRONMENT=$Environment
LOG_LEVEL=$($config.api.log_level)

# Database Configuration
PGHOST=$($config.database.host)
PGPORT=$($config.database.port)
PGDATABASE=$($config.database.name)
PGUSER=$($config.database.user)

# API Configuration
API_HOST=$($config.api.host)
API_PORT=$($config.api.port)
API_WORKERS=$($config.api.workers)
API_RELOAD=$($config.api.reload)

# Feature Flags
NEO4J_ENABLED=$($config.features.neo4j_enabled.ToString().ToLower())
AGE_WORKER_ENABLED=$($config.features.age_worker_enabled.ToString().ToLower())
AUTH_ENABLED=$($config.features.auth_enabled.ToString().ToLower())
"@

# Add Neo4j config if enabled
if ($config.features.neo4j_enabled) {
    $envTemplate += @"

# Neo4j Configuration
NEO4J_URI=$($config.neo4j.uri)
NEO4J_USER=$($config.neo4j.user)
NEO4J_DATABASE=$($config.neo4j.database)
"@
}

# Add Azure AD config if auth enabled
if ($config.features.auth_enabled) {
    $envTemplate += @"

# Azure AD Configuration
AZURE_AD_TENANT_ID=$($config.azure.tenant_id)
AZURE_AD_CLIENT_ID=$($config.azure.client_id)
"@
}

try {
    Set-Content -Path $envFile -Value $envTemplate -Force
    Write-Success "Environment file created: $envFile"
}
catch {
    Write-Failure "Failed to create .env file: $($_.Exception.Message)"
}

# Get secrets from Azure Key Vault for non-development environments
if ($Environment -ne 'development' -and $config.azure.key_vault_url) {
    Write-Step "Retrieving Secrets from Azure Key Vault"

    # Authenticate to Azure
    Connect-AzureWithServicePrincipal `
        -TenantId $env:AZURE_TENANT_ID `
        -ClientId $env:AZURE_CLIENT_ID `
        -ClientSecret $env:AZURE_CLIENT_SECRET `
        -SubscriptionId $env:AZURE_SUBSCRIPTION_ID

    $kvName = ($config.azure.key_vault_url -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')

    # Get database password
    $dbPassword = Get-KeyVaultSecret -VaultName $kvName -SecretName "PostgreSQL-$($config.database.name)-Password"
    Add-Content -Path $envFile -Value "PGPASSWORD=$dbPassword"

    # Get Neo4j password if enabled
    if ($config.features.neo4j_enabled) {
        $neo4jSecretName = "Neo4j-$($config.target.machine)-Password"
        $neo4jPassword = Get-KeyVaultSecret -VaultName $kvName -SecretName $neo4jSecretName
        Add-Content -Path $envFile -Value "NEO4J_PASSWORD=$neo4jPassword"
    }

    Write-Success "Secrets retrieved from Key Vault"
}
else {
    Write-Log "Using local environment variables for secrets (development)" -Level INFO
}

# Run database migrations (schema deployment)
Write-Step "Running Database Migrations"
& "$PSScriptRoot\..\database\deploy-schema.ps1" -Environment $Environment -SkipBackup
if ($LASTEXITCODE -ne 0) {
    Write-Failure "Database migrations failed"
}

# Stop existing service (if running)
Write-Step "Stopping Existing Service"

$serviceName = "hartonomous-api-$Environment"

try {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Write-Log "Stopping service: $serviceName" -Level INFO
        Stop-Service -Name $serviceName -Force
        Start-Sleep -Seconds 2
        Write-Success "Service stopped"
    }
    else {
        Write-Log "Service not running or not installed: $serviceName" -Level INFO
    }
}
catch {
    Write-Log "No existing service to stop: $($_.Exception.Message)" -Level INFO
}

# Start API (development: foreground, staging/prod: background service)
Write-Step "Starting API Application"

Push-Location $apiPath
try {
    if ($Environment -eq 'development') {
        # Development: Start API as background job
        Write-Log "Starting API as background process for development..." -Level INFO
        
        $pythonExe = ".venv\Scripts\python.exe"
        if (-not (Test-Path $pythonExe)) {
            Write-Failure "Python venv not found: $pythonExe"
        }

        # Kill any existing uvicorn processes on the port
        $existingProcess = Get-NetTCPConnection -LocalPort $config.api.port -ErrorAction SilentlyContinue | 
            Select-Object -ExpandProperty OwningProcess | Get-Process -ErrorAction SilentlyContinue
        
        if ($existingProcess) {
            Write-Log "Stopping existing API process on port $($config.api.port)..." -Level INFO
            Stop-Process -Id $existingProcess.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }

        # Start uvicorn in background
        $startArgs = @(
            "-m", "uvicorn",
            "main:app",
            "--host", $config.api.host,
            "--port", $config.api.port,
            "--reload",
            "--log-level", $config.api.log_level.ToLower()
        )

        $proc = Start-Process -FilePath $pythonExe -ArgumentList $startArgs -WorkingDirectory $apiPath -WindowStyle Hidden -PassThru
        
        Write-Success "API started in background (PID: $($proc.Id))"
        Write-Log "API URL: http://$($config.api.host):$($config.api.port)" -Level INFO
        
        # Wait briefly for API to start
        Start-Sleep -Seconds 3
        
        # Verify it's listening
        try {
            $testConnection = Test-NetConnection -ComputerName localhost -Port $config.api.port -InformationLevel Quiet -WarningAction SilentlyContinue
            if ($testConnection) {
                Write-Success "API is listening on port $($config.api.port)"
            }
            else {
                Write-Log "API may still be starting..." -Level WARNING
            }
        }
        catch {
            Write-Log "Could not verify API port: $($_.Exception.Message)" -Level WARNING
        }
    }
    else {
        # Production/Staging: Start as Windows service
        Write-Log "Configuring API as Windows service..." -Level INFO

        # Determine Python executable in venv
        $pythonExe = Join-Path $apiPath ".venv\Scripts\python.exe"
        if (-not (Test-Path $pythonExe)) {
            Write-Failure "Python venv not found: $pythonExe"
        }

        # Determine uvicorn module path
        $uvicornPath = Join-Path $apiPath ".venv\Scripts\uvicorn.exe"

        # Create service wrapper script
        $wrapperScript = Join-Path $apiPath "start-service.ps1"
        $wrapperContent = @"
# Hartonomous API Service Wrapper
# Auto-generated by deploy-api.ps1
Set-Location "$apiPath"
`$env:DEPLOYMENT_ENVIRONMENT = "$Environment"

# Load environment from .env file
Get-Content "$envFile" | ForEach-Object {
    if (`$_ -match '^([^=]+)=(.*)$') {
        `$key = `$matches[1].Trim()
        `$value = `$matches[2].Trim()
        [Environment]::SetEnvironmentVariable(`$key, `$value, 'Process')
    }
}

# Start uvicorn
& "$pythonExe" -m uvicorn main:app ``
    --host $($config.api.host) ``
    --port $($config.api.port) ``
    --workers $($config.api.workers) ``
    --log-level $($config.api.log_level.ToLower())
"@

        Set-Content -Path $wrapperScript -Value $wrapperContent
        Write-Log "Created service wrapper: $wrapperScript" -Level INFO

        # Check if NSSM is available (preferred method)
        $nssmPath = Get-Command nssm -ErrorAction SilentlyContinue

        if ($nssmPath) {
            Write-Log "Using NSSM for service installation" -Level INFO

            # Install service with NSSM
            $nssmArgs = @(
                "install", $serviceName,
                "powershell.exe",
                "-ExecutionPolicy", "Bypass",
                "-File", $wrapperScript
            )

            & nssm @nssmArgs

            # Configure service
            & nssm set $serviceName AppDirectory $apiPath
            & nssm set $serviceName DisplayName "Hartonomous API ($Environment)"
            & nssm set $serviceName Description "Hartonomous Atomic Graph Intelligence Platform API"
            & nssm set $serviceName Start SERVICE_AUTO_START
            & nssm set $serviceName AppStdout "$apiPath\logs\service-stdout.log"
            & nssm set $serviceName AppStderr "$apiPath\logs\service-stderr.log"
            & nssm set $serviceName AppRotateFiles 1
            & nssm set $serviceName AppRotateBytes 10485760  # 10MB

            Write-Success "Service installed with NSSM: $serviceName"
        }
        else {
            Write-Log "NSSM not found, using native Windows service" -Level INFO
            Write-Log "Note: Native services don't support PowerShell directly" -Level WARNING
            Write-Log "Creating service with manual start" -Level INFO

            # Create batch file wrapper for native service
            $batchWrapper = Join-Path $apiPath "start-service.bat"
            $batchContent = @"
@echo off
cd /d "$apiPath"
powershell.exe -ExecutionPolicy Bypass -File "$wrapperScript"
"@
            Set-Content -Path $batchWrapper -Value $batchContent

            # Use sc.exe to create service
            $scArgs = @(
                "create", $serviceName,
                "binPath=", "`"$batchWrapper`"",
                "start=", "demand",
                "DisplayName=", "`"Hartonomous API ($Environment)`""
            )

            $scResult = & sc.exe @scArgs 2>&1

            if ($LASTEXITCODE -eq 0 -or $scResult -match "already exists") {
                Write-Success "Service created: $serviceName"
                Write-Log "To start service: sc.exe start $serviceName" -Level INFO
            }
            else {
                Write-Log "Service creation output: $scResult" -Level WARNING
                Write-Log "You may need to run as Administrator" -Level WARNING
            }

            Write-Log "For better service management, consider installing NSSM:" -Level INFO
            Write-Log "  choco install nssm" -Level INFO
        }

        # Start the service
        Write-Step "Starting Service"
        try {
            Start-Service -Name $serviceName -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3

            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($service -and $service.Status -eq 'Running') {
                Write-Success "Service started successfully: $serviceName"
            }
            else {
                Write-Log "Service did not start (may require manual start)" -Level WARNING
                Write-Log "Start manually: Start-Service -Name $serviceName" -Level INFO
            }
        }
        catch {
            Write-Log "Could not start service: $($_.Exception.Message)" -Level WARNING
            Write-Log "Start manually: Start-Service -Name $serviceName" -Level INFO
        }
    }
}
catch {
    Write-Failure "Failed to start API: $($_.Exception.Message)"
}
finally {
    Pop-Location
}

# Summary
Write-Step "Deployment Summary"
Write-Success "API deployment completed for: $Environment"
Write-Log "API path: $apiPath" -Level INFO
Write-Log "Configuration: $envFile" -Level INFO

if ($Environment -eq 'development') {
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. cd api" -ForegroundColor White
    Write-Host "2. python -m uvicorn main:app --reload" -ForegroundColor White
    Write-Host "3. Test: http://localhost:$($config.api.port)/health" -ForegroundColor White
}

Write-Log "API application deployment completed" -Level INFO

