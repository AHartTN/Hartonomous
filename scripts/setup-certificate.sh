#!/bin/bash
#
# Automated Certificate Management for Hart Industries
# Creates/rotates code signing certificate in Azure Key Vault
# No manual steps, fully idempotent, cross-platform
#

set -euo pipefail

# Configuration
RESOURCE_GROUP="${RESOURCE_GROUP:-Hartonomous-RG}"
KEY_VAULT_NAME="${KEY_VAULT_NAME:-hartonomous-kv}"
CERT_NAME="${CERT_NAME:-HartIndustries-CodeSigning}"
LOCATION="${LOCATION:-eastus}"
SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-}"

echo "================================================================"
echo " Automated Certificate Management - Hart Industries"
echo "================================================================"
echo ""
echo "Configuration:"
echo "  Resource Group: $RESOURCE_GROUP"
echo "  Key Vault: $KEY_VAULT_NAME"
echo "  Certificate: $CERT_NAME"
echo "  Location: $LOCATION"
echo ""

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo "? Azure CLI not found. Install from: https://aka.ms/installazureclilinux"
    exit 1
fi

echo "? Azure CLI installed"

# Login check
if ! az account show &>/dev/null; then
    echo "? Not logged in to Azure. Logging in..."
    az login
fi

echo "? Authenticated to Azure"

# Set subscription if provided
if [ -n "$SUBSCRIPTION_ID" ]; then
    az account set --subscription "$SUBSCRIPTION_ID"
fi

CURRENT_SUB=$(az account show --query name -o tsv)
echo "? Using subscription: $CURRENT_SUB"

# Create resource group (idempotent)
echo ""
echo "Ensuring resource group exists..."
if az group show --name "$RESOURCE_GROUP" &>/dev/null; then
    echo "? Resource group exists"
else
    echo "  Creating resource group..."
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none
    echo "? Resource group created"
fi

# Create Key Vault (idempotent)
echo ""
echo "Ensuring Key Vault exists..."
if az keyvault show --name "$KEY_VAULT_NAME" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    echo "? Key Vault exists"
else
    echo "  Creating Key Vault..."
    az keyvault create \
        --name "$KEY_VAULT_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --location "$LOCATION" \
        --enable-rbac-authorization false \
        --enabled-for-deployment true \
        --enabled-for-template-deployment true \
        --output none
    echo "? Key Vault created"
fi

# Get current user object ID for access policy
USER_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)

# Set access policy for current user (idempotent)
echo ""
echo "Ensuring Key Vault access policies..."
az keyvault set-policy \
    --name "$KEY_VAULT_NAME" \
    --object-id "$USER_OBJECT_ID" \
    --certificate-permissions create get list update delete \
    --secret-permissions get list \
    --output none

echo "? Access policies configured"

# Check if certificate exists
echo ""
echo "Checking certificate status..."
if az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" &>/dev/null; then
    EXPIRES=$(az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" --query "attributes.expires" -o tsv)
    EXPIRES_EPOCH=$(date -d "$EXPIRES" +%s 2>/dev/null || date -j -f "%Y-%m-%dT%H:%M:%SZ" "$EXPIRES" +%s 2>/dev/null || echo "0")
    NOW_EPOCH=$(date +%s)
    DAYS_REMAINING=$(( ($EXPIRES_EPOCH - $NOW_EPOCH) / 86400 ))
    
    echo "? Certificate exists"
    echo "  Expires: $EXPIRES"
    echo "  Days remaining: $DAYS_REMAINING"
    
    # Rotate if less than 90 days remaining
    if [ "$DAYS_REMAINING" -lt 90 ]; then
        echo "? Certificate expiring soon, rotating..."
        ROTATE=true
    else
        echo "? Certificate is valid, no rotation needed"
        ROTATE=false
    fi
else
    echo "? Certificate does not exist, creating..."
    ROTATE=true
fi

# Create/rotate certificate
if [ "$ROTATE" = true ]; then
    echo ""
    echo "Creating certificate policy..."
    
    # Create certificate policy JSON
    POLICY=$(cat <<EOF
{
  "issuerParameters": {
    "name": "Self"
  },
  "keyProperties": {
    "exportable": true,
    "keySize": 2048,
    "keyType": "RSA",
    "reuseKey": false
  },
  "secretProperties": {
    "contentType": "application/x-pkcs12"
  },
  "x509CertificateProperties": {
    "subject": "CN=Hart Industries",
    "ekus": [
      "1.3.6.1.5.5.7.3.3"
    ],
    "keyUsage": [
      "digitalSignature"
    ],
    "validityInMonths": 60
  },
  "lifetimeActions": [
    {
      "action": {
        "actionType": "AutoRenew"
      },
      "trigger": {
        "daysBeforeExpiry": 90
      }
    }
  ]
}
EOF
)
    
    echo "$POLICY" > /tmp/cert-policy.json
    
    echo "  Creating certificate in Key Vault..."
    az keyvault certificate create \
        --vault-name "$KEY_VAULT_NAME" \
        --name "$CERT_NAME" \
        --policy @/tmp/cert-policy.json \
        --output none
    
    rm /tmp/cert-policy.json
    
    echo "? Certificate created/rotated"
    
    # Wait for certificate to be ready
    echo "  Waiting for certificate to be ready..."
    for i in {1..30}; do
        STATUS=$(az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" --query "attributes.enabled" -o tsv 2>/dev/null || echo "false")
        if [ "$STATUS" = "true" ]; then
            echo "? Certificate is ready"
            break
        fi
        sleep 2
    done
fi

# Get certificate details
echo ""
echo "Certificate Details:"
THUMBPRINT=$(az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" --query "x509Thumbprint" -o tsv)
EXPIRES=$(az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" --query "attributes.expires" -o tsv)

echo "  Thumbprint: $THUMBPRINT"
echo "  Expires: $EXPIRES"
echo "  Key Vault URL: https://${KEY_VAULT_NAME}.vault.azure.net/certificates/${CERT_NAME}"

# Configure Azure DevOps Service Connection (optional)
echo ""
echo "Azure DevOps Integration:"
echo "  1. Go to: https://dev.azure.com/aharttn/Hartonomous/_settings/adminservices"
echo "  2. Create service connection: Azure Resource Manager"
echo "  3. Connection name: AzureKeyVault-CodeSigning"
echo "  4. Use Key Vault: $KEY_VAULT_NAME"
echo ""

# Export instructions (for local use only, pipeline uses Key Vault directly)
echo "Local Certificate Export (optional):"
echo "  az keyvault certificate download --vault-name $KEY_VAULT_NAME --name $CERT_NAME --file HartIndustries.cer"
echo "  az keyvault secret download --vault-name $KEY_VAULT_NAME --name $CERT_NAME --encoding base64 --file HartIndustries.pfx.b64"
echo ""

echo "================================================================"
echo " Certificate Management Complete"
echo "================================================================"
echo ""
echo "? Certificate is stored in Azure Key Vault"
echo "? Auto-rotation enabled (90 days before expiry)"
echo "? No manual password management required"
echo "? Pipeline will use managed identity to access certificate"
echo ""
