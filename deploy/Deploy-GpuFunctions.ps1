# Deploy PL/Python GPU Functions to PostgreSQL (Windows)
# Requires Administrator privileges

param(
    [string]$DbHost = "localhost",
    [string]$DbPort = "5432",
    [string]$DbName = "hartonomous",
    [string]$DbUser = "postgres",
    [string]$DeployDir = "C:\ProgramData\Hartonomous\Functions\PlPython"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================="
Write-Host "Deploying PL/Python GPU Functions (Windows)"
Write-Host "==========================================="

# Get script paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PythonFunctionsDir = Join-Path $ScriptDir "..\Hartonomous.Data\Functions\PlPython"
$SqlFunctionsFile = Join-Path $ScriptDir "..\Hartonomous.Data\Migrations\20250101000002_AddGpuFunctions.sql"

Write-Host ""
Write-Host "Configuration:"
Write-Host "  Database: $DbUser@$DbHost:$DbPort/$DbName"
Write-Host "  Python Functions: $PythonFunctionsDir"
Write-Host "  Deploy Directory: $DeployDir"
Write-Host ""

# Step 1: Check PostgreSQL connectivity
Write-Host "[1/6] Checking PostgreSQL connectivity..."
$env:PGPASSWORD = Read-Host "Enter PostgreSQL password" -AsSecureString
$env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($env:PGPASSWORD))

$testQuery = "SELECT version();"
$result = & psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -c $testQuery 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "Cannot connect to PostgreSQL: $result"
    exit 1
}
Write-Host "✓ PostgreSQL is accessible" -ForegroundColor Green

# Step 2: Check plpython3u extension
Write-Host ""
Write-Host "[2/6] Checking plpython3u extension..."
$checkExtQuery = "SELECT COUNT(*) FROM pg_available_extensions WHERE name = 'plpython3u';"
$extAvailable = & psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -t -c $checkExtQuery | Out-String
$extAvailable = $extAvailable.Trim()

if ($extAvailable -eq "0") {
    Write-Error "plpython3u extension not available. PostgreSQL must be compiled with Python support."
    exit 1
}

# Check if installed
$checkInstQuery = "SELECT COUNT(*) FROM pg_extension WHERE extname = 'plpython3u';"
$extInstalled = & psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -t -c $checkInstQuery | Out-String
$extInstalled = $extInstalled.Trim()

if ($extInstalled -eq "0") {
    Write-Host "Installing plpython3u extension..."
    & psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -c "CREATE EXTENSION IF NOT EXISTS plpython3u;"
}
Write-Host "✓ plpython3u extension is installed" -ForegroundColor Green

# Step 3: Create deployment directory
Write-Host ""
Write-Host "[3/6] Creating deployment directory..."
if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
}
Write-Host "✓ Deployment directory ready: $DeployDir" -ForegroundColor Green

# Step 4: Copy Python files
Write-Host ""
Write-Host "[4/6] Copying Python function files..."
Get-ChildItem "$PythonFunctionsDir\*.py" | ForEach-Object {
    Copy-Item $_.FullName -Destination $DeployDir -Force
    Write-Host "  Copied: $($_.Name)"
}
Write-Host "✓ Python files deployed" -ForegroundColor Green

# Step 5: Update function path in SQL
Write-Host ""
Write-Host "[5/6] Updating function path configuration..."
$sqlContent = Get-Content $SqlFunctionsFile -Raw
$deployDirEscaped = $DeployDir -replace '\\', '\\\\'
$sqlContent = $sqlContent -replace '/var/lib/hartonomous/functions/plpython', $deployDirEscaped
$tempSqlFile = [System.IO.Path]::GetTempFileName()
$sqlContent | Out-File -FilePath $tempSqlFile -Encoding UTF8
Write-Host "✓ Function path configured" -ForegroundColor Green

# Step 6: Execute SQL migration
Write-Host ""
Write-Host "[6/6] Creating PostgreSQL functions..."
& psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -f $tempSqlFile

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create PostgreSQL functions"
    Remove-Item $tempSqlFile
    exit 1
}

Remove-Item $tempSqlFile
Write-Host "✓ PostgreSQL functions created" -ForegroundColor Green

# Step 7: Test GPU availability
Write-Host ""
Write-Host "==========================================="
Write-Host "Testing GPU Availability"
Write-Host "==========================================="
& psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -c "SELECT * FROM gpu_check_availability();"

# Step 8: List installed functions
Write-Host ""
Write-Host "==========================================="
Write-Host "Installed GPU Functions"
Write-Host "==========================================="
$listFunctionsQuery = @"
SELECT 
    routine_name,
    routine_type,
    data_type as return_type
FROM information_schema.routines
WHERE routine_schema = 'public'
  AND routine_name LIKE 'gpu_%'
ORDER BY routine_name;
"@

& psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -c $listFunctionsQuery

Write-Host ""
Write-Host "==========================================="
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "==========================================="
Write-Host ""
Write-Host "GPU-accelerated functions are now available:"
Write-Host "  • gpu_spatial_knn           - K-nearest neighbors search"
Write-Host "  • gpu_spatial_clustering    - DBSCAN clustering"
Write-Host "  • gpu_similarity_search     - Cosine similarity"
Write-Host "  • gpu_bpe_learn             - BPE vocabulary learning"
Write-Host "  • gpu_hilbert_index_batch   - Hilbert curve indexing"
Write-Host "  • gpu_check_availability    - GPU status check"
Write-Host "  • update_hilbert_indices_gpu       - Batch index update helper"
Write-Host "  • detect_landmarks_from_clustering - Landmark detection helper"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Install Python GPU libraries: pip install cupy-cuda12x cuml-cu12"
Write-Host "  2. Verify GPU availability: SELECT * FROM gpu_check_availability();"
Write-Host "  3. Test with sample query: SELECT * FROM gpu_spatial_knn(0, 0, 0, 10);"
Write-Host ""

# Clear password from environment
$env:PGPASSWORD = $null
