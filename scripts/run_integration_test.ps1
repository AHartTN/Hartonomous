#!/usr/bin/env pwsh
# End-to-End Integration Test

$ErrorActionPreference = "Stop"

Write-Host "=== Hartonomous Integration Test ===" -ForegroundColor Cyan

# 1. Database setup
Write-Host "`n[1/5] Setting up database..." -ForegroundColor Yellow
.\scripts\setup_database.ps1
if ($LASTEXITCODE -ne 0) { exit 1 }

# 2. Populate test data
Write-Host "`n[2/5] Populating test data..." -ForegroundColor Yellow
psql -U postgres -d hartonomous -f database\test_data.sql
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ Test data population failed" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Test data loaded" -ForegroundColor Green

# 3. Build components
Write-Host "`n[3/5] Building Shader..." -ForegroundColor Yellow
.\scripts\build_shader.ps1
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "`n[4/5] Building Cortex..." -ForegroundColor Yellow
if ($IsLinux -or $IsMacOS) {
    .\scripts\build_cortex.ps1
    if ($LASTEXITCODE -ne 0) { exit 1 }
} else {
    Write-Host "  → Skipping Cortex build on Windows" -ForegroundColor Gray
}

# 5. Run Python tests
Write-Host "`n[5/5] Running connector tests..." -ForegroundColor Yellow
cd connector
pip install -q -r requirements.txt
cd ..

python -m unittest discover -s tests -p "test_*.py" -v
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ Tests failed" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ All tests passed" -ForegroundColor Green

# Run examples
Write-Host "`nRunning examples..." -ForegroundColor Yellow
python examples\basic_inference.py

Write-Host "`n=== Integration Test Complete ===" -ForegroundColor Cyan
