# Hartonomous Azure DevOps Integration Setup
# Run this script to initialize Wiki, Boards, Pipelines, and Azure Arc integration

param(
    [Parameter(Mandatory=$false)]
    [string]$PAT = $env:AZURE_DEVOPS_PAT,
    
    [Parameter(Mandatory=$false)]
    [string]$Organization = "https://dev.azure.com/aharttn",
    
    [Parameter(Mandatory=$false)]
    [string]$Project = "Hartonomous",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipArc
)

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Hartonomous Azure DevOps Integration Setup" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

if (-not $PAT) {
    Write-Host "ERROR: Azure DevOps PAT not found!" -ForegroundColor Red
    Write-Host "Please set AZURE_DEVOPS_PAT environment variable or pass -PAT parameter" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To create a PAT:" -ForegroundColor Yellow
    Write-Host "1. Go to $Organization/_usersSettings/tokens" -ForegroundColor Gray
    Write-Host "2. Click 'New Token'" -ForegroundColor Gray
    Write-Host "3. Select scopes: Code (Read & Write), Wiki (Read & Write), Work Items (Read & Write), Agent Pools (Read & Manage)" -ForegroundColor Gray
    Write-Host "4. Copy the token and set: `$env:AZURE_DEVOPS_PAT = 'your-token-here'" -ForegroundColor Gray
    exit 1
}

# Set PAT for az cli
$env:AZURE_DEVOPS_EXT_PAT = $PAT

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Organization: $Organization" -ForegroundColor Gray
Write-Host "  Project: $Project" -ForegroundColor Gray
Write-Host ""

# Step 1: Create Wiki
Write-Host "[1/5] Creating Project Wiki..." -ForegroundColor Cyan

$existingWiki = az devops wiki list `
    --org $Organization `
    --project $Project `
    --query "[?name=='Hartonomous-Documentation'].id" `
    --output tsv

if ($existingWiki) {
    Write-Host "  ? Wiki already exists (ID: $existingWiki)" -ForegroundColor Green
}
else {
    Write-Host "  Creating new wiki..." -ForegroundColor Yellow
    
    $wikiId = az devops wiki create `
        --name "Hartonomous-Documentation" `
        --type projectwiki `
        --mapped-path /docs `
        --repository Hartonomous `
        --org $Organization `
        --project $Project `
        --query "id" `
        --output tsv
    
    Write-Host "  ? Wiki created (ID: $wikiId)" -ForegroundColor Green
}

# Step 2: Sync Documentation
Write-Host ""
Write-Host "[2/5] Syncing Documentation to Wiki..." -ForegroundColor Cyan

& "$PSScriptRoot\docs\scripts\sync-to-wiki.ps1" `
    -Organization $Organization `
    -Project $Project `
    -PAT $PAT

# Step 3: Create Work Items
Write-Host ""
Write-Host "[3/5] Creating Work Item Structure..." -ForegroundColor Cyan
Write-Host "  This will create Epics, Features, and Tasks from documentation..." -ForegroundColor Yellow

$createWorkItems = Read-Host "  Do you want to create work items? (y/n)"

if ($createWorkItems -eq 'y') {
    & "$PSScriptRoot\docs\scripts\generate-work-items.ps1" `
        -Organization $Organization `
        -Project $Project `
        -PAT $PAT
}
else {
    Write-Host "  Skipped work item creation" -ForegroundColor Gray
}

# Step 4: Create Pipelines
Write-Host ""
Write-Host "[4/5] Creating Build Pipelines..." -ForegroundColor Cyan

# Check if pipelines exist
$existingValidation = az pipelines list `
    --org $Organization `
    --project $Project `
    --query "[?name=='Docs-Validation'].id" `
    --output tsv

if ($existingValidation) {
    Write-Host "  ? Docs-Validation pipeline already exists" -ForegroundColor Green
}
else {
    Write-Host "  Creating Docs-Validation pipeline..." -ForegroundColor Yellow
    
    az pipelines create `
        --name "Docs-Validation" `
        --repository Hartonomous `
        --branch main `
        --yml-path ".azure-pipelines/docs-validation.yml" `
        --org $Organization `
        --project $Project `
        --skip-first-run
    
    Write-Host "  ? Docs-Validation pipeline created" -ForegroundColor Green
}

$existingPublish = az pipelines list `
    --org $Organization `
    --project $Project `
    --query "[?name=='Docs-Publish'].id" `
    --output tsv

if ($existingPublish) {
    Write-Host "  ? Docs-Publish pipeline already exists" -ForegroundColor Green
}
else {
    Write-Host "  Creating Docs-Publish pipeline..." -ForegroundColor Yellow
    Write-Host "  NOTE: You'll need to create a variable group 'AzureDevOps-Secrets' with AZURE_DEVOPS_PAT" -ForegroundColor Yellow
    
    az pipelines create `
        --name "Docs-Publish" `
        --repository Hartonomous `
        --branch main `
        --yml-path ".azure-pipelines/docs-publish.yml" `
        --org $Organization `
        --project $Project `
        --skip-first-run
    
    Write-Host "  ? Docs-Publish pipeline created" -ForegroundColor Green
}

# Step 5: Setup Branch Policies
Write-Host ""
Write-Host "[5/5] Configuring Branch Policies..." -ForegroundColor Cyan

$repoId = az repos list `
    --org $Organization `
    --project $Project `
    --query "[?name=='Hartonomous'].id" `
    --output tsv

if ($repoId) {
    Write-Host "  Repository ID: $repoId" -ForegroundColor Gray
    
    # Check if policies exist (simplified check)
    Write-Host "  Configuring policies for 'main' branch..." -ForegroundColor Yellow
    Write-Host "  NOTE: Branch policies require manual configuration in Azure DevOps UI" -ForegroundColor Yellow
    Write-Host "  Go to: $Organization/$Project/_settings/repositories?repo=$repoId&_a=policiesMid" -ForegroundColor Gray
}

# Step 5: Azure Arc Integration (if not skipped)
if (-not $SkipArc) {
    Write-Host ""
    Write-Host "[BONUS] Azure Arc Integration..." -ForegroundColor Cyan
    Write-Host "  Detected Azure Arc-connected machines:" -ForegroundColor Yellow
    
    $setupArc = Read-Host "  Setup self-hosted agents on HART-SERVER/HART-DESKTOP? (y/n)"
    
    if ($setupArc -eq 'y') {
        Write-Host ""
        Write-Host "  Creating agent pool 'Hartonomous-OnPrem'..." -ForegroundColor Yellow
        
        $existingPool = az pipelines pool list `
            --org $Organization `
            --query "[?name=='Hartonomous-OnPrem'].id" `
            --output tsv
        
        if ($existingPool) {
            Write-Host "  ? Agent pool already exists" -ForegroundColor Green
        }
        else {
            az pipelines pool create `
                --name "Hartonomous-OnPrem" `
                --org $Organization `
                --project $Project `
                --pool-type private
            
            Write-Host "  ? Agent pool created" -ForegroundColor Green
        }
        
        Write-Host ""
        Write-Host "  Next: Install agents on your Arc machines" -ForegroundColor Yellow
        Write-Host "  See: docs/AZURE_ARC_INTEGRATION.md for instructions" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  Agent download:" -ForegroundColor Yellow
        Write-Host "  https://vstsagentpackage.azureedge.net/agent/3.236.1/vsts-agent-win-x64-3.236.1.zip" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  Configure command:" -ForegroundColor Yellow
        Write-Host "  .\config.cmd --unattended --url $Organization --auth pat --token $PAT --pool Hartonomous-OnPrem --agent HART-SERVER" -ForegroundColor Gray
    }
}

# Summary
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Setup Complete!" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. View Wiki:" -ForegroundColor White
Write-Host "   $Organization/$Project/_wiki/wikis/Hartonomous-Documentation" -ForegroundColor Gray
Write-Host ""
Write-Host "2. View Work Items (if created):" -ForegroundColor White
Write-Host "   $Organization/$Project/_boards/board" -ForegroundColor Gray
Write-Host ""
Write-Host "3. View Pipelines:" -ForegroundColor White
Write-Host "   $Organization/$Project/_build" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Create Variable Group for Docs-Publish pipeline:" -ForegroundColor White
Write-Host "   $Organization/$Project/_library?itemType=VariableGroups" -ForegroundColor Gray
Write-Host "   - Name: AzureDevOps-Secrets" -ForegroundColor Gray
Write-Host "   - Variable: AZURE_DEVOPS_PAT (secret)" -ForegroundColor Gray
Write-Host ""
Write-Host "5. Configure Branch Policies:" -ForegroundColor White
Write-Host "   $Organization/$Project/_settings/repositories" -ForegroundColor Gray
Write-Host ""
