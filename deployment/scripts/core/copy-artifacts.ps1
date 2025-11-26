# Copy Artifacts Script
# Copies source code to deployment location
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

Write-Step "Copy Artifacts"

# Load config
$config = Get-DeploymentConfig -Environment $Environment

# Determine paths
$repoRoot = Resolve-Path "$scriptRoot\..\..\..\"
$sourceApiPath = Join-Path $repoRoot "api"
$targetApiPath = $config.deployment.install_path

Write-Log "Source: $sourceApiPath" -Level INFO
Write-Log "Target: $targetApiPath" -Level INFO

# Validate source
if (-not (Test-Path $sourceApiPath)) {
    Write-Failure "Source not found: $sourceApiPath"
}

# Create target
if (-not (Test-Path $targetApiPath)) {
    New-Item -ItemType Directory -Path $targetApiPath -Force | Out-Null
    Write-Log "Created target directory: $targetApiPath" -Level INFO
}

# Copy with robocopy
Write-Log "Syncing files..." -Level INFO

$robocopyArgs = @(
    $sourceApiPath,
    $targetApiPath,
    "/MIR",                    # Mirror
    "/XD", ".venv", "__pycache__", ".pytest_cache", ".git",  # Exclude directories
    "/XF", "*.pyc", ".env",    # Exclude files
    "/NFL", "/NDL",            # No file/directory lists
    "/NJH", "/NJS",            # No job header/summary
    "/NC", "/NS", "/NP"        # No class, size, progress
)

$result = & robocopy @robocopyArgs

# Robocopy exit codes: 0-7 are success, >7 is error
if ($LASTEXITCODE -gt 7) {
    Write-Failure "File copy failed (robocopy exit code: $LASTEXITCODE)"
}

Write-Success "Artifacts copied to: $targetApiPath"
exit 0
