# Preflight Check - Validate Secrets (PowerShell)
# Validates that required secrets are accessible and valid
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

# Initialize logger
$logPath = if ($env:LOG_PATH) { $env:LOG_PATH } else { "D:\Hartonomous\logs" }
$logFile = Join-Path $logPath "validate-secrets-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
Initialize-Logger -Level ($env:LOG_LEVEL ?? 'INFO') -LogFilePath $logFile

Write-Step "Validating Secrets and Credentials"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set"
}

Write-Log "Environment: $Environment" -Level INFO

# Required environment variables for Azure authentication
$requiredVars = @{
    'AZURE_TENANT_ID' = 'Azure Tenant ID'
    'AZURE_CLIENT_ID' = 'Azure Client ID (Service Principal)'
    'AZURE_CLIENT_SECRET' = 'Azure Client Secret (Service Principal)'
    'KEY_VAULT_URL' = 'Azure Key Vault URL'
}

# Check 1: Validate environment variables exist
Write-Step "Checking Required Environment Variables"
$missingVars = @()

foreach ($var in $requiredVars.Keys) {
    $value = [Environment]::GetEnvironmentVariable($var)
    if ([string]::IsNullOrWhiteSpace($value)) {
        Write-Log "Missing: $var ($($requiredVars[$var]))" -Level ERROR
        $missingVars += $var
    }
    else {
        # Mask the secret in the log
        $maskedValue = if ($var -match 'SECRET|PASSWORD') {
            $value.Substring(0, [Math]::Min(4, $value.Length)) + "****"
        }
        else {
            $value
        }
        Write-Success "Found: $var = $maskedValue"
    }
}

if ($missingVars.Count -gt 0) {
    Write-Failure "Missing required environment variables: $($missingVars -join ', ')"
}

# Check 2: Validate Azure Tenant ID format (GUID)
Write-Step "Validating Azure Tenant ID Format"
$tenantId = $env:AZURE_TENANT_ID
if ($tenantId -match '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
    Write-Success "Tenant ID format is valid"
}
else {
    Write-Failure "Invalid Tenant ID format (must be a GUID): $tenantId"
}

# Check 3: Validate Azure Client ID format (GUID)
Write-Step "Validating Azure Client ID Format"
$clientId = $env:AZURE_CLIENT_ID
if ($clientId -match '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') {
    Write-Success "Client ID format is valid"
}
else {
    Write-Failure "Invalid Client ID format (must be a GUID): $clientId"
}

# Check 4: Validate Key Vault URL format
Write-Step "Validating Key Vault URL Format"
$kvUrl = $env:KEY_VAULT_URL
if ($kvUrl -match '^https://[a-z0-9-]+\.vault\.azure\.net/?$') {
    Write-Success "Key Vault URL format is valid"
}
else {
    Write-Failure "Invalid Key Vault URL format: $kvUrl (Expected: https://<name>.vault.azure.net/)"
}

# Check 5: Validate Client Secret is not empty and meets minimum complexity
Write-Step "Validating Client Secret"
$clientSecret = $env:AZURE_CLIENT_SECRET
if ([string]::IsNullOrWhiteSpace($clientSecret)) {
    Write-Failure "Client Secret is empty"
}
elseif ($clientSecret.Length -lt 20) {
    Write-Log "Client Secret is shorter than expected (may be invalid)" -Level WARNING
    Write-Success "Client Secret exists (length: $($clientSecret.Length))"
}
else {
    Write-Success "Client Secret exists and meets minimum length requirements"
}

# Check 6: Test Azure authentication
Write-Step "Testing Azure Service Principal Authentication"
try {
    # Try to authenticate using az CLI
    $env:AZURE_CONFIG_DIR = "$env:TEMP\.azure-validate"
    
    # Clear any existing Azure CLI sessions
    & az logout 2>&1 | Out-Null
    
    # Login with service principal
    $loginResult = & az login --service-principal `
        --username $env:AZURE_CLIENT_ID `
        --password $env:AZURE_CLIENT_SECRET `
        --tenant $env:AZURE_TENANT_ID `
        --output json 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Azure authentication successful"
        
        # Try to access the subscription
        if ($env:AZURE_SUBSCRIPTION_ID) {
            & az account set --subscription $env:AZURE_SUBSCRIPTION_ID 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Subscription access verified"
            }
        }
    }
    else {
        Write-Log "Azure authentication failed: $loginResult" -Level ERROR
        Write-Failure "Failed to authenticate with Azure using provided Service Principal credentials"
    }
}
catch {
    Write-Log "Azure authentication test failed: $($_.Exception.Message)" -Level ERROR
    Write-Failure "Failed to test Azure authentication"
}

# Check 7: Test Key Vault connectivity
Write-Step "Testing Key Vault Connectivity"
try {
    $kvName = ($kvUrl -replace 'https://', '' -replace '\.vault\.azure\.net.*', '')
    
    # Try to list secrets (just to verify connectivity)
    $kvTest = & az keyvault secret list --vault-name $kvName --output json 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Key Vault is accessible"
        
        # Parse the result
        $secrets = $kvTest | ConvertFrom-Json
        Write-Log "Key Vault contains $($secrets.Count) secrets" -Level INFO
    }
    else {
        Write-Log "Key Vault access failed: $kvTest" -Level ERROR
        Write-Failure "Failed to access Key Vault: $kvName"
    }
}
catch {
    Write-Log "Key Vault connectivity test failed: $($_.Exception.Message)" -Level ERROR
    Write-Failure "Failed to test Key Vault connectivity"
}

# Check 8: Verify specific secrets exist in Key Vault (for non-development environments)
if ($Environment -ne 'development') {
    Write-Step "Verifying Required Secrets in Key Vault"
    
    $config = Get-DeploymentConfig -Environment $Environment
    
    # Expected secrets based on configuration
    $expectedSecrets = @()
    
    # Database password
    if ($config.database.name) {
        $expectedSecrets += "PostgreSQL-$($config.database.name)-Password"
    }
    
    # Neo4j password (if enabled)
    if ($config.features.neo4j_enabled -and $config.target.machine) {
        $expectedSecrets += "Neo4j-$($config.target.machine)-Password"
    }
    
    $missingSecrets = @()
    foreach ($secretName in $expectedSecrets) {
        try {
            $secretCheck = & az keyvault secret show --vault-name $kvName --name $secretName --output json 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Secret exists: $secretName"
            }
            else {
                Write-Log "Secret not found: $secretName" -Level ERROR
                $missingSecrets += $secretName
            }
        }
        catch {
            Write-Log "Failed to check secret: $secretName - $($_.Exception.Message)" -Level ERROR
            $missingSecrets += $secretName
        }
    }
    
    if ($missingSecrets.Count -gt 0) {
        Write-Failure "Missing required secrets in Key Vault: $($missingSecrets -join ', ')"
    }
}
else {
    Write-Log "Development environment - skipping Key Vault secret verification" -Level INFO
}

# Cleanup Azure CLI session
Write-Log "Cleaning up Azure CLI session..." -Level DEBUG
& az logout 2>&1 | Out-Null
if (Test-Path $env:AZURE_CONFIG_DIR) {
    Remove-Item -Path $env:AZURE_CONFIG_DIR -Recurse -Force -ErrorAction SilentlyContinue
}

# Final Summary
Write-Step "Secret Validation Complete"
Write-Success "All secrets and credentials are valid and accessible"
Write-Log "Ready for deployment" -Level INFO

exit 0
