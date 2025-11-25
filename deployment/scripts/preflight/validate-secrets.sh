#!/bin/bash
# Secret Validation Script (Bash)
# Validates required secrets are accessible
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Secret Validation"

# Check if Azure authentication is configured
if [[ -z "${AZURE_TENANT_ID:-}" ]] || [[ -z "${AZURE_CLIENT_ID:-}" ]]; then
    write_log "Azure credentials not configured - skipping secret validation" "WARNING"
    write_log "Set AZURE_TENANT_ID and AZURE_CLIENT_ID environment variables" "INFO"
    exit 0
fi

# Check if Key Vault URL is provided
if [[ -z "${KEY_VAULT_URL:-}" ]]; then
    write_log "KEY_VAULT_URL not set - skipping secret validation" "WARNING"
    exit 0
fi

write_log "Key Vault URL: $KEY_VAULT_URL" "INFO"

# Extract Key Vault name from URL
KV_NAME=$(echo "$KEY_VAULT_URL" | sed 's|https://||' | sed 's|\.vault\.azure\.net.*||')
write_log "Key Vault name: $KV_NAME" "INFO"

# Authenticate to Azure
azure_login

# Define required secrets
declare -a REQUIRED_SECRETS=(
    "PostgreSQL-Hartonomous-Password"
)

# Add environment-specific secrets
ENVIRONMENT="${DEPLOYMENT_ENVIRONMENT:-development}"

case $ENVIRONMENT in
    production)
        REQUIRED_SECRETS+=(
            "Neo4j-hart-server-Password"
            "AzureAd-ClientSecret"
        )
        ;;
    staging)
        REQUIRED_SECRETS+=(
            "Neo4j-hart-server-Password"
            "AzureAd-ClientSecret"
        )
        ;;
    development)
        # Development uses local credentials
        ;;
esac

# Validate secrets
write_step "Validating Secrets"

VALIDATION_FAILED=false

for SECRET_NAME in "${REQUIRED_SECRETS[@]}"; do
    write_log "Checking secret: $SECRET_NAME" "DEBUG"

    # Check if secret exists and is accessible
    if SECRET_VALUE=$(az keyvault secret show \
        --vault-name "$KV_NAME" \
        --name "$SECRET_NAME" \
        --query "value" \
        --output tsv 2>/dev/null); then

        # Check if secret has a value
        if [[ -n "$SECRET_VALUE" ]]; then
            write_success "Secret exists: $SECRET_NAME"
        else
            write_log "Secret exists but is empty: $SECRET_NAME" "ERROR"
            VALIDATION_FAILED=true
        fi
    else
        write_log "Secret not found or not accessible: $SECRET_NAME" "ERROR"
        VALIDATION_FAILED=true
    fi
done

# Summary
write_step "Validation Summary"

if [[ "$VALIDATION_FAILED" == "true" ]]; then
    write_failure "Secret validation failed - some secrets are missing or inaccessible"
else
    write_success "All required secrets are accessible"
fi

write_log "Secret validation completed" "INFO"
