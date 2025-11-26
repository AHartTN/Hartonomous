#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Shared deployment helper functions for cross-platform deployment
.DESCRIPTION
    Platform-agnostic helper functions used by all deployment pipelines
#>

# =============================================================================
# PLATFORM DETECTION
# =============================================================================

function Get-PlatformInfo {
    <#
    .SYNOPSIS
        Detect current platform and return platform-specific settings
    #>
    $platform = @{
        IsWindows = $IsWindows
        IsLinux = $IsLinux
        IsMacOS = $IsMacOS
        Shell = if ($IsWindows) { "pwsh" } else { "bash" }
        PathSeparator = if ($IsWindows) { "\" } else { "/" }
        PythonCommand = if ($IsWindows) { "python" } else { "python3" }
        PipCommand = if ($IsWindows) { "pip" } else { "pip3" }
        ServiceManager = if ($IsWindows) { "windows-service" } elseif ($IsLinux) { "systemd" } else { "launchd" }
    }
    
    return $platform
}

# =============================================================================
# AZURE CONFIGURATION
# =============================================================================

function Get-AzureAppConfig {
    <#
    .SYNOPSIS
        Retrieve configuration value from Azure App Configuration
    .PARAMETER AppConfigName
        Azure App Configuration name
    .PARAMETER Key
        Configuration key
    .PARAMETER ResolveKeyVault
        Automatically resolve Key Vault references
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$AppConfigName,
        
        [Parameter(Mandatory=$true)]
        [string]$Key,
        
        [switch]$ResolveKeyVault
    )
    
    Write-Host "  ?? Loading config: $Key" -ForegroundColor Gray
    
    $value = az appconfig kv show `
        --name $AppConfigName `
        --auth-mode login `
        --key $Key `
        --query value `
        -o tsv
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to retrieve configuration key: $Key"
    }
    
    # Check if value is a Key Vault reference
    if ($ResolveKeyVault -and $value -like '*vault.azure.net*') {
        $kvRef = $value | ConvertFrom-Json
        $secretUrl = $kvRef.uri
        
        # Extract vault name and secret name from URL
        if ($secretUrl -match 'https://([^.]+)\.vault\.azure\.net/secrets/([^/]+)') {
            $vaultName = $Matches[1]
            $secretName = $Matches[2]
            
            Write-Host "  ?? Resolving secret: $secretName" -ForegroundColor Gray
            
            $value = az keyvault secret show `
                --vault-name $vaultName `
                --name $secretName `
                --query value `
                -o tsv
            
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to retrieve secret: $secretName"
            }
        }
    }
    
    return $value
}

function Get-EnvironmentConfig {
    <#
    .SYNOPSIS
        Load all configuration for an environment
    .PARAMETER AppConfigName
        Azure App Configuration name
    .PARAMETER Environment
        Environment name (development, staging, production)
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$AppConfigName,
        
        [Parameter(Mandatory=$true)]
        [string]$Environment
    )
    
    Write-Host "?? Loading configuration for: $Environment" -ForegroundColor Cyan
    
    $keyPrefix = "App:${Environment}:"
    
    $config = @{
        InstallPath = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}InstallPath"
        ApiHost = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}ApiHost"
        ApiPort = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}ApiPort"
        DatabaseHost = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}DatabaseHost"
        DatabasePort = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}DatabasePort"
        DatabaseName = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}DatabaseName"
        DatabaseUser = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}DatabaseUser"
        DatabasePassword = Get-AzureAppConfig -AppConfigName $AppConfigName -Key "${keyPrefix}DatabasePassword" -ResolveKeyVault
    }
    
    Write-Host "  ? Configuration loaded" -ForegroundColor Green
    
    return $config
}

# =============================================================================
# APPLICATION DEPLOYMENT
# =============================================================================

function Initialize-PythonEnvironment {
    <#
    .SYNOPSIS
        Create and configure Python virtual environment
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$InstallPath,
        
        [Parameter(Mandatory=$true)]
        [string]$RequirementsFile
    )
    
    $platform = Get-PlatformInfo
    
    Write-Host "?? Setting up Python environment..." -ForegroundColor Cyan
    
    $venvPath = Join-Path $InstallPath ".venv"
    
    # Create virtual environment if it doesn't exist
    if (-not (Test-Path $venvPath)) {
        Write-Host "  Creating virtual environment..." -ForegroundColor Gray
        & $platform.PythonCommand -m venv $venvPath
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create virtual environment"
        }
    }
    
    # Activate virtual environment and install dependencies
    Write-Host "  Installing dependencies..." -ForegroundColor Gray
    
    $activateScript = if ($IsWindows) {
        Join-Path $venvPath "Scripts\Activate.ps1"
    } else {
        Join-Path $venvPath "bin/activate"
    }
    
    if ($IsWindows) {
        & $activateScript
        pip install --quiet --upgrade pip
        pip install --quiet -r (Join-Path $InstallPath $RequirementsFile)
    } else {
        bash -c "source $activateScript && pip install --quiet --upgrade pip && pip install --quiet -r $(Join-Path $InstallPath $RequirementsFile)"
    }
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install dependencies"
    }
    
    Write-Host "  ? Python environment ready" -ForegroundColor Green
}

function Set-ApplicationConfig {
    <#
    .SYNOPSIS
        Create application .env configuration file
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Config,
        
        [Parameter(Mandatory=$true)]
        [string]$InstallPath,
        
        [Parameter(Mandatory=$true)]
        [string]$Environment
    )
    
    Write-Host "?? Configuring application..." -ForegroundColor Cyan
    
    $dbUrl = "postgresql://$($Config.DatabaseUser):$($Config.DatabasePassword)@$($Config.DatabaseHost):$($Config.DatabasePort)/$($Config.DatabaseName)"
    
    $envContent = @"
DATABASE_URL=$dbUrl
API_HOST=$($Config.ApiHost)
API_PORT=$($Config.ApiPort)
ENVIRONMENT=$Environment
"@
    
    $envFile = Join-Path $InstallPath ".env"
    $envContent | Set-Content -Path $envFile -Force
    
    Write-Host "  ? Configuration file created" -ForegroundColor Green
}

function Invoke-DatabaseMigration {
    <#
    .SYNOPSIS
        Run database migrations using Alembic
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$InstallPath
    )
    
    $platform = Get-PlatformInfo
    
    Write-Host "??? Running database migrations..." -ForegroundColor Cyan
    
    $alembicConfig = Join-Path $InstallPath "alembic.ini"
    
    if (-not (Test-Path $alembicConfig)) {
        Write-Host "  ?? No alembic.ini found, skipping migrations" -ForegroundColor Yellow
        return
    }
    
    Push-Location $InstallPath
    try {
        if ($IsWindows) {
            .\.venv\Scripts\Activate.ps1
            alembic upgrade head
        } else {
            bash -c "source .venv/bin/activate && alembic upgrade head"
        }
        
        if ($LASTEXITCODE -ne 0) {
            throw "Database migration failed"
        }
        
        Write-Host "  ? Migrations completed" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Restart-ApplicationService {
    <#
    .SYNOPSIS
        Restart the application service
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Config,
        
        [Parameter(Mandatory=$true)]
        [string]$InstallPath
    )
    
    $platform = Get-PlatformInfo
    
    Write-Host "?? Restarting application service..." -ForegroundColor Cyan
    
    # Stop existing processes
    if ($IsWindows) {
        Get-Process -ErrorAction SilentlyContinue | 
            Where-Object { $_.CommandLine -like "*uvicorn*main:app*" } | 
            Stop-Process -Force
    } else {
        bash -c "pkill -f 'uvicorn main:app' || true"
    }
    
    Start-Sleep -Seconds 2
    
    # Start new process
    Push-Location $InstallPath
    try {
        $pythonExe = if ($IsWindows) {
            Join-Path $InstallPath ".venv\Scripts\python.exe"
        } else {
            Join-Path $InstallPath ".venv/bin/python"
        }
        
        if ($IsWindows) {
            Start-Process -FilePath $pythonExe `
                -ArgumentList "-m", "uvicorn", "main:app", "--host", $Config.ApiHost, "--port", $Config.ApiPort `
                -WindowStyle Hidden
        } else {
            bash -c "nohup $pythonExe -m uvicorn main:app --host $($Config.ApiHost) --port $($Config.ApiPort) > /dev/null 2>&1 &"
        }
        
        Write-Host "  ? Service started" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Test-ApplicationHealth {
    <#
    .SYNOPSIS
        Verify application is responding to health checks
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Config,
        
        [int]$Timeout = 10,
        [int]$Retries = 3,
        [int]$Delay = 5
    )
    
    Write-Host "?? Verifying application health..." -ForegroundColor Cyan
    
    $healthUrl = "http://$($Config.ApiHost):$($Config.ApiPort)/v1/health"
    
    Start-Sleep -Seconds $Delay
    
    for ($i = 1; $i -le $Retries; $i++) {
        Write-Host "  Attempt $i of $Retries..." -ForegroundColor Gray
        
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -TimeoutSec $Timeout -UseBasicParsing -ErrorAction Stop
            
            if ($response.StatusCode -eq 200) {
                Write-Host "  ? Health check passed" -ForegroundColor Green
                Write-Host "  Response: $($response.Content)" -ForegroundColor Gray
                return $true
            }
        }
        catch {
            Write-Host "  ?? Health check failed: $($_.Exception.Message)" -ForegroundColor Yellow
            
            if ($i -lt $Retries) {
                Start-Sleep -Seconds $Delay
            }
        }
    }
    
    Write-Host "  ? Health check failed after $Retries attempts" -ForegroundColor Red
    return $false
}

# =============================================================================
# EXPORT FUNCTIONS
# =============================================================================

Export-ModuleMember -Function @(
    'Get-PlatformInfo'
    'Get-AzureAppConfig'
    'Get-EnvironmentConfig'
    'Initialize-PythonEnvironment'
    'Set-ApplicationConfig'
    'Invoke-DatabaseMigration'
    'Restart-ApplicationService'
    'Test-ApplicationHealth'
)
