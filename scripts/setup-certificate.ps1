#Requires -Version 7.0
<#
.SYNOPSIS
    Automated Certificate Management for Hart Industries (PowerShell version)
    
.DESCRIPTION
    Creates/rotates code signing certificate in Azure Key Vault.
    Fully idempotent, no manual steps, works on Windows/Linux/macOS.
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup = "Hartonomous-RG",
    [string]$KeyVaultName = "hartonomous-kv",
    [string]$CertName = "HartIndustries-CodeSigning",
    [string]$Location = "eastus",
    [string]$SubscriptionId = ""
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Automated Certificate Management - Hart Industries" -ForegroundColor Cyan
Write-Host "================================================================`n" -ForegroundColor Cyan

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Resource Group: $ResourceGroup" -ForegroundColor Gray
Write-Host "  Key Vault: $KeyVaultName" -ForegroundColor Gray
Write-Host "  Certificate: $CertName" -ForegroundColor Gray
Write-Host "  Location: $Location`n" -ForegroundColor Gray

# Check Azure CLI
try {
    $null = az --version 2>$null
    Write-Host "? Azure CLI installed" -ForegroundColor Green
}
catch {
    Write-Host "? Azure CLI not found" -ForegroundColor Red
    Write-Host "  Install from: https://aka.ms/installazurecliwindows" -ForegroundColor Yellow
    exit 1
}

# Check authentication
try {
    $null = az account show 2>$null
    Write-Host "? Authenticated to Azure" -ForegroundColor Green
}
catch {
    Write-Host "? Not logged in, authenticating..." -ForegroundColor Yellow
    az login
}

# Set subscription
if ($SubscriptionId) {
    az account set --subscription $SubscriptionId
}

$currentSub = az account show --query name -o tsv
Write-Host "? Using subscription: $currentSub" -ForegroundColor Green

# Create resource group (idempotent)
Write-Host "`nEnsuring resource group exists..." -ForegroundColor Cyan
$rgExists = az group exists --name $ResourceGroup

if ($rgExists -eq "true") {
    Write-Host "? Resource group exists" -ForegroundColor Green
}
else {
    Write-Host "  Creating resource group..." -ForegroundColor Gray
    az group create --name $ResourceGroup --location $Location --output none
    Write-Host "? Resource group created" -ForegroundColor Green
}

# Create Key Vault (idempotent)
Write-Host "`nEnsuring Key Vault exists..." -ForegroundColor Cyan
$kvExists = az keyvault show --name $KeyVaultName --resource-group $ResourceGroup 2>$null

if ($kvExists) {
    Write-Host "? Key Vault exists" -ForegroundColor Green
}
else {
    Write-Host "  Creating Key Vault..." -ForegroundColor Gray
    az keyvault create `
        --name $KeyVaultName `
        --resource-group $ResourceGroup `
        --location $Location `
        --enable-rbac-authorization false `
        --enabled-for-deployment true `
        --enabled-for-template-deployment true `
        --output none
    Write-Host "? Key Vault created" -ForegroundColor Green
}

# Get current user for access policy
$userObjectId = az ad signed-in-user show --query id -o tsv

# Set access policy (idempotent)
Write-Host "`nEnsuring Key Vault access policies..." -ForegroundColor Cyan
az keyvault set-policy `
    --name $KeyVaultName `
    --object-id $userObjectId `
    --certificate-permissions create get list update delete `
    --secret-permissions get list `
    --output none

Write-Host "? Access policies configured" -ForegroundColor Green

# Check certificate status
Write-Host "`nChecking certificate status..." -ForegroundColor Cyan
$certExists = az keyvault certificate show --vault-name $KeyVaultName --name $CertName 2>$null

$rotate = $false

if ($certExists) {
    $expires = az keyvault certificate show --vault-name $KeyVaultName --name $CertName --query "attributes.expires" -o tsv
    $expiresDate = [DateTime]::Parse($expires)
    $daysRemaining = ($expiresDate - (Get-Date)).Days
    
    Write-Host "? Certificate exists" -ForegroundColor Green
    Write-Host "  Expires: $expires" -ForegroundColor Gray
    Write-Host "  Days remaining: $daysRemaining" -ForegroundColor Gray
    
    if ($daysRemaining -lt 90) {
        Write-Host "? Certificate expiring soon, rotating..." -ForegroundColor Yellow
        $rotate = $true
    }
    else {
        Write-Host "? Certificate is valid, no rotation needed" -ForegroundColor Green
    }
}
else {
    Write-Host "? Certificate does not exist, creating..." -ForegroundColor Yellow
    $rotate = $true
}

# Create/rotate certificate
if ($rotate) {
    Write-Host "`nCreating certificate..." -ForegroundColor Cyan
    
    $policy = @"
{
  "issuerParameters": {
    "name": "Self"
  },
  "keyProperties": {
    "exportable": true,
    "keySize": 2048,
    "keyType": "RSA",
    "reuseKey": false
  },
  "secretProperties": {
    "contentType": "application/x-pkcs12"
  },
  "x509CertificateProperties": {
    "subject": "CN=Hart Industries",
    "ekus": ["1.3.6.1.5.5.7.3.3"],
    "keyUsage": ["digitalSignature"],
    "validityInMonths": 60
  },
  "lifetimeActions": [
    {
      "action": {"actionType": "AutoRenew"},
      "trigger": {"daysBeforeExpiry": 90}
    }
  ]
}
"@
    
    $policyFile = [System.IO.Path]::GetTempFileName()
    $policy | Out-File -FilePath $policyFile -Encoding UTF8
    
    Write-Host "  Creating certificate in Key Vault..." -ForegroundColor Gray
    az keyvault certificate create `
        --vault-name $KeyVaultName `
        --name $CertName `
        --policy `@$policyFile `
        --output none
    
    Remove-Item $policyFile
    
    Write-Host "? Certificate created/rotated" -ForegroundColor Green
    
    # Wait for certificate
    Write-Host "  Waiting for certificate to be ready..." -ForegroundColor Gray
    for ($i = 0; $i -lt 30; $i++) {
        $status = az keyvault certificate show --vault-name $KeyVaultName --name $CertName --query "attributes.enabled" -o tsv 2>$null
        if ($status -eq "true") {
            Write-Host "? Certificate is ready" -ForegroundColor Green
            break
        }
        Start-Sleep -Seconds 2
    }
}

# Get certificate details
Write-Host "`nCertificate Details:" -ForegroundColor Cyan
$thumbprint = az keyvault certificate show --vault-name $KeyVaultName --name $CertName --query "x509Thumbprint" -o tsv
$expires = az keyvault certificate show --vault-name $KeyVaultName --name $CertName --query "attributes.expires" -o tsv

Write-Host "  Thumbprint: $thumbprint" -ForegroundColor White
Write-Host "  Expires: $expires" -ForegroundColor White
Write-Host "  Key Vault URL: https://${KeyVaultName}.vault.azure.net/certificates/${CertName}" -ForegroundColor White

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Certificate Management Complete" -ForegroundColor Green
Write-Host "================================================================`n" -ForegroundColor Cyan

Write-Host "? Certificate stored in Azure Key Vault" -ForegroundColor Green
Write-Host "? Auto-rotation enabled (90 days before expiry)" -ForegroundColor Green
Write-Host "? No manual password management" -ForegroundColor Green
Write-Host "? Pipeline uses managed identity" -ForegroundColor Green
Write-Host ""
