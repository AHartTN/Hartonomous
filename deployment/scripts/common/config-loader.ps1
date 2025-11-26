# Configuration Loader Module (PowerShell)
# Loads environment-specific configuration
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Note: Assumes logger.ps1 is already imported by calling script

function Get-DeploymentConfig {
    <#
    .SYNOPSIS
        Load environment-specific configuration
    .PARAMETER Environment
        Environment name (development, staging, production)
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('development', 'staging', 'production')]
        [string]$Environment
    )

    Write-Step "Loading Configuration for $Environment"

    # Config file path
    $configPath = Join-Path $PSScriptRoot "..\..\config\$Environment.json"

    if (-not (Test-Path $configPath)) {
        Write-Failure "Configuration file not found: $configPath"
    }

    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        Write-Success "Configuration loaded: $Environment"
        return $config
    }
    catch {
        Write-Failure "Failed to load configuration: $($_.Exception.Message)"
    }
}

function Get-DeploymentTarget {
    <#
    .SYNOPSIS
        Get deployment target machine info
    #>
    param()

    $hostname = $env:COMPUTERNAME

    # Check if running on Windows (compatible with PS 5.1 and PS Core)
    $isWindowsOS = (-not (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue)) -or $IsWindows -or ($env:OS -match 'Windows')
    $osType = if ($isWindowsOS) { 'windows' } else { 'linux' }

    return @{
        Hostname = $hostname
        OSType   = $osType
        Platform = [System.Environment]::OSVersion.Platform
    }
}

function Test-EnvironmentVariables {
    <#
    .SYNOPSIS
        Validate required environment variables are set
    .PARAMETER Required
        Array of required variable names
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Required
    )

    Write-Log "Validating environment variables" -Level DEBUG

    $missing = @()

    foreach ($var in $Required) {
        if (-not (Test-Path "env:$var")) {
            $missing += $var
            Write-Log "Missing required environment variable: $var" -Level ERROR
        }
        else {
            Write-Log "Found environment variable: $var" -Level DEBUG
        }
    }

    if ($missing.Count -gt 0) {
        Write-Failure "Missing required environment variables: $($missing -join ', ')"
    }

    Write-Success "All required environment variables are set"
}

# Functions are available when dot-sourced
# Get-DeploymentConfig, Get-DeploymentTarget, Test-EnvironmentVariables
