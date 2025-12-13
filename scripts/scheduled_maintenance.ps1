# Windows Task Scheduler script for automated maintenance
# Schedule this to run nightly during low-traffic hours

param(
    [string]$LogDir = ".\logs"
)

$ErrorActionPreference = "Stop"

# Ensure log directory exists
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$logFile = Join-Path $LogDir "maintenance_$(Get-Date -Format 'yyyyMMdd').log"

function Write-Log {
    param([string]$Message)
    $logLine = "[$timestamp] $Message"
    Add-Content -Path $logFile -Value $logLine
    Write-Host $logLine
}

Write-Log "=== Starting Scheduled Maintenance ==="

try {
    # Step 1: Backup database
    Write-Log "Creating database backup..."
    & ".\scripts\backup_database.ps1" -OutputDir ".\backups" -ErrorAction Stop
    Write-Log "✅ Backup completed"
    
    # Step 2: VACUUM ANALYZE
    Write-Log "Running VACUUM ANALYZE..."
    & ".\tools\vacuum_analyze.ps1" -Scheduled -ErrorAction Stop
    Write-Log "✅ VACUUM completed"
    
    # Step 3: Rebuild indexes (weekly - check day of week)
    $dayOfWeek = (Get-Date).DayOfWeek
    if ($dayOfWeek -eq "Sunday") {
        Write-Log "Running weekly index rebuild..."
        
        $pgBin = "D:\PostgreSQL\18\bin"
        $env:PATH = "$pgBin;$env:PATH"
        
        psql -h localhost -U hartonomous -d hartonomous -c "REINDEX INDEX CONCURRENTLY idx_atoms_geom_gist;" 2>&1 | Out-Null
        psql -h localhost -U hartonomous -d hartonomous -c "REINDEX INDEX CONCURRENTLY idx_atoms_hilbert;" 2>&1 | Out-Null
        
        Write-Log "✅ Index rebuild completed"
    }
    
    # Step 4: Collect statistics
    Write-Log "Gathering system statistics..."
    $stats = psql -h localhost -U hartonomous -d hartonomous -t -c "
        SELECT 
            COUNT(*) as atoms,
            pg_size_pretty(pg_database_size('hartonomous')) as db_size,
            (SELECT COUNT(*) FROM pg_stat_activity) as connections
        FROM atom;
    " 2>$null
    
    Write-Log "Stats: $stats"
    
    # Step 5: Check for anomalies
    Write-Log "Running anomaly detection..."
    $invalid = psql -h localhost -U hartonomous -d hartonomous -t -c "SELECT COUNT(*) FROM atom WHERE NOT ST_IsValid(geom);" 2>$null
    $invalid = $invalid.Trim()
    
    if ([int]$invalid -gt 0) {
        Write-Log "⚠️  WARNING: $invalid atoms with invalid geometry"
    }
    
    Write-Log "=== Maintenance Completed Successfully ==="
    
} catch {
    Write-Log "❌ ERROR: $_"
    Write-Log "=== Maintenance Failed ==="
    exit 1
}

# Cleanup old logs (keep last 30 days)
Get-ChildItem $LogDir -Filter "maintenance_*.log" |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force

Write-Log "Log cleanup completed"
