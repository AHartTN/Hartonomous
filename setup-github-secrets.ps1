# Setup GitHub Secrets Script
# Configures all required secrets for CI/CD
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

Write-Host "Setting up GitHub Secrets for Hartonomous..." -ForegroundColor Cyan
Write-Host ""

# Azure Configuration - READ FROM ENVIRONMENT OR PROMPT USER
$AZURE_SUBSCRIPTION_ID = Read-Host "Enter Azure Subscription ID"
$AZURE_TENANT_ID = Read-Host "Enter Azure Tenant ID"
$KEY_VAULT_URL = Read-Host "Enter Key Vault URL (e.g., https://kv-name.vault.azure.net)"

# Development Service Principal
$AZURE_CLIENT_ID = Read-Host "Enter Azure Client ID"
$AZURE_CLIENT_SECRET = Read-Host "Enter Azure Client Secret" -AsSecureString
$AZURE_CLIENT_SECRET_PLAIN = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($AZURE_CLIENT_SECRET))

Write-Host ""
Write-Host "Configuring repository secrets..." -ForegroundColor Yellow

# Set repository secrets
gh secret set AZURE_SUBSCRIPTION_ID --body $AZURE_SUBSCRIPTION_ID --repo AHartTN/Hartonomous
gh secret set AZURE_TENANT_ID --body $AZURE_TENANT_ID --repo AHartTN/Hartonomous
gh secret set KEY_VAULT_URL --body $KEY_VAULT_URL --repo AHartTN/Hartonomous
gh secret set AZURE_CLIENT_ID --body $AZURE_CLIENT_ID --repo AHartTN/Hartonomous
gh secret set AZURE_CLIENT_SECRET --body $AZURE_CLIENT_SECRET_PLAIN --repo AHartTN/Hartonomous

# Clear sensitive variable
$AZURE_CLIENT_SECRET_PLAIN = $null

Write-Host ""
Write-Host "✓ GitHub secrets configured successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Configured secrets:" -ForegroundColor White
Write-Host "  - AZURE_SUBSCRIPTION_ID" -ForegroundColor Gray
Write-Host "  - AZURE_TENANT_ID" -ForegroundColor Gray
Write-Host "  - KEY_VAULT_URL" -ForegroundColor Gray
Write-Host "  - AZURE_CLIENT_ID (Development SP)" -ForegroundColor Gray
Write-Host "  - AZURE_CLIENT_SECRET (Development SP)" -ForegroundColor Gray
Write-Host ""
