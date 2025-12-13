#Requires -RunAsAdministrator

# Cortex PostgreSQL Extension Installation Script
# Run this script with Administrator privileges

$ErrorActionPreference = "Stop"

Write-Host "Installing Cortex extension to PostgreSQL 18..." -ForegroundColor Cyan

# Source files
$SourceDLL = "D:\Repositories\Hartonomous\cortex\build\Release\cortex.dll"
$SourceControl = "D:\Repositories\Hartonomous\cortex\cortex.control"
$SourceSQL = "D:\Repositories\Hartonomous\cortex\cortex--1.0.sql"

# Destination paths
$DestLib = "D:\PostgreSQL\18\lib"
$DestExtension = "D:\PostgreSQL\18\share\extension"

# Verify source files exist
if (-not (Test-Path $SourceDLL)) {
    throw "cortex.dll not found. Run 'cmake --build build --config Release' first."
}

# Copy files
Copy-Item $SourceDLL -Destination "$DestLib\cortex.dll" -Force
Write-Host "  Installed: cortex.dll" -ForegroundColor Green

Copy-Item $SourceControl -Destination "$DestExtension\cortex.control" -Force
Write-Host "  Installed: cortex.control" -ForegroundColor Green

Copy-Item $SourceSQL -Destination "$DestExtension\cortex--1.0.sql" -Force
Write-Host "  Installed: cortex--1.0.sql" -ForegroundColor Green

Write-Host "`nCortex extension installed successfully!" -ForegroundColor Green
Write-Host "Run 'CREATE EXTENSION cortex;' in your hartonomous database to activate." -ForegroundColor Yellow
