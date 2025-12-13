# Hartonomous Database Maintenance Script
# Performs VACUUM ANALYZE to update query planner statistics and reclaim space

param(
    [switch]$Full,
    [switch]$Verbose,
    [switch]$Scheduled
)

$ErrorActionPreference = "Stop"

# Ensure PostgreSQL bin is in PATH
$pgBin = "D:\PostgreSQL\18\bin"
$env:PATH = "$pgBin;$env:PATH"

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

if (-not $Scheduled) {
    Write-Host "🔧 Starting database maintenance..." -ForegroundColor Cyan
    Write-Host "   Time: $timestamp"
}

# Build VACUUM command
$vacuumCmd = "VACUUM"
if ($Full) {
    $vacuumCmd += " FULL"
    if (-not $Scheduled) { Write-Host "   Mode: FULL (locks table, reclaims all dead space)" }
}
else {
    if (-not $Scheduled) { Write-Host "   Mode: Standard (concurrent, faster)" }
}

if ($Verbose) {
    $vacuumCmd += " VERBOSE"
}

$vacuumCmd += " ANALYZE atom;"

# Execute maintenance
try {
    $output = psql -h localhost -U hartonomous -d hartonomous -c $vacuumCmd 2>&1
    
    # Parse statistics from output
    if ($Verbose) {
        $deadRows = ($output | Select-String "dead row versions cannot be removed yet" | Select-Object -First 1)
        $pagesRemoved = ($output | Select-String "pages removed" | Select-Object -First 1)
    }
    
    # Get table statistics
    $stats = psql -h localhost -U hartonomous -d hartonomous -t -c "
        SELECT 
            pg_size_pretty(pg_total_relation_size('atom')) as total_size,
            (SELECT COUNT(*) FROM atom) as atom_count,
            n_live_tup,
            n_dead_tup,
            last_vacuum,
            last_autovacuum,
            last_analyze,
            last_autoanalyze
        FROM pg_stat_user_tables
        WHERE relname = 'atom';
    " 2>$null
    
    if (-not $Scheduled) {
        Write-Host "✅ Maintenance completed" -ForegroundColor Green
        Write-Host "   Statistics: $stats"
    }
    
    # Log to file for scheduled runs
    if ($Scheduled) {
        $logFile = ".\logs\vacuum_$(Get-Date -Format 'yyyyMMdd').log"
        $logDir = Split-Path $logFile -Parent
        if (-not (Test-Path $logDir)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
        Add-Content -Path $logFile -Value "[$timestamp] VACUUM completed: $stats"
    }
}
catch {
    Write-Host "❌ Maintenance failed: $_" -ForegroundColor Red
    exit 1
}

# Update index statistics
try {
    psql -h localhost -U hartonomous -d hartonomous -c "REINDEX INDEX CONCURRENTLY idx_atoms_geom_gist;" 2>&1 | Out-Null
    if (-not $Scheduled) {
        Write-Host "✅ GiST index rebuilt" -ForegroundColor Green
    }
}
catch {
    # REINDEX CONCURRENTLY may fail if already running, ignore
}
