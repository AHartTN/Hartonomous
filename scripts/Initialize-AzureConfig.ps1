#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Initialize Azure App Configuration and Key Vault for Hartonomous deployment
.DESCRIPTION
    This script creates all necessary configuration entries in Azure App Configuration
    and secrets in Azure Key Vault for the Hartonomous deployment pipeline.
.PARAMETER Environment
    The environment to configure (development, staging, production)
.PARAMETER AppConfigName
    Azure App Configuration name
.PARAMETER KeyVaultName
    Azure Key Vault name
.PARAMETER ResourceGroup
    Azure Resource Group name
.EXAMPLE
    .\Initialize-AzureConfig.ps1 -Environment development -AppConfigName appconfig-hartonomous -KeyVaultName kv-hartonomous -ResourceGroup rg-hartonomous
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment,
    
    [Parameter(Mandatory=$true)]
    [string]$AppConfigName,
    
    [Parameter(Mandatory=$true)]
    [string]$KeyVaultName,
    
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup
)

$ErrorActionPreference = "Stop"

Write-Host "?? Initializing Azure Configuration for: $Environment" -ForegroundColor Cyan
Write-Host "?? App Config: $AppConfigName" -ForegroundColor Yellow
Write-Host "?? Key Vault: $KeyVaultName" -ForegroundColor Yellow
Write-Host ""

# =============================================================================
# HELPER FUNCTIONS
# =============================================================================

function Set-AppConfigValue {
    param(
        [string]$Key,
        [string]$Value,
        [string]$Label = $null
    )
    
    Write-Host "  ? Setting: $Key" -ForegroundColor Green
    
    $params = @(
        "--name", $AppConfigName
        "--auth-mode", "login"
        "--key", $Key
        "--value", $Value
        "--yes"
    )
    
    if ($Label) {
        $params += @("--label", $Label)
    }
    
    az appconfig kv set @params | Out-Null
}

function Set-KeyVaultSecret {
    param(
        [string]$Name,
        [string]$Value
    )
    
    Write-Host "  ?? Setting secret: $Name" -ForegroundColor Green
    
    az keyvault secret set `
        --vault-name $KeyVaultName `
        --name $Name `
        --value $Value `
        --output none
}

function Get-UserInput {
    param(
        [string]$Prompt,
        [string]$DefaultValue = ""
    )
    
    if ($DefaultValue) {
        $input = Read-Host "$Prompt [$DefaultValue]"
        if ([string]::IsNullOrWhiteSpace($input)) {
            return $DefaultValue
        }
        return $input
    } else {
        return Read-Host $Prompt
    }
}

function Get-SecureUserInput {
    param([string]$Prompt)
    
    $secureString = Read-Host $Prompt -AsSecureString
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureString)
    return [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
}

# =============================================================================
# COLLECT CONFIGURATION VALUES
# =============================================================================

Write-Host "?? Please provide configuration values for $Environment environment:" -ForegroundColor Cyan
Write-Host ""

# Installation path
$defaultInstallPath = if ($IsWindows) { "D:\Hartonomous\$Environment" } else { "/opt/hartonomous/$Environment" }
$installPath = Get-UserInput -Prompt "Installation Path" -DefaultValue $defaultInstallPath

# API configuration
$apiHost = Get-UserInput -Prompt "API Host" -DefaultValue "0.0.0.0"
$apiPort = Get-UserInput -Prompt "API Port" -DefaultValue "8000"

# Database configuration
$dbHost = Get-UserInput -Prompt "Database Host" -DefaultValue "localhost"
$dbPort = Get-UserInput -Prompt "Database Port" -DefaultValue "5432"
$dbName = Get-UserInput -Prompt "Database Name" -DefaultValue "hartonomous"
$dbUser = Get-UserInput -Prompt "Database User" -DefaultValue "postgres"
$dbPassword = Get-SecureUserInput -Prompt "Database Password"

Write-Host ""

# =============================================================================
# CREATE APP CONFIGURATION ENTRIES
# =============================================================================

Write-Host "?? Creating App Configuration entries..." -ForegroundColor Cyan

$keyPrefix = "App:${Environment}:"

Set-AppConfigValue -Key "${keyPrefix}InstallPath" -Value $installPath
Set-AppConfigValue -Key "${keyPrefix}ApiHost" -Value $apiHost
Set-AppConfigValue -Key "${keyPrefix}ApiPort" -Value $apiPort
Set-AppConfigValue -Key "${keyPrefix}DatabaseHost" -Value $dbHost
Set-AppConfigValue -Key "${keyPrefix}DatabasePort" -Value $dbPort
Set-AppConfigValue -Key "${keyPrefix}DatabaseName" -Value $dbName
Set-AppConfigValue -Key "${keyPrefix}DatabaseUser" -Value $dbUser

Write-Host ""

# =============================================================================
# CREATE KEY VAULT SECRETS
# =============================================================================

Write-Host "?? Creating Key Vault secrets..." -ForegroundColor Cyan

$secretPrefix = "${Environment}-"

Set-KeyVaultSecret -Name "${secretPrefix}DatabasePassword" -Value $dbPassword

# Create Key Vault reference in App Configuration
$kvReference = @"
{
  "uri": "https://${KeyVaultName}.vault.azure.net/secrets/${secretPrefix}DatabasePassword"
}
"@

Set-AppConfigValue -Key "${keyPrefix}DatabasePassword" -Value $kvReference

Write-Host ""

# =============================================================================
# DISPLAY CONFIGURATION SUMMARY
# =============================================================================

Write-Host "? Configuration initialized successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "?? Configuration Summary:" -ForegroundColor Cyan
Write-Host "  Environment: $Environment" -ForegroundColor White
Write-Host "  Install Path: $installPath" -ForegroundColor White
Write-Host "  API: ${apiHost}:${apiPort}" -ForegroundColor White
Write-Host "  Database: ${dbUser}@${dbHost}:${dbPort}/${dbName}" -ForegroundColor White
Write-Host ""
Write-Host "?? Secrets stored in Key Vault:" -ForegroundColor Cyan
Write-Host "  - ${secretPrefix}DatabasePassword" -ForegroundColor White
Write-Host ""
Write-Host "?? Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Verify configuration in Azure Portal" -ForegroundColor White
Write-Host "  2. Ensure deployment agents have access to App Config and Key Vault" -ForegroundColor White
Write-Host "  3. Run deployment pipeline" -ForegroundColor White
Write-Host ""
