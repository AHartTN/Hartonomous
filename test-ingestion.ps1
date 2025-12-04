#!/usr/bin/env pwsh
# Test repository ingestion endpoint
# Usage: .\test-ingestion.ps1

$body = @{
    repositoryPath = "D:\Repositories\Hartonomous"
    includePatterns = @("*.cs")
    excludePatterns = @("**/bin/**", "**/obj/**", "**/artifacts/**", "**/*.g.cs")
    maxFileSizeBytes = 10485760
    learnBPE = $true
    metadata = @{
        repositoryName = "Hartonomous"
        language = "CSharp"
    }
} | ConvertTo-Json -Depth 10

Write-Host "Testing repository ingestion endpoint..."
Write-Host "Body:" -ForegroundColor Cyan
Write-Host $body

try {
    $response = Invoke-RestMethod -Uri "https://localhost:7001/api/v1/ingestions/repository" `
        -Method POST `
        -Body $body `
        -ContentType "application/json" `
        -SkipCertificateCheck
    
    Write-Host "`nSuccess!" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 10 | Write-Host
}
catch {
    Write-Host "`nError:" -ForegroundColor Red
    Write-Host $_.Exception.Message
    if ($_.ErrorDetails) {
        Write-Host $_.ErrorDetails.Message
    }
}
