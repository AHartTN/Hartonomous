param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("localhost", "dev", "staging", "production")]
    [string]$Environment
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deploying to $Environment environment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Load environment variables
if (Test-Path ".env.$Environment") {
    Write-Host "Loading environment variables from .env.$Environment" -ForegroundColor Yellow
    Get-Content ".env.$Environment" | ForEach-Object {
        if ($_ -match "^([^=]+)=(.*)$") {
            $name = $matches[1]
            $value = $matches[2]
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

# Set environment
$env:ASPNETCORE_ENVIRONMENT = $Environment

# Get port mapping
$portMap = @{
    "localhost" = 5432
    "dev" = 5433
    "staging" = 5434
    "production" = 5435
}
$port = $portMap[$Environment]

Write-Host "`nStarting Docker containers for $Environment..." -ForegroundColor Yellow
docker-compose up -d "postgres-$Environment"

Write-Host "`nWaiting for database to be ready..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

$maxRetries = 30
$retryCount = 0
$isReady = $false

while (-not $isReady -and $retryCount -lt $maxRetries) {
    try {
        $result = docker exec "hartonomous-db-$Environment" pg_isready -U postgres 2>&1
        if ($LASTEXITCODE -eq 0) {
            $isReady = $true
            Write-Host "Database is ready!" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "Waiting for database... ($retryCount/$maxRetries)" -ForegroundColor Gray
    }

    if (-not $isReady) {
        Start-Sleep -Seconds 2
        $retryCount++
    }
}

if (-not $isReady) {
    Write-Host "ERROR: Database failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "`nApplying EF Core migrations..." -ForegroundColor Yellow
Push-Location "Hartonomous.Db"
try {
    dotnet ef database update --verbose
    if ($LASTEXITCODE -ne 0) {
        throw "Migration failed"
    }
    Write-Host "Migrations applied successfully!" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: Failed to apply migrations: $_" -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Deployment completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nDatabase: hartonomous_$Environment"
Write-Host "Port: $port"
Write-Host "Connection: Host=localhost;Port=$port;Database=hartonomous_$Environment;Username=postgres"
Write-Host "`nPgAdmin: http://localhost:5050"
Write-Host "  Email: admin@hartonomous.local"
Write-Host "  Password: admin"
