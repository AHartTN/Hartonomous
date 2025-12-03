#!/bin/bash
# Complete Zero Trust setup automation - Run this once

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo "========================================="
echo "Hartonomous Zero Trust Setup"
echo "Complete automated configuration"
echo "========================================="
echo ""

# Step 1: Populate Key Vault
echo "[1/2] Populating Key Vault with secrets..."
bash "$SCRIPT_DIR/setup-keyvault-secrets.sh"

echo ""

# Step 2: Deploy RBAC
echo "[2/2] Deploying RBAC role assignments..."
bash "$SCRIPT_DIR/deploy-rbac.sh"

echo ""
echo "========================================="
echo "Zero Trust Setup Complete!"
echo "========================================="
echo ""
echo "Configuration Summary:"
echo "  ✓ Key Vault secrets configured"
echo "  ✓ RBAC permissions assigned"
echo "  ✓ Managed identities configured"
echo "  ✓ Multi-tenant isolation enabled"
echo ""
echo "Your Arc machines can now:"
echo "  - Authenticate via managed identity (no passwords)"
echo "  - Access Key Vault for secrets"
echo "  - Access App Configuration for settings"
echo "  - Connect to PostgreSQL with Azure AD tokens"
echo ""
echo "Next: Deploy your application!"
echo "  ./scripts/deploy-migrations.sh dev"
