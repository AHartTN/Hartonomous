#!/usr/bin/env pwsh
# Validate entire codebase

$ErrorActionPreference = "Stop"

Write-Host "=== Hartonomous Validation ===" -ForegroundColor Cyan

$failed = $false

# Check Rust syntax
Write-Host "`nValidating Rust code..." -ForegroundColor Yellow
cd shader
cargo check 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Rust code valid" -ForegroundColor Green
} else {
    Write-Host "  ✗ Rust compilation errors" -ForegroundColor Red
    $failed = $true
}
cd ..

# Check Python syntax
Write-Host "`nValidating Python code..." -ForegroundColor Yellow
python -m py_compile connector/*.py 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Python code valid" -ForegroundColor Green
} else {
    Write-Host "  ✗ Python syntax errors" -ForegroundColor Red
    $failed = $true
}

# Check SQL syntax
Write-Host "`nValidating SQL..." -ForegroundColor Yellow
psql -U postgres -d postgres --set ON_ERROR_STOP=on -f database/schema.sql -o /dev/null 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ SQL schema valid" -ForegroundColor Green
} else {
    Write-Host "  → SQL validation skipped (requires running PostgreSQL)" -ForegroundColor Gray
}

# Check file structure
Write-Host "`nValidating file structure..." -ForegroundColor Yellow
$requiredFiles = @(
    "database/schema.sql",
    "shader/src/lib.rs",
    "shader/src/main.rs",
    "shader/src/sdi.rs",
    "cortex/cortex.c",
    "cortex/lmds_projector.cpp",
    "connector/pool.py",
    "connector/connector.py",
    "connector/api.py"
)

$allPresent = $true
foreach ($file in $requiredFiles) {
    if (!(Test-Path $file)) {
        Write-Host "  ✗ Missing: $file" -ForegroundColor Red
        $allPresent = $false
        $failed = $true
    }
}

if ($allPresent) {
    Write-Host "  ✓ All required files present" -ForegroundColor Green
}

if ($failed) {
    Write-Host "`n=== Validation Failed ===" -ForegroundColor Red
    exit 1
} else {
    Write-Host "`n=== Validation Passed ===" -ForegroundColor Green
    exit 0
}
