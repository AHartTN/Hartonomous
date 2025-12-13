# Hartonomous Database Backup Script
# Creates timestamped full database dumps with compression

param(
    [string]$OutputDir = ".\backups",
    [switch]$SchemaOnly,
    [switch]$DataOnly
)

$ErrorActionPreference = "Stop"

# Ensure PostgreSQL bin is in PATH
$pgBin = "D:\PostgreSQL\18\bin"
$env:PATH = "$pgBin;$env:PATH"

# Get timestamp for backup file
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFile = Join-Path $OutputDir "hartonomous_$timestamp.sql"

# Create output directory if it doesn't exist
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "🔄 Starting Hartonomous database backup..." -ForegroundColor Cyan
Write-Host "   Output: $backupFile"

# Build pg_dump command
$dumpArgs = @(
    "-h", "localhost",
    "-U", "hartonomous",
    "-d", "hartonomous",
    "-F", "c",  # Custom format (compressed)
    "-f", $backupFile
)

if ($SchemaOnly) {
    $dumpArgs += "-s"
    Write-Host "   Mode: Schema only"
}
elseif ($DataOnly) {
    $dumpArgs += "-a"
    Write-Host "   Mode: Data only"
}
else {
    Write-Host "   Mode: Full backup (schema + data)"
}

# Execute backup
try {
    & pg_dump $dumpArgs
    
    $size = (Get-Item $backupFile).Length / 1KB
    Write-Host "✅ Backup completed successfully" -ForegroundColor Green
    Write-Host "   Size: $([math]::Round($size, 2)) KB"
    
    # Keep only last 10 backups
    $oldBackups = Get-ChildItem $OutputDir -Filter "hartonomous_*.sql" | 
                  Sort-Object LastWriteTime -Descending | 
                  Select-Object -Skip 10
    
    if ($oldBackups) {
        Write-Host "🗑️  Removing $($oldBackups.Count) old backup(s)"
        $oldBackups | Remove-Item -Force
    }
}
catch {
    Write-Host "❌ Backup failed: $_" -ForegroundColor Red
    exit 1
}
