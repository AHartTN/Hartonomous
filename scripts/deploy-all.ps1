$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deploying ALL environments" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$environments = @("localhost", "dev", "staging", "production")

foreach ($env in $environments) {
    Write-Host "`n`nDeploying $env..." -ForegroundColor Magenta
    & "$PSScriptRoot\deploy-environment.ps1" -Environment $env

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Deployment failed for $env" -ForegroundColor Red
        exit 1
    }
}

Write-Host "`n`n========================================" -ForegroundColor Cyan
Write-Host "All environments deployed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`nDatabase Ports:"
Write-Host "  Localhost:  5432"
Write-Host "  Dev:        5433"
Write-Host "  Staging:    5434"
Write-Host "  Production: 5435"
Write-Host "`nPgAdmin: http://localhost:5050"
Write-Host "  Email: admin@hartonomous.local"
Write-Host "  Password: admin"
