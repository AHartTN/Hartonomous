# Azure Production Deployment

**Enterprise deployment with Zero Trust architecture**

---

## ??? Architecture

```
???????????????????? AZURE CLOUD ?????????????????????????
?                                                         ?
?  ????????????????      ??????????????????????????     ?
?  ? Entra ID     ???????? API Management         ?     ?
?  ? - Internal   ?      ? - OAuth2 Gateway       ?     ?
?  ? - B2C (CIAM) ?      ? - Rate Limiting        ?     ?
?  ????????????????      ? - Zero Trust Policies  ?     ?
?                        ??????????????????????????     ?
?                                 ?                      ?
?  ????????????????              ?                      ?
?  ? Key Vault    ?      ??????????????????????????     ?
?  ? - Secrets    ???????? Container Apps / AKS   ?     ?
?  ? - Certs      ?      ? - Hartonomous API      ?     ?
?  ????????????????      ? - Managed Identity     ?     ?
?                        ? - Private Endpoints    ?     ?
?  ????????????????      ??????????????????????????     ?
?  ? App Config   ?              ?                      ?
?  ? - Settings   ????????????????                      ?
?  ? - Features   ?                                     ?
?  ????????????????              ?                      ?
?                        ??????????????????????????     ?
?                        ? Private Link           ?     ?
?                        ??????????????????????????     ?
???????????????????????????????????????????????????????????
                                  ?
????????????????? ON-PREMISE ??????????????????????????????
?                                                          ?
?  ??????????????????         ??????????????????          ?
?  ? Azure Arc #1   ??????????? Azure Arc #2   ?          ?
?  ? - PostgreSQL   ?   HA    ? - PostgreSQL   ?          ?
?  ? - Private Link ?         ? - Replica      ?          ?
?  ??????????????????         ??????????????????          ?
?                                                          ?
????????????????????????????????????????????????????????????
```

---

## ?? Prerequisites

### Azure Resources

1. ? **Entra ID Tenant** (for internal users)
2. ? **Azure AD B2C** (for external users/CIAM)
3. ? **Key Vault** (for secrets)
4. ? **App Configuration** (for settings)
5. ? **Container Apps or AKS** (for API hosting)
6. ? **Azure Arc** (for on-prem servers)
7. ? **Private Link** (for secure connectivity)
8. ? **API Management** (optional, for gateway)

---

## ?? Deployment Steps

### Step 1: Create Azure Resources

#### 1.1 Resource Group

```bash
az group create \
  --name hartonomous-prod \
  --location eastus
```

#### 1.2 Key Vault

```bash
az keyvault create \
  --name hartonomous-kv \
  --resource-group hartonomous-prod \
  --location eastus \
  --enable-rbac-authorization
```

#### 1.3 App Configuration

```bash
az appconfig create \
  --name hartonomous-config \
  --resource-group hartonomous-prod \
  --location eastus \
  --sku Standard
```

#### 1.4 Managed Identity

```bash
az identity create \
  --name hartonomous-identity \
  --resource-group hartonomous-prod
```

---

### Step 2: Configure Key Vault

#### 2.1 Store Secrets

```bash
# PostgreSQL password
az keyvault secret set \
  --vault-name hartonomous-kv \
  --name postgres-password \
  --value "YOUR_SECURE_PASSWORD"

# Entra ID client secret
az keyvault secret set \
  --vault-name hartonomous-kv \
  --name entra-client-secret \
  --value "YOUR_CLIENT_SECRET"
```

#### 2.2 Grant Access to Managed Identity

```bash
# Get identity principal ID
IDENTITY_ID=$(az identity show \
  --name hartonomous-identity \
  --resource-group hartonomous-prod \
  --query principalId -o tsv)

# Grant Key Vault Secrets User role
az role assignment create \
  --assignee $IDENTITY_ID \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/{subscription-id}/resourceGroups/hartonomous-prod/providers/Microsoft.KeyVault/vaults/hartonomous-kv
```

---

### Step 3: Configure App Configuration

#### 3.1 Add Settings

```bash
# API configuration
az appconfig kv set \
  --name hartonomous-config \
  --key api:host \
  --value "0.0.0.0"

az appconfig kv set \
  --name hartonomous-config \
  --key api:port \
  --value "8000"

# Database configuration
az appconfig kv set \
  --name hartonomous-config \
  --key database:host \
  --value "postgres.internal.contoso.com"
```

#### 3.2 Reference Key Vault Secrets

```bash
# PostgreSQL password (reference from Key Vault)
az appconfig kv set-keyvault \
  --name hartonomous-config \
  --key database:password \
  --secret-identifier https://hartonomous-kv.vault.azure.net/secrets/postgres-password
```

---

### Step 4: Deploy to Container Apps

#### 4.1 Create Container Apps Environment

```bash
az containerapp env create \
  --name hartonomous-env \
  --resource-group hartonomous-prod \
  --location eastus
```

#### 4.2 Deploy API

```bash
az containerapp create \
  --name hartonomous-api \
  --resource-group hartonomous-prod \
  --environment hartonomous-env \
  --image ghcr.io/aharttn/hartonomous:latest \
  --target-port 8000 \
  --ingress external \
  --min-replicas 2 \
  --max-replicas 10 \
  --cpu 1.0 \
  --memory 2.0Gi \
  --user-assigned "hartonomous-identity" \
  --env-vars \
    USE_AZURE_CONFIG=true \
    KEY_VAULT_URL=https://hartonomous-kv.vault.azure.net/ \
    APP_CONFIG_ENDPOINT=https://hartonomous-config.azconfig.io \
    AUTH_ENABLED=true \
    ENTRA_TENANT_ID=YOUR_TENANT_ID \
    ENTRA_CLIENT_ID=YOUR_CLIENT_ID
```

---

### Step 5: Configure Entra ID Authentication

#### 5.1 Register Application

```bash
# Create app registration
az ad app create \
  --display-name "Hartonomous API" \
  --sign-in-audience AzureADMyOrg
```

#### 5.2 Configure Redirect URIs

```bash
# Add redirect URI
az ad app update \
  --id {app-id} \
  --web-redirect-uris https://hartonomous-api.azurecontainerapps.io/auth/callback
```

#### 5.3 Assign Roles

```bash
# Create custom role
az ad app role create \
  --id {app-id} \
  --display-name "Admin" \
  --description "Administrator role" \
  --value "admin"
```

---

### Step 6: Configure B2C (External Users)

#### 6.1 Create B2C Tenant

1. Azure Portal ? Create Azure AD B2C
2. Link to subscription
3. Create user flows (sign-up, sign-in)

#### 6.2 Register API Application

1. Azure Portal ? B2C ? App registrations
2. New registration ? "Hartonomous API"
3. Configure scopes: `api://hartonomous/access_api`

#### 6.3 Update Settings

```bash
# Update API with B2C settings
az containerapp update \
  --name hartonomous-api \
  --resource-group hartonomous-prod \
  --set-env-vars \
    B2C_ENABLED=true \
    B2C_TENANT_NAME=hartonomousb2c \
    B2C_CLIENT_ID=YOUR_B2C_CLIENT_ID \
    B2C_POLICY_NAME=B2C_1_signupsignin
```

---

### Step 7: Configure Azure Arc (On-Premise)

#### 7.1 Install Arc Agent

```bash
# On-premise server
wget https://aka.ms/azcmagent -O arc-install.sh
bash arc-install.sh

# Connect to Azure
azcmagent connect \
  --resource-group hartonomous-prod \
  --tenant-id YOUR_TENANT_ID \
  --subscription-id YOUR_SUBSCRIPTION_ID \
  --location eastus
```

#### 7.2 Enable Azure Arc for PostgreSQL

```bash
# Install PostgreSQL extension
az arcdata dc create \
  --name hart-datacontroller \
  --resource-group hartonomous-prod \
  --location eastus \
  --connectivity-mode indirect
```

#### 7.3 Configure Private Link

```bash
# Create private endpoint
az network private-endpoint create \
  --name hart-postgres-pe \
  --resource-group hartonomous-prod \
  --vnet-name hart-vnet \
  --subnet hart-subnet \
  --private-connection-resource-id {arc-resource-id} \
  --group-id postgresqlServer \
  --connection-name hart-postgres-connection
```

---

## ?? Security Configuration

### Zero Trust Policies

#### 1. Conditional Access

```bash
# Require MFA for admin access
az ad policy conditional-access create \
  --display-name "Require MFA for Hartonomous Admins" \
  --conditions '{
    "users": {"includeRoles": ["admin"]},
    "applications": {"includeApplications": ["{app-id}"]}
  }' \
  --grant-controls '{
    "operator": "AND",
    "builtInControls": ["mfa"]
  }'
```

#### 2. Network Security

```bash
# Restrict to specific IPs
az containerapp ingress access-restriction set \
  --name hartonomous-api \
  --resource-group hartonomous-prod \
  --rule-name "allow-corporate" \
  --ip-address-range "203.0.113.0/24" \
  --action Allow
```

### Monitoring

#### 1. Enable Application Insights

```bash
az containerapp update \
  --name hartonomous-api \
  --resource-group hartonomous-prod \
  --set-env-vars \
    APPLICATIONINSIGHTS_CONNECTION_STRING="YOUR_CONNECTION_STRING"
```

#### 2. Configure Alerts

```bash
# Alert on high error rate
az monitor metrics alert create \
  --name hart-api-errors \
  --resource-group hartonomous-prod \
  --scopes {containerapp-resource-id} \
  --condition "avg Percentage CPU > 80" \
  --description "API CPU usage high"
```

---

## ?? Testing

### Test Authentication

```bash
# Get access token
TOKEN=$(az account get-access-token \
  --resource https://hartonomous-api.azurecontainerapps.io \
  --query accessToken -o tsv)

# Call API with token
curl -H "Authorization: Bearer $TOKEN" \
  https://hartonomous-api.azurecontainerapps.io/v1/health
```

### Test Key Vault Integration

```bash
# Check if secrets loaded
curl -H "Authorization: Bearer $TOKEN" \
  https://hartonomous-api.azurecontainerapps.io/v1/stats
```

---

## ?? Documentation

- [Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/)
- [Azure Key Vault](https://learn.microsoft.com/en-us/azure/key-vault/)
- [Entra ID](https://learn.microsoft.com/en-us/entra/identity/)
- [Azure Arc](https://learn.microsoft.com/en-us/azure/azure-arc/)

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
