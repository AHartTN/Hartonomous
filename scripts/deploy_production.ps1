#!/usr/bin/env pwsh
# Production deployment script

$ErrorActionPreference = "Stop"

Write-Host "=== Hartonomous Production Deployment ===" -ForegroundColor Cyan

# Configuration
$BACKUP_DIR = "backups"
$LOG_DIR = "logs"

# Create directories
New-Item -ItemType Directory -Force -Path $BACKUP_DIR | Out-Null
New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null

# 1. Pre-deployment backup
Write-Host "`n[1/8] Creating backup..." -ForegroundColor Yellow
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup_file = "$BACKUP_DIR/hartonomous_$timestamp.sql"

pg_dump -U postgres -d hartonomous -F c -f $backup_file
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Backup saved to $backup_file" -ForegroundColor Green
} else {
    Write-Host "  ✗ Backup failed" -ForegroundColor Red
    exit 1
}

# 2. Run migrations
Write-Host "`n[2/8] Running database migrations..." -ForegroundColor Yellow
python tools/migrate.py
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ Migrations failed" -ForegroundColor Red
    exit 1
}

# 3. Build Shader
Write-Host "`n[3/8] Building Shader (optimized)..." -ForegroundColor Yellow
cd shader
cargo build --release
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ Shader build failed" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Shader built" -ForegroundColor Green
cd ..

# 4. Build Cortex
Write-Host "`n[4/8] Building Cortex extension..." -ForegroundColor Yellow
if ($IsLinux -or $IsMacOS) {
    cd cortex
    make clean
    make
    sudo make install
    cd ..
    
    # Restart PostgreSQL to load new extension
    sudo systemctl restart postgresql
    Write-Host "  ✓ Cortex installed and PostgreSQL restarted" -ForegroundColor Green
} else {
    Write-Host "  → Cortex build skipped on Windows" -ForegroundColor Gray
}

# 5. Install Python dependencies
Write-Host "`n[5/8] Installing Python dependencies..." -ForegroundColor Yellow
cd connector
pip install --upgrade -r requirements.txt
cd ..
Write-Host "  ✓ Dependencies installed" -ForegroundColor Green

# 6. Run tests
Write-Host "`n[6/8] Running test suite..." -ForegroundColor Yellow
python -m unittest discover -s tests -p "test_*.py"
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ Tests failed - deployment aborted" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ All tests passed" -ForegroundColor Green

# 7. Optimize database
Write-Host "`n[7/8] Optimizing database..." -ForegroundColor Yellow
psql -U postgres -d hartonomous -c "SELECT maintain_atoms_table();"
psql -U postgres -d hartonomous -c "VACUUM FULL ANALYZE;"
Write-Host "  ✓ Database optimized" -ForegroundColor Green

# 8. Health check
Write-Host "`n[8/8] Running health check..." -ForegroundColor Yellow
$health = python -c "from connector import Hartonomous; h = Hartonomous(); s = h.status(); print(s['is_running']); h.close()"
if ($health -eq "True") {
    Write-Host "  ✓ System healthy" -ForegroundColor Green
} else {
    Write-Host "  ⚠ System health check inconclusive" -ForegroundColor Yellow
}

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Backup: $backup_file" -ForegroundColor Gray
Write-Host "Logs:   $LOG_DIR/" -ForegroundColor Gray
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "  1. Monitor: python tools/benchmark.py" -ForegroundColor Gray
Write-Host "  2. Check logs: psql -d hartonomous -c 'SELECT * FROM v_cortex_status;'" -ForegroundColor Gray
Write-Host "  3. Run load test if needed" -ForegroundColor Gray
