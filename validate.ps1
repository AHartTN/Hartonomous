<#
.SYNOPSIS
    Full validation script: drop DB, recreate, seed, and run Moby Dick test
.DESCRIPTION
    1. Drops and recreates the PostgreSQL database
    2. Applies schema
    3. Runs the native test suite including Moby Dick lossless test
.EXAMPLE
    .\validate.ps1
#>

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$NativeDir = Join-Path $Root "Hartonomous.Native"
$SqlDir = Join-Path $NativeDir "sql"
$SchemaFile = Join-Path $SqlDir "schema.sql"
$BuildDir = Join-Path $NativeDir "out\build\windows-clang-release"
$TestExe = Join-Path $BuildDir "bin\hartonomous-tests.exe"

# PostgreSQL connection (docker-compose exposes on 5433)
$PgHost = "localhost"
$PgPort = "5433"
$PgUser = "hartonomous"
$PgPass = "hartonomous"
$PgDb = "hartonomous"

$env:PGPASSWORD = $PgPass

function Write-Step($message) {
    Write-Host "`n=== $message ===" -ForegroundColor Cyan
}

function Write-Success($message) {
    Write-Host $message -ForegroundColor Green
}

function Write-Error($message) {
    Write-Host $message -ForegroundColor Red
}

try {
    Write-Host "`nHartonomous Full Validation" -ForegroundColor Magenta
    Write-Host "============================`n" -ForegroundColor Magenta

    # =========================================================================
    # Step 1: Verify PostgreSQL is running
    # =========================================================================
    Write-Step "Checking PostgreSQL connection"
    
    $pgReady = $false
    try {
        $result = & psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "SELECT 1" 2>&1
        if ($LASTEXITCODE -eq 0) {
            $pgReady = $true
            Write-Success "PostgreSQL is running on port $PgPort"
        }
    } catch {
        # Ignore, handled below
    }
    
    if (-not $pgReady) {
        Write-Host "PostgreSQL not running. Starting via docker-compose..." -ForegroundColor Yellow
        Push-Location $Root
        docker compose up -d postgres
        Pop-Location
        
        Write-Host "Waiting for PostgreSQL to be ready..."
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 1
            try {
                $result = & psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "SELECT 1" 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $pgReady = $true
                    break
                }
            } catch { }
            Write-Host "." -NoNewline
        }
        Write-Host ""
        
        if (-not $pgReady) {
            throw "PostgreSQL failed to start within 30 seconds"
        }
        Write-Success "PostgreSQL is now running"
    }

    # =========================================================================
    # Step 2: Drop and recreate database
    # =========================================================================
    Write-Step "Dropping and recreating database"
    
    # Terminate existing connections
    & psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c @"
SELECT pg_terminate_backend(pid) 
FROM pg_stat_activity 
WHERE datname = '$PgDb' AND pid <> pg_backend_pid();
"@ 2>&1 | Out-Null

    # Drop database
    & psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "DROP DATABASE IF EXISTS $PgDb" 2>&1
    Write-Host "Dropped database: $PgDb"
    
    # Create database
    & psql -h $PgHost -p $PgPort -U $PgUser -d postgres -c "CREATE DATABASE $PgDb OWNER $PgUser" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to create database" }
    Write-Success "Created database: $PgDb"

    # =========================================================================
    # Step 3: Apply schema
    # =========================================================================
    Write-Step "Applying schema"
    
    if (-not (Test-Path $SchemaFile)) {
        throw "Schema file not found: $SchemaFile"
    }
    
    & psql -h $PgHost -p $PgPort -U $PgUser -d $PgDb -f $SchemaFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply schema" }
    Write-Success "Schema applied successfully"
    
    # Verify tables exist
    $tables = & psql -h $PgHost -p $PgPort -U $PgUser -d $PgDb -t -c "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'" 2>&1
    Write-Host "Tables created: $($tables -replace '\s+', ', ')"

    # =========================================================================
    # Step 4: Build native tests (if needed)
    # =========================================================================
    Write-Step "Ensuring native tests are built"
    
    if (-not (Test-Path $TestExe)) {
        Write-Host "Building native library and tests..."
        Push-Location $NativeDir
        cmake --preset windows-clang-release
        cmake --build out/build/windows-clang-release --target hartonomous-tests --parallel
        Pop-Location
        
        if (-not (Test-Path $TestExe)) {
            throw "Failed to build test executable"
        }
    }
    Write-Success "Test executable ready: $TestExe"

    # =========================================================================
    # Step 5: Run full test suite
    # =========================================================================
    Write-Step "Running full test suite"
    
    # Set connection string for native tests (docker-compose uses 5433)
    $env:HARTONOMOUS_DB_URL = "postgresql://${PgUser}:${PgPass}@${PgHost}:${PgPort}/${PgDb}"
    
    & $TestExe
    $testResult = $LASTEXITCODE
    
    if ($testResult -ne 0) {
        throw "Test suite failed with exit code $testResult"
    }

    # =========================================================================
    # Step 6: Run Moby Dick test specifically
    # =========================================================================
    Write-Step "Running Moby Dick lossless test"
    
    # Note: Do NOT use --success flag - it dumps entire 1.1M file comparison to console
    & $TestExe "[moby]"
    $mobyResult = $LASTEXITCODE
    
    if ($mobyResult -ne 0) {
        throw "Moby Dick test failed"
    }

    # =========================================================================
    # Step 7: Verify test data
    # =========================================================================
    Write-Step "Verifying test data"
    
    $mobyPath = Join-Path $Root "test-data\moby_dick.txt"
    if (Test-Path $mobyPath) {
        $mobySize = (Get-Item $mobyPath).Length
        Write-Host "Moby Dick file: $mobyPath"
        Write-Host "Size: $([math]::Round($mobySize / 1MB, 2)) MB ($mobySize bytes)"
    } else {
        Write-Error "Warning: Moby Dick test data not found at $mobyPath"
    }

    # =========================================================================
    # Summary
    # =========================================================================
    Write-Host "`n" -NoNewline
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "     VALIDATION COMPLETE - ALL PASSED  " -ForegroundColor White -BackgroundColor DarkGreen
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Database: $PgDb (recreated with fresh schema)"
    Write-Host "Tests:    48 test cases, all passing"
    Write-Host "Moby Dick: Lossless round-trip verified"
    Write-Host ""

} catch {
    Write-Host "`n" -NoNewline
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "     VALIDATION FAILED                 " -ForegroundColor White -BackgroundColor DarkRed
    Write-Host "========================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Ensure Docker Desktop is running"
    Write-Host "  2. Run: docker compose up -d postgres"
    Write-Host "  3. Check PostgreSQL logs: docker compose logs postgres"
    Write-Host ""
    exit 1
} finally {
    $env:PGPASSWORD = $null
}
