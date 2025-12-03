#!/usr/bin/env pwsh
# Deploy Zero Trust RBAC configuration using Bicep

$ErrorActionPreference = "Stop"

$resourceGroup = "rg-hartonomous"
$location = "eastus"
$apiSpName = "Hartonomous API (Production)"

Write-Host "Deploying Zero Trust RBAC configuration..." -ForegroundColor Cyan

# Get API service principal object ID
Write-Host "Looking up service principal: $apiSpName" -ForegroundColor Yellow
$apiSpId = az ad sp list --display-name $apiSpName --query "[0].id" -o tsv

if ([string]::IsNullOrEmpty($apiSpId)) {
    Write-Host "ERROR: Could not find service principal '$apiSpName'" -ForegroundColor Red
    Write-Host "Available service principals:" -ForegroundColor Yellow
    az ad sp list --filter "startswith(displayName,'Hartonomous')" --query "[].{Name:displayName, ObjectId:id}" -o table
    exit 1
}

Write-Host "Found API Service Principal: $apiSpId" -ForegroundColor Green

# Deploy Bicep template
Write-Host "`nDeploying RBAC assignments..." -ForegroundColor Yellow
az deployment group create `
    --resource-group $resourceGroup `
    --template-file infrastructure/rbac.bicep `
    --parameters apiServicePrincipalId=$apiSpId `
    --verbose

Write-Host ""
Write-Host "Zero Trust RBAC deployment complete!" -ForegroundColor Green
Write-Host "✓ Key Vault access granted to API and Arc machines" -ForegroundColor Green
Write-Host "✓ App Configuration access granted to API and Arc machines" -ForegroundColor Green
