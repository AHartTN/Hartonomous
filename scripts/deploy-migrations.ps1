#!/usr/bin/env pwsh
# Hartonomous - Cross-platform idempotent database migration deployment
# Works on Windows PowerShell, PowerShell Core (Linux/macOS)

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("localhost", "dev", "staging", "production")]
    [string]$Environment = "localhost"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Deploying Migrations to: $Environment" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# Get project root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

# Set environment variable
$env:ASPNETCORE_ENVIRONMENT = $Environment

# Navigate to project root
Push-Location $ProjectRoot

try {
    # Check if dotnet is installed
    $dotnetVersion = dotnet --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK not found. Please install .NET 10 SDK."
    }

    Write-Host "`nUsing .NET version: $dotnetVersion" -ForegroundColor Yellow

    # Generate idempotent SQL script (optional - for auditing)
    Write-Host "`nGenerating idempotent migration script..." -ForegroundColor Yellow
    Push-Location "Hartonomous.Db"

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $scriptPath = "../migrations/migration-${Environment}-${timestamp}.sql"

    # Ensure migrations directory exists
    $migrationsDir = Join-Path $ProjectRoot "migrations"
    if (-not (Test-Path $migrationsDir)) {
        New-Item -ItemType Directory -Path $migrationsDir | Out-Null
    }

    dotnet ef migrations script --idempotent --output $scriptPath --project Hartonomous.Db.csproj 2>$null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Migration script saved to: $scriptPath" -ForegroundColor Green
    }

    # Apply migrations
    Write-Host "`nApplying migrations to $Environment database..." -ForegroundColor Yellow
    dotnet ef database update --project Hartonomous.Db.csproj --verbose

    if ($LASTEXITCODE -ne 0) {
        throw "Migration deployment failed!"
    }

    Pop-Location

    Write-Host "`n=========================================" -ForegroundColor Cyan
    Write-Host "Migration deployment completed successfully!" -ForegroundColor Green
    Write-Host "Environment: $Environment" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Cyan
}
catch {
    Write-Host "`nError: $_" -ForegroundColor Red
    Pop-Location
    exit 1
}
finally {
    Pop-Location
}
