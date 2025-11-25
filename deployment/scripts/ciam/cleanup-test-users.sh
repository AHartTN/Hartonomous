#!/bin/bash
# CIAM Test User Cleanup Script
# Removes test users from Azure AD B2C
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "CIAM Test User Cleanup"

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

# Test Users to delete
declare -a TEST_USERS=(
    "testuser1@$B2C_TENANT_NAME.onmicrosoft.com"
    "testuser2@$B2C_TENANT_NAME.onmicrosoft.com"
    "testadmin@$B2C_TENANT_NAME.onmicrosoft.com"
)

USERS_DELETED=0
USERS_FAILED=0

# Delete test users
write_step "Deleting Test Users"

for EMAIL in "${TEST_USERS[@]}"; do
    write_log "Deleting user: $EMAIL" "INFO"

    # Check if user exists
    if ! az ad user show --id "$EMAIL" &>/dev/null; then
        write_log "User does not exist: $EMAIL" "WARNING"
        continue
    fi

    # Delete user
    if az ad user delete --id "$EMAIL"; then
        write_success "Deleted user: $EMAIL"
        ((USERS_DELETED++))
    else
        write_log "Failed to delete user: $EMAIL" "ERROR"
        ((USERS_FAILED++))
    fi
done

# Clean up test group if empty
write_step "Cleaning Up Test Group"

TEST_GROUP_NAME="B2C-Test-Users"

if GROUP_ID=$(az ad group show --group "$TEST_GROUP_NAME" --query id -o tsv 2>/dev/null); then
    write_log "Found test group: $TEST_GROUP_NAME" "INFO"

    # Check if group has members
    MEMBER_COUNT=$(az ad group member list --group "$GROUP_ID" --query "length(@)" -o tsv)

    if [[ "$MEMBER_COUNT" -eq 0 ]]; then
        write_log "Test group is empty, deleting..." "INFO"

        if az ad group delete --group "$GROUP_ID"; then
            write_success "Deleted test group: $TEST_GROUP_NAME"
        else
            write_log "Failed to delete test group" "WARNING"
        fi
    else
        write_log "Test group still has $MEMBER_COUNT members, keeping group" "INFO"
    fi
else
    write_log "Test group does not exist: $TEST_GROUP_NAME" "INFO"
fi

# Summary
write_step "Cleanup Summary"
write_log "Users deleted: $USERS_DELETED" "INFO"
write_log "Users failed: $USERS_FAILED" "INFO"

if [[ $USERS_FAILED -eq 0 ]]; then
    write_success "Test user cleanup completed"
else
    write_log "Some users failed to delete" "WARNING"
fi

write_log "Test user cleanup completed for B2C tenant" "INFO"
