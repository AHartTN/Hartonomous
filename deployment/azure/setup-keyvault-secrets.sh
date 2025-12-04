#!/bin/bash
set -e

# Hartonomous - Azure Key Vault Secrets Setup
# For Arc-enabled servers with Managed Identity

KEY_VAULT_NAME="hartonomous-kv"

echo "Setting up secrets in Azure Key Vault: $KEY_VAULT_NAME"
echo ""

# Check if logged in to Azure
if ! az account show &> /dev/null; then
    echo "Not logged in to Azure. Logging in..."
    az login
fi

echo "Current subscription:"
az account show --query "{Name:name, ID:id}" -o table
echo ""

read -p "Is this the correct subscription? (y/n) " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Please run 'az account set --subscription <subscription-id>' first"
    exit 1
fi

# Prompt for secrets
echo "Enter database password for hartonomous_user:"
read -sp "PostgreSQL Password: " POSTGRES_PASSWORD
echo ""

echo "Generating JWT secret key..."
JWT_SECRET=$(openssl rand -base64 32)

echo "Generating data protection key..."
DATA_PROTECTION_KEY=$(openssl rand -base64 32)

# Set secrets in Key Vault
echo ""
echo "Setting secrets in Key Vault..."

az keyvault secret set \
    --vault-name "$KEY_VAULT_NAME" \
    --name "PostgreSQL--Password" \
    --value "$POSTGRES_PASSWORD" \
    --description "Hartonomous PostgreSQL user password"

az keyvault secret set \
    --vault-name "$KEY_VAULT_NAME" \
    --name "PostgreSQL--ConnectionString" \
    --value "Host=localhost;Port=5432;Database=hartonomous;Username=hartonomous_user;Password=$POSTGRES_PASSWORD" \
    --description "Full PostgreSQL connection string"

az keyvault secret set \
    --vault-name "$KEY_VAULT_NAME" \
    --name "JWT--SecretKey" \
    --value "$JWT_SECRET" \
    --description "JWT signing secret key"

az keyvault secret set \
    --vault-name "$KEY_VAULT_NAME" \
    --name "DataProtection--Key" \
    --value "$DATA_PROTECTION_KEY" \
    --description "Data protection encryption key"

echo ""
echo "✓ Secrets stored in Key Vault successfully!"
echo ""
echo "Key Vault: $KEY_VAULT_NAME"
echo "Secrets created:"
echo "  - PostgreSQL--Password"
echo "  - PostgreSQL--ConnectionString"
echo "  - JWT--SecretKey"
echo "  - DataProtection--Key"
echo ""
echo "Next: Grant Arc-enabled server managed identity access to Key Vault"
