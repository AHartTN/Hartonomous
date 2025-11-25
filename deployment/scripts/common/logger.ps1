# Common Logger Module (PowerShell)
# Provides consistent logging across all deployment scripts
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.

# Strict mode for better error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Log levels
$script:LogLevel = @{
    DEBUG   = 0
    INFO    = 1
    WARNING = 2
    ERROR   = 3
}

# Current log level (default: INFO)
$script:CurrentLogLevel = $script:LogLevel.INFO

# Log file path
$script:LogFile = $null

function Initialize-Logger {
    <#
    .SYNOPSIS
        Initialize the logging system
    .PARAMETER LogFilePath
        Path to log file (optional)
    .PARAMETER Level
        Minimum log level (DEBUG, INFO, WARNING, ERROR)
    #>
    param(
        [string]$LogFilePath = $null,
        [ValidateSet('DEBUG', 'INFO', 'WARNING', 'ERROR')]
        [string]$Level = 'INFO'
    )

    $script:CurrentLogLevel = $script:LogLevel[$Level]
    $script:LogFile = $LogFilePath

    if ($LogFilePath) {
        $logDir = Split-Path -Parent $LogFilePath
        if (-not (Test-Path $logDir)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
    }

    Write-Log "Logger initialized (Level: $Level)" -Level DEBUG
}

function Write-Log {
    <#
    .SYNOPSIS
        Write a log message
    .PARAMETER Message
        Log message
    .PARAMETER Level
        Log level (DEBUG, INFO, WARNING, ERROR)
    .PARAMETER NoConsole
        Skip console output
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [ValidateSet('DEBUG', 'INFO', 'WARNING', 'ERROR')]
        [string]$Level = 'INFO',

        [switch]$NoConsole
    )

    # Check if we should log this level
    if ($script:LogLevel[$Level] -lt $script:CurrentLogLevel) {
        return
    }

    # Format timestamp
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    # Format log entry
    $logEntry = "[$timestamp] [$Level] $Message"

    # Console output with colors
    if (-not $NoConsole) {
        $color = switch ($Level) {
            'DEBUG'   { 'Gray' }
            'INFO'    { 'White' }
            'WARNING' { 'Yellow' }
            'ERROR'   { 'Red' }
        }

        Write-Host $logEntry -ForegroundColor $color
    }

    # File output
    if ($script:LogFile) {
        Add-Content -Path $script:LogFile -Value $logEntry
    }

    # GitHub Actions annotation
    if ($env:GITHUB_ACTIONS -eq 'true') {
        switch ($Level) {
            'WARNING' { Write-Output "::warning::$Message" }
            'ERROR'   { Write-Output "::error::$Message" }
        }
    }
}

function Write-Success {
    <#
    .SYNOPSIS
        Write a success message
    #>
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host "✅ $Message" -ForegroundColor Green
    Write-Log "SUCCESS: $Message" -Level INFO
}

function Write-Failure {
    <#
    .SYNOPSIS
        Write a failure message and exit
    #>
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host "❌ $Message" -ForegroundColor Red
    Write-Log "FAILURE: $Message" -Level ERROR
    exit 1
}

function Write-Step {
    <#
    .SYNOPSIS
        Write a step header
    #>
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Log "STEP: $Message" -Level INFO
}

# Functions are available when dot-sourced
# Initialize-Logger, Write-Log, Write-Success, Write-Failure, Write-Step
