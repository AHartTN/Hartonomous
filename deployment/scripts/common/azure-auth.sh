#!/usr/bin/env bash
# Azure Authentication Module (Bash)
# Handles Azure authentication for deployment scripts
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import logger
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./logger.sh
source "$SCRIPT_DIR/logger.sh"

function azure_login() {
    # Smart Azure login - uses OIDC in GitHub Actions, falls back to service principal
    # Reads from environment variables

    write_step "Authenticating to Azure"

    # Check if already authenticated
    if az account show &>/dev/null; then
        local account_name
        account_name=$(az account show --query "user.name" -o tsv)
        write_log "Already authenticated as: $account_name" "INFO"
        return 0
    fi

    # Try OIDC first (GitHub Actions with federated credentials)
    if [[ -n "${ACTIONS_ID_TOKEN_REQUEST_URL:-}" ]] && [[ -n "${AZURE_CLIENT_ID:-}" ]] && [[ -n "${AZURE_TENANT_ID:-}" ]]; then
        write_log "Detected GitHub Actions OIDC environment" "INFO"

        if az login \
            --service-principal \
            --username "$AZURE_CLIENT_ID" \
            --tenant "$AZURE_TENANT_ID" \
            --federated-token "$(curl -sS -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=api://AzureADTokenExchange" | jq -r .value)" \
            --output none 2>&1; then

            write_success "Authenticated via OIDC"
        else
            write_failure "OIDC authentication failed"
        fi

    # Fallback to service principal with client secret
    elif [[ -n "${AZURE_CLIENT_ID:-}" ]] && [[ -n "${AZURE_CLIENT_SECRET:-}" ]] && [[ -n "${AZURE_TENANT_ID:-}" ]]; then
        write_log "Using service principal with client secret" "INFO"

        if az login --service-principal \
            --username "$AZURE_CLIENT_ID" \
            --password "$AZURE_CLIENT_SECRET" \
            --tenant "$AZURE_TENANT_ID" \
            --output none 2>&1; then

            write_success "Authenticated via service principal"
        else
            write_failure "Service principal authentication failed"
        fi

    else
        write_failure "Missing Azure credentials. Set AZURE_CLIENT_ID and AZURE_TENANT_ID (and optionally AZURE_CLIENT_SECRET)"
    fi

    # Set subscription if provided
    if [[ -n "${AZURE_SUBSCRIPTION_ID:-}" ]]; then
        write_log "Setting subscription: $AZURE_SUBSCRIPTION_ID" "DEBUG"
        az account set --subscription "$AZURE_SUBSCRIPTION_ID"
    fi

    # Verify authentication
    local account_name
    account_name=$(az account show --query "user.name" -o tsv)
    write_success "Authenticated as: $account_name"

    local subscription_name
    subscription_name=$(az account show --query "name" -o tsv)
    write_log "Subscription: $subscription_name" "INFO"
}

function connect_azure_service_principal() {
    # Legacy function - deprecated, use azure_login() instead
    # Usage: connect_azure_service_principal <tenant_id> <client_id> <client_secret> [subscription_id]

    export AZURE_TENANT_ID="$1"
    export AZURE_CLIENT_ID="$2"
    export AZURE_CLIENT_SECRET="$3"
    export AZURE_SUBSCRIPTION_ID="${4:-}"

    azure_login
}

function get_keyvault_secret() {
    # Retrieve a secret from Azure Key Vault
    # Usage: get_keyvault_secret <vault_name> <secret_name>

    local vault_name="$1"
    local secret_name="$2"

    write_log "Retrieving secret '$secret_name' from Key Vault '$vault_name'" "DEBUG"

    local secret
    if secret=$(az keyvault secret show \
        --vault-name "$vault_name" \
        --name "$secret_name" \
        --query "value" \
        -o tsv 2>/dev/null); then

        write_log "Successfully retrieved secret '$secret_name'" "DEBUG"
        echo "$secret"
    else
        write_failure "Failed to retrieve secret '$secret_name' from Key Vault '$vault_name'"
    fi
}

function get_appconfig_value() {
    # Retrieve a value from Azure App Configuration
    # Usage: get_appconfig_value <endpoint> <key>

    local endpoint="$1"
    local key="$2"

    write_log "Retrieving config '$key' from App Configuration" "DEBUG"

    # Extract store name from endpoint
    local store_name
    store_name=$(echo "$endpoint" | sed 's|https://||' | sed 's|\.azconfig\.io.*||')

    local value
    if value=$(az appconfig kv show \
        --endpoint "$endpoint" \
        --key "$key" \
        --query "value" \
        -o tsv 2>/dev/null); then

        write_log "Successfully retrieved config '$key'" "DEBUG"
        echo "$value"
    else
        write_log "Failed to retrieve config '$key' (using default)" "WARNING"
        echo ""
    fi
}

function test_azure_connectivity() {
    # Test connectivity to Azure services

    write_step "Testing Azure Connectivity"

    # Test Azure AD
    if ! az account show &>/dev/null; then
        write_failure "Not authenticated to Azure"
    fi
    write_success "Azure AD: Connected"

    # Test subscription access
    local subscription_name
    subscription_name=$(az account show --query "name" -o tsv)
    write_success "Subscription: $subscription_name"

    # Test resource group access (if in DEPLOYMENT_ENVIRONMENT)
    if [[ -n "${DEPLOYMENT_ENVIRONMENT:-}" ]]; then
        local rg_name="rg-hartonomous"
        if az group show --name "$rg_name" &>/dev/null; then
            write_success "Resource Group: $rg_name"
        else
            write_log "Resource group '$rg_name' not found or no access" "WARNING"
        fi
    fi
}

# Export functions (documented for bash)
# Available functions:
# - azure_login (recommended - auto-detects OIDC vs service principal)
# - connect_azure_service_principal (legacy)
# - get_keyvault_secret
# - get_appconfig_value
# - test_azure_connectivity
