#!/bin/bash
# Azure AD B2C Policy Deployment Script
# Deploys custom policies to Azure AD B2C
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

set -euo pipefail

# Import common modules
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../common/logger.sh"
source "$SCRIPT_DIR/../common/azure-auth.sh"

# Initialize logger
initialize_logger "${LOG_LEVEL:-INFO}"

write_step "Azure AD B2C Policy Deployment"

# Azure AD B2C Configuration
B2C_TENANT_NAME="${B2C_TENANT_NAME:-}"
B2C_TENANT_ID="${AZURE_AD_B2C_TENANT_ID:-}"

if [[ -z "$B2C_TENANT_NAME" ]]; then
    write_failure "B2C_TENANT_NAME environment variable not set"
fi

if [[ -z "$B2C_TENANT_ID" ]]; then
    write_failure "AZURE_AD_B2C_TENANT_ID environment variable not set"
fi

write_log "B2C Tenant: $B2C_TENANT_NAME.onmicrosoft.com" "INFO"

# Authenticate to Azure
write_step "Authenticating to Azure"
connect_azure_service_principal \
    "$AZURE_TENANT_ID" \
    "$AZURE_CLIENT_ID" \
    "$AZURE_CLIENT_SECRET" \
    "${AZURE_SUBSCRIPTION_ID:-}"

# B2C Policies directory
POLICIES_DIR="$SCRIPT_DIR/../../../ciam/b2c-policies"

if [[ ! -d "$POLICIES_DIR" ]]; then
    write_failure "B2C policies directory not found: $POLICIES_DIR

Create the directory and add your custom B2C policy XML files:
  - TrustFrameworkBase.xml
  - TrustFrameworkExtensions.xml  
  - SignUpOrSignin.xml
  - PasswordReset.xml
  - ProfileEdit.xml

Refer to Azure documentation:
https://docs.microsoft.com/azure/active-directory-b2c/custom-policy-get-started"
fi

# Find policy files
write_step "Discovering Policy Files"

if ! find "$POLICIES_DIR" -name "*.xml" -type f | grep -q .; then
    write_failure "No XML policy files found in: $POLICIES_DIR

Add your custom policy XML files to this directory before deploying."
fi

POLICY_FILES=($(find "$POLICIES_DIR" -name "*.xml" -type f | sort))
POLICY_COUNT=${#POLICY_FILES[@]}
write_log "Found $POLICY_COUNT policy files" "INFO"

# Deploy policies in order
# Order matters: Base -> Extensions -> Relying Party policies
DEPLOYMENT_ORDER=(
    "TrustFrameworkBase.xml"
    "TrustFrameworkLocalization.xml"
    "TrustFrameworkExtensions.xml"
    "SignUpOrSignin.xml"
    "ProfileEdit.xml"
    "PasswordReset.xml"
)

POLICIES_DEPLOYED=0
POLICIES_FAILED=0
POLICIES_SKIPPED=0

write_step "Deploying B2C Policies"

for POLICY_NAME in "${DEPLOYMENT_ORDER[@]}"; do
    POLICY_FILE="$POLICIES_DIR/$POLICY_NAME"

    if [[ ! -f "$POLICY_FILE" ]]; then
        write_log "Policy not found, skipping: $POLICY_NAME" "INFO"
        ((POLICIES_SKIPPED++))
        continue
    fi

    write_log "Deploying policy: $POLICY_NAME" "INFO"

    # Validate XML before deploying
    if command -v xmllint &>/dev/null; then
        if ! xmllint --noout "$POLICY_FILE" 2>/dev/null; then
            write_log "Invalid XML in policy file: $POLICY_NAME" "ERROR"
            ((POLICIES_FAILED++))
            continue
        fi
    fi

    # Extract policy ID from XML
    POLICY_ID=$(grep -oP '(?<=PolicyId=")[^"]+' "$POLICY_FILE" | head -1 || echo "")

    if [[ -z "$POLICY_ID" ]]; then
        write_log "Could not extract PolicyId from: $POLICY_NAME" "ERROR"
        write_log "Ensure the XML contains a valid PolicyId attribute" "ERROR"
        ((POLICIES_FAILED++))
        continue
    fi

    write_log "Policy ID: $POLICY_ID" "DEBUG"

    # Upload policy using Microsoft Graph API
    if az rest --method PUT \
        --url "https://graph.microsoft.com/v1.0/trustFramework/policies/$POLICY_ID/\$value" \
        --headers "Content-Type=application/xml" \
        --body "@$POLICY_FILE" \
        --output none 2>&1 | tee -a "$LOG_FILE"; then

        write_success "Deployed policy: $POLICY_NAME ($POLICY_ID)"
        ((POLICIES_DEPLOYED++))
    else
        EXIT_CODE=$?
        write_log "Failed to deploy policy: $POLICY_NAME (exit code: $EXIT_CODE)" "ERROR"
        write_log "Check that the service principal has TrustFrameworkKeySet.ReadWrite.All permission" "ERROR"
        ((POLICIES_FAILED++))
    fi
done

# Verify deployed policies
write_step "Verifying Deployed Policies"

if DEPLOYED_POLICIES=$(az rest --method GET \
    --url "https://graph.microsoft.com/v1.0/trustFramework/policies" \
    --query "value[].id" -o tsv 2>/dev/null); then
    
    if [[ -n "$DEPLOYED_POLICIES" ]]; then
        DEPLOYED_COUNT=$(echo "$DEPLOYED_POLICIES" | wc -l)
        write_success "Found $DEPLOYED_COUNT policies in B2C tenant"

        write_log "Deployed policies:" "INFO"
        echo "$DEPLOYED_POLICIES" | while read -r POLICY; do
            write_log "  - $POLICY" "INFO"
        done
    else
        write_log "No policies found in B2C tenant" "WARNING"
    fi
else
    write_log "Could not retrieve deployed policies from Graph API" "ERROR"
    write_log "Verify the service principal has TrustFrameworkKeySet.Read.All permission" "ERROR"
fi

# Summary
write_step "Deployment Summary"
write_log "Policies deployed: $POLICIES_DEPLOYED" "INFO"
write_log "Policies skipped: $POLICIES_SKIPPED" "INFO"
write_log "Policies failed: $POLICIES_FAILED" "INFO"

if [[ $POLICIES_FAILED -eq 0 ]]; then
    write_success "B2C policy deployment completed successfully"
    exit 0
else
    write_failure "B2C policy deployment failed ($POLICIES_FAILED policies failed)"
fi
