# Deployment Prerequisites

This document outlines the necessary steps to set up the Azure infrastructure and GitHub configuration for the Hartonomous API.

## 1. Environment Variables

The following environment variables are required for the application to function correctly.

### Core Configuration
| Variable | Description | Required | Default |
|:---|:---|:---|:---|
| `USE_AZURE_CONFIG` | Enable Azure Key Vault integration | Yes (Prod) | `false` |
| `AZURE_CLIENT_ID` | Managed Identity Client ID | Yes (Prod) | - |
| `AZURE_TENANT_ID` | Azure Tenant ID | Yes (Prod) | - |
| `KEY_VAULT_URL` | URL of the Azure Key Vault | Yes (Prod) | - |
| `APP_CONFIG_ENDPOINT` | Endpoint for Azure App Configuration | Yes (Prod) | - |

### Database Credentials (Local/Dev)
| Variable | Description | Required | Default |
|:---|:---|:---|:---|
| `PGHOST` | PostgreSQL Host | Yes | `localhost` |
| `PGPORT` | PostgreSQL Port | Yes | `5432` |
| `PGUSER` | PostgreSQL User | Yes | `postgres` |
| `PGPASSWORD` | PostgreSQL Password | **YES** | - |
| `PGDATABASE` | PostgreSQL Database | Yes | `hartonomous` |
| `NEO4J_PASSWORD` | Neo4j Password | **YES** | - |

### Authentication (Entra ID)
| Variable | Description | Required | Default |
|:---|:---|:---|:---|
| `ENTRA_CLIENT_ID` | Application ID for API | Yes (if Auth enabled) | - |
| `ENTRA_TENANT_ID` | Directory (Tenant) ID | Yes (if Auth enabled) | - |
| `ENTRA_CLIENT_SECRET` | Application Secret (stored in Key Vault) | No (Load from KV) | - |

---

## 2. Infrastructure Setup

We provide a script to automate the provisioning of Azure resources.

### Run the Setup Script
**Prerequisites:**
- Azure CLI installed (`az`)
- Logged in (`az login`)
- `jq` installed (for JSON parsing)

```bash
# Linux/Mac/WSL
chmod +x scripts/setup-azure-infrastructure.sh
./scripts/setup-azure-infrastructure.sh [RESOURCE_GROUP_NAME] [LOCATION]

# Example
./scripts/setup-azure-infrastructure.sh rg-hartonomous-prod eastus
```

**What this script does:**
1.  Creates a **Resource Group**.
2.  Creates a **User-Assigned Managed Identity** for the API.
3.  Creates an **Azure Key Vault** and assigns RBAC roles.
4.  Creates an **Azure App Configuration** store.
5.  Provisions a **PostgreSQL Flexible Server** (basic tier).
6.  Generates a random database password and saves it to Key Vault.
7.  Populates initial App Configuration settings.

---

## 3. GitHub Configuration

To enable the CI/CD pipeline (`.github/workflows/ci-cd.yml`), you must configure **Secrets** and **Variables** in your GitHub repository settings.

### GitHub Secrets (Sensitive)
Go to **Settings > Secrets and variables > Actions > New repository secret**.

*   `AZURE_CLIENT_ID`: The Client ID of the User-Assigned Managed Identity (output by setup script).
*   `AZURE_TENANT_ID`: Your Azure Tenant ID.
*   `AZURE_SUBSCRIPTION_ID`: Your Azure Subscription ID.
*   `PGPASSWORD`: Database password (for CI/CD testing environment).
*   `NEO4J_PASSWORD`: Neo4j password (for CI/CD testing environment).

### GitHub Variables (Non-Sensitive)
Go to **Settings > Secrets and variables > Actions > New repository variable**.

*   `PGUSER`: `hartonomous` (or your CI DB user)
*   `PGDATABASE`: `hartonomous` (or your CI DB name)
*   `NEO4J_USER`: `neo4j`
*   `DEPLOYMENT_URL`: URL of your deployed application (optional).

---

## 4. Entra ID (App Registration) Setup

The API requires an App Registration in Microsoft Entra ID to handle authentication.

1.  **Create App Registration:**
    *   Go to Azure Portal > Microsoft Entra ID > App registrations > New registration.
    *   Name: `Hartonomous API`.
    *   Accounts: "Accounts in this organizational directory only" (Single tenant).
    *   Redirect URI: `http://localhost:8000/docs/oauth2-redirect` (for Swagger UI) and your production URL.

2.  **Expose an API:**
    *   Go to "Expose an API" > Add a scope.
    *   Scope name: `access_as_user`.
    *   Admin consent display name: "Access Hartonomous API".

3.  **Add API Permissions:**
    *   Confirm `User.Read` is present.

4.  **Update Configuration:**
    *   Update `ENTRA_CLIENT_ID` and `ENTRA_TENANT_ID` in your App Configuration or `.env` file.
    *   Store the Client Secret in Key Vault if using a web app flow (though Managed Identity is preferred for service-to-service).

---

## 5. External ID (CIAM)

For external users (customers), use **Microsoft Entra External ID**.

1.  Create a new External Tenant in Azure Portal.
2.  Register an application in the External Tenant.
3.  Configure User Flows (Sign-up/Sign-in).
4.  Update `B2C_TENANT_NAME`, `B2C_CLIENT_ID`, and `B2C_POLICY_NAME` in your configuration.
