<#
.SYNOPSIS
    Hartonomous Application Deployment Script for Windows Server
.DESCRIPTION
    Deploys built application artifacts to Windows Server with IIS
.PARAMETER Environment
    Target environment: Development, Staging, or Production
.PARAMETER Component
    Component to deploy: api, worker, web, or all
.PARAMETER ApiArtifact
    Path to API artifact folder
.PARAMETER WorkerArtifact
    Path to Worker artifact folder
.PARAMETER WebArtifact
    Path to Web artifact folder
.PARAMETER RemoteHost
    Remote Windows server hostname or IP (if not local)
.EXAMPLE
    .\Deploy-App.ps1 -Environment Production -Component all
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('Development', 'Staging', 'Production')]
    [string]$Environment = 'Production',

    [Parameter(Mandatory = $false)]
    [ValidateSet('api', 'worker', 'web', 'all')]
    [string]$Component = 'all',

    [Parameter(Mandatory = $false)]
    [string]$ApiArtifact,

    [Parameter(Mandatory = $false)]
    [string]$WorkerArtifact,

    [Parameter(Mandatory = $false)]
    [string]$WebArtifact,

    [Parameter(Mandatory = $false)]
    [string]$RemoteHost = $env:COMPUTERNAME
)

$ErrorActionPreference = 'Stop'

# Configuration
$DeployRoot = "C:\inetpub\wwwroot\hartonomous"
$ServicesRoot = "C:\Services\Hartonomous"

switch ($Environment) {
    'Production' {
        $DeployDir = "$DeployRoot\production"
        $ServicesDir = "$ServicesRoot\production"
        $BuildConfig = 'Release'
    }
    'Staging' {
        $DeployDir = "$DeployRoot\staging"
        $ServicesDir = "$ServicesRoot\staging"
        $BuildConfig = 'Release'
    }
    'Development' {
        $DeployDir = "$DeployRoot\development"
        $ServicesDir = "$ServicesRoot\development"
        $BuildConfig = 'Debug'
    }
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "Deploying Hartonomous to Windows Server" -ForegroundColor Green
Write-Host "Environment: $Environment" -ForegroundColor Green
Write-Host "Target: $RemoteHost" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

# Function to stop Windows Service or IIS App Pool
function Stop-Component {
    param(
        [string]$ComponentName,
        [string]$Type  # 'Service' or 'IIS'
    )
    
    Write-Host "Stopping $ComponentName..." -ForegroundColor Yellow
    
    try {
        if ($Type -eq 'Service') {
            $serviceName = "Hartonomous.$ComponentName.$Environment"
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if ($service -and $service.Status -eq 'Running') {
                Stop-Service -Name $serviceName -Force
                Write-Host "✓ Service $serviceName stopped" -ForegroundColor Green
            }
        }
        elseif ($Type -eq 'IIS') {
            Import-Module WebAdministration
            $appPoolName = "Hartonomous.$ComponentName.$Environment"
            if (Test-Path "IIS:\AppPools\$appPoolName") {
                Stop-WebAppPool -Name $appPoolName
                Write-Host "✓ App Pool $appPoolName stopped" -ForegroundColor Green
            }
        }
    }
    catch {
        Write-Warning "Failed to stop $ComponentName : $_"
    }
}

# Function to start Windows Service or IIS App Pool
function Start-Component {
    param(
        [string]$ComponentName,
        [string]$Type
    )
    
    Write-Host "Starting $ComponentName..." -ForegroundColor Yellow
    
    try {
        if ($Type -eq 'Service') {
            $serviceName = "Hartonomous.$ComponentName.$Environment"
            Start-Service -Name $serviceName
            Start-Sleep -Seconds 3
            $status = (Get-Service -Name $serviceName).Status
            Write-Host "✓ Service $serviceName started (Status: $status)" -ForegroundColor Green
        }
        elseif ($Type -eq 'IIS') {
            Import-Module WebAdministration
            $appPoolName = "Hartonomous.$ComponentName.$Environment"
            Start-WebAppPool -Name $appPoolName
            Write-Host "✓ App Pool $appPoolName started" -ForegroundColor Green
        }
    }
    catch {
        Write-Error "Failed to start $ComponentName : $_"
    }
}

# Function to deploy component
function Deploy-Component {
    param(
        [string]$ComponentName,
        [string]$SourcePath,
        [string]$Type
    )
    
    Write-Host "Deploying $ComponentName..." -ForegroundColor Yellow
    
    # Determine target directory
    if ($Type -eq 'IIS') {
        $targetDir = "$DeployDir\$ComponentName"
    }
    else {
        $targetDir = "$ServicesDir\$ComponentName"
    }
    
    # Create backup
    if (Test-Path $targetDir) {
        $backupDir = "$targetDir.backup.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Write-Host "Creating backup: $backupDir" -ForegroundColor Yellow
        Copy-Item -Path $targetDir -Destination $backupDir -Recurse -Force
    }
    
    # Create target directory if it doesn't exist
    if (-not (Test-Path $targetDir)) {
        New-Item -Path $targetDir -ItemType Directory -Force | Out-Null
    }
    
    # Stop component
    Stop-Component -ComponentName $ComponentName -Type $Type
    
    # Clear target directory
    Write-Host "Clearing target directory: $targetDir" -ForegroundColor Yellow
    Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Force -Recurse
    
    # Copy new files
    Write-Host "Copying files from $SourcePath to $targetDir" -ForegroundColor Yellow
    if (Test-Path $SourcePath) {
        # Handle zip file if artifact is zipped
        if ((Get-Item $SourcePath).Extension -eq '.zip') {
            Expand-Archive -Path $SourcePath -DestinationPath $targetDir -Force
        }
        else {
            Copy-Item -Path "$SourcePath\*" -Destination $targetDir -Recurse -Force
        }
    }
    else {
        Write-Error "Source path not found: $SourcePath"
        return
    }
    
    # Set permissions (NETWORK SERVICE for services, IIS_IUSRS for IIS)
    if ($Type -eq 'Service') {
        icacls $targetDir /grant "NETWORK SERVICE:(OI)(CI)F" /T | Out-Null
    }
    else {
        icacls $targetDir /grant "IIS_IUSRS:(OI)(CI)RX" /T | Out-Null
    }
    
    # Start component
    Start-Component -ComponentName $ComponentName -Type $Type
    
    Write-Host "✓ $ComponentName deployed successfully" -ForegroundColor Green
}

# Deploy based on component selection
switch ($Component) {
    'api' {
        if (-not $ApiArtifact) {
            $ApiArtifact = ".\artifacts\$Environment\api"
        }
        Deploy-Component -ComponentName 'API' -SourcePath $ApiArtifact -Type 'IIS'
    }
    'worker' {
        if (-not $WorkerArtifact) {
            $WorkerArtifact = ".\artifacts\$Environment\worker"
        }
        Deploy-Component -ComponentName 'Worker' -SourcePath $WorkerArtifact -Type 'Service'
    }
    'web' {
        if (-not $WebArtifact) {
            $WebArtifact = ".\artifacts\$Environment\web"
        }
        Deploy-Component -ComponentName 'Web' -SourcePath $WebArtifact -Type 'IIS'
    }
    'all' {
        if (-not $ApiArtifact) { $ApiArtifact = ".\artifacts\$Environment\api" }
        if (-not $WorkerArtifact) { $WorkerArtifact = ".\artifacts\$Environment\worker" }
        if (-not $WebArtifact) { $WebArtifact = ".\artifacts\$Environment\web" }
        
        Deploy-Component -ComponentName 'API' -SourcePath $ApiArtifact -Type 'IIS'
        Deploy-Component -ComponentName 'Worker' -SourcePath $WorkerArtifact -Type 'Service'
        Deploy-Component -ComponentName 'Web' -SourcePath $WebArtifact -Type 'IIS'
    }
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Deployed to: $DeployDir / $ServicesDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Services/App Pools:" -ForegroundColor Cyan
if ($Component -eq 'all' -or $Component -eq 'api') {
    Write-Host "  - Hartonomous.API.$Environment (IIS)" -ForegroundColor White
}
if ($Component -eq 'all' -or $Component -eq 'worker') {
    Write-Host "  - Hartonomous.Worker.$Environment (Windows Service)" -ForegroundColor White
}
if ($Component -eq 'all' -or $Component -eq 'web') {
    Write-Host "  - Hartonomous.Web.$Environment (IIS)" -ForegroundColor White
}
Write-Host ""
Write-Host "View logs:" -ForegroundColor Cyan
Write-Host "  Get-EventLog -LogName Application -Source Hartonomous* -Newest 50" -ForegroundColor White
