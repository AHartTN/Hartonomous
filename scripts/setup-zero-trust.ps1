#Requires -Version 7.0
<#
.SYNOPSIS
    Zero-Trust Infrastructure Setup for Hartonomous
    
.DESCRIPTION
    Automates creation of:
    - Managed Identities (system and user-assigned)
    - Azure Key Vault with RBAC
    - Federated credentials for GitHub/Azure DevOps
    - Service connections with workload identity
    - Database connection strings (in Key Vault)
    - API keys and secrets (in Key Vault)
    
    NO MANUAL SECRET MANAGEMENT. EVER.
#>

[CmdletBinding()]
param(
    [string]$SubscriptionId = "",
    [string]$ResourceGroup = "Hartonomous-RG",
    [string]$Location = "eastus",
    [string]$KeyVaultName = "hartonomous-kv",
    [string]$Organization = "https://dev.azure.com/aharttn",
    [string]$Project = "Hartonomous"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Zero-Trust Infrastructure Setup - Hartonomous" -ForegroundColor Cyan
Write-Host "================================================================`n" -ForegroundColor Cyan

#region Authentication
if ($SubscriptionId) {
    az account set --subscription $SubscriptionId
}

$sub = az account show --query "{id:id, name:name}" -o json | ConvertFrom-Json
Write-Host "? Using subscription: $($sub.name)" -ForegroundColor Green
Write-Host "  ID: $($sub.id)`n" -ForegroundColor Gray
#endregion

#region Resource Group
Write-Host "Setting up Resource Group..." -ForegroundColor Cyan
$rgExists = az group exists --name $ResourceGroup

if ($rgExists -eq "true") {
    Write-Host "? Resource group exists: $ResourceGroup" -ForegroundColor Green
} else {
    az group create --name $ResourceGroup --location $Location --output none
    Write-Host "? Resource group created: $ResourceGroup" -ForegroundColor Green
}
#endregion

#region Key Vault
Write-Host "`nSetting up Key Vault with RBAC..." -ForegroundColor Cyan

$kv = az keyvault show --name $KeyVaultName --resource-group $ResourceGroup 2>$null | ConvertFrom-Json

if ($kv) {
    Write-Host "? Key Vault exists: $KeyVaultName" -ForegroundColor Green
} else {
    az keyvault create `
        --name $KeyVaultName `
        --resource-group $ResourceGroup `
        --location $Location `
        --enable-rbac-authorization true `
        --enabled-for-deployment true `
        --enabled-for-template-deployment true `
        --public-network-access Enabled `
        --output none
    
    Write-Host "? Key Vault created: $KeyVaultName" -ForegroundColor Green
}

# Get current user
$currentUser = az ad signed-in-user show --query id -o tsv

# Assign Key Vault Administrator role to current user
Write-Host "  Assigning Key Vault Administrator role..." -ForegroundColor Gray

az role assignment create `
    --role "Key Vault Administrator" `
    --assignee $currentUser `
    --scope "/subscriptions/$($sub.id)/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$KeyVaultName" `
    --output none 2>$null

Write-Host "? RBAC configured for Key Vault" -ForegroundColor Green
#endregion

#region User-Assigned Managed Identity
Write-Host "`nCreating User-Assigned Managed Identity..." -ForegroundColor Cyan

$identityName = "hartonomous-identity"
$identity = az identity show --name $identityName --resource-group $ResourceGroup 2>$null | ConvertFrom-Json

if ($identity) {
    Write-Host "? Managed identity exists: $identityName" -ForegroundColor Green
} else {
    $identity = az identity create `
        --name $identityName `
        --resource-group $ResourceGroup `
        --location $Location `
        --output json | ConvertFrom-Json
    
    Write-Host "? Managed identity created: $identityName" -ForegroundColor Green
}

Write-Host "  Principal ID: $($identity.principalId)" -ForegroundColor Gray
Write-Host "  Client ID: $($identity.clientId)" -ForegroundColor Gray

# Grant identity Key Vault Secrets User role
az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee-object-id $identity.principalId `
    --assignee-principal-type ServicePrincipal `
    --scope "/subscriptions/$($sub.id)/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$KeyVaultName" `
    --output none 2>$null

Write-Host "? Managed identity granted Key Vault access" -ForegroundColor Green
#endregion

#region Federated Credentials for Azure DevOps
Write-Host "`nConfiguring Federated Credentials for Azure DevOps..." -ForegroundColor Cyan

$federatedCredName = "azure-devops-pipeline"
$issuer = "https://vstoken.dev.azure.com/$(($Organization -split '/')[-1])"
$subject = "sc://aharttn/Hartonomous/Azure-Service-Connection"

$existingCred = az identity federated-credential show `
    --name $federatedCredName `
    --identity-name $identityName `
    --resource-group $ResourceGroup 2>$null

if ($existingCred) {
    Write-Host "? Federated credential exists: $federatedCredName" -ForegroundColor Green
} else {
    az identity federated-credential create `
        --name $federatedCredName `
        --identity-name $identityName `
        --resource-group $ResourceGroup `
        --issuer $issuer `
        --subject $subject `
        --audiences "api://AzureADTokenExchange" `
        --output none
    
    Write-Host "? Federated credential created: $federatedCredName" -ForegroundColor Green
}

Write-Host "  Issuer: $issuer" -ForegroundColor Gray
Write-Host "  Subject: $subject" -ForegroundColor Gray
#endregion

#region Database Connection Strings (Secrets in Key Vault)
Write-Host "`nStoring Database Connection Strings in Key Vault..." -ForegroundColor Cyan

$connectionStrings = @{
    "PostgreSQL-Local" = "Host=localhost;Port=5432;Database=hartonomous_local;Username=postgres;Password=GENERATED_PASSWORD"
    "PostgreSQL-Dev" = "Host=HART-SERVER;Port=5432;Database=hartonomous_dev;Username=hart_dev;Password=GENERATED_PASSWORD;SSL Mode=Require"
    "PostgreSQL-Staging" = "Host=staging-db.postgres.database.azure.com;Port=5432;Database=hartonomous_staging;Username=hart_admin;Password=GENERATED_PASSWORD;SSL Mode=Require"
    "PostgreSQL-Production" = "Host=prod-db.postgres.database.azure.com;Port=5432;Database=hartonomous;Username=hart_admin;Password=GENERATED_PASSWORD;SSL Mode=Require"
    "Redis-Local" = "localhost:6379"
    "Redis-Dev" = "HART-SERVER:6379"
    "Redis-Staging" = "hartonomous-staging.redis.cache.windows.net:6380,ssl=True,password=GENERATED_PASSWORD"
    "Redis-Production" = "hartonomous-prod.redis.cache.windows.net:6380,ssl=True,password=GENERATED_PASSWORD"
}

foreach ($key in $connectionStrings.Keys) {
    $existingSecret = az keyvault secret show --vault-name $KeyVaultName --name $key 2>$null
    
    if ($existingSecret) {
        Write-Host "  ? Secret exists: $key" -ForegroundColor Gray
    } else {
        # Generate secure password
        $password = -join ((65..90) + (97..122) + (48..57) + (33,35,36,37,38,42,43,45,61) | Get-Random -Count 32 | ForEach-Object {[char]$_})
        $connString = $connectionStrings[$key] -replace "GENERATED_PASSWORD", $password
        
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $key `
            --value $connString `
            --output none
        
        Write-Host "  ? Secret created: $key" -ForegroundColor Green
    }
}

Write-Host "? Connection strings stored securely" -ForegroundColor Green
#endregion

#region Application Secrets
Write-Host "`nStoring Application Secrets in Key Vault..." -ForegroundColor Cyan

$appSecrets = @{
    "JWT-Secret" = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 64 | ForEach-Object {[char]$_})
    "API-Key-Internal" = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object {[char]$_})
    "Encryption-Key" = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object {[char]$_})
}

foreach ($key in $appSecrets.Keys) {
    $existingSecret = az keyvault secret show --vault-name $KeyVaultName --name $key 2>$null
    
    if ($existingSecret) {
        Write-Host "  ? Secret exists: $key" -ForegroundColor Gray
    } else {
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $key `
            --value $appSecrets[$key] `
            --output none
        
        Write-Host "  ? Secret created: $key" -ForegroundColor Green
    }
}

Write-Host "? Application secrets stored securely" -ForegroundColor Green
#endregion

#region Code Signing Certificate
Write-Host "`nSetting up Code Signing Certificate..." -ForegroundColor Cyan

& "$PSScriptRoot\setup-certificate.ps1" `
    -ResourceGroup $ResourceGroup `
    -KeyVaultName $KeyVaultName `
    -Location $Location `
    -SubscriptionId $sub.id
#endregion

#region Azure DevOps Service Connection Instructions
Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Manual Configuration Required (One-Time)" -ForegroundColor Yellow
Write-Host "================================================================`n" -ForegroundColor Cyan

Write-Host "Azure DevOps Service Connection Setup:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Navigate to:" -ForegroundColor White
Write-Host "   $Organization/$Project/_settings/adminservices" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Click 'New service connection' ? 'Azure Resource Manager'" -ForegroundColor White
Write-Host ""
Write-Host "3. Select 'Workload Identity federation (automatic)'" -ForegroundColor White
Write-Host ""
Write-Host "4. Enter the following:" -ForegroundColor White
Write-Host "   Subscription: $($sub.name)" -ForegroundColor Gray
Write-Host "   Resource Group: $ResourceGroup" -ForegroundColor Gray
Write-Host "   Service connection name: Azure-Service-Connection" -ForegroundColor Gray
Write-Host ""
Write-Host "5. Check 'Grant access permission to all pipelines'" -ForegroundColor White
Write-Host ""
Write-Host "6. Click 'Save'" -ForegroundColor White
Write-Host ""

Write-Host "Managed Identity Details:" -ForegroundColor Yellow
Write-Host "  Name: $identityName" -ForegroundColor White
Write-Host "  Client ID: $($identity.clientId)" -ForegroundColor White
Write-Host "  Principal ID: $($identity.principalId)" -ForegroundColor White
Write-Host "  Resource ID: $($identity.id)" -ForegroundColor White
Write-Host ""
#endregion

#region Summary
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Zero-Trust Setup Complete" -ForegroundColor Green
Write-Host "================================================================`n" -ForegroundColor Cyan

Write-Host "Resources Created:" -ForegroundColor Yellow
Write-Host "  ? Resource Group: $ResourceGroup" -ForegroundColor Green
Write-Host "  ? Key Vault: $KeyVaultName" -ForegroundColor Green
Write-Host "  ? Managed Identity: $identityName" -ForegroundColor Green
Write-Host "  ? Federated Credential: $federatedCredName" -ForegroundColor Green
Write-Host "  ? Code Signing Certificate: HartIndustries-CodeSigning" -ForegroundColor Green
Write-Host "  ? Connection Strings: $(($connectionStrings.Keys).Count) secrets" -ForegroundColor Green
Write-Host "  ? Application Secrets: $(($appSecrets.Keys).Count) secrets" -ForegroundColor Green
Write-Host ""

Write-Host "Security Features:" -ForegroundColor Yellow
Write-Host "  ? RBAC-based Key Vault access" -ForegroundColor Green
Write-Host "  ? Workload Identity Federation (no passwords)" -ForegroundColor Green
Write-Host "  ? Managed Identity for pipeline" -ForegroundColor Green
Write-Host "  ? Auto-generated secure passwords" -ForegroundColor Green
Write-Host "  ? Secrets never leave Key Vault" -ForegroundColor Green
Write-Host ""

Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Complete Azure DevOps service connection setup (instructions above)" -ForegroundColor White
Write-Host "  2. Run pipelines - they'll automatically use managed identity" -ForegroundColor White
Write-Host "  3. Applications retrieve secrets from Key Vault at runtime" -ForegroundColor White
Write-Host "  4. Never touch credentials again!" -ForegroundColor White
Write-Host ""
#endregion
