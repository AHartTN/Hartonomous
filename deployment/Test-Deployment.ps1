# Test-Deployment.ps1
# Validates deployment health
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment
)

$ErrorActionPreference = "Stop"

Write-Host "Validating $Environment deployment..." -ForegroundColor Cyan

# Load config
$configPath = Join-Path $PSScriptRoot "config\$Environment.json"
$config = Get-Content $configPath | ConvertFrom-Json

# Check API
$apiUrl = "http://$($config.api.host):$($config.api.port)/health"
Write-Host "Checking API: $apiUrl" -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri $apiUrl -TimeoutSec 10 -ErrorAction Stop
    
    if ($response.StatusCode -eq 200) {
        Write-Host "? API is healthy" -ForegroundColor Green
    }
}
catch {
    Write-Host "? API health check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Check database
Write-Host "Checking database connection..." -ForegroundColor Yellow

$env:PGHOST = $config.database.host
$env:PGPORT = $config.database.port
$env:PGDATABASE = $config.database.name
$env:PGUSER = $config.database.user

$result = & psql -c "SELECT 1" 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "? Database is accessible" -ForegroundColor Green
} else {
    Write-Host "? Database connection failed" -ForegroundColor Red
    exit 1
}

Write-Host "? All validation checks passed" -ForegroundColor Green
