#!/bin/bash
set -e

# Grant Azure Arc Server Managed Identity access to Key Vault

KEY_VAULT_NAME="hartonomous-kv"
ARC_RESOURCE_GROUP="your-arc-resource-group"
ARC_SERVER_NAME="hart-server"

echo "Granting Arc-enabled server access to Key Vault..."
echo ""

# Get the Arc server's managed identity principal ID
echo "Getting Arc server managed identity..."
PRINCIPAL_ID=$(az connectedmachine show \
    --resource-group "$ARC_RESOURCE_GROUP" \
    --name "$ARC_SERVER_NAME" \
    --query "identity.principalId" \
    --output tsv)

if [ -z "$PRINCIPAL_ID" ]; then
    echo "❌ Could not get managed identity for Arc server: $ARC_SERVER_NAME"
    echo "Ensure the server is Arc-enabled and has system-assigned managed identity enabled"
    exit 1
fi

echo "Arc Server: $ARC_SERVER_NAME"
echo "Principal ID: $PRINCIPAL_ID"
echo ""

# Grant Key Vault secrets access
echo "Granting Key Vault Secrets User role..."
az keyvault set-policy \
    --name "$KEY_VAULT_NAME" \
    --object-id "$PRINCIPAL_ID" \
    --secret-permissions get list

echo ""
echo "✓ Access granted successfully!"
echo ""
echo "Arc Server: $ARC_SERVER_NAME"
echo "Key Vault: $KEY_VAULT_NAME"
echo "Permissions: Get, List secrets"
echo ""
echo "The server can now access secrets using its managed identity."
echo ""
echo "Optional: Grant access to hart-desktop (Windows) as well:"
echo "  az connectedmachine show --resource-group $ARC_RESOURCE_GROUP --name hart-desktop --query identity.principalId -o tsv"
echo "  az keyvault set-policy --name $KEY_VAULT_NAME --object-id <principal-id> --secret-permissions get list"
