param(
    [Parameter(Mandatory=$true)]
    [string]$Organization,
    
    [Parameter(Mandatory=$true)]
    [string]$Project,
    
    [Parameter(Mandatory=$true)]
    [string]$PAT
)

Write-Host "Azure DevOps Wiki Sync Script" -ForegroundColor Cyan
Write-Host "Organization: $Organization" -ForegroundColor Gray
Write-Host "Project: $Project" -ForegroundColor Gray
Write-Host ""

# Set PAT for az cli
$env:AZURE_DEVOPS_EXT_PAT = $PAT

# Get wiki ID
Write-Host "Fetching wiki ID..." -ForegroundColor Yellow
$wikiId = az devops wiki list `
    --org $Organization `
    --project $Project `
    --query "[?name=='Hartonomous-Documentation'].id" `
    --output tsv

if (-not $wikiId) {
    Write-Host "Wiki 'Hartonomous-Documentation' not found. Creating..." -ForegroundColor Yellow
    
    $wikiId = az devops wiki create `
        --name "Hartonomous-Documentation" `
        --type projectwiki `
        --mapped-path /docs `
        --repository Hartonomous `
        --org $Organization `
        --project $Project `
        --query "id" `
        --output tsv
    
    Write-Host "Wiki created with ID: $wikiId" -ForegroundColor Green
}
else {
    Write-Host "Wiki ID: $wikiId" -ForegroundColor Green
}

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$docsDir = Join-Path (Split-Path -Parent $scriptDir) ""

Write-Host ""
Write-Host "Syncing documentation files..." -ForegroundColor Yellow
Write-Host "Docs directory: $docsDir" -ForegroundColor Gray
Write-Host ""

# Track synced files
$syncedCount = 0
$failedCount = 0

# Sync each markdown file
Get-ChildItem -Path $docsDir -Filter "*.md" -Recurse | ForEach-Object {
    # Skip files in scripts directory
    if ($_.FullName -like "*\scripts\*") {
        return
    }
    
    $relativePath = $_.FullName.Replace($docsDir, "").TrimStart("\").Replace("\", "/")
    $wikiPath = "/$($relativePath.Replace(".md", ""))"
    
    Write-Host "  Syncing: $relativePath" -ForegroundColor Cyan
    Write-Host "  To wiki: $wikiPath" -ForegroundColor Gray
    
    try {
        # Check if page exists
        $existingPage = az devops wiki page show `
            --wiki $wikiId `
            --path $wikiPath `
            --org $Organization `
            --project $Project `
            2>$null
        
        if ($existingPage) {
            # Update existing page
            az devops wiki page update `
                --wiki $wikiId `
                --path $wikiPath `
                --file-path $_.FullName `
                --org $Organization `
                --project $Project `
                --output none
        }
        else {
            # Create new page
            az devops wiki page create `
                --wiki $wikiId `
                --path $wikiPath `
                --file-path $_.FullName `
                --org $Organization `
                --project $Project `
                --output none
        }
        
        Write-Host "  ? Synced successfully" -ForegroundColor Green
        $syncedCount++
    }
    catch {
        Write-Host "  ? Failed: $_" -ForegroundColor Red
        $failedCount++
    }
    
    Write-Host ""
}

Write-Host ""
Write-Host "Sync Summary:" -ForegroundColor Cyan
Write-Host "  Synced: $syncedCount files" -ForegroundColor Green
Write-Host "  Failed: $failedCount files" -ForegroundColor $(if ($failedCount -gt 0) { "Red" } else { "Gray" })
Write-Host ""

if ($syncedCount -gt 0) {
    Write-Host "Documentation synced successfully to Wiki!" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "No files were synced. Check for errors above." -ForegroundColor Red
    exit 1
}
