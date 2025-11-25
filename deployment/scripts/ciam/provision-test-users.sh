#!/bin/bash
# CIAM Test User Provisioning Script
# Creates test users in Azure AD B2C for testing
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "CIAM Test User Provisioning"

# Azure AD B2C Configuration
B2C_TENANT_NAME="${B2C_TENANT_NAME:-hartonomous}"
B2C_TENANT_ID="${AZURE_AD_B2C_TENANT_ID:-}"

if [[ -z "$B2C_TENANT_ID" ]]; then
    write_failure "AZURE_AD_B2C_TENANT_ID environment variable not set"
fi

write_log "B2C Tenant: $B2C_TENANT_NAME.onmicrosoft.com" "INFO"

# Authenticate to Azure
write_step "Authenticating to Azure"
azure_login

# Test Users to create
declare -a TEST_USERS=(
    "testuser1@$B2C_TENANT_NAME.onmicrosoft.com:Test User 1:Password123!"
    "testuser2@$B2C_TENANT_NAME.onmicrosoft.com:Test User 2:Password123!"
    "testadmin@$B2C_TENANT_NAME.onmicrosoft.com:Test Admin:Password123!"
)

USERS_CREATED=0
USERS_FAILED=0

# Create test users
write_step "Creating Test Users"

for USER_INFO in "${TEST_USERS[@]}"; do
    IFS=':' read -r EMAIL DISPLAY_NAME PASSWORD <<< "$USER_INFO"

    write_log "Creating user: $EMAIL" "INFO"

    # Check if user already exists
    if az ad user show --id "$EMAIL" &>/dev/null; then
        write_log "User already exists: $EMAIL" "WARNING"
        continue
    fi

    # Create user
    if az ad user create \
        --display-name "$DISPLAY_NAME" \
        --user-principal-name "$EMAIL" \
        --password "$PASSWORD" \
        --force-change-password-next-login false \
        --output none; then

        write_success "Created user: $EMAIL"
        ((USERS_CREATED++))

        # Add additional user attributes for B2C
        USER_ID=$(az ad user show --id "$EMAIL" --query id -o tsv)

        # Set user as test user (extension attribute)
        az rest --method PATCH \
            --url "https://graph.microsoft.com/v1.0/users/$USER_ID" \
            --headers "Content-Type=application/json" \
            --body "{\"extension_isTestUser\": true}" \
            &>/dev/null || write_log "Could not set test user attribute" "WARNING"

    else
        write_log "Failed to create user: $EMAIL" "ERROR"
        ((USERS_FAILED++))
    fi
done

# Assign users to test group (if exists)
write_step "Assigning Users to Test Group"

TEST_GROUP_NAME="B2C-Test-Users"

# Check if test group exists
if GROUP_ID=$(az ad group show --group "$TEST_GROUP_NAME" --query id -o tsv 2>/dev/null); then
    write_log "Found test group: $TEST_GROUP_NAME" "INFO"

    for USER_INFO in "${TEST_USERS[@]}"; do
        IFS=':' read -r EMAIL _ _ <<< "$USER_INFO"

        # Get user ID
        USER_ID=$(az ad user show --id "$EMAIL" --query id -o tsv 2>/dev/null || true)

        if [[ -n "$USER_ID" ]]; then
            # Add user to group
            if az ad group member add --group "$GROUP_ID" --member-id "$USER_ID" &>/dev/null; then
                write_log "Added $EMAIL to test group" "INFO"
            else
                write_log "User may already be in group: $EMAIL" "WARNING"
            fi
        fi
    done
else
    write_log "Test group does not exist: $TEST_GROUP_NAME" "WARNING"
    write_log "Creating test group..." "INFO"

    if az ad group create --display-name "$TEST_GROUP_NAME" --mail-nickname "b2c-test-users" --output none; then
        write_success "Created test group: $TEST_GROUP_NAME"
    else
        write_log "Failed to create test group" "WARNING"
    fi
fi

# Summary
write_step "Provisioning Summary"
write_log "Users created: $USERS_CREATED" "INFO"
write_log "Users failed: $USERS_FAILED" "INFO"

if [[ $USERS_FAILED -eq 0 ]]; then
    write_success "Test user provisioning completed"
else
    write_log "Some users failed to provision" "WARNING"
fi

write_log "Test users provisioned for B2C tenant" "INFO"
