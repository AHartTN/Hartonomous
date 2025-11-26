# ============================================================================
# Hartonomous Database Initialization Script (PowerShell)
# 
# Initializes PostgreSQL database with all required schema, functions, and
# indexes for Hartonomous system.
#
# Usage:
#   .\scripts\Initialize-Database.ps1 [-Environment <env>] [-Verbose]
#
# Environments:
#   - localhost (default)
#   - development
#   - staging
#   - production
#
# Copyright (c) 2025 Anthony Hart. All Rights Reserved.
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('localhost', 'development', 'staging', 'production')]
    [string]$Environment = 'localhost',
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipValidation
)

$ErrorActionPreference = 'Stop'

# ============================================================================
# Configuration
# ============================================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$schemaDir = Join-Path $projectRoot 'schema'

Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Hartonomous Database Initialization" -ForegroundColor Blue
Write-Host "  Environment: $Environment" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

# Load .env file
$envFile = Join-Path $projectRoot '.env'
if (Test-Path $envFile) {
    Write-Host "? Loading .env file" -ForegroundColor Green
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^([^#][^=]+)=(.*)$') {
            $key = $matches[1].Trim()
            $value = $matches[2].Trim()
            [Environment]::SetEnvironmentVariable($key, $value, 'Process')
        }
    }
}

# Database connection
$env:PGHOST = if ($env:PGHOST) { $env:PGHOST } else { 'localhost' }
$env:PGPORT = if ($env:PGPORT) { $env:PGPORT } else { '5432' }
$env:PGUSER = if ($env:PGUSER) { $env:PGUSER } else { 'hartonomous' }
$env:PGDATABASE = if ($env:PGDATABASE) { $env:PGDATABASE } else { 'hartonomous' }

# Validate PostgreSQL connection
Write-Host "`n?  Testing database connection..." -ForegroundColor Yellow

try {
    $testQuery = "SELECT 1"
    $null = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -c $testQuery 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL connection failed"
    }
    
    Write-Host "? Database connection successful" -ForegroundColor Green
}
catch {
    Write-Host "? Failed to connect to PostgreSQL" -ForegroundColor Red
    Write-Host "   Host: $($env:PGHOST):$($env:PGPORT)" -ForegroundColor Red
    Write-Host "   User: $env:PGUSER" -ForegroundColor Red
    Write-Host "   Database: $env:PGDATABASE" -ForegroundColor Red
    exit 1
}

# ============================================================================
# Helper Functions
# ============================================================================

function Execute-SqlFile {
    param(
        [Parameter(Mandatory=$true)]
        [string]$FilePath,
        
        [Parameter(Mandatory=$true)]
        [string]$Description
    )
    
    Write-Host "? $Description" -ForegroundColor Blue
    
    if (-not (Test-Path $FilePath)) {
        Write-Host "? File not found: $FilePath" -ForegroundColor Red
        return $false
    }
    
    try {
        $output = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -f $FilePath 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "? Success" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "? Failed to execute: $FilePath" -ForegroundColor Red
            Write-Host $output -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "? Error: $_" -ForegroundColor Red
        return $false
    }
}

function Execute-SqlPattern {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Pattern,
        
        [Parameter(Mandatory=$true)]
        [string]$Description
    )
    
    Write-Host "`n??? $Description ???" -ForegroundColor Yellow
    
    $files = Get-ChildItem -Path $Pattern -ErrorAction SilentlyContinue
    $count = 0
    
    foreach ($file in $files) {
        $filename = $file.Name
        if (Execute-SqlFile -FilePath $file.FullName -Description "  $filename") {
            $count++
        }
    }
    
    if ($count -eq 0) {
        Write-Host "?  No files found matching pattern" -ForegroundColor Yellow
    }
    
    Write-Host "? Completed $count files" -ForegroundColor Green
}

# ============================================================================
# Schema Initialization
# ============================================================================

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 1: Extensions" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlFile -FilePath "$schemaDir\extensions\001_postgis.sql" -Description "PostGIS (spatial types)"
Execute-SqlFile -FilePath "$schemaDir\extensions\003_pg_trgm.sql" -Description "pg_trgm (trigram similarity)"
Execute-SqlFile -FilePath "$schemaDir\extensions\004_btree_gin.sql" -Description "btree_gin (GIN indexes)"
Execute-SqlFile -FilePath "$schemaDir\extensions\005_pgcrypto.sql" -Description "pgcrypto (SHA-256 hashing)"

# PL/Python (optional)
try {
    $null = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -c "CREATE EXTENSION IF NOT EXISTS plpython3u" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? PL/Python3u extension enabled" -ForegroundColor Green
    }
}
catch {
    Write-Host "?  PL/Python3u not available (optional)" -ForegroundColor Yellow
}

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 2: Custom Types" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlFile -FilePath "$schemaDir\types\001_modality_type.sql" -Description "modality_type enum"
Execute-SqlFile -FilePath "$schemaDir\types\002_relation_type.sql" -Description "relation_type enum"

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 3: Core Tables" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlFile -FilePath "$schemaDir\core\tables\001_atom.sql" -Description "atom table"
Execute-SqlFile -FilePath "$schemaDir\core\tables\002_atom_composition.sql" -Description "atom_composition table"
Execute-SqlFile -FilePath "$schemaDir\core\tables\003_atom_relation.sql" -Description "atom_relation table"
Execute-SqlFile -FilePath "$schemaDir\core\tables\004_history_tables.sql" -Description "history tables"
Execute-SqlFile -FilePath "$schemaDir\core\tables\005_ooda_tables.sql" -Description "OODA loop tables"

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 4: Indexes" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlPattern -Pattern "$schemaDir\core\indexes\core\*.sql" -Description "Core Indexes"
Execute-SqlPattern -Pattern "$schemaDir\core\indexes\spatial\*.sql" -Description "Spatial Indexes"
Execute-SqlPattern -Pattern "$schemaDir\core\indexes\relations\*.sql" -Description "Relation Indexes"
Execute-SqlPattern -Pattern "$schemaDir\core\indexes\composition\*.sql" -Description "Composition Indexes"

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 5: Functions" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlPattern -Pattern "$schemaDir\core\functions\atomization\*.sql" -Description "Atomization Functions"
Execute-SqlPattern -Pattern "$schemaDir\core\functions\spatial\*.sql" -Description "Spatial Functions"
Execute-SqlPattern -Pattern "$schemaDir\core\functions\relations\*.sql" -Description "Relation Functions"
Execute-SqlPattern -Pattern "$schemaDir\core\functions\composition\*.sql" -Description "Composition Functions"
Execute-SqlPattern -Pattern "$schemaDir\core\functions\ooda\*.sql" -Description "OODA Loop Functions"
Execute-SqlPattern -Pattern "$schemaDir\core\functions\provenance\*.sql" -Description "Provenance Functions"

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 6: Triggers" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlFile -FilePath "$schemaDir\core\triggers\001_temporal_versioning.sql" -Description "Temporal versioning triggers"
Execute-SqlFile -FilePath "$schemaDir\core\triggers\002_reference_counting.sql" -Description "Reference counting triggers"
Execute-SqlFile -FilePath "$schemaDir\core\triggers\003_provenance_notify.sql" -Description "Provenance notification triggers"

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "  Step 7: Views" -ForegroundColor Blue
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue

Execute-SqlPattern -Pattern "$schemaDir\views\*.sql" -Description "Analytical Views"

# ============================================================================
# Validation
# ============================================================================

if (-not $SkipValidation) {
    Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
    Write-Host "  Validation" -ForegroundColor Blue
    Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue
    
    # Count tables
    $tableCount = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -t -c @"
        SELECT COUNT(*) FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_type = 'BASE TABLE'
"@
    
    # Count functions
    $functionCount = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -t -c @"
        SELECT COUNT(*) FROM pg_proc 
        WHERE pronamespace = (SELECT oid FROM pg_namespace WHERE nspname = 'public')
"@
    
    # Count indexes
    $indexCount = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -t -c @"
        SELECT COUNT(*) FROM pg_indexes 
        WHERE schemaname = 'public'
"@
    
    Write-Host "? Tables:    $($tableCount.Trim())" -ForegroundColor Green
    Write-Host "? Functions: $($functionCount.Trim())" -ForegroundColor Green
    Write-Host "? Indexes:   $($indexCount.Trim())" -ForegroundColor Green
    
    # Test basic atomization
    Write-Host "`n? Testing atomization..." -ForegroundColor Yellow
    try {
        $testResult = & psql -h $env:PGHOST -p $env:PGPORT -U $env:PGUSER -d $env:PGDATABASE -t -c @"
            SELECT atomize_value('\x48'::bytea, 'H', '{"modality": "character"}'::jsonb);
"@
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "? Atomization test successful (atom_id: $($testResult.Trim()))" -ForegroundColor Green
        }
        else {
            Write-Host "? Atomization test failed" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "? Atomization test failed: $_" -ForegroundColor Red
    }
}

# ============================================================================
# Summary
# ============================================================================

Write-Host "`n???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host "?  Database initialization complete!" -ForegroundColor Green
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue
Write-Host ""
Write-Host "  Database:  $env:PGDATABASE" -ForegroundColor White
Write-Host "  Host:      $($env:PGHOST):$($env:PGPORT)" -ForegroundColor White
Write-Host "  User:      $env:PGUSER" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Start the API:     " -NoNewline; Write-Host "uvicorn api.main:app --reload" -ForegroundColor Blue
Write-Host "  2. View docs:         " -NoNewline; Write-Host "http://localhost:8000/docs" -ForegroundColor Blue
Write-Host "  3. Health check:      " -NoNewline; Write-Host "Invoke-WebRequest http://localhost:8000/v1/health" -ForegroundColor Blue
Write-Host ""
Write-Host "???????????????????????????????????????????????????????????" -ForegroundColor Blue
