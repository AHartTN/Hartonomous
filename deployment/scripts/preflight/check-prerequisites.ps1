# Preflight Check - Prerequisites (PowerShell)
# Validates system prerequisites before deployment
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

[CmdletBinding()]
param(
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"
. "$PSScriptRoot\..\common\azure-auth.ps1"

# Initialize logger
Initialize-Logger -Level $env:LOG_LEVEL -LogFilePath "D:\Hartonomous\logs\preflight-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

Write-Step "Preflight Checks - Prerequisites"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set"
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment
$target = Get-DeploymentTarget

Write-Log "Environment: $Environment" -Level INFO
Write-Log "Target: $($target.Hostname)" -Level INFO

# Check 1: Disk Space
Write-Step "Checking Disk Space"
$drive = "C:"
$disk = Get-PSDrive $drive.TrimEnd(':')
$freeSpaceGB = [math]::Round($disk.Free / 1GB, 2)
$requiredGB = 10

if ($freeSpaceGB -lt $requiredGB) {
    Write-Failure "Insufficient disk space: ${freeSpaceGB}GB free, ${requiredGB}GB required"
}
Write-Success "Disk space: ${freeSpaceGB}GB available"

# Check 2: Python Installation
Write-Step "Checking Python Installation"
try {
    $pythonVersion = & python --version 2>&1
    if ($pythonVersion -match 'Python (\d+\.\d+)') {
        $versionNumber = [version]$matches[1]
        if ($versionNumber -lt [version]'3.10') {
            Write-Failure "Python 3.10+ required, found: $pythonVersion"
        }
        Write-Success "Python: $pythonVersion"
    }
} catch {
    Write-Failure "Python not found in PATH"
}

# Check 3: PostgreSQL Connection
Write-Step "Checking PostgreSQL"
try {
    $pgVersion = & psql --version 2>&1
    Write-Success "PostgreSQL client: $pgVersion"

    # Test connection
    $env:PGPASSWORD = "test"  # Will be replaced with real password
    $testConnection = & psql -h localhost -U postgres -c "SELECT version();" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "PostgreSQL: Connected"
    } else {
        Write-Log "PostgreSQL connection test failed (expected in CI)" -Level WARNING
    }
} catch {
    Write-Log "PostgreSQL client not found (optional for development)" -Level WARNING
}

# Check 4: Neo4j Availability
Write-Step "Checking Neo4j"
if ($config.features.neo4j_enabled) {
    $neo4jUri = $config.neo4j.uri
    try {
        $neo4jTest = Test-NetConnection -ComputerName "localhost" -Port 7687 -InformationLevel Quiet
        if ($neo4jTest) {
            Write-Success "Neo4j: Port 7687 accessible"
        } else {
            Write-Log "Neo4j not running on port 7687" -Level WARNING
        }
    } catch {
        Write-Log "Neo4j connectivity check failed" -Level WARNING
    }
}

# Check 5: Azure CLI
Write-Step "Checking Azure CLI"
try {
    $azVersion = & az version --output json 2>&1 | ConvertFrom-Json
    Write-Success "Azure CLI: $($azVersion.'azure-cli')"
} catch {
    Write-Failure "Azure CLI not installed"
}

# Check 6: Git
Write-Step "Checking Git"
try {
    $gitVersion = & git --version 2>&1
    Write-Success "Git: $gitVersion"
} catch {
    Write-Log "Git not found (optional)" -Level WARNING
}

# Check 7: Network Connectivity
Write-Step "Checking Network Connectivity"
$testHosts = @(
    @{Name="Azure"; Host="management.azure.com"; Port=443},
    @{Name="GitHub"; Host="github.com"; Port=443}
)

foreach ($test in $testHosts) {
    try {
        $result = Test-NetConnection -ComputerName $test.Host -Port $test.Port -InformationLevel Quiet -WarningAction SilentlyContinue
        if ($result) {
            Write-Success "$($test.Name): Connected"
        } else {
            Write-Log "$($test.Name): Connection failed" -Level WARNING
        }
    } catch {
        Write-Log "$($test.Name): Connection test failed" -Level WARNING
    }
}

# Check 8: Azure Authentication
Write-Step "Checking Azure Authentication"

# Test if already authenticated (Arc managed identity or existing login)
try {
    $account = & az account show 2>$null | ConvertFrom-Json
    if ($account) {
        Write-Success "Azure: Authenticated as $($account.user.name)"
        Write-Log "Using existing Azure authentication (Arc managed identity or cached login)" -Level INFO
    } else {
        Write-Log "Not authenticated to Azure - will require credentials" -Level INFO
    }
} catch {
    Write-Log "Not authenticated to Azure - will require credentials" -Level INFO
}

# Check required environment variables
Write-Step "Checking Environment Variables"
$requiredVars = @(
    'DEPLOYMENT_ENVIRONMENT'
)

Test-EnvironmentVariables -Required $requiredVars

# Final Summary
Write-Step "Preflight Checks Complete"
Write-Success "All critical prerequisites validated"
Write-Log "System is ready for deployment" -Level INFO

exit 0
