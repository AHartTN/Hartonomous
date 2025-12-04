#Requires -Version 7.0
<#
.SYNOPSIS
    Hartonomous Azure DevOps Integration - Enterprise Setup
    
.DESCRIPTION
    Idempotent setup script for Azure DevOps Wiki, Boards, Pipelines, and Arc integration.
    Safe to run multiple times - checks existing state before making changes.
    
.PARAMETER PAT
    Azure DevOps Personal Access Token (or use $env:AZURE_DEVOPS_PAT)
    
.PARAMETER Organization
    Azure DevOps organization URL (default: https://dev.azure.com/aharttn)
    
.PARAMETER Project
    Azure DevOps project name (default: Hartonomous)
    
.PARAMETER SkipWiki
    Skip Wiki creation and sync
    
.PARAMETER SkipWorkItems
    Skip work item creation
    
.PARAMETER SkipPipelines
    Skip pipeline creation
    
.PARAMETER SkipArc
    Skip Azure Arc integration setup
    
.EXAMPLE
    .\Initialize-AzureDevOps.ps1
    
.EXAMPLE
    .\Initialize-AzureDevOps.ps1 -PAT "your-token" -SkipWorkItems
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$PAT = $env:AZURE_DEVOPS_PAT,
    
    [Parameter(Mandatory=$false)]
    [string]$Organization = "https://dev.azure.com/aharttn",
    
    [Parameter(Mandatory=$false)]
    [string]$Project = "Hartonomous",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipWiki,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipWorkItems,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipPipelines,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipArc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

#region Functions

function Write-Section {
    param([string]$Title)
    Write-Host "`n================================================================" -ForegroundColor Cyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host "================================================================`n" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "? $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Write-Warning {
    param([string]$Message)
    Write-Host "? $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "? $Message" -ForegroundColor Red
}

function Test-AzureDevOpsCLI {
    try {
        $version = az --version 2>$null | Select-Object -First 1
        if ($version) {
            Write-Success "Azure CLI installed: $($version -replace 'azure-cli\s+', '')"
            
            # Check DevOps extension
            $extensions = az extension list --query "[?name=='azure-devops'].version" -o tsv
            if ($extensions) {
                Write-Success "Azure DevOps extension installed: $extensions"
                return $true
            }
            else {
                Write-Warning "Azure DevOps extension not installed"
                Write-Info "Installing azure-devops extension..."
                az extension add --name azure-devops --only-show-errors
                Write-Success "Azure DevOps extension installed"
                return $true
            }
        }
    }
    catch {
        Write-Error "Azure CLI not found"
        Write-Info "Install from: https://aka.ms/installazurecliwindows"
        return $false
    }
}

function Initialize-AzureDevOpsAuth {
    param([string]$Pat, [string]$Org, [string]$Proj)
    
    if (-not $Pat) {
        Write-Error "Azure DevOps PAT required"
        Write-Info "Set environment variable: `$env:AZURE_DEVOPS_PAT = 'your-token'"
        Write-Info "Or pass -PAT parameter"
        Write-Info ""
        Write-Info "Create PAT at: $Org/_usersSettings/tokens"
        Write-Info "Required scopes: Code (Read & Write), Wiki (Read & Write), Work Items (Read & Write), Build (Read & Execute)"
        return $false
    }
    
    $env:AZURE_DEVOPS_EXT_PAT = $Pat
    
    try {
        az devops configure --defaults organization=$Org project=$Proj --use-git-aliases false 2>$null | Out-Null
        
        # Test authentication
        $testProject = az devops project show --project $Proj --org $Org --query "name" -o tsv 2>$null
        if ($testProject -eq $Proj) {
            Write-Success "Authenticated to $Org/$Proj"
            return $true
        }
        else {
            Write-Error "Authentication failed"
            return $false
        }
    }
    catch {
        Write-Error "Failed to configure Azure DevOps: $_"
        return $false
    }
}

function Get-OrCreateWiki {
    param([string]$WikiName, [string]$Org, [string]$Proj, [string]$RepoName)
    
    Write-Host "`nChecking Wiki status..." -ForegroundColor Cyan
    
    $existingWiki = az devops wiki list --org $Org --project $Proj --query "[?name=='$WikiName'].id" -o tsv 2>$null
    
    if ($existingWiki) {
        Write-Success "Wiki '$WikiName' exists (ID: $existingWiki)"
        return $existingWiki
    }
    
    Write-Info "Creating Wiki '$WikiName'..."
    
    try {
        $wikiId = az devops wiki create `
            --name $WikiName `
            --type projectwiki `
            --mapped-path /docs `
            --repository $RepoName `
            --org $Org `
            --project $Proj `
            --query "id" `
            -o tsv 2>$null
        
        if ($wikiId) {
            Write-Success "Wiki created (ID: $wikiId)"
            return $wikiId
        }
        else {
            Write-Warning "Failed to create Wiki"
            return $null
        }
    }
    catch {
        Write-Warning "Wiki creation failed: $_"
        return $null
    }
}

function Sync-DocumentationToWiki {
    param([string]$WikiId, [string]$Org, [string]$Proj, [string]$DocsPath)
    
    if (-not $WikiId) {
        Write-Warning "No Wiki ID provided, skipping sync"
        return $false
    }
    
    Write-Host "`nSyncing documentation to Wiki..." -ForegroundColor Cyan
    
    $mdFiles = Get-ChildItem -Path $DocsPath -Filter "*.md" -Recurse | Where-Object { 
        $_.FullName -notlike "*\scripts\*" 
    }
    
    $syncedCount = 0
    $skippedCount = 0
    $failedCount = 0
    
    foreach ($file in $mdFiles) {
        $relativePath = $file.FullName.Replace("$DocsPath\", "").Replace("\", "/").Replace(".md", "")
        $wikiPath = "/$relativePath"
        
        Write-Info "Syncing: $($file.Name) ? $wikiPath"
        
        try {
            # Check if page exists
            $existingPage = az devops wiki page show `
                --wiki $WikiId `
                --path $wikiPath `
                --org $Org `
                --project $Proj `
                --query "id" `
                -o tsv 2>$null
            
            if ($existingPage) {
                # Update existing
                az devops wiki page update `
                    --wiki $WikiId `
                    --path $wikiPath `
                    --file-path $file.FullName `
                    --org $Org `
                    --project $Proj `
                    --output none 2>$null
                
                $syncedCount++
            }
            else {
                # Create new
                az devops wiki page create `
                    --wiki $WikiId `
                    --path $wikiPath `
                    --file-path $file.FullName `
                    --org $Org `
                    --project $Proj `
                    --output none 2>$null
                
                $syncedCount++
            }
        }
        catch {
            Write-Warning "Failed to sync $($file.Name): $_"
            $failedCount++
        }
    }
    
    Write-Host ""
    Write-Success "Synced $syncedCount files"
    if ($skippedCount -gt 0) { Write-Info "Skipped $skippedCount files (unchanged)" }
    if ($failedCount -gt 0) { Write-Warning "Failed $failedCount files" }
    
    return $true
}

function Get-OrCreateAgentPool {
    param([string]$PoolName, [string]$Org, [string]$Proj)
    
    Write-Host "`nChecking agent pool status..." -ForegroundColor Cyan
    
    $existingPool = az pipelines pool list --org $Org --query "[?name=='$PoolName'].id" -o tsv 2>$null
    
    if ($existingPool) {
        Write-Success "Agent pool '$PoolName' exists (ID: $existingPool)"
        return $existingPool
    }
    
    Write-Info "Creating agent pool '$PoolName'..."
    
    try {
        $poolId = az pipelines pool create `
            --name $PoolName `
            --org $Org `
            --pool-type private `
            --query "id" `
            -o tsv 2>$null
        
        if ($poolId) {
            Write-Success "Agent pool created (ID: $poolId)"
            
            # Grant permissions
            az pipelines pool permission set `
                --pool-name $PoolName `
                --allow-pipelines true `
                --org $Org `
                --project $Proj `
                --output none 2>$null
            
            return $poolId
        }
        else {
            Write-Warning "Failed to create agent pool"
            return $null
        }
    }
    catch {
        Write-Warning "Agent pool creation failed: $_"
        return $null
    }
}

function Get-OrCreatePipeline {
    param([string]$Name, [string]$YmlPath, [string]$Org, [string]$Proj, [string]$RepoName)
    
    Write-Host "`nChecking pipeline '$Name'..." -ForegroundColor Cyan
    
    $existing = az pipelines list --org $Org --project $Proj --query "[?name=='$Name'].id" -o tsv 2>$null
    
    if ($existing) {
        Write-Success "Pipeline '$Name' exists (ID: $existing)"
        return $existing
    }
    
    Write-Info "Creating pipeline '$Name'..."
    
    try {
        $pipelineId = az pipelines create `
            --name $Name `
            --repository $RepoName `
            --branch main `
            --yml-path $YmlPath `
            --org $Org `
            --project $Proj `
            --skip-first-run `
            --query "id" `
            -o tsv 2>$null
        
        if ($pipelineId) {
            Write-Success "Pipeline created (ID: $pipelineId)"
            return $pipelineId
        }
        else {
            Write-Warning "Failed to create pipeline"
            return $null
        }
    }
    catch {
        Write-Warning "Pipeline creation failed: $_"
        return $null
    }
}

#endregion

#region Main Execution

Write-Section "Hartonomous Azure DevOps Integration"

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Organization: $Organization" -ForegroundColor Gray
Write-Host "  Project: $Project" -ForegroundColor Gray
Write-Host "  Skip Wiki: $SkipWiki" -ForegroundColor Gray
Write-Host "  Skip Work Items: $SkipWorkItems" -ForegroundColor Gray
Write-Host "  Skip Pipelines: $SkipPipelines" -ForegroundColor Gray
Write-Host "  Skip Arc: $SkipArc" -ForegroundColor Gray

# Validate prerequisites
Write-Section "Validating Prerequisites"

if (-not (Test-AzureDevOpsCLI)) {
    exit 1
}

if (-not (Initialize-AzureDevOpsAuth -Pat $PAT -Org $Organization -Proj $Project)) {
    exit 1
}

# Get repository name from git config
$repoName = "Hartonomous"  # Default
try {
    $gitRemote = git remote get-url origin 2>$null
    if ($gitRemote -match '/([^/]+)$') {
        $repoName = $matches[1]
    }
}
catch { }

Write-Info "Repository: $repoName"

# Wiki setup
if (-not $SkipWiki) {
    Write-Section "Wiki Setup"
    
    $wikiId = Get-OrCreateWiki -WikiName "Hartonomous-Documentation" -Org $Organization -Proj $Project -RepoName $repoName
    
    if ($wikiId) {
        $docsPath = Join-Path $PSScriptRoot "docs"
        if (Test-Path $docsPath) {
            Sync-DocumentationToWiki -WikiId $wikiId -Org $Organization -Proj $Project -DocsPath $docsPath
        }
        else {
            Write-Warning "docs folder not found at $docsPath"
        }
    }
}

# Pipeline setup
if (-not $SkipPipelines) {
    Write-Section "Pipeline Setup"
    
    $validationPipeline = ".azure-pipelines/docs-validation.yml"
    $publishPipeline = ".azure-pipelines/docs-publish.yml"
    
    if (Test-Path $validationPipeline) {
        Get-OrCreatePipeline -Name "Docs-Validation" -YmlPath $validationPipeline -Org $Organization -Proj $Project -RepoName $repoName
    }
    else {
        Write-Warning "Pipeline file not found: $validationPipeline"
    }
    
    if (Test-Path $publishPipeline) {
        Get-OrCreatePipeline -Name "Docs-Publish" -YmlPath $publishPipeline -Org $Organization -Proj $Project -RepoName $repoName
    }
    else {
        Write-Warning "Pipeline file not found: $publishPipeline"
    }
}

# Azure Arc integration
if (-not $SkipArc) {
    Write-Section "Azure Arc Integration"
    
    $poolId = Get-OrCreateAgentPool -PoolName "Hartonomous-OnPrem" -Org $Organization -Proj $Project
    
    if ($poolId) {
        Write-Host ""
        Write-Host "Agent Pool Created: Hartonomous-OnPrem" -ForegroundColor Cyan
        Write-Host ""
        Write-Info "Install agents on HART-SERVER and HART-DESKTOP:"
        Write-Info "  1. Download: https://vstsagentpackage.azureedge.net/agent/3.236.1/vsts-agent-win-x64-3.236.1.zip"
        Write-Info "  2. Extract to D:\azagent"
        Write-Info "  3. Run: .\config.cmd --unattended --url $Organization --auth pat --token `$env:AZURE_DEVOPS_PAT --pool Hartonomous-OnPrem --agent HART-SERVER"
        Write-Info ""
        Write-Info "See docs/AZURE_ARC_INTEGRATION.md for complete instructions"
    }
}

# Work items (optional - can create many items)
if (-not $SkipWorkItems) {
    Write-Section "Work Item Creation"
    
    $createWorkItems = Read-Host "Create Epic/Feature/Task structure from documentation? This will create ~80 work items (y/n)"
    
    if ($createWorkItems -eq 'y') {
        $workItemScript = Join-Path $PSScriptRoot "docs\scripts\generate-work-items.ps1"
        if (Test-Path $workItemScript) {
            Write-Info "Running work item generation script..."
            & $workItemScript -Organization $Organization -Project $Project -PAT $PAT
        }
        else {
            Write-Warning "Work item script not found: $workItemScript"
        }
    }
    else {
        Write-Info "Skipping work item creation"
    }
}

# Summary
Write-Section "Setup Complete"

Write-Host "Resources created/verified:" -ForegroundColor Yellow
Write-Host ""
if (-not $SkipWiki) {
    Write-Success "Wiki: $Organization/$Project/_wiki/wikis/Hartonomous-Documentation"
}
if (-not $SkipPipelines) {
    Write-Success "Pipelines: $Organization/$Project/_build"
}
if (-not $SkipArc) {
    Write-Success "Agent Pool: Hartonomous-OnPrem"
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Review Wiki pages" -ForegroundColor White
Write-Host "  2. Configure branch policies" -ForegroundColor White
Write-Host "  3. Install self-hosted agents (if using Arc)" -ForegroundColor White
Write-Host "  4. Create variable group 'Hartonomous-Secrets' with AZURE_DEVOPS_PAT" -ForegroundColor White
Write-Host ""

#endregion
