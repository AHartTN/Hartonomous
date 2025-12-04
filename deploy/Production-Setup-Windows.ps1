<#
.SYNOPSIS
    Hartonomous Production Setup Script for Windows Server
.DESCRIPTION
    Configures Windows Server with IIS, Windows Services, and necessary dependencies
.PARAMETER Environment
    Target environment: Development, Staging, or Production
.EXAMPLE
    .\Production-Setup-Windows.ps1 -Environment Production
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('Development', 'Staging', 'Production')]
    [string]$Environment = 'Production'
)

$ErrorActionPreference = 'Stop'

# Require Administrator
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator"
    exit 1
}

Write-Host "========================================" -ForegroundColor Green
Write-Host "Hartonomous $Environment Environment Setup" -ForegroundColor Green
Write-Host "Target: Windows Server" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

# Configuration
$DeployRoot = "C:\inetpub\wwwroot\hartonomous"
$ServicesRoot = "C:\Services\Hartonomous"
$LogRoot = "C:\Logs\Hartonomous"

switch ($Environment) {
    'Production' {
        $DeployDir = "$DeployRoot\production"
        $ServicesDir = "$ServicesRoot\production"
        $LogDir = "$LogRoot\production"
        $ApiPort = 5000
        $ApiDomain = "api.hartonomous.com"
        $WebDomain = "app.hartonomous.com"
    }
    'Staging' {
        $DeployDir = "$DeployRoot\staging"
        $ServicesDir = "$ServicesRoot\staging"
        $LogDir = "$LogRoot\staging"
        $ApiPort = 5001
        $ApiDomain = "api-staging.hartonomous.com"
        $WebDomain = "app-staging.hartonomous.com"
    }
    'Development' {
        $DeployDir = "$DeployRoot\development"
        $ServicesDir = "$ServicesRoot\development"
        $LogDir = "$LogRoot\development"
        $ApiPort = 5002
        $ApiDomain = "api-dev.hartonomous.com"
        $WebDomain = "app-dev.hartonomous.com"
    }
}

Write-Host "Step 1: Installing .NET 10 Runtime..." -ForegroundColor Yellow

# Download and install .NET 10 Runtime
$dotnetVersion = "10.0"
$dotnetUrl = "https://download.visualstudio.microsoft.com/download/pr/dotnet-hosting-$dotnetVersion-win.exe"
$installerPath = "$env:TEMP\dotnet-hosting.exe"

try {
    Write-Host "Downloading .NET $dotnetVersion Hosting Bundle..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri $dotnetUrl -OutFile $installerPath -UseBasicParsing
    
    Write-Host "Installing .NET Runtime..." -ForegroundColor Yellow
    Start-Process -FilePath $installerPath -ArgumentList '/install', '/quiet', '/norestart' -Wait
    
    Remove-Item $installerPath -Force
    Write-Host "✓ .NET $dotnetVersion Runtime installed" -ForegroundColor Green
}
catch {
    Write-Warning ".NET installation may have failed: $_"
    Write-Host "Please install manually from: https://dotnet.microsoft.com/download/dotnet/$dotnetVersion" -ForegroundColor Yellow
}

Write-Host "Step 2: Installing IIS and features..." -ForegroundColor Yellow

# Install IIS with necessary features
$iisFeatures = @(
    'Web-Server',
    'Web-WebServer',
    'Web-Common-Http',
    'Web-Default-Doc',
    'Web-Dir-Browsing',
    'Web-Http-Errors',
    'Web-Static-Content',
    'Web-Health',
    'Web-Http-Logging',
    'Web-Performance',
    'Web-Stat-Compression',
    'Web-Dyn-Compression',
    'Web-Security',
    'Web-Filtering',
    'Web-App-Dev',
    'Web-Net-Ext45',
    'Web-Asp-Net45',
    'Web-ISAPI-Ext',
    'Web-ISAPI-Filter',
    'Web-Mgmt-Tools',
    'Web-Mgmt-Console'
)

foreach ($feature in $iisFeatures) {
    Write-Host "  Installing $feature..." -ForegroundColor Gray
    Install-WindowsFeature -Name $feature -IncludeManagementTools | Out-Null
}

Write-Host "✓ IIS and features installed" -ForegroundColor Green

Write-Host "Step 3: Creating directory structure..." -ForegroundColor Yellow

# Create directories
@("$DeployDir\api", "$DeployDir\web", "$ServicesDir\worker", $LogDir) | ForEach-Object {
    if (-not (Test-Path $_)) {
        New-Item -Path $_ -ItemType Directory -Force | Out-Null
        Write-Host "  Created: $_" -ForegroundColor Gray
    }
}

Write-Host "✓ Directories created" -ForegroundColor Green

Write-Host "Step 4: Configuring IIS Application Pools..." -ForegroundColor Yellow

Import-Module WebAdministration

# Function to create or update App Pool
function New-HartonomousAppPool {
    param(
        [string]$Name,
        [string]$DotNetVersion = 'v10.0'
    )
    
    $appPoolPath = "IIS:\AppPools\$Name"
    
    if (-not (Test-Path $appPoolPath)) {
        New-WebAppPool -Name $Name | Out-Null
    }
    
    Set-ItemProperty -Path $appPoolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty -Path $appPoolPath -Name processModel.identityType -Value 'NetworkService'
    Set-ItemProperty -Path $appPoolPath -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty -Path $appPoolPath -Name processModel.idleTimeout -Value ([TimeSpan]::FromMinutes(0))
    Set-ItemProperty -Path $appPoolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::FromMinutes(0))
    
    Write-Host "  ✓ App Pool: $Name" -ForegroundColor Green
}

# Create App Pools
New-HartonomousAppPool -Name "Hartonomous.API.$Environment"
New-HartonomousAppPool -Name "Hartonomous.Web.$Environment"

Write-Host "✓ App Pools configured" -ForegroundColor Green

Write-Host "Step 5: Creating IIS Websites..." -ForegroundColor Yellow

# Create API Site
$apiSiteName = "Hartonomous.API.$Environment"
$apiSitePath = "IIS:\Sites\$apiSiteName"

if (-not (Test-Path $apiSitePath)) {
    New-Website -Name $apiSiteName `
        -PhysicalPath "$DeployDir\api" `
        -ApplicationPool "Hartonomous.API.$Environment" `
        -Port $ApiPort `
        -HostHeader $ApiDomain | Out-Null
}

Write-Host "  ✓ API Site: $apiSiteName (Port $ApiPort)" -ForegroundColor Green

# Create Web Site
$webSiteName = "Hartonomous.Web.$Environment"
$webSitePath = "IIS:\Sites\$webSiteName"

if (-not (Test-Path $webSitePath)) {
    New-Website -Name $webSiteName `
        -PhysicalPath "$DeployDir\web\wwwroot" `
        -ApplicationPool "Hartonomous.Web.$Environment" `
        -Port 443 `
        -HostHeader $WebDomain `
        -Ssl | Out-Null
}

Write-Host "  ✓ Web Site: $webSiteName (HTTPS)" -ForegroundColor Green

Write-Host "✓ IIS Websites created" -ForegroundColor Green

Write-Host "Step 6: Configuring Windows Service for Worker..." -ForegroundColor Yellow

# Create NSSM service wrapper script
$nssmScript = @"
# Install NSSM if not present
if (-not (Get-Command nssm -ErrorAction SilentlyContinue)) {
    Write-Host "Installing NSSM..." -ForegroundColor Yellow
    choco install nssm -y
}

# Create Worker Service
`$serviceName = "Hartonomous.Worker.$Environment"
`$serviceExe = "$ServicesDir\worker\Hartonomous.Worker.exe"

if (Get-Service -Name `$serviceName -ErrorAction SilentlyContinue) {
    nssm stop `$serviceName
    nssm remove `$serviceName confirm
}

nssm install `$serviceName `$serviceExe
nssm set `$serviceName AppDirectory "$ServicesDir\worker"
nssm set `$serviceName AppEnvironmentExtra "DOTNET_ENVIRONMENT=$Environment"
nssm set `$serviceName DisplayName "Hartonomous Worker ($Environment)"
nssm set `$serviceName Description "Hartonomous Background Worker Service"
nssm set `$serviceName Start SERVICE_AUTO_START
nssm set `$serviceName AppStdout "$LogDir\worker-stdout.log"
nssm set `$serviceName AppStderr "$LogDir\worker-stderr.log"
nssm set `$serviceName AppRotateFiles 1
nssm set `$serviceName AppRotateBytes 10485760

Write-Host "Service `$serviceName configured" -ForegroundColor Green
"@

Set-Content -Path "$env:TEMP\setup-worker-service.ps1" -Value $nssmScript
Write-Host "  Worker service configuration script created" -ForegroundColor Gray
Write-Host "  Run after deploying worker: $env:TEMP\setup-worker-service.ps1" -ForegroundColor Yellow

Write-Host "✓ Service configuration prepared" -ForegroundColor Green

Write-Host "Step 7: Configuring Windows Firewall..." -ForegroundColor Yellow

# Open HTTP/HTTPS ports
New-NetFirewallRule -DisplayName "Hartonomous HTTP" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "Hartonomous HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "Hartonomous API" -Direction Inbound -Protocol TCP -LocalPort $ApiPort -Action Allow -ErrorAction SilentlyContinue | Out-Null

Write-Host "✓ Firewall rules configured" -ForegroundColor Green

Write-Host "========================================" -ForegroundColor Green
Write-Host "$Environment Environment Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Deployment directories:" -ForegroundColor Cyan
Write-Host "  - API: $DeployDir\api" -ForegroundColor White
Write-Host "  - Worker: $ServicesDir\worker" -ForegroundColor White
Write-Host "  - Web: $DeployDir\web" -ForegroundColor White
Write-Host ""
Write-Host "Log directory: $LogDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Deploy application files using Deploy-App.ps1" -ForegroundColor White
Write-Host "2. Configure SSL certificates in IIS for:" -ForegroundColor White
Write-Host "   - $ApiDomain" -ForegroundColor White
Write-Host "   - $WebDomain" -ForegroundColor White
Write-Host "3. Set up Windows Service: $env:TEMP\setup-worker-service.ps1" -ForegroundColor White
Write-Host "4. Configure connection strings in:" -ForegroundColor White
Write-Host "   - $DeployDir\api\appsettings.$Environment.json" -ForegroundColor White
Write-Host "   - $ServicesDir\worker\appsettings.$Environment.json" -ForegroundColor White
Write-Host ""
Write-Host "IIS Sites:" -ForegroundColor Cyan
Write-Host "  - $apiSiteName (http://localhost:$ApiPort)" -ForegroundColor White
Write-Host "  - $webSiteName (https://localhost)" -ForegroundColor White
Write-Host ""
Write-Host "Server is ready for $Environment deployment!" -ForegroundColor Green
