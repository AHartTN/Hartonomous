# Azure DevOps Integration Plan

## Overview

Integrate Hartonomous documentation and project management into Azure DevOps infrastructure:
- **Wiki**: Technical documentation with version control
- **Boards**: Epic/Feature/Task structure for implementation tracking
- **Repos**: Documentation as code with branch policies
- **Pipelines**: Automated documentation validation and deployment

---

## 1. Wiki Setup

### Create Project Wiki

```bash
# Create wiki from docs folder
az devops wiki create \
    --name "Hartonomous-Documentation" \
    --type projectwiki \
    --mapped-path /docs \
    --repository Hartonomous \
    --organization https://dev.azure.com/aharttn \
    --project Hartonomous
```

### Wiki Structure

```
Hartonomous-Documentation/
??? Home.md                           (Overview + Quick Links)
??? Getting-Started/
?   ??? Quick-Start.md
?   ??? Installation.md
?   ??? First-Content-Ingestion.md
??? Architecture/
?   ??? Overview.md                   (ARCHITECTURE.md)
?   ??? Core-Principles.md
?   ??? Landmark-Projection.md
?   ??? BPE-Composition.md
?   ??? Geometric-Indexing.md
??? Technical-Specification/
?   ??? Data-Structures.md            (TECHNICAL_SPECIFICATION.md)
?   ??? Algorithms.md
?   ??? Performance-Optimization.md
?   ??? Hardware-Requirements.md
??? Database/
?   ??? Schema-Reference.md           (DATABASE_SCHEMA.md)
?   ??? Geometric-Queries.md
?   ??? Optimization-Strategies.md
?   ??? Maintenance.md
??? API-Reference/
?   ??? REST-Endpoints.md
?   ??? WebSocket-API.md
?   ??? Authentication.md
??? Implementation/
?   ??? Development-Phases.md
?   ??? Code-Examples.md
?   ??? Testing-Strategy.md
?   ??? Deployment.md
??? Operations/
?   ??? Monitoring.md
?   ??? Performance-Tuning.md
?   ??? Troubleshooting.md
?   ??? Disaster-Recovery.md
??? Research/
    ??? Novel-Query-Types.md
    ??? GPU-Acceleration.md
    ??? SIMD-Optimizations.md
    ??? Future-Enhancements.md
```

---

## 2. Azure Boards Setup

### Epic Structure

Create hierarchical work items representing the system architecture:

**Epic 1: Core Infrastructure**
- Feature: Landmark Projection System
  - Task: Implement XXHash64 projection
  - Task: Implement Hilbert curve encoding
  - Task: SIMD/AVX optimization
  - Task: GPU acceleration (optional)
- Feature: Database Schema
  - Task: Create atoms table with PostGIS
  - Task: Create atom_edges table
  - Task: Implement spatial indexes
  - Task: Create triggers for ref_count

**Epic 2: Ingestion Pipeline**
- Feature: Content Decomposition
  - Task: Text decomposer
  - Task: Image decomposer
  - Task: Audio decomposer
  - Task: Video decomposer
- Feature: BPE Processing
  - Task: Pair detection algorithm
  - Task: Composite atom creation
  - Task: Edge creation with geometry
- Feature: Batch Operations
  - Task: Set-based SQL operations
  - Task: Parallel hash computation
  - Task: Bulk insert with deduplication

**Epic 3: Query Engine**
- Feature: Geometric Queries
  - Task: k-NN proximity search
  - Task: Bounded region search
  - Task: Convex hull similarity
  - Task: Density clustering
- Feature: Graph Traversal
  - Task: Recursive parent lookup
  - Task: Content reconstruction
  - Task: Path finding
- Feature: Caching Layer
  - Task: LRU cache for hot atoms
  - Task: Materialized views
  - Task: Query result caching

**Epic 4: API Layer**
- Feature: REST API
  - Task: Ingestion endpoints
  - Task: Query endpoints
  - Task: Reconstruction endpoints
  - Task: Statistics endpoints
- Feature: Real-time Communication
  - Task: SignalR hub
  - Task: WebSocket API
  - Task: Live statistics streaming

**Epic 5: UI/Visualization**
- Feature: Blazor Web UI
  - Task: Dashboard page
  - Task: Atom explorer
  - Task: Query interface
  - Task: Statistics charts
- Feature: 3D Visualization
  - Task: Three.js/Babylon.js integration
  - Task: Hilbert space rendering
  - Task: Graph visualization
  - Task: Interactive navigation

**Epic 6: Performance Optimization**
- Feature: SIMD/AVX
  - Task: Hash computation vectorization
  - Task: Coordinate extraction
  - Task: Hilbert encoding batch
- Feature: GPU Acceleration
  - Task: CUDA kernel for Hilbert encoding
  - Task: Parallel BPE pair counting
  - Task: Batch reconstruction
- Feature: Database Optimization
  - Task: Set-based operations
  - Task: Parallel query execution
  - Task: Partitioning strategy

---

## 3. Repository Integration

### Branch Strategy

```
main                     (production-ready documentation)
??? develop              (active development)
?   ??? feature/wiki-structure
?   ??? feature/api-docs
?   ??? feature/implementation-guide
??? release/*           (release documentation versions)
```

### Branch Policies

```bash
# Require pull request reviews
az repos policy create \
    --repository-id <repo-id> \
    --branch main \
    --blocking true \
    --enabled true \
    --policy-type Minimum-reviewers \
    --minimum-reviewer-count 1

# Require linked work items
az repos policy create \
    --repository-id <repo-id> \
    --branch main \
    --blocking true \
    --enabled true \
    --policy-type Required-work-items

# Require build validation
az repos policy create \
    --repository-id <repo-id> \
    --branch main \
    --blocking true \
    --enabled true \
    --policy-type Build \
    --build-definition-id <build-id>
```

### Documentation as Code

```
docs/
??? .azure-pipelines/
?   ??? docs-validation.yml      (Markdown linting, link checking)
?   ??? docs-publish.yml         (Deploy to Wiki)
?   ??? docs-versioning.yml      (Create versioned snapshots)
??? schemas/
?   ??? architecture.schema.json
?   ??? api-spec.schema.json
?   ??? database.schema.json
??? templates/
?   ??? feature-template.md
?   ??? api-endpoint-template.md
?   ??? query-example-template.md
??? scripts/
    ??? generate-wiki-toc.ps1
    ??? validate-links.ps1
    ??? generate-diagrams.ps1
    ??? sync-to-wiki.ps1
```

---

## 4. Pipeline Integration

### Documentation Validation Pipeline

**File:** `.azure-pipelines/docs-validation.yml`

```yaml
trigger:
  branches:
    include:
      - main
      - develop
  paths:
    include:
      - docs/**

pool:
  vmImage: 'ubuntu-latest'

steps:
  - task: NodeTool@0
    inputs:
      versionSpec: '18.x'
    displayName: 'Install Node.js'

  - script: |
      npm install -g markdownlint-cli
      markdownlint docs/**/*.md
    displayName: 'Lint Markdown'

  - script: |
      npm install -g markdown-link-check
      find docs -name '*.md' -exec markdown-link-check {} \;
    displayName: 'Check Links'

  - task: PowerShell@2
    inputs:
      filePath: 'docs/scripts/validate-code-blocks.ps1'
    displayName: 'Validate Code Blocks'

  - task: PowerShell@2
    inputs:
      filePath: 'docs/scripts/generate-diagrams.ps1'
    displayName: 'Generate Architecture Diagrams'

  - task: PublishPipelineArtifact@1
    inputs:
      targetPath: '$(Build.ArtifactStagingDirectory)'
      artifact: 'validated-docs'
    displayName: 'Publish Validated Docs'
```

### Wiki Sync Pipeline

**File:** `.azure-pipelines/docs-publish.yml`

```yaml
trigger:
  branches:
    include:
      - main
  paths:
    include:
      - docs/**

pool:
  vmImage: 'ubuntu-latest'

variables:
  - group: AzureDevOps-Secrets

steps:
  - checkout: self
    persistCredentials: true

  - task: PowerShell@2
    inputs:
      filePath: 'docs/scripts/sync-to-wiki.ps1'
      arguments: '-Organization "$(System.CollectionUri)" -Project "$(System.TeamProject)" -PAT "$(AZURE_DEVOPS_PAT)"'
    displayName: 'Sync Docs to Wiki'

  - task: AzureCLI@2
    inputs:
      azureSubscription: 'Azure-Subscription'
      scriptType: 'ps'
      scriptLocation: 'inlineScript'
      inlineScript: |
        az devops wiki page create \
          --path /Release-Notes/$(Build.BuildNumber) \
          --content "Release $(Build.BuildNumber) documentation updated" \
          --org $(System.CollectionUri) \
          --project $(System.TeamProject)
    displayName: 'Create Release Note'
```

---

## 5. Work Item Templates

### Feature Template

```yaml
# Feature: [Feature Name]

## Description
Brief description of the feature and its business value.

## Acceptance Criteria
- [ ] Criterion 1
- [ ] Criterion 2
- [ ] Criterion 3

## Technical Design
Link to Wiki page with detailed technical design.

## Dependencies
- Feature/Task IDs that must be completed first

## Risks
- Potential risks and mitigation strategies

## Testing Strategy
- Unit tests
- Integration tests
- Performance benchmarks

## Documentation
- [ ] Architecture documentation updated
- [ ] API reference updated
- [ ] Code examples added
- [ ] Wiki pages created
```

### Task Template

```yaml
# Task: [Task Name]

## Description
Detailed description of the task.

## Implementation Notes
Technical details and approach.

## Acceptance Criteria
- [ ] Code complete
- [ ] Unit tests pass
- [ ] Code reviewed
- [ ] Documentation updated

## Definition of Done
- [ ] Code merged to develop
- [ ] Build pipeline passes
- [ ] Wiki documentation updated
- [ ] Linked feature updated
```

---

## 6. Dashboard Configuration

### Custom Dashboard Widgets

**Documentation Health Dashboard:**
- Wiki pages count
- Last documentation update timestamp
- Open documentation issues
- Documentation coverage (% of features documented)
- Broken links count
- Outdated pages alert

**Implementation Progress Dashboard:**
- Epic/Feature/Task burndown
- Code coverage trend
- Performance benchmark trend
- Open bugs by priority
- Pull requests pending review

---

## 7. Integration Scripts

### Sync Docs to Wiki

**File:** `docs/scripts/sync-to-wiki.ps1`

```powershell
param(
    [string]$Organization,
    [string]$Project,
    [string]$PAT
)

# Authenticate
$env:AZURE_DEVOPS_EXT_PAT = $PAT

# Get wiki ID
$wikiId = az devops wiki list `
    --org $Organization `
    --project $Project `
    --query "[?name=='Hartonomous-Documentation'].id" `
    --output tsv

# Sync each markdown file
Get-ChildItem -Path "docs" -Filter "*.md" -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Replace("$PSScriptRoot\..\", "").Replace("\", "/")
    $wikiPath = "/$($relativePath.Replace("docs/", "").Replace(".md", ""))"
    
    Write-Host "Syncing $relativePath to $wikiPath"
    
    az devops wiki page create-or-update `
        --wiki $wikiId `
        --path $wikiPath `
        --file-path $_.FullName `
        --org $Organization `
        --project $Project
}
```

### Generate Work Items from Documentation

**File:** `docs/scripts/generate-work-items.ps1`

```powershell
param(
    [string]$Organization,
    [string]$Project,
    [string]$DocumentationPath
)

# Parse ARCHITECTURE.md for components
$architecture = Get-Content "$DocumentationPath/ARCHITECTURE.md" -Raw

# Extract sections as Epics/Features
$sections = [regex]::Matches($architecture, '## (.+)')

foreach ($section in $sections) {
    $title = $section.Groups[1].Value
    
    # Create Epic
    az boards work-item create `
        --type Epic `
        --title $title `
        --org $Organization `
        --project $Project `
        --fields "System.Description=Auto-generated from documentation"
}
```

---

## 8. Automated Documentation Generation

### Architecture Diagrams

**File:** `docs/scripts/generate-diagrams.ps1`

```powershell
# Generate Mermaid diagrams from documentation

# Install mermaid-cli
npm install -g @mermaid-js/mermaid-cli

# Generate architecture diagram
$mermaidContent = @"
graph TD
    A[Client] --> B[API Gateway]
    B --> C[Ingestion Service]
    B --> D[Query Service]
    B --> E[Reconstruction Service]
    C --> F[PostgreSQL + PostGIS]
    D --> F
    E --> F
    C --> G[BPE Processor]
    G --> F
"@

$mermaidContent | Out-File "docs/diagrams/architecture.mmd"

mmdc -i docs/diagrams/architecture.mmd -o docs/diagrams/architecture.svg
```

### API Documentation from Code

```powershell
# Generate Swagger/OpenAPI spec
dotnet swagger tofile --output docs/api/swagger.json Hartonomous.API/bin/Release/net10.0/Hartonomous.API.dll v1

# Convert to Markdown for Wiki
npx widdershins --language_tabs csharp:C# python:Python --summary docs/api/swagger.json -o docs/API-Reference/REST-Endpoints.md
```

---

## 9. Implementation Steps

### Step 1: Initialize Wiki

```bash
# Create wiki
az devops wiki create \
    --name "Hartonomous-Documentation" \
    --type projectwiki \
    --mapped-path /docs \
    --repository Hartonomous \
    --org https://dev.azure.com/aharttn \
    --project Hartonomous

# Set permissions
az devops security permission update \
    --namespace <wiki-namespace-id> \
    --subject <group-id> \
    --allow-bit 1 \
    --token <wiki-token>
```

### Step 2: Create Work Item Structure

```bash
# Create Epics from documentation sections
./docs/scripts/generate-work-items.ps1 `
    -Organization "https://dev.azure.com/aharttn" `
    -Project "Hartonomous" `
    -DocumentationPath "./docs"
```

### Step 3: Setup Pipelines

```bash
# Create validation pipeline
az pipelines create \
    --name "Docs-Validation" \
    --yml-path ".azure-pipelines/docs-validation.yml" \
    --org https://dev.azure.com/aharttn \
    --project Hartonomous

# Create publish pipeline
az pipelines create \
    --name "Docs-Publish" \
    --yml-path ".azure-pipelines/docs-publish.yml" \
    --org https://dev.azure.com/aharttn \
    --project Hartonomous
```

### Step 4: Configure Branch Policies

```bash
# Get repo ID
$repoId = az repos list --org https://dev.azure.com/aharttn --project Hartonomous --query "[?name=='Hartonomous'].id" -o tsv

# Apply policies
az repos policy create \
    --repository-id $repoId \
    --branch main \
    --blocking true \
    --enabled true \
    --policy-type Minimum-reviewers \
    --minimum-reviewer-count 1
```

### Step 5: Initial Sync

```bash
# Sync existing docs to Wiki
./docs/scripts/sync-to-wiki.ps1 `
    -Organization "https://dev.azure.com/aharttn" `
    -Project "Hartonomous" `
    -PAT $env:AZURE_DEVOPS_PAT
```

---

## 10. Maintenance and Updates

### Daily Operations

- Pipeline automatically validates new docs on PR
- Wiki syncs on merge to main
- Work items auto-link to documentation pages
- Dashboard shows documentation health

### Weekly Reviews

- Review outdated documentation alerts
- Update work item progress
- Sync implementation status to Wiki
- Generate progress reports

### Release Process

1. Documentation freeze 1 week before release
2. Generate versioned documentation snapshot
3. Create release Wiki page with changelog
4. Tag documentation version in Git
5. Publish release notes to Wiki

---

## Benefits

1. **Single Source of Truth**: Documentation lives in Git, syncs to Wiki
2. **Version Control**: Full history of documentation changes
3. **Automated Validation**: Linting, link checking, diagram generation
4. **Work Item Integration**: Features link directly to technical docs
5. **CI/CD for Docs**: Same rigor as code development
6. **Searchable**: Azure DevOps search indexes Wiki content
7. **Collaborative**: Pull request reviews for documentation
8. **Metrics**: Track documentation coverage and health

