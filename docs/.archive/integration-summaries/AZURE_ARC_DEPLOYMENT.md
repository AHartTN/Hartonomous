# Azure Arc-Enabled Deployment Guide

## Overview

This deployment infrastructure uses **Azure Arc-enabled servers** for secure, cloud-native deployment without requiring direct SSH/WinRM access. The pipeline deploys applications via Azure Run Command, which executes through the Azure control plane using the Arc agent.

## Architecture

```
Azure DevOps Pipeline → Azure Storage (temporary) → Azure Arc Run Command → Target Server
                                                    ↓
                                        Uses Server's Managed Identity
```

## Prerequisites

### 1. Azure Arc-Enabled Servers

Both `hart-server` and `hart-desktop` must be onboarded to Azure Arc:

```powershell
# Download and run Arc onboarding script
$servicePrincipalClientId = "<your-sp-client-id>"
$servicePrincipalSecret = "<your-sp-secret>"
$tenantId = "<your-tenant-id>"
$subscriptionId = "<your-subscription-id>"
$resourceGroup = "<your-resource-group>"
$location = "eastus"  # or your preferred location

# Download the installation package
Invoke-WebRequest -Uri https://aka.ms/AzureConnectedMachineAgent -OutFile AzureConnectedMachineAgent.msi

# Install the agent
msiexec /i AzureConnectedMachineAgent.msi /l*v installationlog.txt /qn

# Connect to Azure Arc
& "$env:ProgramFiles\AzureConnectedMachineAgent\azcmagent.exe" connect `
  --service-principal-id $servicePrincipalClientId `
  --service-principal-secret $servicePrincipalSecret `
  --tenant-id $tenantId `
  --subscription-id $subscriptionId `
  --resource-group $resourceGroup `
  --location $location

# Verify connection
& "$env:ProgramFiles\AzureConnectedMachineAgent\azcmagent.exe" show
```

**Linux:**
```bash
# Download installation script
wget https://aka.ms/azcmagent -O ~/install_linux_azcmagent.sh

# Install the agent
bash ~/install_linux_azcmagent.sh

# Connect to Azure Arc
sudo azcmagent connect \
  --service-principal-id "<your-sp-client-id>" \
  --service-principal-secret "<your-sp-secret>" \
  --tenant-id "<your-tenant-id>" \
  --subscription-id "<your-subscription-id>" \
  --resource-group "<your-resource-group>" \
  --location "eastus"

# Verify connection
sudo azcmagent show
```

### 2. Server Managed Identity Configuration

Each Arc-enabled server has a **system-assigned managed identity** that must be granted access to Azure Storage:

```bash
# Get the managed identity principal ID
az connectedmachine show \
  --resource-group <resource-group> \
  --name hart-server \
  --query "identity.principalId" -o tsv

# Grant Storage Blob Data Reader role
az role assignment create \
  --assignee <principal-id> \
  --role "Storage Blob Data Reader" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Storage/storageAccounts/<storage-account>"

# Repeat for hart-desktop
az connectedmachine show \
  --resource-group <resource-group> \
  --name hart-desktop \
  --query "identity.principalId" -o tsv

az role assignment create \
  --assignee <principal-id> \
  --role "Storage Blob Data Reader" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Storage/storageAccounts/<storage-account>"
```

### 3. Azure Storage Account

Create a storage account for temporary deployment artifacts:

```bash
# Create storage account
az storage account create \
  --name hartonomousdeployXXXX \  # Replace XXXX with unique suffix
  --resource-group <resource-group> \
  --location eastus \
  --sku Standard_LRS \
  --allow-blob-public-access false \
  --min-tls-version TLS1_2

# Enable Azure AD authentication
az storage account update \
  --name hartonomousdeployXXXX \
  --resource-group <resource-group> \
  --allow-shared-key-access false
```

### 4. Azure DevOps Pipeline Variables

Configure the following variables in your Azure DevOps pipeline:

| Variable Name | Value | Type | Description |
|---------------|-------|------|-------------|
| `DEPLOY_STORAGE_ACCOUNT` | `hartonomousdeployXXXX` | Variable | Storage account for deployment artifacts |
| `DEPLOY_RESOURCE_GROUP` | Your resource group name | Variable | Resource group containing Arc servers |
| `AZURE_LINUX_HOST` | (optional) Arc machine name | Variable | For azure-linux deployment target |
| `AZURE_WINDOWS_HOST` | (optional) Arc machine name | Variable | For azure-windows deployment target |

### 5. Service Connection Permissions

Your existing **Azure-Service-Connection** managed identity needs:

```bash
# Get service connection identity
az identity show \
  --resource-group <resource-group> \
  --name <managed-identity-name> \
  --query principalId -o tsv

# Grant required roles
# 1. Storage Blob Data Contributor (for artifact upload)
az role assignment create \
  --assignee <principal-id> \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Storage/storageAccounts/<storage-account>"

# 2. Azure Connected Machine Resource Administrator (for Run Command execution)
az role assignment create \
  --assignee <principal-id> \
  --role "Azure Connected Machine Resource Administrator" \
  --scope "/subscriptions/<subscription-id>/resourceGroups/<resource-group>"
```

### 6. Server Prerequisites

**Windows Servers:**
- Azure CLI installed: `winget install Microsoft.AzureCLI`
- IIS installed and configured (see `deploy/Production-Setup-Windows.ps1`)
- NSSM installed for Worker service

**Linux Servers:**
- Azure CLI installed: `curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash`
- systemd services configured (see `deploy/production-setup-linux.sh`)
- nginx or apache for web hosting

## Deployment Flow

1. **Build Stage**: Compiles all projects, publishes artifacts
2. **Artifact Upload**: Pipeline uploads artifacts to temporary Azure Storage container
3. **Run Command Execution**: 
   - Pipeline creates a Run Command on the Arc-enabled server
   - Server's managed identity authenticates to Azure
   - Downloads artifacts from Storage using managed identity
   - Executes local deployment script
   - Cleans up temporary files
4. **Storage Cleanup**: Pipeline deletes temporary container

## Security Benefits

### Compared to SSH/WinRM:

✅ **No open firewall ports** - All communication through Azure control plane
✅ **No credential management** - Uses managed identities exclusively  
✅ **Centralized RBAC** - All permissions managed in Azure  
✅ **Audit logging** - All Run Commands logged in Azure Activity Log  
✅ **Conditional access** - Can apply Entra ID policies  
✅ **No network line-of-sight** - Works across firewalls, NAT, proxies  

## Troubleshooting

### Check Arc Agent Status

**Windows:**
```powershell
& "$env:ProgramFiles\AzureConnectedMachineAgent\azcmagent.exe" show
& "$env:ProgramFiles\AzureConnectedMachineAgent\azcmagent.exe" check
```

**Linux:**
```bash
sudo azcmagent show
sudo azcmagent check
sudo systemctl status himdsd
```

### View Run Command History

```bash
# List recent Run Commands
az connectedmachine run-command list \
  --resource-group <resource-group> \
  --machine-name hart-server

# Get Run Command details
az connectedmachine run-command show \
  --resource-group <resource-group> \
  --machine-name hart-server \
  --name deploy-Production-<build-id>
```

### Check Managed Identity Permissions

```bash
# Test from the Arc-enabled server
az login --identity
az storage blob list \
  --account-name <storage-account> \
  --container-name test \
  --auth-mode login
```

### Common Issues

**Issue**: Run Command times out  
**Solution**: Increase timeout in pipeline (default 1800s / 30 minutes)

**Issue**: "Unauthorized" when downloading from storage  
**Solution**: Verify server's managed identity has "Storage Blob Data Reader" role

**Issue**: "The term 'az' is not recognized"  
**Solution**: Install Azure CLI on the target server

**Issue**: Arc agent shows "Disconnected"  
**Solution**: Check network connectivity, proxy settings, firewall rules for Arc endpoints

## Network Requirements

Arc-enabled servers must reach these endpoints:

```
management.azure.com:443
login.microsoftonline.com:443
guestnotificationservice.azure.com:443
*.guestconfiguration.azure.com:443
*.his.arc.azure.com:443
*.guestnotificationservice.azure.com:443
packages.microsoft.com:443  # Linux
dc.services.visualstudio.com:443  # Telemetry
*.blob.core.windows.net:443  # For deployment artifacts
```

For proxy configuration, see: https://learn.microsoft.com/azure/azure-arc/servers/manage-agent#update-or-remove-proxy-settings

## Cost Considerations

- **Arc agent**: Free (no charge for connected machines)
- **Run Command**: Free
- **Azure Storage**: Standard blob storage rates (~$0.018/GB/month)
- **Data transfer**: Outbound data from Azure to on-premises (typically free within limits)

Deployment artifacts are automatically cleaned up after each deployment to minimize storage costs.

## References

- [Azure Arc-enabled servers overview](https://learn.microsoft.com/azure/azure-arc/servers/overview)
- [Run Command for Arc-enabled servers](https://learn.microsoft.com/azure/azure-arc/servers/run-command)
- [Managed identity authentication](https://learn.microsoft.com/azure/azure-arc/servers/managed-identity-authentication)
- [Network requirements](https://learn.microsoft.com/azure/azure-arc/servers/network-requirements)
