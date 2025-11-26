# Health Check Script (PowerShell)
# Validates deployment health after deployment
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,
    [string]$ApiUrl = "http://localhost:8000"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"

# Initialize logger
Initialize-Logger -Level INFO

Write-Step "Health Check - $Environment"

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment

$apiUrl = if ($config.api.port) { "http://localhost:$($config.api.port)" } else { $ApiUrl }

# Health checks
$healthChecks = @()

# Check 1: API Health Endpoint
Write-Step "Checking API Health Endpoint"
try {
    $response = Invoke-RestMethod -Uri "$apiUrl/health" -Method GET -TimeoutSec 10
    if ($response) {
        Write-Success "API Health: $($response.status)"
        $healthChecks += @{Name="API Health"; Status="PASS"; Details=$response}
    }
} catch {
    Write-Log "API health check failed: $($_.Exception.Message)" -Level ERROR
    $healthChecks += @{Name="API Health"; Status="FAIL"; Details=$_.Exception.Message}
}

# Check 2: Database Connection
Write-Step "Checking Database Connection"
try {
    $response = Invoke-RestMethod -Uri "$apiUrl/health/database" -Method GET -TimeoutSec 10
    Write-Success "Database: Connected"
    $healthChecks += @{Name="Database"; Status="PASS"; Details=$response}
} catch {
    Write-Log "Database health check failed: $($_.Exception.Message)" -Level ERROR
    $healthChecks += @{Name="Database"; Status="FAIL"; Details=$_.Exception.Message}
}

# Check 3: Neo4j Connection (if enabled)
if ($config.features.neo4j_enabled) {
    Write-Step "Checking Neo4j Connection"
    try {
        $response = Invoke-RestMethod -Uri "$apiUrl/health/neo4j" -Method GET -TimeoutSec 10
        Write-Success "Neo4j: Connected"
        $healthChecks += @{Name="Neo4j"; Status="PASS"; Details=$response}
    } catch {
        Write-Log "Neo4j health check failed: $($_.Exception.Message)" -Level ERROR
        $healthChecks += @{Name="Neo4j"; Status="FAIL"; Details=$_.Exception.Message}
    }
}

# Check 4: Application Metrics
Write-Step "Checking Application Metrics"
try {
    $response = Invoke-RestMethod -Uri "$apiUrl/metrics" -Method GET -TimeoutSec 10
    Write-Success "Metrics: Available"
    $healthChecks += @{Name="Metrics"; Status="PASS"}
} catch {
    Write-Log "Metrics endpoint failed (optional)" -Level WARNING
    $healthChecks += @{Name="Metrics"; Status="SKIP"}
}

# Summary
Write-Step "Health Check Summary"

$passed = @($healthChecks | Where-Object { $_.Status -eq "PASS" }).Count
$failed = @($healthChecks | Where-Object { $_.Status -eq "FAIL" }).Count
$total = $healthChecks.Count

Write-Host ""
Write-Host "Results: $passed/$total checks passed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Yellow" })
Write-Host ""

foreach ($check in $healthChecks) {
    $symbol = switch ($check.Status) {
        "PASS" { "✅" }
        "FAIL" { "❌" }
        "SKIP" { "⏭️" }
    }
    Write-Host "$symbol $($check.Name): $($check.Status)"
}

if ($failed -gt 0) {
    Write-Failure "Health check failed: $failed checks failed"
}

Write-Success "All health checks passed"
exit 0
