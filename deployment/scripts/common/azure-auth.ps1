# Azure Authentication Module (PowerShell)
# Handles Azure authentication for deployment scripts
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Note: Assumes logger.ps1 is already imported by calling script

function Connect-AzureWithServicePrincipal {
    <#
    .SYNOPSIS
        Authenticate to Azure using Service Principal
    .PARAMETER TenantId
        Azure AD Tenant ID
    .PARAMETER ClientId
        Service Principal Client ID
    .PARAMETER ClientSecret
        Service Principal Client Secret
    .PARAMETER SubscriptionId
        Azure Subscription ID (optional)
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$TenantId,

        [Parameter(Mandatory = $true)]
        [string]$ClientId,

        [Parameter(Mandatory = $true)]
        [string]$ClientSecret,

        [string]$SubscriptionId = $null
    )

    Write-Step "Authenticating to Azure"

    try {
        # Convert client secret to secure string
        $secureSecret = ConvertTo-SecureString -String $ClientSecret -AsPlainText -Force
        $credential = New-Object System.Management.Automation.PSCredential($ClientId, $secureSecret)

        # Connect to Azure
        Write-Log "Connecting to Azure AD tenant: $TenantId" -Level DEBUG
        Connect-AzAccount -ServicePrincipal -Credential $credential -Tenant $TenantId -ErrorAction Stop | Out-Null

        # Set subscription if provided
        if ($SubscriptionId) {
            Write-Log "Setting subscription: $SubscriptionId" -Level DEBUG
            Set-AzContext -Subscription $SubscriptionId -ErrorAction Stop | Out-Null
        }

        # Verify authentication
        $context = Get-AzContext
        Write-Success "Authenticated as: $($context.Account.Id)"
        Write-Log "Subscription: $($context.Subscription.Name)" -Level INFO

        return $true
    }
    catch {
        Write-Failure "Azure authentication failed: $($_.Exception.Message)"
    }
}

function Get-KeyVaultSecret {
    <#
    .SYNOPSIS
        Retrieve a secret from Azure Key Vault
    .PARAMETER VaultName
        Key Vault name
    .PARAMETER SecretName
        Secret name
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$VaultName,

        [Parameter(Mandatory = $true)]
        [string]$SecretName
    )

    try {
        Write-Log "Retrieving secret '$SecretName' from Key Vault '$VaultName'" -Level DEBUG

        $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -AsPlainText -ErrorAction Stop

        if (-not $secret) {
            Write-Failure "Secret '$SecretName' not found in Key Vault '$VaultName'"
        }

        Write-Log "Successfully retrieved secret '$SecretName'" -Level DEBUG
        return $secret
    }
    catch {
        Write-Failure "Failed to retrieve secret '$SecretName': $($_.Exception.Message)"
    }
}

function Get-AppConfigValue {
    <#
    .SYNOPSIS
        Retrieve a value from Azure App Configuration
    .PARAMETER Endpoint
        App Configuration endpoint
    .PARAMETER Key
        Configuration key
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    try {
        Write-Log "Retrieving config '$Key' from App Configuration" -Level DEBUG

        # Extract store name from endpoint
        $storeName = ($Endpoint -replace 'https://', '' -replace '\.azconfig\.io.*', '')

        $value = Get-AzAppConfigurationKeyValue -Endpoint $Endpoint -Key $Key -ErrorAction Stop

        if (-not $value) {
            Write-Log "Config key '$Key' not found (using default)" -Level WARNING
            return $null
        }

        Write-Log "Successfully retrieved config '$Key'" -Level DEBUG
        return $value.Value
    }
    catch {
        Write-Log "Failed to retrieve config '$Key': $($_.Exception.Message)" -Level WARNING
        return $null
    }
}

function Test-AzureConnectivity {
    <#
    .SYNOPSIS
        Test connectivity to Azure services
    #>
    param()

    Write-Step "Testing Azure Connectivity"

    try {
        # Test Azure AD
        $context = Get-AzContext -ErrorAction Stop
        if (-not $context) {
            Write-Failure "Not authenticated to Azure"
        }
        Write-Success "Azure AD: Connected"

        # Test subscription access
        $subscription = Get-AzSubscription -SubscriptionId $context.Subscription.Id -ErrorAction Stop
        Write-Success "Subscription: $($subscription.Name)"

        # Test resource group access (if in DEPLOYMENT_ENVIRONMENT)
        if ($env:DEPLOYMENT_ENVIRONMENT) {
            $rgName = "rg-hartonomous"
            $rg = Get-AzResourceGroup -Name $rgName -ErrorAction SilentlyContinue
            if ($rg) {
                Write-Success "Resource Group: $rgName"
            }
            else {
                Write-Log "Resource group '$rgName' not found or no access" -Level WARNING
            }
        }

        return $true
    }
    catch {
        Write-Failure "Azure connectivity test failed: $($_.Exception.Message)"
    }
}

# Functions are available when dot-sourced
# Connect-AzureWithServicePrincipal, Get-KeyVaultSecret, Get-AppConfigValue, Test-AzureConnectivity
