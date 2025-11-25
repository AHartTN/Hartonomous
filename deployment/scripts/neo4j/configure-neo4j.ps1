# Neo4j Configuration Script (PowerShell)
# Configures Neo4j connection settings
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

Write-Step "Neo4j Configuration"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment

# Check if Neo4j is enabled
if (-not $config.features.neo4j_enabled) {
    Write-Log "Neo4j is disabled for environment: $Environment" -Level INFO
    exit 0
}

Write-Log "Configuring Neo4j for: $Environment" -Level INFO

# Get Neo4j settings
$neo4jUri = $config.neo4j.uri
$neo4jUser = $config.neo4j.user
$neo4jDatabase = $config.neo4j.database
$neo4jEdition = $config.neo4j.edition

Write-Log "Neo4j Edition: $neo4jEdition" -Level INFO
Write-Log "Neo4j URI: $neo4jUri" -Level INFO
Write-Log "Neo4j User: $neo4jUser" -Level INFO
Write-Log "Neo4j Database: $neo4jDatabase" -Level INFO

# Validate Neo4j configuration
Write-Step "Validating Neo4j Configuration"

# Check if URI is valid
if (-not $neo4jUri -or $neo4jUri -eq "null") {
    Write-Failure "Neo4j URI not configured in deployment config"
}

if ($neo4jUri -notmatch '^bolt://') {
    Write-Failure "Neo4j URI must use bolt:// protocol, got: $neo4jUri"
}

Write-Success "Neo4j configuration validated"

# Environment-specific checks
if ($Environment -eq 'development') {
    Write-Log "Development environment: Using Neo4j Desktop edition" -Level INFO
    Write-Log "Expected: bolt://localhost:7687" -Level INFO

    if ($neo4jUri -ne "bolt://localhost:7687") {
        Write-Log "Warning: URI does not match expected development URI" -Level WARNING
    }
}
else {
    Write-Log "Production/Staging environment: Using Neo4j Community edition" -Level INFO

    if ($config.azure.key_vault_url) {
        Write-Log "Credentials will be retrieved from Azure Key Vault" -Level INFO
        Write-Log "Key Vault: $($config.azure.key_vault_url)" -Level INFO
    }
    else {
        Write-Log "Warning: No Key Vault configured for Neo4j credentials" -Level WARNING
    }
}

# Check Neo4j service status (if running locally)
if ($neo4jUri -match 'localhost|127\.0\.0\.1') {
    Write-Step "Checking Neo4j Service Status"

    try {
        # Try to connect to Neo4j port
        $neo4jHost = "localhost"
        $neo4jPort = 7687

        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $connect = $tcpClient.BeginConnect($neo4jHost, $neo4jPort, $null, $null)
        $wait = $connect.AsyncWaitHandle.WaitOne(3000, $false)

        if ($wait) {
            try {
                $tcpClient.EndConnect($connect)
                $tcpClient.Close()
                Write-Success "Neo4j is running and accepting connections"
            }
            catch {
                Write-Log "Neo4j port is open but connection failed: $($_.Exception.Message)" -Level WARNING
            }
        }
        else {
            $tcpClient.Close()
            Write-Log "Neo4j is not responding on port $neo4jPort" -Level WARNING
            Write-Log "Please start Neo4j Desktop before running the application" -Level INFO
        }
    }
    catch {
        Write-Log "Could not check Neo4j status: $($_.Exception.Message)" -Level WARNING
    }
}

# Summary
Write-Step "Configuration Summary"
Write-Success "Neo4j configuration completed"
Write-Log "Environment: $Environment" -Level INFO
Write-Log "Neo4j URI: $neo4jUri" -Level INFO
Write-Log "Neo4j Database: $neo4jDatabase" -Level INFO

Write-Log "Neo4j configuration completed" -Level INFO
