#!/bin/bash
# CIAM Configuration Verification Script
# Verifies Azure AD B2C configuration
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "CIAM Configuration Verification"

# Azure AD B2C Configuration
B2C_TENANT_NAME="${B2C_TENANT_NAME:-hartonomous}"
B2C_TENANT_ID="${AZURE_AD_B2C_TENANT_ID:-}"

if [[ -z "$B2C_TENANT_ID" ]]; then
    write_failure "AZURE_AD_B2C_TENANT_ID environment variable not set"
fi

write_log "B2C Tenant: $B2C_TENANT_NAME.onmicrosoft.com" "INFO"
write_log "Tenant ID: $B2C_TENANT_ID" "INFO"

# Authenticate to Azure
write_step "Authenticating to Azure"
azure_login

CHECKS_PASSED=0
CHECKS_FAILED=0

# Check 1: Verify B2C Tenant Access
write_step "Check 1: B2C Tenant Access"

if az rest --method GET \
    --url "https://graph.microsoft.com/v1.0/organization/$B2C_TENANT_ID" \
    --output none 2>/dev/null; then

    write_success "B2C tenant access confirmed"
    ((CHECKS_PASSED++))
else
    write_log "Cannot access B2C tenant" "ERROR"
    ((CHECKS_FAILED++))
fi

# Check 2: Verify User Flows
write_step "Check 2: User Flows"

USER_FLOWS=$(az rest --method GET \
    --url "https://graph.microsoft.com/beta/identity/b2cUserFlows" \
    --query "value[].id" -o tsv 2>/dev/null || echo "")

if [[ -n "$USER_FLOWS" ]]; then
    FLOW_COUNT=$(echo "$USER_FLOWS" | wc -l)
    write_success "Found $FLOW_COUNT user flows"
    ((CHECKS_PASSED++))

    echo "$USER_FLOWS" | while read -r FLOW; do
        write_log "  - $FLOW" "INFO"
    done
else
    write_log "No user flows found (may be using custom policies)" "WARNING"
fi

# Check 3: Verify Custom Policies
write_step "Check 3: Custom Policies"

POLICIES=$(az rest --method GET \
    --url "https://graph.microsoft.com/beta/trustFramework/policies" \
    --query "value[].id" -o tsv 2>/dev/null || echo "")

if [[ -n "$POLICIES" ]]; then
    POLICY_COUNT=$(echo "$POLICIES" | wc -l)
    write_success "Found $POLICY_COUNT custom policies"
    ((CHECKS_PASSED++))

    echo "$POLICIES" | while read -r POLICY; do
        write_log "  - $POLICY" "INFO"
    done
else
    write_log "No custom policies found" "WARNING"
fi

# Check 4: Verify Application Registrations
write_step "Check 4: Application Registrations"

APPS=$(az ad app list --query "[?contains(displayName, 'Hartonomous') || contains(displayName, 'hartonomous')].{name:displayName, id:appId}" -o json)

if [[ "$APPS" != "[]" ]]; then
    APP_COUNT=$(echo "$APPS" | jq 'length')
    write_success "Found $APP_COUNT Hartonomous app registrations"
    ((CHECKS_PASSED++))

    echo "$APPS" | jq -r '.[] | "  - \(.name) (\(.id))"' | while read -r LINE; do
        write_log "$LINE" "INFO"
    done
else
    write_log "No Hartonomous app registrations found" "WARNING"
    ((CHECKS_FAILED++))
fi

# Check 5: Verify API Permissions
write_step "Check 5: API Permissions"

if [[ "$APPS" != "[]" ]]; then
    APP_ID=$(echo "$APPS" | jq -r '.[0].id')

    API_PERMISSIONS=$(az ad app show --id "$APP_ID" --query "requiredResourceAccess[].resourceAppId" -o tsv 2>/dev/null || echo "")

    if [[ -n "$API_PERMISSIONS" ]]; then
        PERM_COUNT=$(echo "$API_PERMISSIONS" | wc -l)
        write_success "App has $PERM_COUNT API permission scopes"
        ((CHECKS_PASSED++))
    else
        write_log "No API permissions configured" "WARNING"
    fi
fi

# Check 6: Verify Identity Providers
write_step "Check 6: Identity Providers"

IDP_COUNT=$(az rest --method GET \
    --url "https://graph.microsoft.com/beta/identity/identityProviders" \
    --query "value | length(@)" -o tsv 2>/dev/null || echo "0")

if [[ "$IDP_COUNT" -gt 0 ]]; then
    write_success "Found $IDP_COUNT identity providers"
    ((CHECKS_PASSED++))
else
    write_log "No external identity providers configured" "INFO"
fi

# Check 7: Verify Test Users
write_step "Check 7: Test Users"

TEST_USERS=$(az ad user list --query "[?contains(userPrincipalName, 'testuser')].userPrincipalName" -o tsv 2>/dev/null || echo "")

if [[ -n "$TEST_USERS" ]]; then
    TEST_USER_COUNT=$(echo "$TEST_USERS" | wc -l)
    write_success "Found $TEST_USER_COUNT test users"

    echo "$TEST_USERS" | while read -r USER; do
        write_log "  - $USER" "INFO"
    done
else
    write_log "No test users found" "INFO"
fi

# Check 8: Verify B2C Extensions
write_step "Check 8: B2C Extensions App"

EXTENSIONS_APP=$(az ad app list --query "[?displayName=='b2c-extensions-app'].{name:displayName, id:appId}" -o json)

if [[ "$EXTENSIONS_APP" != "[]" ]]; then
    write_success "B2C extensions app found"
    ((CHECKS_PASSED++))
else
    write_log "B2C extensions app not found (required for custom attributes)" "WARNING"
fi

# Summary
write_step "Verification Summary"
TOTAL_CHECKS=$((CHECKS_PASSED + CHECKS_FAILED))

write_log "Total checks: $TOTAL_CHECKS" "INFO"
write_log "Checks passed: $CHECKS_PASSED" "INFO"
write_log "Checks failed: $CHECKS_FAILED" "INFO"

if [[ $CHECKS_FAILED -eq 0 ]]; then
    write_success "CIAM configuration verified"
else
    write_log "Some configuration checks failed" "WARNING"
fi

write_log "CIAM configuration verification completed" "INFO"
