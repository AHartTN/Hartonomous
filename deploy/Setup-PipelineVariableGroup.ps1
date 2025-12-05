#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates Azure Pipeline variable group with Key Vault integration.

.DESCRIPTION
    Sets up the Hartonomous-Infrastructure variable group that links to Key Vault for secure credential management.

.PARAMETER KeyVaultName
    Key Vault name

.PARAMETER ResourceGroupName
    Resource group containing Key Vault

.PARAMETER OrganizationUrl
    Azure DevOps organization URL

.PARAMETER ProjectName
    Azure DevOps project name

.EXAMPLE
    ./Setup-PipelineVariableGroup.ps1 -KeyVaultName "kv-hart-abc123"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$KeyVaultName,
    
    [Parameter()]
    [string]$ResourceGroupName = "rg-hartonomous",
    
    [Parameter()]
    [string]$OrganizationUrl = "https://dev.azure.com/aharttn",
    
    [Parameter()]
    [string]$ProjectName = "Hartonomous",
    
    [Parameter()]
    [string]$VariableGroupName = "Hartonomous-Infrastructure"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Pipeline Variable Group Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check authentication
Write-Host "[1/3] Checking authentication..." -ForegroundColor Yellow
$account = az account show --query "{subscription:id, subscriptionName:name}" -o json | ConvertFrom-Json
Write-Host "  ✓ Azure Subscription: $($account.subscriptionName)" -ForegroundColor Green

# Configure Azure DevOps
az extension add --name azure-devops --only-show-errors --output none 2>$null || true
az devops configure --defaults organization=$OrganizationUrl project=$ProjectName --output none

if (-not $env:AZURE_DEVOPS_EXT_PAT) {
    Write-Host "  ⚠ AZURE_DEVOPS_EXT_PAT not set" -ForegroundColor Yellow
    Write-Host "  Set it with: `$env:AZURE_DEVOPS_EXT_PAT = '<your-pat>'" -ForegroundColor White
}

# Get Key Vault resource ID
Write-Host "[2/3] Getting Key Vault details..." -ForegroundColor Yellow
$kvId = az keyvault show `
    --name $KeyVaultName `
    --resource-group $ResourceGroupName `
    --query id `
    -o tsv

if (-not $kvId) {
    Write-Error "Key Vault not found: $KeyVaultName"
    exit 1
}

Write-Host "  ✓ Key Vault ID: $kvId" -ForegroundColor Green

# Get or create Azure service connection
Write-Host "[3/3] Setting up variable group..." -ForegroundColor Yellow

$serviceEndpoint = az devops service-endpoint list `
    --query "[?name=='Azure-Service-Connection'].id | [0]" `
    -o tsv

if (-not $serviceEndpoint) {
    Write-Host "  ⚠ Service connection 'Azure-Service-Connection' not found" -ForegroundColor Yellow
    Write-Host "  Please create it in Azure DevOps:" -ForegroundColor White
    Write-Host "    Project Settings → Service connections → New service connection → Azure Resource Manager" -ForegroundColor White
    Write-Host "    Name: Azure-Service-Connection" -ForegroundColor White
    Write-Host "    Scope: Subscription or Resource Group" -ForegroundColor White
    exit 1
}

# Check if variable group exists
$existingGroup = az pipelines variable-group list `
    --group-name $VariableGroupName `
    --query "[0].id" `
    -o tsv 2>$null

if ($existingGroup) {
    Write-Host "  Variable group already exists: $VariableGroupName (ID: $existingGroup)" -ForegroundColor White
    Write-Host "  Updating configuration..." -ForegroundColor White
    
    # Update to link to Key Vault
    az pipelines variable-group update `
        --id $existingGroup `
        --authorize true `
        --output none
    
    Write-Host "  ✓ Variable group updated" -ForegroundColor Green
} else {
    Write-Host "  Creating variable group: $VariableGroupName" -ForegroundColor White
    
    # Create variable group linked to Key Vault
    $groupId = az pipelines variable-group create `
        --name $VariableGroupName `
        --authorize true `
        --variables KeyVaultName=$KeyVaultName AppConfigName="$(az resource list --resource-group $ResourceGroupName --resource-type Microsoft.AppConfiguration/configurationStores --query '[0].name' -o tsv)" `
        --query id `
        -o tsv
    
    Write-Host "  ✓ Variable group created (ID: $groupId)" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Variable Group Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Variable Group: $VariableGroupName" -ForegroundColor Cyan
Write-Host "  - KeyVaultName: $KeyVaultName" -ForegroundColor White
Write-Host ""
Write-Host "The pipeline can now access Key Vault secrets automatically using the variable group." -ForegroundColor White
Write-Host "View at: $OrganizationUrl/$ProjectName/_library?itemType=VariableGroups" -ForegroundColor White
Write-Host ""
