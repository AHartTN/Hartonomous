# Azure Configuration Reference

**Complete Azure AD, CIAM, and Key Vault configuration for Hartonomous**

---

## ?? **TENANT ARCHITECTURE**

### **Main Tenant (Workforce)**
- **Tenant Name:** `aharttngmail710.onmicrosoft.com`
- **Tenant ID:** `6c9c44c4-f04b-4b5f-bea0-f1069179799c`
- **Purpose:** Internal applications, GitHub Actions, Azure Arc

### **CIAM Tenant (External ID - Customers)**
- **Tenant Name:** `hartonomous.onmicrosoft.com`
- **Tenant ID:** `1816887e-0796-4065-899f-a7478b3918d7`
- **Purpose:** External user authentication (customers, partners)
- **Authority:** `https://hartonomous.ciamlogin.com/hartonomous.onmicrosoft.com`
- **Location:** United States

---

## ?? **APP REGISTRATIONS**

### **Main Tenant Apps**

#### **1. Hartonomous API (Production)**
- **App ID:** `2e54ba10-1d6e-4a8e-a100-6f84a586e7bf`
- **Audience:** `AzureADMyOrg` (single tenant)
- **Purpose:** Production API authentication
- **Redirect URI:** `https://localhost:5001/signin-oidc`
- **Client Secret:** Stored in Key Vault (`AzureAd-ClientSecret`)

#### **2. Hartonomous (Public API)**
- **App ID:** `c25ed11d-c712-4574-8897-6a3a0c8dbb7f`
- **Audience:** `AzureADandPersonalMicrosoftAccount`
- **Purpose:** Public API with personal account support
- **Redirect URI:** `https://www.hartonomous.com/swagger/oauth2-redirect.html`

#### **3. Hartonomous Admin UI (Production)**
- **App ID:** `cef51df8-96c4-4325-b4bf-447fb27b91a0`
- **Audience:** `AzureADMyOrg`
- **Purpose:** Admin interface authentication
- **Redirect URI:** `https://localhost:7000/signin-oidc`

#### **4. GitHub Actions - Development**
- **App ID:** `66a37c0f-5666-450b-b61f-c9e33b56115e`
- **Audience:** `AzureADMyOrg`
- **Purpose:** CI/CD for development environment
- **Federated Credential:** `repo:AHartTN/Hartonomous-Sandbox:environment:development`

#### **5. GitHub Actions - Staging**
- **App ID:** `f05370b1-d09f-4085-bd04-ac028c28b7f8`
- **Audience:** `AzureADMyOrg`
- **Purpose:** CI/CD for staging environment
- **Federated Credential:** `repo:AHartTN/Hartonomous-Sandbox:environment:staging`

#### **6. GitHub Actions - Production**
- **App ID:** `48a904b7-f070-407d-abab-1b71a3c049a9`
- **Audience:** `AzureADMyOrg`
- **Purpose:** CI/CD for production environment

### **CIAM Tenant Apps**

?? **ACTION REQUIRED:** Create app registration in CIAM tenant for external customer authentication.

**Recommended configuration:**
```
Name: Hartonomous Customer Portal
Audience: Customers (external users)
Redirect URIs:
  - https://www.hartonomous.com/auth/callback
  - https://hartonomous.com/auth/callback
  - http://localhost:8000/auth/callback (dev)
```

---

## ?? **AZURE KEY VAULT**

### **Key Vault Details**
- **Name:** `kv-hartonomous`
- **URL:** `https://kv-hartonomous.vault.azure.net/`
- **Resource Group:** `rg-hartonomous`
- **Location:** `eastus`
- **SKU:** `Standard`
- **RBAC:** Enabled

### **Secrets (14 total)**

#### **PostgreSQL Passwords**
| Secret Name | Description |
|-------------|-------------|
| `PostgreSQL-Hartonomous-Password` | Original password (HART-DESKTOP) |
| `PostgreSQL-HART-DESKTOP-hartonomous-Password` | User: hartonomous on HART-DESKTOP |
| `PostgreSQL-hart-server-postgres-Password` | Superuser on hart-server |
| `PostgreSQL-hart-server-ai-architect-Password` | User: ai_architect on hart-server |

#### **Entra ID / CIAM Secrets**
| Secret Name | Description |
|-------------|-------------|
| `AzureAd-ClientSecret` | Hartonomous API (Production) client secret |
| `EntraExternalId-ClientSecret` | CIAM client secret (for external users) |

#### **Arc Management Secrets**
| Secret Name | Description |
|-------------|-------------|
| `HART-DESKTOP-Management-Secret` | Arc agent management |
| `HART-SERVER-Management-Secret` | Arc agent management |

#### **Neo4j Passwords**
| Secret Name | Description |
|-------------|-------------|
| `Neo4jPassword` | Neo4j database password (hart-server production) |

#### **Application Insights**
| Secret Name | Description |
|-------------|-------------|
| `ApplicationInsights-ConnectionString` | App Insights connection string |

#### **HuggingFace**
| Secret Name | Description |
|-------------|-------------|
| `HuggingFace-ApiToken` | HuggingFace API token (read-only) |

#### **Stripe**
| Secret Name | Description |
|-------------|-------------|
| `Stripe-PublishableKey` | Stripe publishable key |
| `Stripe-SecretKey` | Stripe secret key |
| `Stripe-WebhookSecret` | Stripe webhook secret |

#### **Miscellaneous**
| Secret Name | Description |
|-------------|-------------|
| `clr-pfx-password` | Certificate password |

---

## ?? **APP CONFIGURATION**

### **App Configuration Details**
- **Name:** `appconfig-hartonomous`
- **Endpoint:** `https://appconfig-hartonomous.azconfig.io`
- **Resource Group:** `rg-hartonomous`
- **Location:** `eastus`
- **SKU:** `Standard`

### **Configuration Keys (41 total)**

#### **Key Vault References (7)**
```
Azure:ApplicationInsights:ConnectionString ? Key Vault
AzureAd:ClientSecret ? Key Vault
EntraExternalId:ClientSecret ? Key Vault
HuggingFace:ApiToken ? Key Vault
Neo4j:Password ? Key Vault
ConnectionStrings:PostgreSQL-Password ? Key Vault
```

#### **Azure AD Settings**
```
AzureAd:Domain
AzureAd:Instance
AzureAd:TenantId = 6c9c44c4-f04b-4b5f-bea0-f1069179799c
AzureAd:ClientId = 2e54ba10-1d6e-4a8e-a100-6f84a586e7bf
AzureAd:CallbackPath
AzureAd:Scopes
```

#### **Connection Strings**
```
ConnectionStrings:HartonomousDb
ConnectionStrings:IdentityDb
ConnectionStrings:Neo4j
ConnectionStrings:SqlServer
ConnectionStrings:PostgreSQL-HART-DESKTOP
ConnectionStrings:PostgreSQL-hart-server
```

#### **Hartonomous API Settings**
```
Hartonomous:Api:Host = 0.0.0.0
Hartonomous:Api:Port = 8000
Hartonomous:Api:LogLevel = INFO
Hartonomous:Api:CorsOrigins = (comma-separated)
Hartonomous:Api:AuthEnabled = true
Hartonomous:Api:PoolMinSize = 5
Hartonomous:Api:PoolMaxSize = 20
```

#### **AGE Worker Settings**
```
Hartonomous:AgeWorker:Enabled = true
Hartonomous:AgeWorker:PollInterval = 5
```

#### **Neo4j Settings**
```
Neo4j:Uri
Neo4j:Username
Neo4j:Database
```

#### **Production Settings**
```
Production:Environment
Production:Cors:AllowedOrigins
Production:Security:RequireHttps
ProductionDomain
```

#### **Stripe Settings**
```
Stripe:Enabled
Stripe:Mode
Stripe:PublishableKey
Stripe:SecretKey
Stripe:WebhookSecret
```

#### **Feature Flags (1)**
```
.appconfig.featureflag/RevolutionaryAI
```

---

## ?? **AZURE ARC**

### **Arc-Enabled Machines**

#### **HART-DESKTOP (Windows)**
- **Resource ID:** `/subscriptions/ed614e1a-7d8b-4608-90c8-66e86c37080b/resourceGroups/rg-hartonomous/providers/Microsoft.HybridCompute/machines/HART-DESKTOP`
- **Status:** Connected
- **OS:** Windows
- **Agent Version:** 1.58.03228.2572
- **Managed Identity:**
  - **Principal ID:** `505c61a6-bcd6-4f22-aee5-5c6c0094ae0d`
  - **Tenant ID:** `6c9c44c4-f04b-4b5f-bea0-f1069179799c`
- **Extensions:**
  - `WindowsAgent.SqlServer`
- **RBAC Roles:**
  - Key Vault Secrets User
  - App Configuration Data Reader

#### **hart-server (Linux)**
- **Resource ID:** `/subscriptions/ed614e1a-7d8b-4608-90c8-66e86c37080b/resourceGroups/rg-hartonomous/providers/Microsoft.HybridCompute/machines/hart-server`
- **Status:** Connected
- **OS:** Linux
- **Agent Version:** 1.58.03228.700
- **Managed Identity:**
  - **Principal ID:** `50c98169-43ea-4ee7-9daa-d752ed328994`
  - **Tenant ID:** `6c9c44c4-f04b-4b5f-bea0-f1069179799c`
- **Extensions:**
  - `AADSSHLogin`
  - `LinuxAgent.SqlServer`
- **RBAC Roles:**
  - Key Vault Secrets User
  - App Configuration Data Reader

---

## ?? **SUBSCRIPTION**

- **Subscription ID:** `ed614e1a-7d8b-4608-90c8-66e86c37080b`
- **Resource Group:** `rg-hartonomous`
- **Location:** `eastus`

---

## ?? **CONFIGURATION FOR .ENV**

### **For Local Development (HART-DESKTOP)**
```ini
# Entra ID (Main Tenant)
ENTRA_TENANT_ID=6c9c44c4-f04b-4b5f-bea0-f1069179799c
ENTRA_CLIENT_ID=2e54ba10-1d6e-4a8e-a100-6f84a586e7bf
ENTRA_CLIENT_SECRET=<from-key-vault>

# CIAM (External Customers)
CIAM_ENABLED=true
CIAM_TENANT_ID=1816887e-0796-4065-899f-a7478b3918d7
CIAM_TENANT_NAME=hartonomous
CIAM_AUTHORITY=https://hartonomous.ciamlogin.com/hartonomous.onmicrosoft.com
CIAM_CLIENT_ID=<create-app-in-ciam-tenant>
CIAM_CLIENT_SECRET=<from-key-vault>

# Azure Configuration
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
```

### **For Production (Azure)**
All secrets loaded from Key Vault via Managed Identity.

---

## ?? **NEXT STEPS**

### **1. Create CIAM App Registration**
```bash
# Switch to CIAM tenant
az login --tenant 1816887e-0796-4065-899f-a7478b3918d7

# Create app registration
az ad app create \
  --display-name "Hartonomous Customer Portal" \
  --sign-in-audience AzureADMyOrg \
  --web-redirect-uris \
    https://www.hartonomous.com/auth/callback \
    https://hartonomous.com/auth/callback \
    http://localhost:8000/auth/callback
```

### **2. Store CIAM Client Secret**
```bash
# Generate client secret in Azure Portal
# Then store in Key Vault
az keyvault secret set \
  --vault-name kv-hartonomous \
  --name "CIAM-ClientSecret" \
  --value "<client-secret-value>"
```

### **3. Update App Configuration**
```bash
# Add CIAM settings to App Configuration
az appconfig kv set \
  --name appconfig-hartonomous \
  --key "CIAM:TenantId" \
  --value "1816887e-0796-4065-899f-a7478b3918d7" \
  --yes

az appconfig kv set \
  --name appconfig-hartonomous \
  --key "CIAM:Authority" \
  --value "https://hartonomous.ciamlogin.com/hartonomous.onmicrosoft.com" \
  --yes
```

---

**Last Updated:** 2025-11-25  
**Maintained By:** AI Architect Team

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
