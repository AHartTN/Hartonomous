# 🚀 CI/CD Quick Start

**Get your production-grade CI/CD running in 5 minutes.**

---

## Prerequisites Check

Run these to verify you're ready:

```powershell
# 1. Azure CLI installed and authenticated
az --version
az login
az account show

# 2. Set correct subscription
az account set --subscription "<your-subscription-id>"

# 3. Install Azure DevOps extension (if needed)
az extension add --name azure-devops

# 4. Set Azure DevOps PAT (only needed for deployment group setup)
$env:AZURE_DEVOPS_EXT_PAT = "<your-pat-token>"
```

---

## One-Command Setup

```powershell
cd deploy
./Complete-Setup.ps1
```

**That's it.** The script handles:
- ✓ Key Vault creation with RBAC
- ✓ App Configuration setup
- ✓ Azure Artifacts feed
- ✓ Service principal for pipeline
- ✓ Variable group with Key Vault integration
- ✓ Arc machine RBAC assignments

### With Deployment Group

```powershell
./Complete-Setup.ps1 -SetupDeploymentGroup -ArcMachineNames "hart-server,hart-desktop"
```

---

## What You Get

### Azure Resources
- **Key Vault**: `kv-hart-XXXXXX` with secrets
- **App Configuration**: `appconfig-hart-XXXXXX` with settings
- **Azure Artifacts Feed**: `Hartonomous`
- **Service Principal**: For pipeline authentication
- **Variable Group**: `Hartonomous-Infrastructure`

### RBAC Configured For
- Arc machine managed identities → Key Vault + App Config
- Service principal → Key Vault + App Config
- Your user → Full administrator access

---

## Verify Setup

```powershell
# Check Key Vault
$kvName = az keyvault list --resource-group Hartonomous-RG --query "[0].name" -o tsv
az keyvault secret list --vault-name $kvName -o table

# Check App Configuration
$appConfigName = az appconfig list --query "[0].name" -o tsv
az appconfig kv list --name $appConfigName -o table

# Check Variable Group
az pipelines variable-group list `
    --organization https://dev.azure.com/aharttn `
    --project Hartonomous -o table
```

---

## Update NuGet.config (If Needed)

If your feed URL differs from the default:

```powershell
# Get actual feed URL from Key Vault
$feedUrl = az keyvault secret show `
    --vault-name $kvName `
    --name "NuGetFeedUrl" `
    --query value -o tsv

# Update NuGet.config manually or script will tell you
Write-Host "Feed URL: $feedUrl"
```

---

## Commit and Run Pipeline

```powershell
git add .
git commit -m "feat: automated CI/CD infrastructure with Key Vault and managed identities"
git push origin main
```

Pipeline will:
1. **Build** native C++ libraries (Windows/Linux)
2. **Package** NuGet packages with multi-platform binaries
3. **Publish** to Azure Artifacts automatically
4. **Deploy** to Arc machines (if configured)

**Zero manual steps. Zero credential fuckery.**

---

## Pipeline Authentication Flow

```
Arc Agent → Managed Identity → Key Vault → Secrets
                             ↓
                    Azure DevOps Token
                             ↓
                    NuGet Auth Config
                             ↓
                    Azure Artifacts Upload
```

**No credential provider installation. No manual tokens. No bullshit.**

---

## Troubleshooting

### "Key Vault not found"
```powershell
az keyvault list --resource-group rg-hartonomous -o table
```

### "Can't authenticate to Azure Artifacts"
```powershell
# Test managed identity token
az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798
```

### "Variable group not linked"
```powershell
./Setup-PipelineVariableGroup.ps1 -KeyVaultName $kvName
```

### "Arc machine can't access Key Vault"
```powershell
# Check managed identity
az connectedmachine show --name hart-server --resource-group rg-hartonomous --query identity

# Enable if needed
az connectedmachine update --name hart-server --resource-group rg-hartonomous --enable-identity
```

---

## Full Documentation

See [`deploy/README.md`](deploy/README.md) for comprehensive documentation.

---

## Architecture Summary

```
Pipeline (Arc Agent) → Managed Identity → Key Vault → Secrets
                                       ↓
                              Azure Artifacts Feed
                                       ↓
                         Multi-Platform NuGet Packages
                         (Windows .dll + Linux .so)
                                       ↓
                            C# Projects (Core/Data)
                                       ↓
                      Arc Machines (Deploy via Run Command)
```

**Enterprise-grade. Production-ready. Zero manual credential management.**
