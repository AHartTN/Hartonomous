# Secret Validation Script (PowerShell)
# Validates required secrets are accessible
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

#Requires -Version 7.0

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT
)

$ErrorActionPreference = "Stop"

# Import common modules
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. "$ScriptDir\..\common\logger.ps1"
. "$ScriptDir\..\common\azure-auth.ps1"

# Initialize logger
Initialize-Logger -LogLevel ($env:LOG_LEVEL ?? "INFO")

Write-Step "Secret Validation"

# Check if Azure authentication is configured
if (-not $env:AZURE_TENANT_ID -or -not $env:AZURE_CLIENT_ID) {
    Write-Log "Azure credentials not configured - skipping secret validation" "WARNING"
    Write-Log "Set AZURE_TENANT_ID and AZURE_CLIENT_ID environment variables" "INFO"
    exit 0
}

# Check if Key Vault URL is provided
if (-not $env:KEY_VAULT_URL) {
    Write-Log "KEY_VAULT_URL not set - skipping secret validation" "WARNING"
    exit 0
}

Write-Log "Key Vault URL: $env:KEY_VAULT_URL" "INFO"

# Extract Key Vault name from URL
$KvName = $env:KEY_VAULT_URL -replace 'https://', '' -replace '\.vault\.azure\.net.*', ''
Write-Log "Key Vault name: $KvName" "INFO"

# Authenticate to Azure
Connect-AzureAuth

# Define required secrets
$RequiredSecrets = @(
    "PostgreSQL-Hartonomous-Password"
)

# Add environment-specific secrets
$Environment = $Environment ?? "development"

switch ($Environment) {
    "production" {
        $RequiredSecrets += @(
            "Neo4j-hart-server-Password",
            "AzureAd-ClientSecret"
        )
    }
    "staging" {
        $RequiredSecrets += @(
            "Neo4j-hart-server-Password",
            "AzureAd-ClientSecret"
        )
    }
    "development" {
        # Development uses local credentials
    }
}

# Validate secrets
Write-Step "Validating Secrets"

$ValidationFailed = $false

foreach ($SecretName in $RequiredSecrets) {
    Write-Log "Checking secret: $SecretName" "DEBUG"

    try {
        # Check if secret exists and is accessible
        $Secret = az keyvault secret show `
            --vault-name $KvName `
            --name $SecretName `
            --query "value" `
            --output tsv 2>$null

        if ($LASTEXITCODE -eq 0 -and $Secret) {
            # Check if secret has a value
            if ($Secret.Trim()) {
                Write-Success "Secret exists: $SecretName"
            }
            else {
                Write-Log "Secret exists but is empty: $SecretName" "ERROR"
                $ValidationFailed = $true
            }
        }
        else {
            Write-Log "Secret not found or not accessible: $SecretName" "ERROR"
            $ValidationFailed = $true
        }
    }
    catch {
        Write-Log "Error checking secret $SecretName : $_" "ERROR"
        $ValidationFailed = $true
    }
}

# Summary
Write-Step "Validation Summary"

if ($ValidationFailed) {
    Write-Failure "Secret validation failed - some secrets are missing or inaccessible"
}
else {
    Write-Success "All required secrets are accessible"
}

Write-Log "Secret validation completed" "INFO"
