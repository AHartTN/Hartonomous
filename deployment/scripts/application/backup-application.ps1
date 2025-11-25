# Application Backup Script (PowerShell)
# Creates timestamped backup of API application code
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('development', 'staging', 'production')]
    [string]$Environment = $env:DEPLOYMENT_ENVIRONMENT,

    [Parameter(Mandatory = $false)]
    [string]$BackupPath = $null
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Import common modules
. "$PSScriptRoot\..\common\logger.ps1"
. "$PSScriptRoot\..\common\config-loader.ps1"

# Initialize logger
Initialize-Logger -Level ($env:LOG_LEVEL ?? 'INFO')

Write-Step "Application Backup"

# Validate environment
if (-not $Environment) {
    Write-Failure "DEPLOYMENT_ENVIRONMENT not set. Use -Environment parameter or set environment variable."
}

# Load configuration
$config = Get-DeploymentConfig -Environment $Environment

# Construct backup directory
if (-not $BackupPath) {
    $BackupPath = Join-Path $PSScriptRoot "..\..\..\backups\application"
}

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    Write-Log "Created backup directory: $BackupPath" -Level INFO
}

# Get API path
$repoRoot = Join-Path $PSScriptRoot "..\..\..\"
$apiPath = Join-Path $repoRoot "api"

if (-not (Test-Path $apiPath)) {
    Write-Failure "API directory not found: $apiPath"
}

# Generate backup filename with timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $BackupPath "api-$Environment-$timestamp.zip"

Write-Log "Backup file: $backupFile" -Level INFO

# Create backup archive
Write-Step "Creating Backup Archive"

try {
    Write-Log "Compressing API directory..." -Level DEBUG

    # Exclude virtual environment and cache directories
    $excludePatterns = @(
        ".venv",
        "__pycache__",
        "*.pyc",
        ".pytest_cache",
        ".env"
    )

    # Create temporary directory for filtered copy
    $tempDir = Join-Path $env:TEMP "hartonomous-backup-$timestamp"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    # Copy API files excluding patterns
    Write-Log "Copying API files (excluding: $($excludePatterns -join ', '))..." -Level DEBUG

    Get-ChildItem -Path $apiPath -Recurse | Where-Object {
        $item = $_
        $shouldExclude = $false

        foreach ($pattern in $excludePatterns) {
            if ($item.Name -like $pattern -or $item.FullName -like "*\$pattern\*") {
                $shouldExclude = $true
                break
            }
        }

        -not $shouldExclude
    } | ForEach-Object {
        $relativePath = $_.FullName.Substring($apiPath.Length + 1)
        $targetPath = Join-Path $tempDir $relativePath

        $targetDir = Split-Path -Parent $targetPath
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }

        if (-not $_.PSIsContainer) {
            Copy-Item $_.FullName -Destination $targetPath -Force
        }
    }

    # Compress to ZIP
    Compress-Archive -Path "$tempDir\*" -DestinationPath $backupFile -CompressionLevel Optimal -Force

    # Clean up temp directory
    Remove-Item -Path $tempDir -Recurse -Force

    Write-Success "Backup created: $backupFile"
}
catch {
    Write-Failure "Backup error: $($_.Exception.Message)"
}

# Verify backup file
if (-not (Test-Path $backupFile)) {
    Write-Failure "Backup file not found after creation: $backupFile"
}

$fileSize = (Get-Item $backupFile).Length
$fileSizeMB = [math]::Round($fileSize / 1MB, 2)
Write-Success "Backup size: $fileSizeMB MB"

# Retention policy: Keep last 10 backups per environment
Write-Step "Applying Retention Policy"
$allBackups = Get-ChildItem -Path $BackupPath -Filter "api-$Environment-*.zip" |
    Sort-Object LastWriteTime -Descending

$keepCount = 10
if ($allBackups.Count -gt $keepCount) {
    $toDelete = $allBackups | Select-Object -Skip $keepCount

    foreach ($old in $toDelete) {
        Write-Log "Removing old backup: $($old.Name)" -Level INFO
        Remove-Item $old.FullName -Force
    }

    Write-Success "Retained $keepCount most recent backups, deleted $($toDelete.Count) old backups"
}
else {
    Write-Log "Current backups: $($allBackups.Count) (retention: $keepCount)" -Level INFO
}

Write-Log "Application backup completed: $backupFile" -Level INFO
