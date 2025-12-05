# Migrate Hartonomous Documentation to Azure DevOps Wiki
# This script systematically uploads documentation to the wiki

$org = "https://dev.azure.com/aharttn"
$project = "Hartonomous"
$wikiName = "Hartonomous.wiki"

# Authenticate
Write-Host "Setting up Azure DevOps authentication..." -ForegroundColor Cyan
$env:AZURE_DEVOPS_EXT_PAT = az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv

# Verify wiki exists
Write-Host "Verifying wiki exists..." -ForegroundColor Cyan
$wikis = az devops wiki list --project $project --organization $org --output json | ConvertFrom-Json

if (-not $wikis) {
    Write-Host "Creating project wiki..." -ForegroundColor Yellow
    az devops wiki create --name $wikiName --project $project --organization $org --type projectwiki
}

# Define wiki structure and mappings
$wikiPages = @(
    @{
        Path = "/Home"
        File = "README.md"
        Title = "Hartonomous - Universal Geometric Knowledge Substrate"
    },
    @{
        Path = "/Architecture"
        File = "ARCHITECTURE.md"
        Title = "Core Architecture"
    },
    @{
        Path = "/Architecture/Enterprise-Architecture"
        File = "docs/architecture/ENTERPRISE_ARCHITECTURE.md"
        Title = "Enterprise Architecture & Solution Structure"
    },
    @{
        Path = "/Architecture/4D-Spatial-ADR"
        File = "docs/architecture/PHASE1_4D_SPATIAL_ARCHITECTURE_DECISION.md"
        Title = "4D POINTZM Architecture Decision Record"
    },
    @{
        Path = "/Architecture/Hilbert-Indexing"
        File = "docs/HILBERT_ARCHITECTURE.md"
        Title = "Hilbert Curve Indexing"
    },
    @{
        Path = "/Implementation"
        File = "POINTZM_MASTER_IMPLEMENTATION_PLAN.md"
        Title = "Master Implementation Plan - 8 Phases"
    },
    @{
        Path = "/Implementation/Phase-1-Core-Geometry"
        File = "docs/implementation/PHASE1_CORE_GEOMETRY.md"
        Title = "Phase 1: Core Geometric Foundation"
    },
    @{
        Path = "/Implementation/Phase-2-BPE-Redesign"
        File = "docs/implementation/PHASE2_BPE_REDESIGN.md"
        Title = "Phase 2: BPE Algorithm Redesign"
    },
    @{
        Path = "/Implementation/Phase-3-Universal-Properties"
        File = "docs/implementation/PHASE3_UNIVERSAL_PROPERTIES.md"
        Title = "Phase 3: Universal Properties (Y/Z/M)"
    },
    @{
        Path = "/Implementation/Phase-4-Math-Algorithms"
        File = "docs/implementation/PHASE4_MATH_ALGORITHMS.md"
        Title = "Phase 4: Mathematical Algorithms"
    },
    @{
        Path = "/Implementation/Phase-5-Advanced-Features"
        File = "docs/implementation/PHASE5_ADVANCED_FEATURES.md"
        Title = "Phase 5: Advanced Geometric Features"
    },
    @{
        Path = "/Implementation/Phase-6-Testing"
        File = "docs/implementation/PHASE6_TESTING.md"
        Title = "Phase 6: Testing Strategy"
    },
    @{
        Path = "/Implementation/Phase-7-Documentation"
        File = "docs/implementation/PHASE7_DOCUMENTATION.md"
        Title = "Phase 7: Documentation"
    },
    @{
        Path = "/Implementation/Phase-8-Production"
        File = "docs/implementation/PHASE8_PRODUCTION.md"
        Title = "Phase 8: Production Hardening"
    },
    @{
        Path = "/Implementation/Geometric-Composition-Types"
        File = "docs/implementation/GEOMETRIC_COMPOSITION_TYPES.md"
        Title = "Geometric Composition Type System"
    },
    @{
        Path = "/Technical-Reference/API-Reference"
        File = "docs/api/API_REFERENCE.md"
        Title = "REST API & WebSocket Reference"
    },
    @{
        Path = "/Technical-Reference/Database-Setup"
        File = "docs/database/DATABASE_SETUP.md"
        Title = "PostgreSQL/PostGIS Database Setup"
    },
    @{
        Path = "/Technical-Reference/Deployment"
        File = "docs/deployment/DEPLOYMENT-GUIDE-LINUX.md"
        Title = "Linux Deployment Guide"
    },
    @{
        Path = "/Developer-Guide/Copilot-Instructions"
        File = ".github/copilot-instructions.md"
        Title = "AI Coding Agent Guidelines"
    }
)

# Upload each page
$successCount = 0
$failCount = 0

foreach ($page in $wikiPages) {
    $filePath = Join-Path $PSScriptRoot $page.File
    
    if (-not (Test-Path $filePath)) {
        Write-Host "⚠ Skipping $($page.Path) - File not found: $filePath" -ForegroundColor Yellow
        $failCount++
        continue
    }
    
    Write-Host "📄 Uploading: $($page.Path)" -ForegroundColor Cyan
    
    try {
        # Read content
        $content = Get-Content $filePath -Raw -Encoding UTF8
        
        # Upload to wiki
        az devops wiki page create `
            --path $page.Path `
            --wiki $wikiName `
            --project $project `
            --organization $org `
            --content $content `
            --encoding utf-8 `
            --output none 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ Success: $($page.Title)" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "  ✗ Failed: $($page.Title)" -ForegroundColor Red
            $failCount++
        }
    }
    catch {
        Write-Host "  ✗ Error uploading $($page.Path): $_" -ForegroundColor Red
        $failCount++
    }
    
    Start-Sleep -Milliseconds 500  # Rate limiting
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Migration Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✓ Successful: $successCount pages" -ForegroundColor Green
Write-Host "✗ Failed: $failCount pages" -ForegroundColor Red
Write-Host "`nWiki URL: $org/$project/_wiki/wikis/$wikiName" -ForegroundColor Yellow
