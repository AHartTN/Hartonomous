# ============================================================================
# Windows PostgreSQL Database Initialization
# Mirrors docker/init-db.sh for local development
# ============================================================================

$ErrorActionPreference = "Stop"

# Load database configuration from api/.env
$envFile = ".\api\.env"
if (-not (Test-Path $envFile)) {
    Write-Error "Configuration file not found: $envFile"
    exit 1
}

Write-Host "→ Loading configuration from $envFile..." -ForegroundColor Yellow

Get-Content $envFile | ForEach-Object {
    $line = $_.Trim()
    # Skip comments and empty lines
    if ($line -and -not $line.StartsWith('#')) {
        if ($line -match '^\s*([^=]+?)\s*=\s*(.+?)\s*$') {
            $name = $matches[1]
            $value = $matches[2]
            # Remove quotes if present
            $value = $value -replace '^["'']|["'']$', ''
            Set-Item -Path "env:$name" -Value $value
        }
    }
}

$PGHOST = $env:PGHOST
$PGPORT = $env:PGPORT
$PGUSER = $env:PGUSER
$PGDATABASE = $env:PGDATABASE
# PGPASSWORD already set in environment by the loop above

$SCHEMA_DIR = ".\schema"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Hartonomous Schema Init (Windows Local)" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Extensions
Write-Host "→ Installing extensions..." -ForegroundColor Yellow
@"
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gin;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
"@ | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE

# Core tables
Write-Host "→ Creating core tables..." -ForegroundColor Yellow
Get-Content "$SCHEMA_DIR\core\tables\001_atom.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
Get-Content "$SCHEMA_DIR\core\tables\002_atom_composition.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
Get-Content "$SCHEMA_DIR\core\tables\003_atom_relation.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE

# Additional tables
if (Test-Path "$SCHEMA_DIR\core\tables\004_history_tables.sql") {
    Get-Content "$SCHEMA_DIR\core\tables\004_history_tables.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}
if (Test-Path "$SCHEMA_DIR\core\tables\005_ooda_tables.sql") {
    Get-Content "$SCHEMA_DIR\core\tables\005_ooda_tables.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

# Spatial functions
Write-Host "→ Creating spatial functions..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\functions\spatial\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

# Atomization functions
Write-Host "→ Creating atomization functions..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\functions\atomization\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

# Composition functions
Write-Host "→ Creating composition functions..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\functions\composition\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

# Relation functions
Write-Host "→ Creating relation functions..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\functions\relations\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

# Optional function directories
$optionalDirs = @("ooda", "helpers", "gpu", "inference", "landmarks", "associations")
foreach ($dir in $optionalDirs) {
    $path = "$SCHEMA_DIR\core\functions\$dir"
    if (Test-Path $path) {
        Write-Host "→ Creating $dir functions..." -ForegroundColor Yellow
        Get-ChildItem "$path\*.sql" | ForEach-Object {
            Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
        }
    }
}

# Core Indexes
Write-Host "→ Creating spatial indexes..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\indexes\spatial\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

Write-Host "→ Creating core indexes..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\indexes\core\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

Write-Host "→ Creating composition indexes..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\indexes\composition\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

Write-Host "→ Creating relation indexes..." -ForegroundColor Yellow
Get-ChildItem "$SCHEMA_DIR\core\indexes\relations\*.sql" | ForEach-Object {
    Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}

# Triggers
Write-Host "→ Creating triggers..." -ForegroundColor Yellow
Get-Content "$SCHEMA_DIR\core\triggers\001_temporal_versioning.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
Get-Content "$SCHEMA_DIR\core\triggers\002_reference_counting.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
if (Test-Path "$SCHEMA_DIR\core\triggers\003_provenance_notify.sql") {
    Get-Content "$SCHEMA_DIR\core\triggers\003_provenance_notify.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
}
Get-Content "$SCHEMA_DIR\core\triggers\004_spatial_hilbert.sql" | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE

# Views
if (Test-Path "$SCHEMA_DIR\views") {
    Write-Host "→ Creating views..." -ForegroundColor Yellow
    Get-ChildItem "$SCHEMA_DIR\views\*.sql" | ForEach-Object {
        Get-Content $_.FullName | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE
    }
}

# Verification
Write-Host "" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Verifying Installation" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

@"
\echo 'Extensions:'
SELECT extname, extversion FROM pg_extension 
WHERE extname IN ('postgis', 'pg_trgm', 'btree_gin', 'btree_gist', 'pgcrypto')
ORDER BY extname;

\echo ''
\echo 'Core Tables:'
SELECT tablename FROM pg_tables 
WHERE schemaname = 'public' 
AND tablename IN ('atom', 'atom_composition', 'atom_relation')
ORDER BY tablename;

\echo ''
\echo 'Triggers:'
SELECT tgname, tgrelid::regclass FROM pg_trigger
WHERE tgisinternal = false
ORDER BY tgrelid::regclass::text, tgname;
"@ | psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE

Write-Host "" -ForegroundColor Green
Write-Host "✓ Schema initialization complete" -ForegroundColor Green
Write-Host "" -ForegroundColor Green
