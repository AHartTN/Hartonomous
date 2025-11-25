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
B2C_TENANT_NAME="${B2C_TENANT_NAME:-hartonomous}"
B2C_TENANT_ID="${AZURE_AD_B2C_TENANT_ID:-}"

if [[ -z "$B2C_TENANT_ID" ]]; then
    write_failure "AZURE_AD_B2C_TENANT_ID environment variable not set"
fi

write_log "B2C Tenant: $B2C_TENANT_NAME.onmicrosoft.com" "INFO"

# Authenticate to Azure
write_step "Authenticating to Azure"
azure_login

# B2C Policies directory
POLICIES_DIR="$SCRIPT_DIR/../../../ciam/b2c-policies"

if [[ ! -d "$POLICIES_DIR" ]]; then
    write_log "B2C policies directory not found: $POLICIES_DIR" "WARNING"
    write_log "Creating policies directory..." "INFO"
    mkdir -p "$POLICIES_DIR"

    # Create sample policy structure
    cat > "$POLICIES_DIR/README.md" <<EOF
# Azure AD B2C Custom Policies

This directory contains custom policies for Azure AD B2C.

## Policy Structure

- TrustFrameworkBase.xml: Base policy with common settings
- TrustFrameworkExtensions.xml: Extensions to base policy
- SignUpOrSignin.xml: Sign-up or sign-in policy
- PasswordReset.xml: Password reset policy
- ProfileEdit.xml: Profile edit policy

## Deployment

Policies are deployed automatically by the CI/CD pipeline.

To deploy manually:
\`\`\`bash
./deployment/scripts/ciam/deploy-b2c-policies.sh
\`\`\`

## References

- [Azure AD B2C Custom Policies](https://docs.microsoft.com/azure/active-directory-b2c/custom-policy-overview)
- [Custom Policy Starter Pack](https://github.com/Azure-Samples/active-directory-b2c-custom-policy-starterpack)
EOF

    write_success "Created policies directory structure"
    write_log "Add your custom policies to: $POLICIES_DIR" "INFO"
    exit 0
fi

# Find policy files
write_step "Discovering Policy Files"

POLICY_FILES=$(find "$POLICIES_DIR" -name "*.xml" -type f | sort)

if [[ -z "$POLICY_FILES" ]]; then
    write_log "No policy files found in: $POLICIES_DIR" "WARNING"
    exit 0
fi

POLICY_COUNT=$(echo "$POLICY_FILES" | wc -l)
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

write_step "Deploying B2C Policies"

for POLICY_NAME in "${DEPLOYMENT_ORDER[@]}"; do
    POLICY_FILE="$POLICIES_DIR/$POLICY_NAME"

    if [[ ! -f "$POLICY_FILE" ]]; then
        write_log "Policy not found, skipping: $POLICY_NAME" "INFO"
        continue
    fi

    write_log "Deploying policy: $POLICY_NAME" "INFO"

    # Extract policy ID from XML
    POLICY_ID=$(grep -oP '(?<=PolicyId=")[^"]+' "$POLICY_FILE" | head -1 || echo "")

    if [[ -z "$POLICY_ID" ]]; then
        write_log "Could not extract PolicyId from: $POLICY_NAME" "WARNING"
        continue
    fi

    # Upload policy using Microsoft Graph API
    if az rest --method PUT \
        --url "https://graph.microsoft.com/beta/trustFramework/policies/$POLICY_ID/\$value" \
        --headers "Content-Type=application/xml" \
        --body "@$POLICY_FILE" \
        --output none 2>&1; then

        write_success "Deployed policy: $POLICY_NAME ($POLICY_ID)"
        ((POLICIES_DEPLOYED++))
    else
        write_log "Failed to deploy policy: $POLICY_NAME" "ERROR"
        ((POLICIES_FAILED++))
    fi
done

# Verify deployed policies
write_step "Verifying Deployed Policies"

DEPLOYED_POLICIES=$(az rest --method GET \
    --url "https://graph.microsoft.com/beta/trustFramework/policies" \
    --query "value[].id" -o tsv 2>/dev/null || echo "")

if [[ -n "$DEPLOYED_POLICIES" ]]; then
    DEPLOYED_COUNT=$(echo "$DEPLOYED_POLICIES" | wc -l)
    write_success "Found $DEPLOYED_COUNT policies in B2C tenant"

    write_log "Deployed policies:" "INFO"
    echo "$DEPLOYED_POLICIES" | while read -r POLICY; do
        write_log "  - $POLICY" "INFO"
    done
else
    write_log "Could not retrieve deployed policies" "WARNING"
fi

# Summary
write_step "Deployment Summary"
write_log "Policies deployed: $POLICIES_DEPLOYED" "INFO"
write_log "Policies failed: $POLICIES_FAILED" "INFO"

if [[ $POLICIES_FAILED -eq 0 ]]; then
    write_success "B2C policy deployment completed"
else
    write_log "Some policies failed to deploy" "WARNING"
fi

write_log "B2C policy deployment completed" "INFO"
