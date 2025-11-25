#!/bin/bash
# Preflight Check - Validate Secrets (Bash)
# Validates that required secrets are accessible and valid
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/config-loader.sh"

# Initialize
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-}"
LOG_PATH="${LOG_PATH:-/var/log/hartonomous}"
LOG_FILE="$LOG_PATH/validate-secrets-$(date +%Y%m%d-%H%M%S).log"

# Create log directory if it doesn't exist
mkdir -p "$LOG_PATH"

# Initialize logger
initialize_logger "$LOG_FILE" "${LOG_LEVEL:-INFO}"

# Helper functions to match logger.sh naming
log_step() { write_step "$1"; }
log_info() { write_log "$1" "INFO"; }
log_success() { write_success "$1"; }
log_error() { write_log "$1" "ERROR"; }
log_warning() { write_log "$1" "WARNING"; }
log_failure() { write_failure "$1"; }

log_step "Validating Secrets and Credentials"

# Validate environment
if [ -z "$ENVIRONMENT" ]; then
    log_failure "DEPLOYMENT_ENVIRONMENT not set"
fi

log_info "Environment: $ENVIRONMENT"

# Required environment variables for Azure authentication
declare -A REQUIRED_VARS=(
    ["AZURE_TENANT_ID"]="Azure Tenant ID"
    ["AZURE_CLIENT_ID"]="Azure Client ID (Service Principal)"
    ["AZURE_CLIENT_SECRET"]="Azure Client Secret (Service Principal)"
    ["KEY_VAULT_URL"]="Azure Key Vault URL"
)

# Check 1: Validate environment variables exist
log_step "Checking Required Environment Variables"
MISSING_VARS=()

for var in "${!REQUIRED_VARS[@]}"; do
    if [ -z "${!var:-}" ]; then
        log_error "Missing: $var (${REQUIRED_VARS[$var]})"
        MISSING_VARS+=("$var")
    else
        # Mask the secret in the log
        if [[ $var =~ (SECRET|PASSWORD) ]]; then
            MASKED_VALUE="${!var:0:4}****"
        else
            MASKED_VALUE="${!var}"
        fi
        log_success "Found: $var = $MASKED_VALUE"
    fi
done

if [ ${#MISSING_VARS[@]} -gt 0 ]; then
    log_failure "Missing required environment variables: ${MISSING_VARS[*]}"
fi

# Check 2: Validate Azure Tenant ID format (GUID)
log_step "Validating Azure Tenant ID Format"
if [[ $AZURE_TENANT_ID =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]; then
    log_success "Tenant ID format is valid"
else
    log_failure "Invalid Tenant ID format (must be a GUID): $AZURE_TENANT_ID"
fi

# Check 3: Validate Azure Client ID format (GUID)
log_step "Validating Azure Client ID Format"
if [[ $AZURE_CLIENT_ID =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]; then
    log_success "Client ID format is valid"
else
    log_failure "Invalid Client ID format (must be a GUID): $AZURE_CLIENT_ID"
fi

# Check 4: Validate Key Vault URL format
log_step "Validating Key Vault URL Format"
if [[ $KEY_VAULT_URL =~ ^https://[a-z0-9-]+\.vault\.azure\.net/?$ ]]; then
    log_success "Key Vault URL format is valid"
else
    log_failure "Invalid Key Vault URL format: $KEY_VAULT_URL (Expected: https://<name>.vault.azure.net/)"
fi

# Check 5: Validate Client Secret is not empty and meets minimum complexity
log_step "Validating Client Secret"
if [ -z "$AZURE_CLIENT_SECRET" ]; then
    log_failure "Client Secret is empty"
elif [ ${#AZURE_CLIENT_SECRET} -lt 20 ]; then
    log_warning "Client Secret is shorter than expected (may be invalid)"
    log_success "Client Secret exists (length: ${#AZURE_CLIENT_SECRET})"
else
    log_success "Client Secret exists and meets minimum length requirements"
fi

# Check 6: Test Azure authentication
log_step "Testing Azure Service Principal Authentication"

# Create temporary Azure config directory
TEMP_AZURE_DIR=$(mktemp -d)
export AZURE_CONFIG_DIR="$TEMP_AZURE_DIR"

# Clear any existing Azure CLI sessions
az logout &>/dev/null || true

# Login with service principal
if az login --service-principal \
    --username "$AZURE_CLIENT_ID" \
    --password "$AZURE_CLIENT_SECRET" \
    --tenant "$AZURE_TENANT_ID" \
    --output json &>/dev/null; then
    
    log_success "Azure authentication successful"
    
    # Try to access the subscription
    if [ -n "${AZURE_SUBSCRIPTION_ID:-}" ]; then
        if az account set --subscription "$AZURE_SUBSCRIPTION_ID" &>/dev/null; then
            log_success "Subscription access verified"
        fi
    fi
else
    log_failure "Failed to authenticate with Azure using provided Service Principal credentials"
fi

# Check 7: Test Key Vault connectivity
log_step "Testing Key Vault Connectivity"

KV_NAME=$(echo "$KEY_VAULT_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')

if SECRET_LIST=$(az keyvault secret list --vault-name "$KV_NAME" --output json 2>&1); then
    log_success "Key Vault is accessible"
    
    # Parse the result
    SECRET_COUNT=$(echo "$SECRET_LIST" | jq '. | length' 2>/dev/null || echo "unknown")
    log_info "Key Vault contains $SECRET_COUNT secrets"
else
    log_failure "Failed to access Key Vault: $KV_NAME"
fi

# Check 8: Verify specific secrets exist in Key Vault (for non-development environments)
if [ "$ENVIRONMENT" != "development" ]; then
    log_step "Verifying Required Secrets in Key Vault"
    
    # Load configuration
    CONFIG=$(get_deployment_config "$ENVIRONMENT")
    
    # Expected secrets based on configuration
    EXPECTED_SECRETS=()
    
    # Database password
    DB_NAME=$(echo "$CONFIG" | jq -r '.database.name')
    if [ -n "$DB_NAME" ] && [ "$DB_NAME" != "null" ]; then
        EXPECTED_SECRETS+=("PostgreSQL-${DB_NAME}-Password")
    fi
    
    # Neo4j password (if enabled)
    NEO4J_ENABLED=$(echo "$CONFIG" | jq -r '.features.neo4j_enabled')
    TARGET_MACHINE=$(echo "$CONFIG" | jq -r '.target.machine')
    if [ "$NEO4J_ENABLED" = "true" ] && [ -n "$TARGET_MACHINE" ] && [ "$TARGET_MACHINE" != "null" ]; then
        EXPECTED_SECRETS+=("Neo4j-${TARGET_MACHINE}-Password")
    fi
    
    MISSING_SECRETS=()
    for secret_name in "${EXPECTED_SECRETS[@]}"; do
        if az keyvault secret show --vault-name "$KV_NAME" --name "$secret_name" --output json &>/dev/null; then
            log_success "Secret exists: $secret_name"
        else
            log_error "Secret not found: $secret_name"
            MISSING_SECRETS+=("$secret_name")
        fi
    done
    
    if [ ${#MISSING_SECRETS[@]} -gt 0 ]; then
        log_failure "Missing required secrets in Key Vault: ${MISSING_SECRETS[*]}"
    fi
else
    log_info "Development environment - skipping Key Vault secret verification"
fi

# Cleanup
az logout &>/dev/null || true
rm -rf "$TEMP_AZURE_DIR"

# Final Summary
log_step "Secret Validation Complete"
log_success "All secrets and credentials are valid and accessible"
log_info "Ready for deployment"

exit 0
