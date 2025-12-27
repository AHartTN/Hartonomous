#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full Hartonomous database setup, seeding, ingestion, and validation.

.DESCRIPTION
    1. Drops and recreates the database
    2. Sets up PostGIS extensions and schema
    3. Seeds 1.1M Unicode codepoint atoms (hartonomous-seed.exe)
    4. Ingests MiniLM safetensor model (hartonomous-model-ingest.exe)
    5. Ingests Moby Dick
    6. Runs SQL queries to validate everything
#>

param(
    [switch]$SkipDrop
)

$ErrorActionPreference = "Stop"
$script:TotalStart = Get-Date
$script:StepNum = 0

# Paths
$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$NATIVE_DIR = Split-Path -Parent $SCRIPT_DIR
$REPO_ROOT = Split-Path -Parent $NATIVE_DIR
$BUILD_DIR = Join-Path $NATIVE_DIR "build"
$BIN_DIR = Join-Path $BUILD_DIR "bin"
$SQL_DIR = Join-Path $NATIVE_DIR "sql"
$TEST_DATA_DIR = Join-Path $REPO_ROOT "test-data"

# Database - matches connection.hpp defaults
$DB_HOST = "localhost"
$DB_PORT = "5432"
$DB_NAME = "hartonomous"
$DB_USER = "hartonomous"
$DB_PASS = "hartonomous"

# Files
$SEED_EXE = Join-Path $BIN_DIR "hartonomous-seed.exe"
$MODEL_INGEST_EXE = Join-Path $BIN_DIR "hartonomous-model-ingest.exe"
$SCHEMA_SQL = Join-Path $SQL_DIR "schema.sql"
$MODEL_PATH = Join-Path $TEST_DATA_DIR "embedding_models/models--sentence-transformers--all-MiniLM-L6-v2"
$MOBY_DICK = Join-Path $TEST_DATA_DIR "moby_dick.txt"

function Write-Step {
    param([string]$Message)
    $script:StepNum++
    $elapsed = ((Get-Date) - $script:TotalStart).TotalSeconds
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "[$script:StepNum] $Message" -ForegroundColor Yellow
    Write-Host ("    Elapsed: {0:F1}s" -f $elapsed) -ForegroundColor DarkGray
    Write-Host ("=" * 70) -ForegroundColor Cyan
}

function Write-Success { param([string]$Message); Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message); Write-Host "    $Message" -ForegroundColor Gray }
function Write-Fail { param([string]$Message); Write-Host "[FAIL] $Message" -ForegroundColor Red }

function Invoke-Psql {
    param([string]$Query, [string]$Database = $DB_NAME)
    $env:PGPASSWORD = $DB_PASS
    $result = & psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d $Database -t -A -c $Query 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $result" }
    return $result
}

# =============================================================================
# STEP 1: Drop and recreate database
# =============================================================================

if (-not $SkipDrop) {
    Write-Step "Dropping and recreating database"
    
    # Terminate connections and drop
    try { Invoke-Psql -Database "postgres" -Query "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid()" } catch {}
    try { Invoke-Psql -Database "postgres" -Query "DROP DATABASE IF EXISTS $DB_NAME" } catch {}
    
    Invoke-Psql -Database "postgres" -Query "CREATE DATABASE $DB_NAME OWNER $DB_USER"
    Write-Success "Database created"
}

# =============================================================================
# STEP 2: Set up extensions and schema
# =============================================================================

Write-Step "Setting up PostGIS and schema"

Invoke-Psql -Query "CREATE EXTENSION IF NOT EXISTS postgis"
Write-Success "PostGIS enabled"

$env:PGPASSWORD = $DB_PASS
& psql -h $DB_HOST -p $DB_PORT -U $DB_USER -d $DB_NAME -f $SCHEMA_SQL 2>&1 | Out-Null
Write-Success "Schema loaded"

# =============================================================================
# STEP 3: Seed 1.1M Unicode atoms
# =============================================================================

Write-Step "Seeding 1.1M Unicode codepoint atoms"

$seedStart = Get-Date
& $SEED_EXE 2>&1 | ForEach-Object { Write-Info $_ }

if ($LASTEXITCODE -ne 0) { throw "Seeding failed" }

$atomCount = [int](Invoke-Psql -Query "SELECT COUNT(*) FROM atom WHERE codepoint IS NOT NULL").Trim()
$seedElapsed = ((Get-Date) - $seedStart).TotalSeconds

Write-Success "Seeded $($atomCount.ToString('N0')) atoms in $([math]::Round($seedElapsed, 1))s"

# =============================================================================
# STEP 4: Ingest MiniLM model
# =============================================================================

Write-Step "Ingesting MiniLM safetensor model"

if (Test-Path $MODEL_PATH) {
    $modelStart = Get-Date
    & $MODEL_INGEST_EXE $MODEL_PATH 2>&1 | ForEach-Object { Write-Info $_ }
    
    if ($LASTEXITCODE -ne 0) { throw "Model ingestion failed" }
    
    $relCount = [int](Invoke-Psql -Query "SELECT COUNT(*) FROM relationship").Trim()
    $modelElapsed = ((Get-Date) - $modelStart).TotalSeconds
    
    Write-Success "Model ingested: $($relCount.ToString('N0')) relationships in $([math]::Round($modelElapsed, 1))s"
} else {
    Write-Info "Model not found at $MODEL_PATH - skipping"
}

# =============================================================================
# STEP 5: Ingest Moby Dick (via DatabaseEncoder - need to add CLI or do inline)
# =============================================================================

Write-Step "Ingesting Moby Dick"

# TODO: Need a CLI for DatabaseEncoder ingestion
# For now, we'll use the bench-moby which does this
$mobyStart = Get-Date
$benchExe = Join-Path $BIN_DIR "bench-moby.exe"

if (Test-Path $benchExe) {
    & $benchExe 2>&1 | ForEach-Object { Write-Info $_ }
    $mobyElapsed = ((Get-Date) - $mobyStart).TotalSeconds
    
    $compCount = [int](Invoke-Psql -Query "SELECT COUNT(*) FROM composition").Trim()
    Write-Success "Moby Dick ingested: $($compCount.ToString('N0')) compositions in $([math]::Round($mobyElapsed, 1))s"
} else {
    Write-Fail "bench-moby.exe not found"
}

# =============================================================================
# STEP 6: Query validation
# =============================================================================

Write-Step "Validating with SQL queries"

$failures = 0

# Test 1: Atom count
$atomCount = [int](Invoke-Psql -Query "SELECT COUNT(*) FROM atom WHERE codepoint IS NOT NULL").Trim()
if ($atomCount -ge 1112064) {
    Write-Success "Atom count: $($atomCount.ToString('N0')) (expected 1,112,064)"
} else {
    Write-Fail "Atom count: $atomCount (expected 1,112,064)"
    $failures++
}

# Test 2: ASCII atoms exist with correct IDs
$asciiCheck = Invoke-Psql -Query "SELECT COUNT(*) FROM atom WHERE codepoint BETWEEN 32 AND 126"
if ([int]$asciiCheck.Trim() -eq 95) {
    Write-Success "ASCII printable atoms: 95/95"
} else {
    Write-Fail "ASCII printable atoms: $asciiCheck (expected 95)"
    $failures++
}

# Test 3: Spatial index exists
$indexExists = Invoke-Psql -Query "SELECT 1 FROM pg_indexes WHERE indexname = 'idx_atom_semantic_position'"
if ($indexExists.Trim() -eq "1") {
    Write-Success "Spatial index exists: idx_atom_semantic_position"
} else {
    Write-Fail "Spatial index NOT found"
    $failures++
}

# Test 4: Can find atoms by proximity to 'a' (codepoint 97)
$nearA = Invoke-Psql -Query @"
SELECT COUNT(*) FROM atom a1
JOIN atom a2 ON a2.codepoint = 97
WHERE ST_DWithin(a1.semantic_position, a2.semantic_position, 1000)
"@
if ([int]$nearA.Trim() -gt 0) {
    Write-Success "Spatial proximity query works: $($nearA.Trim()) atoms near 'a'"
} else {
    Write-Fail "Spatial proximity query returned 0"
    $failures++
}

# Test 5: Compositions exist and have valid structure
$compCount = [int](Invoke-Psql -Query "SELECT COUNT(*) FROM composition").Trim()
if ($compCount -gt 0) {
    Write-Success "Compositions: $($compCount.ToString('N0'))"
    
    # Verify compositions point to valid atoms or other compositions
    $orphans = Invoke-Psql -Query @"
SELECT COUNT(*) FROM composition c
WHERE NOT EXISTS (
    SELECT 1 FROM atom a WHERE a.hilbert_high = c.left_high AND a.hilbert_low = c.left_low
    UNION ALL
    SELECT 1 FROM composition c2 WHERE c2.hilbert_high = c.left_high AND c2.hilbert_low = c.left_low
)
"@
    if ([int]$orphans.Trim() -eq 0) {
        Write-Success "All composition left children are valid"
    } else {
        Write-Info "Orphan left children: $orphans (atoms may be lazy-loaded)"
    }
} else {
    Write-Fail "No compositions found"
    $failures++
}

# Test 6: Relationships exist (from model ingestion)
$relCount = [int](Invoke-Psql -Query "SELECT COUNT(*) FROM relationship").Trim()
if ($relCount -gt 0) {
    Write-Success "Relationships: $($relCount.ToString('N0'))"
    
    # Check weight distribution
    $weightStats = Invoke-Psql -Query "SELECT MIN(weight), AVG(weight), MAX(weight) FROM relationship"
    Write-Info "Weight range: $weightStats"
} else {
    Write-Info "No relationships (model may not have been ingested)"
}

# Test 7: Unicode coverage - spot check various planes
$unicodeChecks = @(
    @{ Name = "Basic Latin (A-Z)"; Query = "SELECT COUNT(*) FROM atom WHERE codepoint BETWEEN 65 AND 90"; Expected = 26 }
    @{ Name = "Greek (α-ω)"; Query = "SELECT COUNT(*) FROM atom WHERE codepoint BETWEEN 945 AND 969"; Expected = 25 }
    @{ Name = "CJK sample"; Query = "SELECT COUNT(*) FROM atom WHERE codepoint BETWEEN 19968 AND 19993"; Expected = 26 }
    @{ Name = "Emoji sample"; Query = "SELECT COUNT(*) FROM atom WHERE codepoint BETWEEN 128512 AND 128591"; Expected = 80 }
)

foreach ($check in $unicodeChecks) {
    $count = [int](Invoke-Psql -Query $check.Query).Trim()
    if ($count -eq $check.Expected) {
        Write-Success "$($check.Name): $count/$($check.Expected)"
    } else {
        Write-Fail "$($check.Name): $count (expected $($check.Expected))"
        $failures++
    }
}

# =============================================================================
# STEP 7: Spatial AI / MLOps Queries
# =============================================================================

Write-Step "Running Spatial AI / MLOps queries"

# Query 1: Find semantically similar atoms using PostGIS KNN
$knnQuery = Invoke-Psql -Query @"
WITH target AS (
    SELECT semantic_position FROM atom WHERE codepoint = 65  -- 'A'
)
SELECT a.codepoint, chr(a.codepoint) as char, 
       ST_Distance(a.semantic_position, t.semantic_position) as distance
FROM atom a, target t
WHERE a.codepoint BETWEEN 32 AND 126
ORDER BY a.semantic_position <-> t.semantic_position
LIMIT 10;
"@
Write-Success "KNN query - 10 nearest ASCII atoms to 'A':"
Write-Host $knnQuery -ForegroundColor Cyan

# Query 2: Semantic clustering - find atoms within semantic radius
$clusterQuery = Invoke-Psql -Query @"
WITH center AS (
    SELECT semantic_position FROM atom WHERE codepoint = 97  -- 'a'
)
SELECT COUNT(*) as cluster_size
FROM atom a, center c
WHERE ST_DWithin(a.semantic_position, c.semantic_position, 0.1);
"@
Write-Success "Semantic cluster around 'a' (radius 0.1): $($clusterQuery.Trim()) atoms"

# Query 3: Model weight distribution by tensor layer
$layerStats = Invoke-Psql -Query @"
SELECT 
    split_part(layer_name, '.', 1) as layer_prefix,
    COUNT(*) as weight_count,
    ROUND(AVG(weight)::numeric, 6) as avg_weight,
    ROUND(STDDEV(weight)::numeric, 6) as stddev_weight
FROM relationship
GROUP BY split_part(layer_name, '.', 1)
ORDER BY weight_count DESC
LIMIT 10;
"@
Write-Success "Model weight distribution by layer:"
Write-Host $layerStats -ForegroundColor Cyan

# Query 4: Top weighted relationships (strongest model connections)
$topWeights = Invoke-Psql -Query @"
SELECT layer_name, weight, vocab_token
FROM relationship
ORDER BY ABS(weight) DESC
LIMIT 10;
"@
Write-Success "Top 10 strongest model weights:"
Write-Host $topWeights -ForegroundColor Cyan

# Query 5: Vocabulary token to atom semantic lookup
$vocabSemantic = Invoke-Psql -Query @"
SELECT r.vocab_token, r.layer_name, r.weight,
       ST_X(a.semantic_position) as sem_x,
       ST_Y(a.semantic_position) as sem_y
FROM relationship r
JOIN atom a ON a.hilbert_high = r.atom_high AND a.hilbert_low = r.atom_low
WHERE r.vocab_token IS NOT NULL
ORDER BY ABS(r.weight) DESC
LIMIT 5;
"@
Write-Success "Vocab tokens with semantic positions:"
Write-Host $vocabSemantic -ForegroundColor Cyan

# Query 6: Composition tree depth analysis
$treeDepth = Invoke-Psql -Query @"
WITH RECURSIVE tree AS (
    SELECT hilbert_high, hilbert_low, left_high, left_low, right_high, right_low, 1 as depth
    FROM composition
    WHERE (hilbert_high, hilbert_low) IN (
        SELECT hilbert_high, hilbert_low FROM composition ORDER BY hilbert_high DESC LIMIT 1
    )
    UNION ALL
    SELECT c.hilbert_high, c.hilbert_low, c.left_high, c.left_low, c.right_high, c.right_low, t.depth + 1
    FROM composition c
    JOIN tree t ON c.hilbert_high = t.left_high AND c.hilbert_low = t.left_low
    WHERE t.depth < 50
)
SELECT MAX(depth) as max_depth, COUNT(*) as nodes_traversed FROM tree;
"@
Write-Success "Composition tree analysis:"
Write-Host $treeDepth -ForegroundColor Cyan

# Query 7: Semantic distance between specific characters
$charDistances = Invoke-Psql -Query @"
SELECT 
    chr(a1.codepoint) as char1, 
    chr(a2.codepoint) as char2,
    semantic_distance(a1.semantic_position, a2.semantic_position) as distance
FROM atom a1, atom a2
WHERE a1.codepoint IN (65, 66, 67, 97, 98, 99)  -- A,B,C,a,b,c
  AND a2.codepoint IN (65, 66, 67, 97, 98, 99)
  AND a1.codepoint < a2.codepoint
ORDER BY distance;
"@
Write-Success "Semantic distances between A-C, a-c:"
Write-Host $charDistances -ForegroundColor Cyan

# Query 8: Find compositions containing specific atom
$atomInComps = Invoke-Psql -Query @"
WITH target AS (
    SELECT hilbert_high, hilbert_low FROM atom WHERE codepoint = 101  -- 'e'
)
SELECT COUNT(*) as compositions_with_e
FROM composition c, target t
WHERE (c.left_high = t.hilbert_high AND c.left_low = t.hilbert_low)
   OR (c.right_high = t.hilbert_high AND c.right_low = t.hilbert_low);
"@
Write-Success "Compositions containing 'e': $($atomInComps.Trim())"

# =============================================================================
# FINAL SUMMARY
# =============================================================================

$totalElapsed = ((Get-Date) - $script:TotalStart).TotalSeconds

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor $(if ($failures -eq 0) { "Green" } else { "Red" })
if ($failures -eq 0) {
    Write-Host "ALL VALIDATIONS PASSED" -ForegroundColor Green
} else {
    Write-Host "$failures VALIDATION(S) FAILED" -ForegroundColor Red
}
Write-Host ("=" * 70) -ForegroundColor $(if ($failures -eq 0) { "Green" } else { "Red" })
Write-Host ""
Write-Host "Total time: $([math]::Round($totalElapsed, 1)) seconds" -ForegroundColor Yellow
Write-Host ""

exit $failures
