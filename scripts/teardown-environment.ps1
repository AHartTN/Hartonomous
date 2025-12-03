param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("localhost", "dev", "staging", "production", "all")]
    [string]$Environment
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Tearing down $Environment environment(s)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($Environment -eq "all") {
    Write-Host "`nStopping all Docker containers..." -ForegroundColor Yellow
    docker-compose down -v
    Write-Host "All environments shut down successfully!" -ForegroundColor Green
}
else {
    Write-Host "`nStopping Docker container for $Environment..." -ForegroundColor Yellow
    docker-compose stop "postgres-$Environment"

    $response = Read-Host "`nDo you want to remove the volume (delete all data)? (y/N)"
    if ($response -eq "y" -or $response -eq "Y") {
        Write-Host "Removing volume for $Environment..." -ForegroundColor Red
        docker-compose rm -f -v "postgres-$Environment"
        docker volume rm "hartonomous-001_postgres-${Environment}-data" -f
        Write-Host "Volume removed successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Container stopped, data preserved." -ForegroundColor Yellow
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Teardown completed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
