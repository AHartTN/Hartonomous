#!/bin/bash
set -e

# Deploy Zero Trust RBAC configuration using Bicep

RESOURCE_GROUP="rg-hartonomous"
LOCATION="eastus"
API_SP_NAME="Hartonomous API (Production)"

echo "Deploying Zero Trust RBAC configuration..."

# Get API service principal object ID
echo "Looking up service principal: $API_SP_NAME"
API_SP_ID=$(az ad sp list --display-name "$API_SP_NAME" --query "[0].id" -o tsv)

if [ -z "$API_SP_ID" ]; then
    echo "ERROR: Could not find service principal '$API_SP_NAME'"
    echo "Available service principals:"
    az ad sp list --filter "startswith(displayName,'Hartonomous')" --query "[].{Name:displayName, ObjectId:id}" -o table
    exit 1
fi

echo "Found API Service Principal: $API_SP_ID"

# Deploy Bicep template
echo "Deploying RBAC assignments..."
az deployment group create \
    --resource-group $RESOURCE_GROUP \
    --template-file infrastructure/rbac.bicep \
    --parameters apiServicePrincipalId=$API_SP_ID \
    --verbose

echo ""
echo "Zero Trust RBAC deployment complete!"
echo "✓ Key Vault access granted to API and Arc machines"
echo "✓ App Configuration access granted to API and Arc machines"
