#!/usr/bin/env pwsh
# Hartonomous Database Setup Script (Windows/PowerShell)
# Usage: .\setup-database.ps1

param(
    [string]$DbName = "hartonomous",
    [string]$DbUser = "postgres",
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [switch]$DropExisting,
    [switch]$Help
)

function Show-Help {
    Write-Host @"
Hartonomous Database Setup Script

Usage: .\setup-database.ps1 [OPTIONS]

Options:
  -DbName <name>      Database name (default: hartonomous)
  -DbUser <user>      Database user (default: postgres)
  -DbHost <host>      Database host (default: localhost)
  -DbPort <port>      Database port (default: 5432)
  -DropExisting       Drop existing database if it exists
  -Help               Show this help message

Examples:
  .\setup-database.ps1                  # Create hartonomous database
  .\setup-database.ps1 -DropExisting    # Drop and recreate
  .\setup-database.ps1 -DbName test_db  # Use custom database name

"@
    exit 0
}

if ($Help) {
    Show-Help
}

$ErrorActionPreference = "Stop"

function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Error { Write-Host $args -ForegroundColor Red }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Warning { Write-Host $args -ForegroundColor Yellow }

Write-Info "=== Hartonomous Database Setup ==="
Write-Info ""

# Check PostgreSQL
Write-Info "Checking PostgreSQL..."
try {
    $pgVersion = psql --version | Select-String -Pattern "psql \(PostgreSQL\) (\d+\.\d+)"
    Write-Success "✓ PostgreSQL found: $($pgVersion.Matches.Groups[1].Value)"

    $versionNum = [decimal]$pgVersion.Matches.Groups[1].Value
    if ($versionNum -lt 15.0) {
        Write-Warning "⚠ PostgreSQL 15+ recommended (found $versionNum)"
    }
} catch {
    Write-Error "✗ PostgreSQL not found or not in PATH"
    Write-Error "  Please install PostgreSQL 15+ and add it to PATH"
    exit 1
}

# Check PostGIS
Write-Info "Checking PostGIS..."
$checkPostGIS = @"
SELECT installed_version FROM pg_available_extensions WHERE name = 'postgis';
"@

try {
    $postgisVersion = psql -U $DbUser -h $DbHost -p $DbPort -d postgres -t -c $checkPostGIS | Select-String -Pattern "\d+\.\d+"
    if ($postgisVersion) {
        Write-Success "✓ PostGIS found: $($postgisVersion.Matches[0].Value)"
    } else {
        Write-Warning "⚠ PostGIS not found"
        Write-Warning "  Please install PostGIS 3.3+"
        Write-Warning "  Download: https://postgis.net/windows_downloads/"
    }
} catch {
    Write-Warning "⚠ Could not check PostGIS version"
}

# Drop existing if requested
if ($DropExisting) {
    Write-Info ""
    Write-Info "Dropping existing database..."
    $dropCmd = "DROP DATABASE IF EXISTS $DbName;"
    try {
        psql -U $DbUser -h $DbHost -p $DbPort -d postgres -c $dropCmd
        Write-Success "✓ Dropped existing database"
    } catch {
        Write-Warning "⚠ Could not drop database (may not exist)"
    }
}

# Create database
Write-Info ""
Write-Info "Creating database: $DbName..."
$createCmd = "CREATE DATABASE $DbName;"
try {
    psql -U $DbUser -h $DbHost -p $DbPort -d postgres -c $createCmd
    Write-Success "✓ Database created"
} catch {
    Write-Error "✗ Could not create database"
    Write-Error "  Database may already exist. Use -DropExisting to recreate."
    exit 1
}

# Install PostGIS extension
Write-Info ""
Write-Info "Installing PostGIS extension..."
try {
    psql -U $DbUser -h $DbHost -p $DbPort -d $DbName -c "CREATE EXTENSION IF NOT EXISTS postgis;"
    Write-Success "✓ PostGIS extension installed"
} catch {
    Write-Error "✗ Could not install PostGIS extension"
    Write-Error "  Make sure PostGIS is installed on your system"
    exit 1
}

# Verify PostGIS
Write-Info "Verifying PostGIS..."
$verifyCmd = "SELECT PostGIS_Version();"
try {
    $version = psql -U $DbUser -h $DbHost -p $DbPort -d $DbName -t -c $verifyCmd
    Write-Success "✓ PostGIS verified: $($version.Trim())"
} catch {
    Write-Error "✗ PostGIS verification failed"
    exit 1
}

# Run schema scripts
Write-Info ""
Write-Info "Applying database schema..."

$schemaFiles = @(
    "PostgresExtension/schema/hartonomous_schema.sql",
    "PostgresExtension/schema/relations_schema.sql",
    "PostgresExtension/schema/postgis_spatial_functions.sql",
    "PostgresExtension/schema/security_model.sql"
)

foreach ($schemaFile in $schemaFiles) {
    if (Test-Path $schemaFile) {
        Write-Info "  Applying $schemaFile..."
        try {
            psql -U $DbUser -h $DbHost -p $DbPort -d $DbName -f $schemaFile
            Write-Success "  ✓ Applied $schemaFile"
        } catch {
            Write-Error "  ✗ Failed to apply $schemaFile"
            exit 1
        }
    } else {
        Write-Warning "  ⚠ Schema file not found: $schemaFile"
    }
}

# Create indexes
Write-Info ""
Write-Info "Creating indexes..."
$indexSQL = @"
-- Spatial indexes
CREATE INDEX IF NOT EXISTS idx_atoms_s3_position
    ON atoms USING GIST (st_makepoint(s3_x, s3_y, s3_z));

CREATE INDEX IF NOT EXISTS idx_compositions_centroid
    ON compositions USING GIST (st_makepoint(centroid_x, centroid_y, centroid_z));

-- Hilbert indexes
CREATE INDEX IF NOT EXISTS idx_atoms_hilbert
    ON atoms USING BTREE (hilbert_index);

CREATE INDEX IF NOT EXISTS idx_compositions_hilbert
    ON compositions USING BTREE (hilbert_index);

-- Hash indexes
CREATE INDEX IF NOT EXISTS idx_atoms_hash
    ON atoms USING BTREE (hash);

CREATE INDEX IF NOT EXISTS idx_compositions_hash
    ON compositions USING BTREE (hash);

CREATE INDEX IF NOT EXISTS idx_relations_hash
    ON relations USING BTREE (hash);
"@

try {
    psql -U $DbUser -h $DbHost -p $DbPort -d $DbName -c $indexSQL
    Write-Success "✓ Indexes created"
} catch {
    Write-Error "✗ Failed to create indexes"
    exit 1
}

# Verify installation
Write-Info ""
Write-Info "Verifying installation..."
$verifySQL = @"
SELECT COUNT(*) AS table_count FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE';
"@

try {
    $tableCount = psql -U $DbUser -h $DbHost -p $DbPort -d $DbName -t -c $verifySQL
    Write-Success "✓ Verification complete: $($tableCount.Trim()) tables created"
} catch {
    Write-Error "✗ Verification failed"
    exit 1
}

# Summary
Write-Info ""
Write-Success "=== Database Setup Complete ==="
Write-Info ""
Write-Info "Database: $DbName"
Write-Info "Host: $DbHost:$DbPort"
Write-Info "User: $DbUser"
Write-Info ""
Write-Info "Connection string:"
Write-Info "  psql -U $DbUser -h $DbHost -p $DbPort -d $DbName"
Write-Info ""
Write-Info "Next steps:"
Write-Info "  1. Test connection: psql -U $DbUser -d $DbName -c 'SELECT PostGIS_Version();'"
Write-Info "  2. Run example: .\build\release-native\Engine\example_unicode_projection"
Write-Info "  3. Ingest data using ContentIngester API"
Write-Info ""
