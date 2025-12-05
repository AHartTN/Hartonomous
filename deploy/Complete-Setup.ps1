#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Complete automated setup for Hartonomous Azure DevOps CI/CD infrastructure.

.DESCRIPTION
    One-command setup that provisions everything needed for automated CI/CD:
    1. Azure Key Vault with secrets
    2. Azure App Configuration with settings
    3. Azure Artifacts feed
    4. RBAC for Arc machines and service principals
    5. Pipeline variable group
    6. Deployment group (optional)
    
    NO MANUAL STEPS. NO CREDENTIAL PROVIDER INSTALLATION. NO BULLSHIT.

.PARAMETER ResourceGroupName
    Resource group name (default: rg-hartonomous)

.PARAMETER Location
    Azure region (default: eastus)

.PARAMETER SetupDeploymentGroup
    Set up deployment group targets on Arc machines

.PARAMETER ArcMachineNames
    Comma-separated Arc machine names for deployment group

.EXAMPLE
    # Basic setup (Key Vault, App Config, Artifacts)
    ./Complete-Setup.ps1

.EXAMPLE
    # Full setup including deployment group
    ./Complete-Setup.ps1 -SetupDeploymentGroup -ArcMachineNames "hart-server,hart-desktop"

.EXAMPLE
    # Custom resource group and region
    ./Complete-Setup.ps1 -ResourceGroupName "rg-myproject" -Location "westus2"
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResourceGroupName = "Hartonomous-RG",
    
    [Parameter()]
    [string]$Location = "eastus",
    
    [Parameter()]
    [switch]$SetupDeploymentGroup,
    
    [Parameter()]
    [string]$ArcMachineNames = "hart-server,hart-desktop",
    
    [Parameter()]
    [string]$OrganizationUrl = "https://dev.azure.com/aharttn",
    
    [Parameter()]
    [string]$ProjectName = "Hartonomous"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host @"

╔════════════════════════════════════════════════════════════════╗
║                                                                ║
║           Hartonomous CI/CD Infrastructure Setup               ║
║           Complete Automated Configuration                     ║
║                                                                ║
╚════════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow
Write-Host ""

# Check Azure CLI
try {
    $azVersion = az version --query '\"azure-cli\"' -o tsv
    Write-Host "  ✓ Azure CLI: $azVersion" -ForegroundColor Green
} catch {
    Write-Error "Azure CLI not installed. Install from: https://aka.ms/installazurecli"
    exit 1
}

# Check authentication
try {
    $account = az account show --query "{name:name, id:id}" -o json | ConvertFrom-Json
    Write-Host "  ✓ Authenticated to: $($account.name)" -ForegroundColor Green
} catch {
    Write-Error "Not authenticated. Run: az login"
    exit 1
}

# Check Azure DevOps extension
az extension add --name azure-devops --only-show-errors --output none 2>$null || true
Write-Host "  ✓ Azure DevOps CLI extension" -ForegroundColor Green

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Step 1: Setup Azure infrastructure
Write-Host "STEP 1: Provisioning Azure Infrastructure" -ForegroundColor Cyan
Write-Host "  - Key Vault" -ForegroundColor White
Write-Host "  - App Configuration" -ForegroundColor White
Write-Host "  - Azure Artifacts feed" -ForegroundColor White
Write-Host "  - RBAC assignments" -ForegroundColor White
Write-Host ""

$infraParams = @{
    ResourceGroupName = $ResourceGroupName
    Location = $Location
    OrganizationUrl = $OrganizationUrl
    ProjectName = $ProjectName
}

if ($ArcMachineNames) {
    $infraParams.ArcMachineNames = $ArcMachineNames
}

& "$scriptDir\Setup-AzureInfrastructure.ps1" @infraParams

if ($LASTEXITCODE -ne 0) {
    Write-Error "Infrastructure setup failed"
    exit 1
}

# Get the created Key Vault name
$kvName = az keyvault list --resource-group $ResourceGroupName --query "[0].name" -o tsv

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Step 2: Setup pipeline variable group
Write-Host "STEP 2: Configuring Pipeline Variable Group" -ForegroundColor Cyan
Write-Host ""

& "$scriptDir\Setup-PipelineVariableGroup.ps1" `
    -KeyVaultName $kvName `
    -ResourceGroupName $ResourceGroupName `
    -OrganizationUrl $OrganizationUrl `
    -ProjectName $ProjectName

if ($LASTEXITCODE -ne 0) {
    Write-Warning "Variable group setup failed. You may need to configure it manually."
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Step 3: Setup deployment group (optional)
if ($SetupDeploymentGroup) {
    Write-Host "STEP 3: Registering Deployment Group Targets" -ForegroundColor Cyan
    Write-Host ""
    
    & "$scriptDir\Register-DeploymentGroupTargets.ps1" `
        -KeyVaultName $kvName `
        -MachineNames $ArcMachineNames `
        -ResourceGroupName $ResourceGroupName `
        -OrganizationUrl $OrganizationUrl `
        -ProjectName $ProjectName
    
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Deployment group registration failed. You may need to register manually."
    }
    
    Write-Host ""
    Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
}

# Final summary
Write-Host @"

╔════════════════════════════════════════════════════════════════╗
║                                                                ║
║                    Setup Complete! 🚀                          ║
║                                                                ║
╚════════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Green

Write-Host "Infrastructure Details:" -ForegroundColor Cyan
Write-Host "  Resource Group:    $ResourceGroupName" -ForegroundColor White
Write-Host "  Key Vault:         $kvName" -ForegroundColor White
Write-Host "  Region:            $Location" -ForegroundColor White
Write-Host ""

Write-Host "Azure DevOps Configuration:" -ForegroundColor Cyan
Write-Host "  Organization:      $OrganizationUrl" -ForegroundColor White
Write-Host "  Project:           $ProjectName" -ForegroundColor White
Write-Host "  Variable Group:    Hartonomous-Infrastructure" -ForegroundColor White
Write-Host ""

Write-Host "What Happens Now:" -ForegroundColor Cyan
Write-Host "  1. ✓ Pipeline automatically retrieves secrets from Key Vault" -ForegroundColor White
Write-Host "  2. ✓ NuGet authentication configured via managed identity" -ForegroundColor White
Write-Host "  3. ✓ Packages automatically pushed to Azure Artifacts" -ForegroundColor White
Write-Host "  4. ✓ Arc machines can deploy using their managed identity" -ForegroundColor White
Write-Host ""

Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Update NuGet.config with feed URL from Key Vault:" -ForegroundColor Yellow
$feedUrl = az keyvault secret show --vault-name $kvName --name "NuGetFeedUrl" --query value -o tsv
Write-Host "     $feedUrl" -ForegroundColor White
Write-Host ""
Write-Host "  2. Commit and push to trigger the pipeline:" -ForegroundColor Yellow
Write-Host "     git add ." -ForegroundColor White
Write-Host "     git commit -m 'feat: automated CI/CD infrastructure'" -ForegroundColor White
Write-Host "     git push origin main" -ForegroundColor White
Write-Host ""
Write-Host "  3. Watch the magic happen (no manual steps required!) 🎉" -ForegroundColor Yellow
Write-Host ""

Write-Host "Useful Commands:" -ForegroundColor Cyan
Write-Host "  # View Key Vault secrets" -ForegroundColor White
Write-Host "  az keyvault secret list --vault-name $kvName --query '[].name' -o table" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  # View App Configuration settings" -ForegroundColor White
$appConfigName = az appconfig list --resource-group $ResourceGroupName --query "[0].name" -o tsv
Write-Host "  az appconfig kv list --name $appConfigName -o table" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  # View pipeline runs" -ForegroundColor White
Write-Host "  az pipelines runs list --organization $OrganizationUrl --project $ProjectName -o table" -ForegroundColor DarkGray
Write-Host ""

Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
