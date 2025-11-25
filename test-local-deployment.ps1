# Test Local Deployment on HART-DESKTOP
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

Write-Host "==========================================='" -ForegroundColor Cyan
Write-Host "Testing Hartonomous Deployment (HART-DESKTOP)" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Set environment variables
$env:LOG_LEVEL = "INFO"
$env:DEPLOYMENT_ENVIRONMENT = "development"
$env:PGHOST = "localhost"
$env:PGPORT = "5432"
$env:PGDATABASE = "hartonomous"
$env:PGUSER = "postgres"

Write-Host "Environment Configuration:" -ForegroundColor Yellow
Write-Host "  DEPLOYMENT_ENVIRONMENT: $env:DEPLOYMENT_ENVIRONMENT" -ForegroundColor Gray
Write-Host "  LOG_LEVEL: $env:LOG_LEVEL" -ForegroundColor Gray
Write-Host "  Database: $env:PGHOST:$env:PGPORT/$env:PGDATABASE" -ForegroundColor Gray
Write-Host ""

# Step 1: Run preflight checks
Write-Host "[Step 1/5] Running Preflight Checks..." -ForegroundColor Cyan
& ".\deployment\scripts\preflight\check-prerequisites.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "✗ Preflight checks failed!" -ForegroundColor Red
    Write-Host "Please resolve the issues above before proceeding." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "✓ Preflight checks passed!" -ForegroundColor Green
Write-Host ""

# Summary
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host "Local Deployment Test Complete!" -ForegroundColor Green
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Ensure PostgreSQL is running on localhost:5432" -ForegroundColor White
Write-Host "2. Ensure Neo4j Desktop is running" -ForegroundColor White
Write-Host "3. Run database deployment:" -ForegroundColor White
Write-Host "   .\deployment\scripts\database\deploy-schema.ps1" -ForegroundColor Cyan
Write-Host "4. Run application deployment:" -ForegroundColor White
Write-Host "   .\deployment\scripts\application\deploy-api.ps1" -ForegroundColor Cyan
Write-Host ""
