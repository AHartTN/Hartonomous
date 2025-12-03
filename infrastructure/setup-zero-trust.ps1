#!/usr/bin/env pwsh
# Complete Zero Trust setup automation - Run this once

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Hartonomous Zero Trust Setup" -ForegroundColor Cyan
Write-Host "Complete automated configuration" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# Step 1: Populate Key Vault
Write-Host "[1/2] Populating Key Vault with secrets..." -ForegroundColor Yellow
& "$scriptDir\setup-keyvault-secrets.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Key Vault setup failed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 2: Deploy RBAC
Write-Host "[2/2] Deploying RBAC role assignments..." -ForegroundColor Yellow
& "$scriptDir\deploy-rbac.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: RBAC deployment failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Zero Trust Setup Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration Summary:" -ForegroundColor Yellow
Write-Host "  ✓ Key Vault secrets configured" -ForegroundColor Green
Write-Host "  ✓ RBAC permissions assigned" -ForegroundColor Green
Write-Host "  ✓ Managed identities configured" -ForegroundColor Green
Write-Host "  ✓ Multi-tenant isolation enabled" -ForegroundColor Green
Write-Host ""
Write-Host "Your Arc machines can now:" -ForegroundColor Yellow
Write-Host "  - Authenticate via managed identity (no passwords)" -ForegroundColor Gray
Write-Host "  - Access Key Vault for secrets" -ForegroundColor Gray
Write-Host "  - Access App Configuration for settings" -ForegroundColor Gray
Write-Host "  - Connect to PostgreSQL with Azure AD tokens" -ForegroundColor Gray
Write-Host ""
Write-Host "Next: Deploy your application!" -ForegroundColor Yellow
Write-Host "  ./scripts/deploy-migrations.ps1 -Environment dev" -ForegroundColor Gray
