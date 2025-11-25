#!/usr/bin/env bash
# Azure Authentication Module (Bash)
# Handles Azure authentication for deployment scripts
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import logger
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./logger.sh
source "$SCRIPT_DIR/logger.sh"

function connect_azure_service_principal() {
    # Authenticate to Azure using Service Principal
    # Usage: connect_azure_service_principal <tenant_id> <client_id> <client_secret> [subscription_id]

    local tenant_id="$1"
    local client_id="$2"
    local client_secret="$3"
    local subscription_id="${4:-}"

    write_step "Authenticating to Azure"

    # Login with service principal
    write_log "Connecting to Azure AD tenant: $tenant_id" "DEBUG"

    if az login --service-principal \
        --username "$client_id" \
        --password "$client_secret" \
        --tenant "$tenant_id" \
        --output none 2>&1; then

        write_success "Authenticated to Azure"
    else
        write_failure "Azure authentication failed"
    fi

    # Set subscription if provided
    if [[ -n "$subscription_id" ]]; then
        write_log "Setting subscription: $subscription_id" "DEBUG"
        az account set --subscription "$subscription_id"
    fi

    # Verify authentication
    local account_name
    account_name=$(az account show --query "user.name" -o tsv)
    write_success "Authenticated as: $account_name"

    local subscription_name
    subscription_name=$(az account show --query "name" -o tsv)
    write_log "Subscription: $subscription_name" "INFO"
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
# - connect_azure_service_principal
# - get_keyvault_secret
# - get_appconfig_value
# - test_azure_connectivity
