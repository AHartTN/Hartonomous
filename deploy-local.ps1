# Complete Local Deployment Test on HART-DESKTOP
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Hartonomous Local Deployment (HART-DESKTOP)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Load environment variables from .env
Write-Host "Loading credentials from .env..." -ForegroundColor Cyan
Get-Content ".env" | ForEach-Object {
    if ($_ -match '^([^#][^=]+)=(.*)$') {
        $key = $matches[1].Trim()
        $value = $matches[2].Trim()
        Set-Item -Path "env:$key" -Value $value
    }
}

# Set Azure credentials for deployment scripts
# These should be loaded from environment variables or .env file
$env:AZURE_TENANT_ID = $env:AZURE_TENANT_ID ?? ""
$env:AZURE_CLIENT_ID = $env:AZURE_CLIENT_ID ?? ""
$env:AZURE_CLIENT_SECRET = $env:AZURE_CLIENT_SECRET ?? ""
$env:AZURE_SUBSCRIPTION_ID = $env:AZURE_SUBSCRIPTION_ID ?? ""

# Set deployment-specific variables
$env:LOG_LEVEL = "INFO"
$env:DEPLOYMENT_ENVIRONMENT = "development"

Write-Host "Environment configured for HART-DESKTOP" -ForegroundColor Green
Write-Host ""

# Step 1: Preflight Checks
Write-Host "[1/5] Running Preflight Checks..." -ForegroundColor Yellow
& ".\deployment\scripts\preflight\check-prerequisites.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Preflight checks failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ Preflight checks passed!" -ForegroundColor Green
Write-Host ""

# Step 2: Database Deployment
Write-Host "[2/5] Deploying Database Schema..." -ForegroundColor Yellow
& ".\deployment\scripts\database\deploy-schema.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Database deployment failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ Database schema deployed!" -ForegroundColor Green
Write-Host ""

# Step 3: Application Deployment
Write-Host "[3/5] Deploying API Application..." -ForegroundColor Yellow
& ".\deployment\scripts\application\deploy-api.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "API deployment failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ API application deployed!" -ForegroundColor Green
Write-Host ""

# Step 4: Neo4j Worker Deployment
Write-Host "[4/5] Deploying Neo4j Worker..." -ForegroundColor Yellow
& ".\deployment\scripts\neo4j\deploy-neo4j-worker.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Neo4j worker deployment failed! Aborting." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ Neo4j worker deployed!" -ForegroundColor Green
Write-Host ""

# Step 5: Health Checks
Write-Host "[5/5] Running Health Checks..." -ForegroundColor Yellow
& ".\deployment\scripts\validation\health-check.ps1"

# Health checks are informational - don't abort on failure

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To start the API:" -ForegroundColor Yellow
Write-Host "  cd api" -ForegroundColor White
Write-Host "  python -m uvicorn main:app --reload" -ForegroundColor White
Write-Host ""
Write-Host "API will be available at:" -ForegroundColor Yellow
Write-Host "  http://localhost:8000" -ForegroundColor White
Write-Host "  http://localhost:8000/docs (API documentation)" -ForegroundColor White
Write-Host ""
