#!/bin/bash
# setup-azure-config.sh
# Migrates local configuration to Azure App Configuration + Key Vault
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -e

ENVIRONMENT=$1

if [ -z "$ENVIRONMENT" ]; then
    echo "Usage: setup-azure-config.sh <environment>"
    exit 1
fi

echo "?? Setting up Azure configuration for $ENVIRONMENT..."

# Load local config
CONFIG_FILE="deployment/config/$ENVIRONMENT.json"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "? Config file not found: $CONFIG_FILE"
    exit 1
fi

echo "?? Reading configuration from $CONFIG_FILE..."

# Extract values using jq
RESOURCE_GROUP=$(jq -r '.azure.resource_group' "$CONFIG_FILE")
KEY_VAULT_NAME=$(jq -r '.azure.key_vault_name' "$CONFIG_FILE")
APP_CONFIG_NAME="hartonomous-appconfig"

INSTALL_PATH=$(jq -r '.deployment.install_path' "$CONFIG_FILE")
API_HOST=$(jq -r '.api.host' "$CONFIG_FILE")
API_PORT=$(jq -r '.api.port' "$CONFIG_FILE")

DB_HOST=$(jq -r '.database.host' "$CONFIG_FILE")
DB_PORT=$(jq -r '.database.port' "$CONFIG_FILE")
DB_NAME=$(jq -r '.database.name' "$CONFIG_FILE")
DB_USER=$(jq -r '.database.user' "$CONFIG_FILE")

echo "?? Storing secrets in Key Vault: $KEY_VAULT_NAME..."

# Store database password in Key Vault
read -sp "Enter PostgreSQL password for $ENVIRONMENT: " DB_PASSWORD
echo

az keyvault secret set \
    --vault-name "$KEY_VAULT_NAME" \
    --name "database-$ENVIRONMENT-password" \
    --value "$DB_PASSWORD" \
    --output none

# Build connection string
DB_CONNECTION_STRING="postgresql://$DB_USER:$DB_PASSWORD@$DB_HOST:$DB_PORT/$DB_NAME"

# Store full connection string in Key Vault
az keyvault secret set \
    --vault-name "$KEY_VAULT_NAME" \
    --name "database-$ENVIRONMENT-connection-string" \
    --value "$DB_CONNECTION_STRING" \
    --output none

echo "? Secrets stored in Key Vault"

echo "?? Storing configuration in App Configuration..."

# Get Key Vault reference URI
KEY_VAULT_URI=$(az keyvault show --name "$KEY_VAULT_NAME" --resource-group "$RESOURCE_GROUP" --query properties.vaultUri -o tsv)
SECRET_REF="{\"uri\":\"${KEY_VAULT_URI}secrets/database-$ENVIRONMENT-connection-string\"}"

# Store App Config values
az appconfig kv set \
    --name "$APP_CONFIG_NAME" \
    --key "deployment:$ENVIRONMENT:install_path" \
    --value "$INSTALL_PATH" \
    --yes \
    --output none

az appconfig kv set \
    --name "$APP_CONFIG_NAME" \
    --key "api:$ENVIRONMENT:host" \
    --value "$API_HOST" \
    --yes \
    --output none

az appconfig kv set \
    --name "$APP_CONFIG_NAME" \
    --key "api:$ENVIRONMENT:port" \
    --value "$API_PORT" \
    --yes \
    --output none

# Store Key Vault reference for database connection string
az appconfig kv set-keyvault \
    --name "$APP_CONFIG_NAME" \
    --key "database:$ENVIRONMENT:connection_string" \
    --secret-identifier "${KEY_VAULT_URI}secrets/database-$ENVIRONMENT-connection-string" \
    --yes \
    --output none

echo "? Configuration stored in App Configuration"
echo "?? Setup complete for $ENVIRONMENT!"
echo ""
echo "Configuration stored:"
echo "  - App Config: $APP_CONFIG_NAME"
echo "  - Key Vault: $KEY_VAULT_NAME"
echo "  - Secrets: database-$ENVIRONMENT-*"
