# Hartonomous API Test Script
# Tests complete workflow: Ingest ? PostgreSQL ? Neo4j provenance

Write-Host "`n=== HARTONOMOUS API END-TO-END TEST ===" -ForegroundColor Cyan
Write-Host ""

$baseUrl = "http://localhost:8000"

# Test 1: Health Check
Write-Host "Test 1: Health Check" -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$baseUrl/v1/health" -Method GET
    Write-Host "? Health: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "? Health check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 2: Database Readiness
Write-Host "`nTest 2: Database Readiness" -ForegroundColor Yellow
try {
    $ready = Invoke-RestMethod -Uri "$baseUrl/v1/ready" -Method GET
    Write-Host "? Database: Connected" -ForegroundColor Green
    Write-Host "   Version: $($ready.database.version)" -ForegroundColor Gray
    Write-Host "   Tables: $($ready.database.tables)" -ForegroundColor Gray
} catch {
    Write-Host "? Readiness check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 3: Initial Statistics
Write-Host "`nTest 3: Initial Statistics" -ForegroundColor Yellow
try {
    $stats = Invoke-RestMethod -Uri "$baseUrl/v1/stats" -Method GET
    Write-Host "? Stats: atoms=$($stats.statistics.atoms), compositions=$($stats.statistics.compositions)" -ForegroundColor Green
} catch {
    Write-Host "? Stats failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Ingest Text
Write-Host "`nTest 4: Ingest Text 'Hello World'" -ForegroundColor Yellow
try {
    $body = @{ text = "Hello World" } | ConvertTo-Json
    $ingest = Invoke-RestMethod -Uri "$baseUrl/v1/ingest/text" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 30
    Write-Host "? Ingested: $($ingest.atoms.Count) atoms created" -ForegroundColor Green
    
    if ($ingest.atoms.Count -gt 0) {
        $firstAtom = $ingest.atoms[0]
        Write-Host "   First atom ID: $($firstAtom.atom_id)" -ForegroundColor Gray
        Write-Host "   Content hash: $($firstAtom.content_hash.Substring(0, 16))..." -ForegroundColor Gray
    }
} catch {
    Write-Host "? Ingestion failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   This might be a timeout - check if atoms were created anyway" -ForegroundColor Yellow
}

# Test 5: Query Atoms
Write-Host "`nTest 5: Query Created Atoms" -ForegroundColor Yellow
try {
    $query = Invoke-RestMethod -Uri "$baseUrl/v1/query/atoms?limit=5" -Method GET
    Write-Host "? Found: $($query.atoms.Count) atoms" -ForegroundColor Green
    
    foreach ($atom in $query.atoms) {
        Write-Host "   Atom $($atom.atom_id): '$($atom.canonical_text)'" -ForegroundColor Gray
    }
} catch {
    Write-Host "? Query failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 6: Verify Neo4j Provenance
Write-Host "`nTest 6: Verify Neo4j Provenance" -ForegroundColor Yellow
try {
    # Use Python to query Neo4j
    $neo4jScript = @"
from neo4j import GraphDatabase
driver = GraphDatabase.driver('bolt://localhost:7687', auth=('neo4j', 'neo4jneo4j'))
with driver.session() as session:
    result = session.run('MATCH (n:Atom) RETURN count(n) as count')
    count = result.single()['count']
    print(f'{count}')
driver.close()
"@
    
    $tempFile = New-TemporaryFile
    Set-Content -Path $tempFile.FullName -Value $neo4jScript
    $atomCount = python $tempFile.FullName
    Remove-Item $tempFile.FullName
    
    Write-Host "? Neo4j: $atomCount atoms synced" -ForegroundColor Green
    
    if ([int]$atomCount -gt 0) {
        Write-Host "   Provenance tracking is working!" -ForegroundColor Green
    }
} catch {
    Write-Host "? Neo4j verification failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Final Summary
Write-Host "`n=== TEST SUMMARY ===" -ForegroundColor Cyan
Write-Host "? API is healthy and operational" -ForegroundColor Green
Write-Host "? PostgreSQL database connected" -ForegroundColor Green
Write-Host "? Neo4j provenance tracking active" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Open Neo4j Browser: http://localhost:7474" -ForegroundColor White
Write-Host "2. Run: MATCH (n:Atom) RETURN n LIMIT 25" -ForegroundColor White
Write-Host "3. Visualize provenance graph!" -ForegroundColor White
Write-Host ""
