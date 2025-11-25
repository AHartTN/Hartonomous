# Quick Test Script for Deployment System
# Run this to test the deployment infrastructure

param(
    [switch]$SkipAzure
)

Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Hartonomous Deployment System - Quick Test" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Set environment variables for testing
$env:DEPLOYMENT_ENVIRONMENT = "development"
$env:LOG_LEVEL = "INFO"

if (-not $SkipAzure) {
    Write-Host "⚠️  Azure credentials required for full test" -ForegroundColor Yellow
    Write-Host "   Set these environment variables:" -ForegroundColor Yellow
    Write-Host "   - AZURE_TENANT_ID" -ForegroundColor Yellow
    Write-Host "   - AZURE_CLIENT_ID" -ForegroundColor Yellow
    Write-Host "   - AZURE_CLIENT_SECRET" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   Or run with: .\test-deployment.ps1 -SkipAzure" -ForegroundColor Yellow
    Write-Host ""
}

# Test 1: Check if files exist
Write-Host "Test 1: Checking deployment files..." -ForegroundColor Cyan
$requiredFiles = @(
    ".\deployment\scripts\common\logger.ps1",
    ".\deployment\scripts\common\config-loader.ps1",
    ".\deployment\scripts\preflight\check-prerequisites.ps1",
    ".\deployment\scripts\validation\health-check.ps1",
    ".\deployment\config\development.json"
)

$allExist = $true
foreach ($file in $requiredFiles) {
    if (Test-Path $file) {
        Write-Host "  ✅ $file" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $file MISSING" -ForegroundColor Red
        $allExist = $false
    }
}

if (-not $allExist) {
    Write-Host ""
    Write-Host "❌ Some deployment files are missing!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✅ All deployment files present" -ForegroundColor Green
Write-Host ""

# Test 2: Load modules
Write-Host "Test 2: Loading PowerShell modules..." -ForegroundColor Cyan
try {
    . .\deployment\scripts\common\logger.ps1
    Write-Host "  ✅ Logger module loaded" -ForegroundColor Green

    . .\deployment\scripts\common\config-loader.ps1
    Write-Host "  ✅ Config loader module loaded" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Failed to load modules: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✅ All modules loaded successfully" -ForegroundColor Green
Write-Host ""

# Test 3: Load configuration
Write-Host "Test 3: Loading development configuration..." -ForegroundColor Cyan
try {
    $config = Get-DeploymentConfig -Environment "development"
    Write-Host "  ✅ Configuration loaded" -ForegroundColor Green
    Write-Host "     Environment: $($config.environment)" -ForegroundColor Gray
    Write-Host "     Target: $($config.target.machine)" -ForegroundColor Gray
    Write-Host "     Database: $($config.database.name)" -ForegroundColor Gray
} catch {
    Write-Host "  ❌ Failed to load config: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✅ Configuration valid" -ForegroundColor Green
Write-Host ""

# Test 4: Check GitHub workflows
Write-Host "Test 4: Checking GitHub Actions workflows..." -ForegroundColor Cyan
$workflows = Get-ChildItem ".\.github\workflows\*.yml" -ErrorAction SilentlyContinue
if ($workflows) {
    Write-Host "  ✅ Found $($workflows.Count) workflows" -ForegroundColor Green
    foreach ($wf in $workflows | Select-Object -First 5) {
        Write-Host "     - $($wf.Name)" -ForegroundColor Gray
    }
    if ($workflows.Count -gt 5) {
        Write-Host "     ... and $($workflows.Count - 5) more" -ForegroundColor Gray
    }
} else {
    Write-Host "  ❌ No workflows found" -ForegroundColor Red
}

Write-Host ""

# Summary
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Test Summary" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "✅ Deployment infrastructure is ready!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Set Azure credentials (if not using -SkipAzure)" -ForegroundColor White
Write-Host "2. Run preflight checks:" -ForegroundColor White
Write-Host "   .\deployment\scripts\preflight\check-prerequisites.ps1" -ForegroundColor Cyan
Write-Host "3. Start API and run health checks:" -ForegroundColor White
Write-Host "   .\deployment\scripts\validation\health-check.ps1" -ForegroundColor Cyan
Write-Host "4. Review deployment guide:" -ForegroundColor White
Write-Host "   .\docs\deployment\QUICK-START.md" -ForegroundColor Cyan
Write-Host ""
