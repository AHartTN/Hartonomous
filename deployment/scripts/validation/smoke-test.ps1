# Smoke Test Script (PowerShell)
# Quick validation tests for deployed application
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"

# Initialize logger
Initialize-Logger -Level $env:LOG_LEVEL ?? 'INFO'

Write-Step "Smoke Tests"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
Write-Log "Running smoke tests for: $Environment" -Level INFO

# Get API configuration
$apiHost = $config.api.host
$apiPort = $config.api.port
$apiUrl = "http://$($apiHost):$apiPort"

Write-Log "API URL: $apiUrl" -Level INFO

$testsPassed = 0
$testsFailed = 0

# Test 1: API Health Endpoint
Write-Step "Test 1: API Health Endpoint"
try {
    $response = Invoke-RestMethod -Uri "$apiUrl/health" -Method GET -TimeoutSec 10

    if ($response.status -eq "healthy") {
        Write-Success "API is healthy"
        $testsPassed++
    }
    else {
        Write-Log "API returned unexpected status: $($response.status)" -Level ERROR
        $testsFailed++
    }
}
catch {
    Write-Log "API health check failed: $($_.Exception.Message)" -Level ERROR
    $testsFailed++
}

# Test 2: Database Connectivity
Write-Step "Test 2: Database Connectivity"
try {
    $response = Invoke-RestMethod -Uri "$apiUrl/health/database" -Method GET -TimeoutSec 10

    if ($response.status -eq "connected") {
        Write-Success "Database is connected"
        $testsPassed++
    }
    else {
        Write-Log "Database connectivity check failed: $($response.status)" -Level ERROR
        $testsFailed++
    }
}
catch {
    Write-Log "Database health check failed: $($_.Exception.Message)" -Level ERROR
    $testsFailed++
}

# Test 3: Neo4j Connectivity (if enabled)
if ($config.features.neo4j_enabled) {
    Write-Step "Test 3: Neo4j Connectivity"
    try {
        $response = Invoke-RestMethod -Uri "$apiUrl/health/neo4j" -Method GET -TimeoutSec 10

        if ($response.status -eq "connected") {
            Write-Success "Neo4j is connected"
            $testsPassed++
        }
        else {
            Write-Log "Neo4j connectivity check failed: $($response.status)" -Level ERROR
            $testsFailed++
        }
    }
    catch {
        Write-Log "Neo4j health check failed: $($_.Exception.Message)" -Level ERROR
        $testsFailed++
    }
}
else {
    Write-Log "Neo4j is disabled, skipping test" -Level INFO
}

# Test 4: API Documentation Endpoint
Write-Step "Test 4: API Documentation"
try {
    $response = Invoke-WebRequest -Uri "$apiUrl/docs" -Method GET -TimeoutSec 10

    if ($response.StatusCode -eq 200) {
        Write-Success "API documentation is accessible"
        $testsPassed++
    }
    else {
        Write-Log "API documentation returned unexpected status: $($response.StatusCode)" -Level ERROR
        $testsFailed++
    }
}
catch {
    Write-Log "API documentation check failed: $($_.Exception.Message)" -Level ERROR
    $testsFailed++
}

# Test 5: Create Test Atom (basic functionality)
Write-Step "Test 5: Create Test Atom"
try {
    $testAtom = @{
        text = "Smoke test atom - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        metadata = @{
            test = $true
            environment = $Environment
        }
    } | ConvertTo-Json

    $response = Invoke-RestMethod -Uri "$apiUrl/v1/atoms" -Method POST -Body $testAtom -ContentType "application/json" -TimeoutSec 10

    if ($response.atom_id) {
        Write-Success "Test atom created: $($response.atom_id)"
        $testsPassed++

        # Store for cleanup
        $testAtomId = $response.atom_id
    }
    else {
        Write-Log "Test atom creation failed" -Level ERROR
        $testsFailed++
    }
}
catch {
    Write-Log "Create atom test failed: $($_.Exception.Message)" -Level ERROR
    $testsFailed++
}

# Test 6: Retrieve Test Atom
if ($testAtomId) {
    Write-Step "Test 6: Retrieve Test Atom"
    try {
        $response = Invoke-RestMethod -Uri "$apiUrl/v1/atoms/$testAtomId" -Method GET -TimeoutSec 10

        if ($response.atom_id -eq $testAtomId) {
            Write-Success "Test atom retrieved successfully"
            $testsPassed++
        }
        else {
            Write-Log "Test atom retrieval failed" -Level ERROR
            $testsFailed++
        }
    }
    catch {
        Write-Log "Retrieve atom test failed: $($_.Exception.Message)" -Level ERROR
        $testsFailed++
    }
}

# Summary
Write-Step "Smoke Test Summary"
$totalTests = $testsPassed + $testsFailed

Write-Log "Total tests: $totalTests" -Level INFO
Write-Log "Tests passed: $testsPassed" -Level INFO
Write-Log "Tests failed: $testsFailed" -Level INFO

if ($testsFailed -eq 0) {
    Write-Success "All smoke tests passed"
    exit 0
}
else {
    Write-Failure "Some smoke tests failed ($testsFailed/$totalTests)"
}
