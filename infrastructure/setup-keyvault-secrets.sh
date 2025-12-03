#!/bin/bash
# Populate Key Vault with all Hartonomous secrets for Zero Trust setup

set -e

KEY_VAULT_NAME="${1:-kv-hartonomous}"

echo "========================================="
echo "Hartonomous Key Vault Secret Setup"
echo "========================================="
echo ""

# Get Entra ID Tenant ID
echo "Retrieving Entra ID Tenant ID..."
TENANT_ID=$(az account show --query tenantId -o tsv)
echo "✓ Tenant ID: $TENANT_ID"

# Get API Client ID
echo ""
echo "Retrieving API Service Principal..."
API_CLIENT_ID=$(az ad sp list --display-name "Hartonomous API (Production)" --query "[0].appId" -o tsv)

if [ -z "$API_CLIENT_ID" ]; then
    echo "ERROR: Could not find 'Hartonomous API (Production)' service principal"
    echo "Available Hartonomous service principals:"
    az ad sp list --filter "startswith(displayName,'Hartonomous')" --query "[].{Name:displayName, AppId:appId}" -o table
    exit 1
fi

echo "✓ API Client ID: $API_CLIENT_ID"

# Function to set secret
set_secret() {
    local name=$1
    local value=$2

    echo "Setting: $name"
    if az keyvault secret set \
        --vault-name "$KEY_VAULT_NAME" \
        --name "$name" \
        --value "$value" \
        --output none; then
        echo "  ✓ $name"
    else
        echo "  ✗ Failed to set $name"
        return 1
    fi
}

echo ""
echo "Setting Key Vault secrets in: $KEY_VAULT_NAME"
echo ""

# Set all secrets
set_secret "AzureAd--TenantId" "$TENANT_ID"
set_secret "AzureAd--ClientId" "$API_CLIENT_ID"
set_secret "postgres-connection-localhost" "Host=localhost;Port=5432;Database=hartonomous_localhost;Username=postgres"
set_secret "postgres-connection-dev" "Host=localhost;Port=5433;Database=hartonomous_dev;Username=postgres"
set_secret "postgres-connection-staging" "Host=localhost;Port=5434;Database=hartonomous_staging;Username=postgres"
set_secret "postgres-connection-production" "Host=localhost;Port=5435;Database=hartonomous_production;Username=postgres"

echo ""
echo "========================================="
echo "Key Vault Configuration Complete!"
echo "========================================="
echo ""
echo "Secrets stored in Key Vault:"
echo "  - AzureAd--TenantId"
echo "  - AzureAd--ClientId"
echo "  - postgres-connection-localhost"
echo "  - postgres-connection-dev"
echo "  - postgres-connection-staging"
echo "  - postgres-connection-production"
echo ""
echo "Next steps:"
echo "  1. Run: ./infrastructure/deploy-rbac.sh"
echo "  2. Update API appsettings with values above"
echo "  3. Deploy to Arc machines"
