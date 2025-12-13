#!/usr/bin/env pwsh
# Hartonomous Shader Build Script

$ErrorActionPreference = "Stop"

Write-Host "=== Building Hartonomous Shader ===" -ForegroundColor Cyan

# Check Rust installation
Write-Host "Checking Rust installation..." -ForegroundColor Yellow
try {
    $rustVersion = cargo --version
    Write-Host "  ✓ $rustVersion" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Rust not found. Install from https://rustup.rs" -ForegroundColor Red
    exit 1
}

# Navigate to shader directory
Set-Location shader

# Build release
Write-Host "`nBuilding Shader (release mode)..." -ForegroundColor Yellow
cargo build --release

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Build successful" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build failed" -ForegroundColor Red
    exit 1
}

# Run tests
Write-Host "`nRunning tests..." -ForegroundColor Yellow
cargo test

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ All tests passed" -ForegroundColor Green
} else {
    Write-Host "  ✗ Tests failed" -ForegroundColor Red
    exit 1
}

Set-Location ..

Write-Host "`n=== Shader Build Complete ===" -ForegroundColor Cyan
Write-Host "Binary: shader/target/release/hartonomous-shader" -ForegroundColor Gray
