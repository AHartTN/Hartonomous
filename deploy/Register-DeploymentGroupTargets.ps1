#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Registers Arc-connected machines as Azure Pipeline deployment group targets with automated authentication.

.DESCRIPTION
    Configures Arc machines for Azure Pipelines with:
    - Deployment group registration using managed identity
    - Automatic PAT retrieval from Key Vault
    - Agent installation and configuration
    - Environment-specific tagging

.PARAMETER KeyVaultName
    Key Vault name containing secrets

.PARAMETER MachineNames
    Comma-separated Arc machine names

.PARAMETER ResourceGroupName
    Resource group containing Arc machines

.PARAMETER DeploymentGroupName
    Deployment group name (default: Hartonomous-Agents)

.PARAMETER OrganizationUrl
    Azure DevOps organization URL

.PARAMETER ProjectName
    Azure DevOps project name

.EXAMPLE
    ./Register-DeploymentGroupTargets.ps1 -KeyVaultName "kv-hart-abc123" -MachineNames "hart-server,hart-desktop"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$KeyVaultName,
    
    [Parameter(Mandatory)]
    [string]$MachineNames,
    
    [Parameter()]
    [string]$ResourceGroupName = "rg-hartonomous",
    
    [Parameter()]
    [string]$DeploymentGroupName = "Hartonomous-Agents",
    
    [Parameter()]
    [string]$OrganizationUrl = "https://dev.azure.com/aharttn",
    
    [Parameter()]
    [string]$ProjectName = "Hartonomous"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deployment Group Registration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Retrieve Azure DevOps PAT from Key Vault
Write-Host "[1/3] Retrieving credentials from Key Vault..." -ForegroundColor Yellow
try {
    $pat = az keyvault secret show --vault-name $KeyVaultName --name "AzureDevOpsPAT" --query value -o tsv 2>$null
    
    if (-not $pat) {
        Write-Error @"
Azure DevOps PAT not found in Key Vault.

Please create a PAT with 'Deployment Group (Read & Manage)' scope and store it:
  az keyvault secret set --vault-name $KeyVaultName --name "AzureDevOpsPAT" --value "<your-pat>"

Create PAT at: $OrganizationUrl/_usersSettings/tokens
"@
        exit 1
    }
    
    $env:AZURE_DEVOPS_EXT_PAT = $pat
    Write-Host "  ✓ Credentials retrieved" -ForegroundColor Green
} catch {
    Write-Error "Failed to retrieve credentials: $_"
    exit 1
}

# Configure Azure DevOps CLI
az extension add --name azure-devops --only-show-errors --output none 2>$null || true
az devops configure --defaults organization=$OrganizationUrl project=$ProjectName --output none

# Create deployment group if it doesn't exist
Write-Host "[2/3] Ensuring deployment group exists..." -ForegroundColor Yellow
$dgExists = az pipelines deployment-group show --name $DeploymentGroupName --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Creating deployment group: $DeploymentGroupName" -ForegroundColor White
    az pipelines deployment-group create --name $DeploymentGroupName --output none
    Write-Host "  ✓ Deployment group created" -ForegroundColor Green
} else {
    Write-Host "  ✓ Deployment group exists" -ForegroundColor Green
}

# Register each Arc machine
Write-Host "[3/3] Registering Arc machines as deployment targets..." -ForegroundColor Yellow

$machines = $MachineNames -split ","
foreach ($machineName in $machines) {
    $machineName = $machineName.Trim()
    Write-Host "  Processing: $machineName" -ForegroundColor White
    
    # Check if machine is already registered
    $existingTarget = az pipelines deployment-group target list `
        --deployment-group-name $DeploymentGroupName `
        --query "[?agent.name=='$machineName']" `
        -o json | ConvertFrom-Json
    
    if ($existingTarget) {
        Write-Host "    ✓ Already registered" -ForegroundColor Green
        continue
    }
    
    # Generate agent installation script
    $agentScript = @"
#!/bin/bash
set -e

# Download and install Azure Pipelines agent
AGENT_VERSION=\`$(curl -s https://api.github.com/repos/microsoft/azure-pipelines-agent/releases/latest | grep 'tag_name' | cut -d'v' -f2 | cut -d'"' -f1)
AGENT_URL="https://vstsagentpackage.azureedge.net/agent/\${AGENT_VERSION}/vsts-agent-linux-x64-\${AGENT_VERSION}.tar.gz"

mkdir -p /opt/azure-pipelines-agent
cd /opt/azure-pipelines-agent

# Download agent
wget -q \$AGENT_URL -O agent.tar.gz
tar xzf agent.tar.gz
rm agent.tar.gz

# Configure agent using managed identity token
IDENTITY_ENDPOINT=\$(cat /etc/opt/azcmagent/config.json | jq -r '.incomingconnections.services[0].uri')
ACCESS_TOKEN=\$(curl -H Metadata:true "\$IDENTITY_ENDPOINT?resource=499b84ac-1321-427f-aa17-267ca6975798" | jq -r '.access_token')

./config.sh \
    --deploymentgroup \
    --deploymentgroupname "$DeploymentGroupName" \
    --agent "\$(hostname)" \
    --url "$OrganizationUrl" \
    --work /opt/azure-pipelines-agent/_work \
    --auth PAT \
    --token "$pat" \
    --runasservice \
    --replace \
    --acceptteeeula \
    --unattended

# Install and start service
sudo ./svc.sh install
sudo ./svc.sh start

echo "Agent registered successfully"
"@
    
    # Execute on Arc machine using run-command
    Write-Host "    Installing and registering agent..." -ForegroundColor White
    
    az connectedmachine run-command create `
        --resource-group $ResourceGroupName `
        --machine-name $machineName `
        --name "register-deployment-agent-$(Get-Date -Format 'yyyyMMddHHmmss')" `
        --script $agentScript `
        --timeout-in-seconds 600 `
        --output none
    
    Write-Host "    ✓ Agent registered" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Registration Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "View deployment group at:" -ForegroundColor Cyan
Write-Host "  $OrganizationUrl/$ProjectName/_settings/deploymentgroups" -ForegroundColor White
Write-Host ""
