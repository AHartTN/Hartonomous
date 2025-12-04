<#
.SYNOPSIS
    Idempotent IIS Infrastructure Setup for Hartonomous
.DESCRIPTION
    Configures IIS app pools, sites, and applications in an idempotent manner.
    Can be run multiple times safely - only creates missing resources.
.PARAMETER Environment
    Target environment: Development, Staging, or Production
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('Development', 'Staging', 'Production')]
    [string]$Environment = 'Production'
)

$ErrorActionPreference = 'Stop'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Hartonomous IIS Infrastructure Setup" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Import WebAdministration module
Import-Module WebAdministration

# Configuration
$DeployRoot = "C:\inetpub\wwwroot\hartonomous"
$ServicesRoot = "C:\Services\Hartonomous"

switch ($Environment) {
    'Production' {
        $DeployDir = "$DeployRoot\production"
        $ServicesDir = "$ServicesRoot\production"
        $ApiPort = 5000
        $WebPort = 5001
        $ApiHostname = "api.hartonomous.com"
        $WebHostname = "hartonomous.com"
    }
    'Staging' {
        $DeployDir = "$DeployRoot\staging"
        $ServicesDir = "$ServicesRoot\staging"
        $ApiPort = 5010
        $WebPort = 5011
        $ApiHostname = "api-staging.hartonomous.com"
        $WebHostname = "staging.hartonomous.com"
    }
    'Development' {
        $DeployDir = "$DeployRoot\development"
        $ServicesDir = "$ServicesRoot\development"
        $ApiPort = 5020
        $WebPort = 5021
        $ApiHostname = "api-dev.hartonomous.com"
        $WebHostname = "dev.hartonomous.com"
    }
}

# Function to create directory if it doesn't exist
function Ensure-Directory {
    param([string]$Path)
    
    if (-not (Test-Path $Path)) {
        Write-Host "Creating directory: $Path" -ForegroundColor Yellow
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
        Write-Host "✓ Created directory" -ForegroundColor Green
    }
    else {
        Write-Host "✓ Directory exists: $Path" -ForegroundColor Gray
    }
}

# Function to create or update app pool
function Ensure-AppPool {
    param(
        [string]$Name,
        [string]$Runtime = 'v4.0',
        [string]$PipelineMode = 'Integrated',
        [string]$IdentityType = 'ApplicationPoolIdentity'
    )
    
    Write-Host "Configuring App Pool: $Name" -ForegroundColor Yellow
    
    $appPoolPath = "IIS:\AppPools\$Name"
    
    if (-not (Test-Path $appPoolPath)) {
        Write-Host "  Creating app pool..." -ForegroundColor Yellow
        New-WebAppPool -Name $Name | Out-Null
    }
    else {
        Write-Host "  App pool exists" -ForegroundColor Gray
    }
    
    # Configure app pool settings (idempotent)
    Set-ItemProperty -Path $appPoolPath -Name managedRuntimeVersion -Value $Runtime
    Set-ItemProperty -Path $appPoolPath -Name managedPipelineMode -Value $PipelineMode
    Set-ItemProperty -Path $appPoolPath -Name processModel.identityType -Value $IdentityType
    
    # Additional recommended settings
    Set-ItemProperty -Path $appPoolPath -Name processModel.idleTimeout -Value ([TimeSpan]::FromMinutes(20))
    Set-ItemProperty -Path $appPoolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::FromMinutes(1740)) # 29 hours
    Set-ItemProperty -Path $appPoolPath -Name startMode -Value 'AlwaysRunning'
    
    Write-Host "✓ App Pool configured: $Name" -ForegroundColor Green
}

# Function to create or update IIS site
function Ensure-Site {
    param(
        [string]$Name,
        [string]$PhysicalPath,
        [string]$AppPoolName,
        [int]$Port,
        [string]$Hostname = ""
    )
    
    Write-Host "Configuring Site: $Name" -ForegroundColor Yellow
    
    Ensure-Directory -Path $PhysicalPath
    
    $sitePath = "IIS:\Sites\$Name"
    
    if (-not (Test-Path $sitePath)) {
        Write-Host "  Creating site..." -ForegroundColor Yellow
        
        if ($Hostname) {
            New-Website -Name $Name `
                -PhysicalPath $PhysicalPath `
                -ApplicationPool $AppPoolName `
                -Port $Port `
                -HostHeader $Hostname `
                -Force | Out-Null
        }
        else {
            New-Website -Name $Name `
                -PhysicalPath $PhysicalPath `
                -ApplicationPool $AppPoolName `
                -Port $Port `
                -Force | Out-Null
        }
    }
    else {
        Write-Host "  Site exists" -ForegroundColor Gray
        
        # Update settings (idempotent)
        Set-ItemProperty -Path $sitePath -Name physicalPath -Value $PhysicalPath
        Set-ItemProperty -Path $sitePath -Name applicationPool -Value $AppPoolName
    }
    
    # Set permissions
    Write-Host "  Setting permissions..." -ForegroundColor Yellow
    icacls $PhysicalPath /grant "IIS_IUSRS:(OI)(CI)RX" /T /Q | Out-Null
    icacls $PhysicalPath /grant "IUSR:(OI)(CI)RX" /T /Q | Out-Null
    
    Write-Host "✓ Site configured: $Name" -ForegroundColor Green
}

# Function to ensure Windows Service exists
function Ensure-WindowsService {
    param(
        [string]$ServiceName,
        [string]$DisplayName,
        [string]$BinaryPath,
        [string]$Description
    )
    
    Write-Host "Configuring Windows Service: $ServiceName" -ForegroundColor Yellow
    
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    
    if (-not $service) {
        Write-Host "  Creating service..." -ForegroundColor Yellow
        
        # Create placeholder executable if it doesn't exist (will be replaced during deployment)
        $serviceDir = Split-Path -Parent $BinaryPath
        Ensure-Directory -Path $serviceDir
        
        if (-not (Test-Path $BinaryPath)) {
            # Create a minimal placeholder
            @"
Write-Host "Hartonomous Worker Service Placeholder"
Write-Host "This will be replaced during deployment"
Start-Sleep -Seconds 3600
"@ | Out-File -FilePath "$serviceDir\placeholder.ps1" -Encoding UTF8
            
            # Note: Actual service creation requires the real binary
            Write-Host "  ⚠ Service directory created. Binary will be deployed before service creation." -ForegroundColor Yellow
        }
        else {
            New-Service -Name $ServiceName `
                -BinaryPathName $BinaryPath `
                -DisplayName $DisplayName `
                -Description $Description `
                -StartupType Automatic | Out-Null
                
            Write-Host "✓ Service created: $ServiceName" -ForegroundColor Green
        }
    }
    else {
        Write-Host "  Service exists" -ForegroundColor Gray
        
        # Update description (idempotent)
        Set-Service -Name $ServiceName -Description $Description -StartupType Automatic
        Write-Host "✓ Service configured: $ServiceName" -ForegroundColor Green
    }
    
    # Set permissions on service directory
    if (Test-Path $serviceDir) {
        icacls $serviceDir /grant "NETWORK SERVICE:(OI)(CI)F" /T /Q | Out-Null
    }
}

# Create root directories
Write-Host "`nCreating root directories..." -ForegroundColor Cyan
Ensure-Directory -Path $DeployRoot
Ensure-Directory -Path $ServicesRoot
Ensure-Directory -Path $DeployDir
Ensure-Directory -Path $ServicesDir

# Setup API (IIS)
Write-Host "`nSetting up API infrastructure..." -ForegroundColor Cyan
$apiAppPool = "Hartonomous.API.$Environment"
$apiSite = "Hartonomous.API.$Environment"
$apiPath = "$DeployDir\api"

Ensure-AppPool -Name $apiAppPool -Runtime "" -IdentityType "ApplicationPoolIdentity"
Ensure-Site -Name $apiSite -PhysicalPath $apiPath -AppPoolName $apiAppPool -Port $ApiPort -Hostname $ApiHostname

# Setup Web (IIS)
Write-Host "`nSetting up Web infrastructure..." -ForegroundColor Cyan
$webAppPool = "Hartonomous.Web.$Environment"
$webSite = "Hartonomous.Web.$Environment"
$webPath = "$DeployDir\web"

Ensure-AppPool -Name $webAppPool -Runtime "" -IdentityType "ApplicationPoolIdentity"
Ensure-Site -Name $webSite -PhysicalPath $webPath -AppPoolName $webAppPool -Port $WebPort -Hostname $WebHostname

# Setup Worker (Windows Service)
Write-Host "`nSetting up Worker service infrastructure..." -ForegroundColor Cyan
$workerService = "Hartonomous.Worker.$Environment"
$workerDisplay = "Hartonomous Worker Service ($Environment)"
$workerPath = "$ServicesDir\worker"
$workerBinary = "$workerPath\Hartonomous.Worker.exe"
$workerDescription = "Hartonomous background worker service for $Environment environment"

Ensure-Directory -Path $workerPath
Ensure-WindowsService -ServiceName $workerService `
    -DisplayName $workerDisplay `
    -BinaryPath $workerBinary `
    -Description $workerDescription

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Infrastructure Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nAPI Configuration:" -ForegroundColor Cyan
Write-Host "  App Pool: $apiAppPool" -ForegroundColor White
Write-Host "  Site: $apiSite" -ForegroundColor White
Write-Host "  Path: $apiPath" -ForegroundColor White
Write-Host "  Port: $ApiPort" -ForegroundColor White
Write-Host "  Hostname: $ApiHostname" -ForegroundColor White

Write-Host "`nWeb Configuration:" -ForegroundColor Cyan
Write-Host "  App Pool: $webAppPool" -ForegroundColor White
Write-Host "  Site: $webSite" -ForegroundColor White
Write-Host "  Path: $webPath" -ForegroundColor White
Write-Host "  Port: $WebPort" -ForegroundColor White
Write-Host "  Hostname: $WebHostname" -ForegroundColor White

Write-Host "`nWorker Configuration:" -ForegroundColor Cyan
Write-Host "  Service: $workerService" -ForegroundColor White
Write-Host "  Path: $workerPath" -ForegroundColor White

Write-Host "`n✓ Ready for deployment" -ForegroundColor Green
