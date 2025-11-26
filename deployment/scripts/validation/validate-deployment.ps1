# Validate Deployment Script
# Single validation script for all components
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
$scriptRoot = $PSScriptRoot
. "$scriptRoot\..\common\logger.ps1"
. "$scriptRoot\..\common\config-loader.ps1"

Write-Step "Validate Deployment"

# Load config
$config = Get-DeploymentConfig -Environment $Environment

$healthChecks = @()

# Check API
Write-Log "Checking API..." -Level INFO
try {
    $apiUrl = "http://$($config.api.host):$($config.api.port)/health"
    $response = Invoke-WebRequest -Uri $apiUrl -TimeoutSec 10 -ErrorAction Stop
    
    if ($response.StatusCode -eq 200) {
        $healthChecks += [PSCustomObject]@{ Component = "API"; Status = "PASS"; Message = "Healthy" }
        Write-Success "API: Healthy"
    }
}
catch {
    $healthChecks += [PSCustomObject]@{ Component = "API"; Status = "FAIL"; Message = $_.Exception.Message }
    Write-Log "API: $($_.Exception.Message)" -Level ERROR
}

# Check Database
Write-Log "Checking Database..." -Level INFO
try {
    $env:PGHOST = $config.database.host
    $env:PGPORT = $config.database.port
    $env:PGDATABASE = $config.database.name
    $env:PGUSER = $config.database.user
    
    $result = & psql -c "SELECT 1" 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        $healthChecks += [PSCustomObject]@{ Component = "Database"; Status = "PASS"; Message = "Connected" }
        Write-Success "Database: Connected"
    }
}
catch {
    $healthChecks += [PSCustomObject]@{ Component = "Database"; Status = "FAIL"; Message = $_.Exception.Message }
    Write-Log "Database: $($_.Exception.Message)" -Level ERROR
}

# Check Neo4j if enabled
if ($config.features.neo4j_enabled) {
    Write-Log "Checking Neo4j..." -Level INFO
    try {
        $neo4jHost = ($config.neo4j.uri -replace 'bolt://', '') -replace ':\d+$', ''
        $neo4jPort = if ($config.neo4j.uri -match ':(\d+)$') { $matches[1] } else { 7687 }
        
        $connected = Test-NetConnection -ComputerName $neo4jHost -Port $neo4jPort -InformationLevel Quiet -WarningAction SilentlyContinue
        
        if ($connected) {
            $healthChecks += [PSCustomObject]@{ Component = "Neo4j"; Status = "PASS"; Message = "Accessible" }
            Write-Success "Neo4j: Accessible"
        }
    }
    catch {
        $healthChecks += [PSCustomObject]@{ Component = "Neo4j"; Status = "FAIL"; Message = $_.Exception.Message }
        Write-Log "Neo4j: $($_.Exception.Message)" -Level ERROR
    }
}

# Summary
$passed = @($healthChecks | Where-Object { $_.Status -eq "PASS" }).Count
$failed = @($healthChecks | Where-Object { $_.Status -eq "FAIL" }).Count
$total = $healthChecks.Count

Write-Host ""
Write-Host "Validation Results: $passed/$total passed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Yellow" })

if ($failed -gt 0) {
    Write-Failure "Validation failed: $failed checks failed"
    exit 1
}

Write-Success "All validation checks passed"
exit 0
