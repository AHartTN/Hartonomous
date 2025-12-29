# Hartonomous Build Script (PowerShell)
# Usage: .\build.ps1 [-Clean] [-Test] [-Release] [-Seed] [-Ingest <path>] [-Bench]
# Idempotent, CI/CD compatible

param(
    [switch]$Clean,
    [switch]$Test,
    [switch]$Release,
    [switch]$Seed,
    [switch]$Bench,
    [string]$Ingest
)

$ErrorActionPreference = "Stop"

# Determine preset based on OS and build type
$BuildType = if ($Release) { "release" } else { "debug" }
if ($IsWindows -or $env:OS -match "Windows") {
    $ConfigPreset = "windows-clang-$BuildType"
    $BuildPreset = "windows-clang-$BuildType"
    $TestPreset = "windows-clang-$BuildType"
} elseif ($IsMacOS) {
    $ConfigPreset = "macos-clang-$BuildType"
    $BuildPreset = "macos-clang-$BuildType"
    $TestPreset = "macos-clang-$BuildType"
} else {
    $ConfigPreset = "linux-clang-$BuildType"
    $BuildPreset = "linux-clang-$BuildType"
    $TestPreset = "linux-clang-$BuildType"
}

$BuildDir = "$PSScriptRoot/../artifacts/native/build/$ConfigPreset"
$OutDir = "$PSScriptRoot/../artifacts/native/$ConfigPreset"

# Clean if requested
if ($Clean -and (Test-Path $BuildDir)) {
    Write-Host "Cleaning $BuildDir..."
    Remove-Item -Recurse -Force $BuildDir
}

# Configure (idempotent - only if needed)
if (-not (Test-Path "$BuildDir/build.ninja")) {
    Write-Host "Configuring with preset: $ConfigPreset"
    cmake --preset $ConfigPreset
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Build
Write-Host "Building with preset: $BuildPreset"
cmake --build --preset $BuildPreset --parallel
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Test if requested
if ($Test) {
    Write-Host "Testing with preset: $TestPreset"
    ctest --preset $TestPreset --output-on-failure
    exit $LASTEXITCODE
}

# Seed if requested (requires HARTONOMOUS_DB_URL environment variable)
if ($Seed) {
    $SeedExe = "$OutDir/bin/hartonomous-seed"
    if ($IsWindows -or $env:OS -match "Windows") {
        $SeedExe = "$SeedExe.exe"
    }
    if (-not (Test-Path $SeedExe)) {
        Write-Error "Seed executable not found: $SeedExe (PostgreSQL may not be installed)"
        exit 1
    }
    if (-not $env:HARTONOMOUS_DB_URL) {
        Write-Error "HARTONOMOUS_DB_URL environment variable not set"
        Write-Host "Example: `$env:HARTONOMOUS_DB_URL = 'postgresql://user:pass@localhost/hartonomous'"
        exit 1
    }
    Write-Host "Seeding database..."
    & $SeedExe
    exit $LASTEXITCODE
}

# Ingest if requested
if ($Ingest) {
    $IngestExe = "$OutDir/bin/hartonomous-ingest"
    if ($IsWindows -or $env:OS -match "Windows") {
        $IngestExe = "$IngestExe.exe"
    }
    if (-not (Test-Path $IngestExe)) {
        Write-Error "Ingest executable not found: $IngestExe"
        exit 1
    }
    Write-Host "Ingesting: $Ingest"
    & $IngestExe $Ingest
    exit $LASTEXITCODE
}

# Benchmark if requested - full database round-trip with SHA256 verification
if ($Bench) {
    # Determine PostgreSQL path
    $PgDir = if (Test-Path "D:\PostgreSQL\18\bin") { "D:\PostgreSQL\18\bin" } 
             elseif (Test-Path "C:\Program Files\PostgreSQL\18\bin") { "C:\Program Files\PostgreSQL\18\bin" }
             elseif (Test-Path "C:\Program Files\PostgreSQL\17\bin") { "C:\Program Files\PostgreSQL\17\bin" }
             elseif (Test-Path "C:\Program Files\PostgreSQL\16\bin") { "C:\Program Files\PostgreSQL\16\bin" }
             else { $null }
    
    if (-not $PgDir) {
        Write-Error "PostgreSQL not found. Install PostgreSQL or set path manually."
        exit 1
    }
    
    # Parse connection string for host/port/user/password/dbname
    if (-not $env:HARTONOMOUS_DB_URL) {
        Write-Error "HARTONOMOUS_DB_URL environment variable not set"
        Write-Host "Example: `$env:HARTONOMOUS_DB_URL = 'postgresql://hartonomous:hartonomous@localhost:5432/hartonomous'"
        exit 1
    }
    
    # Parse: postgresql://user:pass@host:port/dbname
    if ($env:HARTONOMOUS_DB_URL -match 'postgresql://([^:]+):([^@]+)@([^:]+):(\d+)/(.+)') {
        $DbUser = $Matches[1]
        $DbPass = $Matches[2]
        $DbHost = $Matches[3]
        $DbPort = $Matches[4]
        $DbName = $Matches[5]
    } else {
        Write-Error "Cannot parse HARTONOMOUS_DB_URL. Expected: postgresql://user:pass@host:port/dbname"
        exit 1
    }
    
    $env:PGPASSWORD = $DbPass
    $Psql = "$PgDir\psql.exe"
    
    # Create database if it doesn't exist (idempotent)
    Write-Host "Ensuring database '$DbName' exists on $DbHost`:$DbPort..."
    $DbExists = & $Psql -U $DbUser -h $DbHost -p $DbPort -d postgres -t -c "SELECT 1 FROM pg_database WHERE datname = '$DbName'" 2>$null
    if (-not $DbExists -or $DbExists.Trim() -ne "1") {
        Write-Host "Creating database '$DbName'..."
        & $Psql -U postgres -h $DbHost -p $DbPort -c "CREATE DATABASE $DbName OWNER $DbUser" 2>$null
        if ($LASTEXITCODE -ne 0) {
            # Try with the user account if postgres fails
            & $Psql -U $DbUser -h $DbHost -p $DbPort -d postgres -c "CREATE DATABASE $DbName" 2>$null
        }
    }
    
    # Run benchmark
    $BenchExe = "$OutDir/bin/bench-moby"
    if ($IsWindows -or $env:OS -match "Windows") {
        $BenchExe = "$BenchExe.exe"
    }
    if (-not (Test-Path $BenchExe)) {
        Write-Error "Benchmark executable not found: $BenchExe"
        Write-Host "Build it first: cmake --build build --target bench-moby"
        exit 1
    }
    
    Write-Host ""
    Write-Host "Running full database benchmark..."
    Write-Host ""
    & $BenchExe
    exit $LASTEXITCODE
}

Write-Host "Build complete. Run with -Test to execute tests."
