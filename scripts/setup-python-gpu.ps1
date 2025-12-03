#!/usr/bin/env pwsh
# Setup Python 3.13 environment with CUDA support for Hartonomous

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Setting up Python 3.13 + CUDA Environment" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Check Python version
$pythonCmd = "python3.13"
try {
    & $pythonCmd --version 2>$null | Out-Null
} catch {
    $pythonCmd = "python"
}

try {
    $pythonVersion = & $pythonCmd --version 2>&1
    Write-Host "Using Python: $pythonVersion" -ForegroundColor Yellow
} catch {
    Write-Host "Error: Python 3.13 not found. Please install Python 3.13" -ForegroundColor Red
    exit 1
}

# Check for CUDA
Write-Host ""
try {
    $nvidiaInfo = nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader 2>$null
    Write-Host "CUDA detected:" -ForegroundColor Green
    Write-Host $nvidiaInfo -ForegroundColor Gray
} catch {
    Write-Host "WARNING: NVIDIA GPU not detected. CuPy will fall back to CPU." -ForegroundColor Yellow
}

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

# Create virtual environment
$venvDir = Join-Path $ProjectRoot ".venv"
if (-not (Test-Path $venvDir)) {
    Write-Host ""
    Write-Host "Creating virtual environment..." -ForegroundColor Yellow
    & $pythonCmd -m venv $venvDir
}

# Activate virtual environment
$activateScript = Join-Path $venvDir "Scripts\Activate.ps1"
if (Test-Path $activateScript) {
    . $activateScript
} else {
    Write-Host "Error: Could not find activation script" -ForegroundColor Red
    exit 1
}

# Upgrade pip
Write-Host ""
Write-Host "Upgrading pip..." -ForegroundColor Yellow
python -m pip install --upgrade pip setuptools wheel | Out-Null

# Install dependencies
Write-Host ""
Write-Host "Installing Python dependencies..." -ForegroundColor Yellow
$requirementsPath = Join-Path $ProjectRoot "requirements.txt"
pip install -r $requirementsPath

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Python environment setup complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To activate the environment, run:" -ForegroundColor Yellow
Write-Host "  .venv\Scripts\Activate.ps1" -ForegroundColor Gray
Write-Host ""
Write-Host "To test GPU availability:" -ForegroundColor Yellow
Write-Host "  python -c 'import cupy; print(cupy.cuda.runtime.getDeviceCount())'" -ForegroundColor Gray
