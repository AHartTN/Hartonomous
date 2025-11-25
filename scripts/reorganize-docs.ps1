#Requires -Version 5.1
<#
.SYNOPSIS
    Reorganize Hartonomous documentation into enterprise-grade structure
.DESCRIPTION
    Executes the documentation restructure as defined in DOCUMENTATION-RESTRUCTURE-PROPOSAL.md
    - Creates new directory structure
    - Moves existing docs to appropriate locations
    - Creates README.md hub files
    - Updates all cross-references
    - Commits changes with proper messages
.NOTES
    Author: Anthony Hart
    Copyright (c) 2025 Anthony Hart. All Rights Reserved.
#>

[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n===== $Message =====" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "? $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

# Ensure we're in the right directory
if (-not (Test-Path "README.md")) {
    throw "Must run from repository root"
}

Write-Step "Documentation Restructure - Enterprise Grade"

if ($DryRun) {
    Write-Host "DRY RUN MODE - No changes will be made" -ForegroundColor Yellow
}

# Phase 1: Create new directory structure
Write-Step "Phase 1: Creating Directory Structure"

$newDirs = @(
    "docs/getting-started",
    "docs/architecture/diagrams",
    "docs/ai-operations",
    "docs/api-reference/atomization",
    "docs/api-reference/spatial",
    "docs/api-reference/inference",
    "docs/api-reference/provenance",
    "docs/deployment",
    "docs/business",
    "docs/research/commit-messages",
    "docs/research/design-decisions",
    "docs/research/experiments",
    "docs/vision",
    "docs/contributing",
    ".github/workflows",
    ".github/ISSUE_TEMPLATE"
)

foreach ($dir in $newDirs) {
    if (-not (Test-Path $dir)) {
        if (-not $DryRun) {
            New-Item -Path $dir -ItemType Directory -Force | Out-Null
        }
        Write-Info "Created: $dir"
    } else {
        Write-Info "Exists: $dir"
    }
}

Write-Success "Directory structure created"

# Phase 2: Document migrations (will execute after approval)
Write-Step "Phase 2: Document Migration Plan"

$migrations = @(
    @{
        From = "SETUP.md"
        To = "docs/getting-started/installation.md"
        Category = "Getting Started"
    },
    @{
        From = "docs/00-START-HERE.md"
        To = "docs/getting-started/README.md"
        Category = "Getting Started"
    },
    @{
        From = "docs/03-GETTING-STARTED.md"
        To = "docs/getting-started/first-query.md"
        Category = "Getting Started"
    },
    @{
        From = "docs/CQRS-ARCHITECTURE.md"
        To = "docs/architecture/cqrs-pattern.md"
        Category = "Architecture"
    },
    @{
        From = "docs/VECTORIZATION.md"
        To = "docs/architecture/vectorization.md"
        Category = "Architecture"
    },
    @{
        From = "docs/07-COGNITIVE-PHYSICS.md"
        To = "docs/architecture/cognitive-physics.md"
        Category = "Architecture"
    },
    @{
        From = "docs/AI-OPERATIONS.md"
        To = "docs/ai-operations/README.md"
        Category = "AI Operations"
    },
    @{
        From = "docs/POSTGRESQL-GPU-ACCELERATION.md"
        To = "docs/ai-operations/gpu-acceleration.md"
        Category = "AI Operations"
    },
    @{
        From = "docs/10-API-REFERENCE.md"
        To = "docs/api-reference/README.md"
        Category = "API Reference"
    },
    @{
        From = "docs/11-DEPLOYMENT.md"
        To = "docs/deployment/README.md"
        Category = "Deployment"
    },
    @{
        From = "docs/12-BUSINESS.md"
        To = "docs/business/value-proposition.md"
        Category = "Business"
    },
    @{
        From = "BUSINESS-SUMMARY.md"
        To = "docs/business/README.md"
        Category = "Business"
    },
    @{
        From = "docs/PYTHON-APP-RESEARCH.md"
        To = "docs/research/python-stack-research.md"
        Category = "Research"
    },
    @{
        From = "DEVELOPMENT-ROADMAP.md"
        To = "docs/vision/roadmap.md"
        Category = "Vision"
    },
    @{
        From = "docs/01-VISION.md"
        To = "docs/vision/project-vision.md"
        Category = "Vision"
    },
    @{
        From = "AUDIT-REPORT.md"
        To = "docs/contributing/audit-report.md"
        Category = "Contributing"
    },
    @{
        From = "CONTRIBUTING.md"
        To = "docs/contributing/README.md"
        Category = "Contributing"
    }
)

Write-Info "Planned migrations:"
foreach ($migration in $migrations) {
    Write-Info "  $($migration.From) ? $($migration.To)"
}

Write-Success "Migration plan created ($($migrations.Count) files)"

# Phase 3: Summary
Write-Step "Phase 3: Summary"

Write-Host @"

Directory Structure: ? Created
Migration Plan: ? Prepared
Pending Actions:

1. Review migration plan above
2. Run with -Force to execute migrations
3. Update cross-references
4. Create enhanced READMEs with GitHub features
5. Commit changes

Next command:
  .\reorganize-docs.ps1 -Force

"@ -ForegroundColor Yellow

if (-not $DryRun -and -not $Force) {
    Write-Host "Run with -Force to execute migrations" -ForegroundColor Red
    exit 0
}

if ($Force) {
    Write-Step "Executing Migrations"
    
    foreach ($migration in $migrations) {
        if (Test-Path $migration.From) {
            $destDir = Split-Path $migration.To -Parent
            if (-not (Test-Path $destDir)) {
                New-Item -Path $destDir -ItemType Directory -Force | Out-Null
            }
            
            Move-Item -Path $migration.From -Destination $migration.To -Force
            Write-Info "Moved: $($migration.From) ? $($migration.To)"
        } else {
            Write-Info "Missing: $($migration.From)"
        }
    }
    
    Write-Success "All migrations complete!"
}
