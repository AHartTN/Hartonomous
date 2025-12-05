#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up complete Azure infrastructure for Hartonomous CI/CD with automated authentication.

.DESCRIPTION
    Provisions and configures:
    - Azure Key Vault with secrets for NuGet feeds, connection strings
    - Azure App Configuration for centralized settings
    - Azure Artifacts feed with proper RBAC
    - Service principals and managed identities
    - RBAC assignments for Arc-connected machines
    - Pipeline service connection configurations

.PARAMETER ResourceGroupName
    Resource group name (default: rg-hartonomous)

.PARAMETER Location
    Azure region (default: eastus)

.PARAMETER KeyVaultName
    Key Vault name (default: kv-hartonomous-{unique})

.PARAMETER AppConfigName
    App Configuration name (default: appconfig-hartonomous-{unique})

.PARAMETER ArtifactsFeedName
    Azure Artifacts feed name (default: Hartonomous)

.PARAMETER ArcMachineNames
    Comma-separated list of Arc machine names (default: hart-server,hart-desktop)

.EXAMPLE
    ./Setup-AzureInfrastructure.ps1 -ResourceGroupName "rg-hartonomous" -Location "eastus"
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResourceGroupName = "Hartonomous-RG",
    
    [Parameter()]
    [string]$Location = "eastus",
    
    [Parameter()]
    [string]$KeyVaultName,
    
    [Parameter()]
    [string]$AppConfigName,
    
    [Parameter()]
    [string]$ArtifactsFeedName = "Hartonomous",
    
    [Parameter()]
    [string]$ArcMachineNames = "hart-server,hart-desktop",
    
    [Parameter()]
    [string]$OrganizationUrl = "https://dev.azure.com/aharttn",
    
    [Parameter()]
    [string]$ProjectName = "Hartonomous"
)

$ErrorActionPreference = "Stop"

# Use existing resources if available, otherwise generate unique names
if (-not $KeyVaultName) {
    # Check if hartonomous-kv exists
    $existingKv = az keyvault show --name "hartonomous-kv" --query "name" -o tsv 2>$null
    $KeyVaultName = if ($existingKv) { "hartonomous-kv" } else { "kv-hart-$(-join ((97..122) | Get-Random -Count 6 | ForEach-Object { [char]$_ }))" }
}

if (-not $AppConfigName) {
    # Check if appconfig-hartonomous exists
    $existingAppConfig = az appconfig show --name "appconfig-hartonomous" --query "name" -o tsv 2>$null
    $AppConfigName = if ($existingAppConfig) { "appconfig-hartonomous" } else { "appconfig-hart-$(-join ((97..122) | Get-Random -Count 6 | ForEach-Object { [char]$_ }))" }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Hartonomous Azure Infrastructure Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check Azure CLI authentication
Write-Host "[1/12] Checking Azure CLI authentication..." -ForegroundColor Yellow
try {
    $account = az account show --query "{subscription:id, tenant:tenantId}" -o json | ConvertFrom-Json
    Write-Host "  ✓ Authenticated to subscription: $($account.subscription)" -ForegroundColor Green
} catch {
    Write-Error "Not authenticated to Azure CLI. Run 'az login' first."
    exit 1
}

# Get current user/service principal
$currentUser = az ad signed-in-user show --query id -o tsv 2>$null
if (-not $currentUser) {
    # Might be a service principal
    $currentUser = az account show --query user.name -o tsv
}

# Ensure resource group exists
Write-Host "[2/12] Ensuring resource group exists..." -ForegroundColor Yellow
$rgExists = az group exists --name $ResourceGroupName
if ($rgExists -eq "false") {
    Write-Host "  Creating resource group: $ResourceGroupName" -ForegroundColor White
    az group create --name $ResourceGroupName --location $Location --output none
    Write-Host "  ✓ Resource group created" -ForegroundColor Green
} else {
    Write-Host "  ✓ Resource group exists" -ForegroundColor Green
}

# Create Key Vault
Write-Host "[3/12] Creating Azure Key Vault..." -ForegroundColor Yellow
$kvExists = az keyvault show --name $KeyVaultName --resource-group $ResourceGroupName 2>$null
if (-not $kvExists) {
    az keyvault create `
        --name $KeyVaultName `
        --resource-group $ResourceGroupName `
        --location $Location `
        --enable-rbac-authorization true `
        --enabled-for-deployment true `
        --enabled-for-template-deployment true `
        --public-network-access Enabled `
        --output none
    
    Write-Host "  ✓ Key Vault created: $KeyVaultName" -ForegroundColor Green
} else {
    Write-Host "  ✓ Key Vault exists: $KeyVaultName" -ForegroundColor Green
}

# Assign Key Vault Administrator role to current user
Write-Host "[4/12] Assigning Key Vault RBAC roles..." -ForegroundColor Yellow
$kvScope = "/subscriptions/$($account.subscription)/resourceGroups/$ResourceGroupName/providers/Microsoft.KeyVault/vaults/$KeyVaultName"

az role assignment create `
    --role "Key Vault Administrator" `
    --assignee $currentUser `
    --scope $kvScope `
    --output none 2>$null

Write-Host "  ✓ Key Vault Administrator role assigned" -ForegroundColor Green

# Create App Configuration
Write-Host "[5/12] Creating Azure App Configuration..." -ForegroundColor Yellow
$appConfigExists = az appconfig show --name $AppConfigName --resource-group $ResourceGroupName 2>$null
if (-not $appConfigExists) {
    az appconfig create `
        --name $AppConfigName `
        --resource-group $ResourceGroupName `
        --location $Location `
        --sku Standard `
        --enable-public-network true `
        --output none
    
    Write-Host "  ✓ App Configuration created: $AppConfigName" -ForegroundColor Green
} else {
    Write-Host "  ✓ App Configuration exists: $AppConfigName" -ForegroundColor Green
}

# Assign App Configuration Data Owner role
Write-Host "[6/12] Assigning App Configuration RBAC roles..." -ForegroundColor Yellow
$appConfigScope = "/subscriptions/$($account.subscription)/resourceGroups/$ResourceGroupName/providers/Microsoft.AppConfiguration/configurationStores/$AppConfigName"

az role assignment create `
    --role "App Configuration Data Owner" `
    --assignee $currentUser `
    --scope $appConfigScope `
    --output none 2>$null

Write-Host "  ✓ App Configuration Data Owner role assigned" -ForegroundColor Green

# Configure Azure DevOps extension
Write-Host "[7/12] Configuring Azure DevOps CLI extension..." -ForegroundColor Yellow
az extension add --name azure-devops --only-show-errors --output none 2>$null || true
az devops configure --defaults organization=$OrganizationUrl project=$ProjectName --output none

# Check if we need PAT for Azure DevOps operations
if (-not $env:AZURE_DEVOPS_EXT_PAT) {
    Write-Host "  ⚠ AZURE_DEVOPS_EXT_PAT not set. Azure DevOps operations may require interactive auth." -ForegroundColor Yellow
}

# Create or verify Azure Artifacts feed
Write-Host "[8/12] Setting up Azure Artifacts feed..." -ForegroundColor Yellow
$feedExists = az artifacts universal show --organization $OrganizationUrl --project $ProjectName --scope project --feed $ArtifactsFeedName 2>$null
if (-not $feedExists) {
    Write-Host "  Creating Azure Artifacts feed: $ArtifactsFeedName" -ForegroundColor White
    
    # Create feed using REST API (az artifacts feed create not available in all versions)
    $feedJson = @{
        name = $ArtifactsFeedName
        description = "Hartonomous internal NuGet packages"
        upstreamEnabled = $true
    } | ConvertTo-Json
    
    try {
        az rest --method POST `
            --uri "$OrganizationUrl/$ProjectName/_apis/packaging/feeds?api-version=7.1" `
            --body $feedJson `
            --headers "Content-Type=application/json" `
            --output none
        
        Write-Host "  ✓ Azure Artifacts feed created" -ForegroundColor Green
    } catch {
        Write-Host "  ⚠ Feed may already exist or requires manual creation" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ✓ Azure Artifacts feed exists" -ForegroundColor Green
}

# Get feed URL and store in Key Vault
$feedUrl = "$OrganizationUrl/$ProjectName/_packaging/$ArtifactsFeedName/nuget/v3/index.json"

Write-Host "[9/12] Storing secrets in Key Vault..." -ForegroundColor Yellow

# Store NuGet feed URL
az keyvault secret set `
    --vault-name $KeyVaultName `
    --name "NuGetFeedUrl" `
    --value $feedUrl `
    --output none

# Store Azure DevOps organization URL
az keyvault secret set `
    --vault-name $KeyVaultName `
    --name "AzureDevOpsOrgUrl" `
    --value $OrganizationUrl `
    --output none

# Store project name
az keyvault secret set `
    --vault-name $KeyVaultName `
    --name "AzureDevOpsProject" `
    --value $ProjectName `
    --output none

Write-Host "  ✓ Secrets stored in Key Vault" -ForegroundColor Green

# Configure App Configuration settings
Write-Host "[10/12] Configuring App Configuration settings..." -ForegroundColor Yellow

$appConfigEndpoint = "https://$AppConfigName.azconfig.io"

# Store configuration values
$configs = @{
    "Hartonomous:NuGet:FeedName" = $ArtifactsFeedName
    "Hartonomous:NuGet:FeedUrl" = $feedUrl
    "Hartonomous:Azure:KeyVaultName" = $KeyVaultName
    "Hartonomous:Azure:AppConfigName" = $AppConfigName
    "Hartonomous:Azure:ResourceGroup" = $ResourceGroupName
    "Hartonomous:Package:Version" = "1.0.0"
}

foreach ($key in $configs.Keys) {
    az appconfig kv set `
        --name $AppConfigName `
        --key $key `
        --value $configs[$key] `
        --yes `
        --output none
}

Write-Host "  ✓ App Configuration settings stored" -ForegroundColor Green

# Configure Arc machine access
Write-Host "[11/12] Configuring Arc machine managed identity access..." -ForegroundColor Yellow

$arcMachines = $ArcMachineNames -split ","
foreach ($machineName in $arcMachines) {
    $machineName = $machineName.Trim()
    
    # Check if Arc machine exists
    $arcMachine = az connectedmachine show `
        --name $machineName `
        --resource-group $ResourceGroupName `
        --query "{id:id, principalId:identity.principalId}" `
        -o json 2>$null | ConvertFrom-Json
    
    if ($arcMachine -and $arcMachine.principalId) {
        Write-Host "  Configuring access for: $machineName" -ForegroundColor White
        
        # Grant Key Vault Secrets User role
        az role assignment create `
            --role "Key Vault Secrets User" `
            --assignee $arcMachine.principalId `
            --scope $kvScope `
            --output none 2>$null
        
        # Grant App Configuration Data Reader role
        az role assignment create `
            --role "App Configuration Data Reader" `
            --assignee $arcMachine.principalId `
            --scope $appConfigScope `
            --output none 2>$null
        
        Write-Host "    ✓ Roles assigned to $machineName" -ForegroundColor Green
    } else {
        Write-Host "    ⚠ Arc machine not found or managed identity not enabled: $machineName" -ForegroundColor Yellow
    }
}

# Create service principal for pipeline if needed
Write-Host "[12/12] Creating service principal for pipeline..." -ForegroundColor Yellow

$spName = "sp-hartonomous-pipeline"
$sp = az ad sp list --display-name $spName --query "[0].{appId:appId, objectId:id}" -o json | ConvertFrom-Json

if (-not $sp) {
    Write-Host "  Creating service principal: $spName" -ForegroundColor White
    $sp = az ad sp create-for-rbac `
        --name $spName `
        --role Contributor `
        --scopes "/subscriptions/$($account.subscription)/resourceGroups/$ResourceGroupName" `
        --query "{appId:appId, password:password, tenant:tenant}" `
        -o json | ConvertFrom-Json
    
    # Get object ID
    Start-Sleep -Seconds 5
    $spObjectId = az ad sp show --id $sp.appId --query id -o tsv
    
    # Store service principal credentials in Key Vault
    az keyvault secret set `
        --vault-name $KeyVaultName `
        --name "PipelineServicePrincipalAppId" `
        --value $sp.appId `
        --output none
    
    az keyvault secret set `
        --vault-name $KeyVaultName `
        --name "PipelineServicePrincipalPassword" `
        --value $sp.password `
        --output none
    
    az keyvault secret set `
        --vault-name $KeyVaultName `
        --name "PipelineServicePrincipalTenant" `
        --value $sp.tenant `
        --output none
    
    Write-Host "  ✓ Service principal created and credentials stored" -ForegroundColor Green
} else {
    $spObjectId = $sp.objectId
    Write-Host "  ✓ Service principal exists" -ForegroundColor Green
}

# Grant service principal access to Key Vault and App Config
az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee $spObjectId `
    --scope $kvScope `
    --output none 2>$null

az role assignment create `
    --role "App Configuration Data Reader" `
    --assignee $spObjectId `
    --scope $appConfigScope `
    --output none 2>$null

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Infrastructure Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Configuration Details:" -ForegroundColor Cyan
Write-Host "  Resource Group:      $ResourceGroupName" -ForegroundColor White
Write-Host "  Key Vault:           $KeyVaultName" -ForegroundColor White
Write-Host "  App Configuration:   $AppConfigName" -ForegroundColor White
Write-Host "  Artifacts Feed:      $ArtifactsFeedName" -ForegroundColor White
Write-Host "  Feed URL:            $feedUrl" -ForegroundColor White
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Update NuGet.config with: $feedUrl" -ForegroundColor White
Write-Host "  2. Configure Azure Pipeline variable group 'Hartonomous-Infrastructure':" -ForegroundColor White
Write-Host "     - KeyVaultName: $KeyVaultName" -ForegroundColor White
Write-Host "     - AppConfigName: $AppConfigName" -ForegroundColor White
Write-Host "  3. Ensure Arc machines have system-assigned managed identity enabled" -ForegroundColor White
Write-Host "  4. Run pipeline to test automated authentication" -ForegroundColor White
Write-Host ""
