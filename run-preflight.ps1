# Run Preflight Checks with Credentials from .env
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

Write-Host "Loading credentials from .env..." -ForegroundColor Cyan

# Load environment variables from .env
Get-Content ".env" | ForEach-Object {
    if ($_ -match '^([^#][^=]+)=(.*)$') {
        $key = $matches[1].Trim()
        $value = $matches[2].Trim()
        Set-Item -Path "env:$key" -Value $value
    }
}

# Set deployment-specific variables
$env:LOG_LEVEL = "INFO"
$env:DEPLOYMENT_ENVIRONMENT = "development"

Write-Host "Environment configured:" -ForegroundColor Green
Write-Host "  PGHOST: $env:PGHOST" -ForegroundColor Gray
Write-Host "  PGPORT: $env:PGPORT" -ForegroundColor Gray
Write-Host "  PGDATABASE: $env:PGDATABASE" -ForegroundColor Gray
Write-Host "  PGUSER: $env:PGUSER" -ForegroundColor Gray
Write-Host "  NEO4J_ENABLED: $env:NEO4J_ENABLED" -ForegroundColor Gray
Write-Host ""

Write-Host "Running preflight checks..." -ForegroundColor Cyan
Write-Host ""

# Run preflight checks
& ".\deployment\scripts\preflight\check-prerequisites.ps1"
