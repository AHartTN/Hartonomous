#!/usr/bin/env pwsh
# Populate Key Vault with all Hartonomous secrets for Zero Trust setup

param(
    [Parameter(Mandatory=$false)]
    [string]$KeyVaultName = "kv-hartonomous",

    [Parameter(Mandatory=$false)]
    [switch]$UseCurrentValues
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Hartonomous Key Vault Secret Setup" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Get Entra ID Tenant ID
Write-Host "Retrieving Entra ID Tenant ID..." -ForegroundColor Yellow
$tenantId = az account show --query tenantId -o tsv
Write-Host "✓ Tenant ID: $tenantId" -ForegroundColor Green

# Get API Client ID
Write-Host "`nRetrieving API Service Principal..." -ForegroundColor Yellow
$apiClientId = az ad sp list --display-name "Hartonomous API (Production)" --query "[0].appId" -o tsv

if ([string]::IsNullOrEmpty($apiClientId)) {
    Write-Host "ERROR: Could not find 'Hartonomous API (Production)' service principal" -ForegroundColor Red
    Write-Host "Available Hartonomous service principals:" -ForegroundColor Yellow
    az ad sp list --filter "startswith(displayName,'Hartonomous')" --query "[].{Name:displayName, AppId:appId}" -o table
    exit 1
}

Write-Host "✓ API Client ID: $apiClientId" -ForegroundColor Green

# PostgreSQL connection strings (without passwords for managed identity)
$secrets = @{
    "AzureAd--TenantId" = $tenantId
    "AzureAd--ClientId" = $apiClientId
    "postgres-connection-localhost" = "Host=localhost;Port=5432;Database=hartonomous_localhost;Username=postgres"
    "postgres-connection-dev" = "Host=localhost;Port=5433;Database=hartonomous_dev;Username=postgres"
    "postgres-connection-staging" = "Host=localhost;Port=5434;Database=hartonomous_staging;Username=postgres"
    "postgres-connection-production" = "Host=localhost;Port=5435;Database=hartonomous_production;Username=postgres"
}

Write-Host "`nSetting Key Vault secrets in: $KeyVaultName" -ForegroundColor Yellow
Write-Host ""

foreach ($secretName in $secrets.Keys) {
    $secretValue = $secrets[$secretName]

    Write-Host "Setting: $secretName" -ForegroundColor Gray

    try {
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $secretName `
            --value $secretValue `
            --output none

        Write-Host "  ✓ $secretName" -ForegroundColor Green
    }
    catch {
        Write-Host "  ✗ Failed to set $secretName" -ForegroundColor Red
        Write-Host "    Error: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Key Vault Configuration Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Secrets stored in Key Vault:" -ForegroundColor Yellow
Write-Host "  - AzureAd--TenantId" -ForegroundColor Gray
Write-Host "  - AzureAd--ClientId" -ForegroundColor Gray
Write-Host "  - postgres-connection-localhost" -ForegroundColor Gray
Write-Host "  - postgres-connection-dev" -ForegroundColor Gray
Write-Host "  - postgres-connection-staging" -ForegroundColor Gray
Write-Host "  - postgres-connection-production" -ForegroundColor Gray
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run: .\infrastructure\deploy-rbac.ps1" -ForegroundColor Gray
Write-Host "  2. Update API appsettings with values above" -ForegroundColor Gray
Write-Host "  3. Deploy to Arc machines" -ForegroundColor Gray
