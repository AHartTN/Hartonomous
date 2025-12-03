#!/usr/bin/env pwsh
# Verify all prerequisites are installed for Hartonomous deployment

Write-Host "Hartonomous Environment Verification" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$Missing = 0

# Check .NET SDK
Write-Host -NoNewline "Checking .NET SDK... "
try {
    $version = dotnet --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK ($version)" -ForegroundColor Green
        if (-not $version.StartsWith("10.")) {
            Write-Host "  WARNING: .NET 10 recommended, found $version" -ForegroundColor Yellow
        }
    } else {
        throw
    }
} catch {
    Write-Host "MISSING" -ForegroundColor Red
    $Missing = 1
}

# Check Docker
Write-Host -NoNewline "Checking Docker... "
try {
    $version = docker --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK ($version)" -ForegroundColor Green
    } else {
        throw
    }
} catch {
    Write-Host "MISSING" -ForegroundColor Red
    $Missing = 1
}

# Check Python 3
Write-Host -NoNewline "Checking Python 3... "
try {
    $version = python --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK ($version)" -ForegroundColor Green
    } else {
        throw
    }
} catch {
    Write-Host "MISSING (optional)" -ForegroundColor Yellow
}

# Check EF Core tools
Write-Host -NoNewline "Checking EF Core tools... "
try {
    $tools = dotnet tool list --global 2>$null
    if ($tools -match "dotnet-ef") {
        $version = (dotnet ef --version 2>$null) -split "`n" | Select-Object -First 1
        Write-Host "OK ($version)" -ForegroundColor Green
    } else {
        throw
    }
} catch {
    Write-Host "MISSING" -ForegroundColor Red
    Write-Host "  Install with: dotnet tool install --global dotnet-ef" -ForegroundColor Yellow
    $Missing = 1
}

# Check PowerShell version
Write-Host -NoNewline "Checking PowerShell... "
$psVersion = $PSVersionTable.PSVersion
Write-Host "OK ($psVersion)" -ForegroundColor Green

Write-Host ""
if ($Missing -eq 0) {
    Write-Host "All required tools are installed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Some required tools are missing. Please install them." -ForegroundColor Red
    exit 1
}
