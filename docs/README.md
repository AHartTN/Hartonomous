# Hartonomous Documentation

> Enterprise-grade documentation integrated with Azure DevOps Wiki, Boards, and Pipelines

## Quick Links

- ?? **[Wiki](https://dev.azure.com/aharttn/Hartonomous/_wiki/wikis/Hartonomous-Documentation)** - Technical documentation
- ?? **[Boards](https://dev.azure.com/aharttn/Hartonomous/_boards)** - Project tracking
- ?? **[Pipelines](https://dev.azure.com/aharttn/Hartonomous/_build)** - CI/CD for docs
- ?? **[Repos](https://dev.azure.com/aharttn/Hartonomous/_git/Hartonomous)** - Source code

## Documentation Structure

```
docs/
??? ARCHITECTURE.md              # System architecture and core principles
??? TECHNICAL_SPECIFICATION.md   # Algorithms, data structures, performance
??? DATABASE_SCHEMA.md           # PostgreSQL + PostGIS schema
??? IMPLEMENTATION_GUIDE.md      # Development phases and code examples
??? SYSTEM_ARCHITECTURE.md       # Component design and data flow
??? API_REFERENCE.md             # REST API endpoints and WebSocket
??? AZURE_DEVOPS_INTEGRATION.md  # This integration guide
??? README.md                    # This file
```

## Setup

### Prerequisites

- Azure CLI with DevOps extension (`az extension add --name azure-devops`)
- Azure DevOps Personal Access Token (PAT)
- Access to Azure DevOps organization: `https://dev.azure.com/aharttn`

### Quick Start

1. **Set your PAT:**
   ```powershell
   $env:AZURE_DEVOPS_PAT = "your-pat-here"
   ```

2. **Run setup script:**
   ```powershell
   .\setup-azure-devops.ps1
   ```

3. **Access the Wiki:**
   Navigate to: https://dev.azure.com/aharttn/Hartonomous/_wiki/wikis/Hartonomous-Documentation

### Manual Setup

If you prefer manual setup or need to customize:

#### Create Wiki
```bash
az devops wiki create \
    --name "Hartonomous-Documentation" \
    --type projectwiki \
    --mapped-path /docs \
    --repository Hartonomous \
    --org https://dev.azure.com/aharttn \
    --project Hartonomous
```

#### Sync Documentation
```powershell
.\docs\scripts\sync-to-wiki.ps1 `
    -Organization "https://dev.azure.com/aharttn" `
    -Project "Hartonomous" `
    -PAT $env:AZURE_DEVOPS_PAT
```

#### Create Work Items
```powershell
.\docs\scripts\generate-work-items.ps1 `
    -Organization "https://dev.azure.com/aharttn" `
    -Project "Hartonomous" `
    -PAT $env:AZURE_DEVOPS_PAT
```

## CI/CD Pipelines

### Documentation Validation (`docs-validation.yml`)

**Triggers:** PRs and commits to `docs/**`

**What it does:**
- Lints Markdown files
- Checks for broken links
- Validates SQL and C# code blocks
- Verifies documentation completeness
- Publishes validated docs as artifact

**Status:** [![Docs Validation](https://dev.azure.com/aharttn/Hartonomous/_apis/build/status/Docs-Validation)](https://dev.azure.com/aharttn/Hartonomous/_build)

### Documentation Publish (`docs-publish.yml`)

**Triggers:** Commits to `main` branch affecting `docs/**`

**What it does:**
- Syncs documentation to Wiki
- Creates release notes
- Updates Wiki pages automatically

**Status:** [![Docs Publish](https://dev.azure.com/aharttn/Hartonomous/_apis/build/status/Docs-Publish)](https://dev.azure.com/aharttn/Hartonomous/_build)

## Work Item Structure

### Epics

1. **Core Infrastructure** - Landmark projection, Hilbert encoding, database
2. **Ingestion Pipeline** - Content decomposition, BPE, batch operations
3. **Query Engine** - Geometric queries, graph traversal, caching
4. **API Layer** - REST endpoints, real-time communication
5. **UI and Visualization** - Blazor UI, 3D visualization
6. **Performance Optimization** - SIMD/AVX, GPU, database tuning

Each Epic contains Features, which contain Tasks - all linked to documentation pages.

## Contributing

### Documentation Standards

- Use Markdown with proper formatting
- Include code examples for all algorithms
- Add SQL examples for database operations
- Keep line length reasonable (no hard limit)
- Use consistent heading levels
- Link related documentation pages

### Making Changes

1. Create feature branch: `git checkout -b feature/update-docs`
2. Edit documentation files in `docs/`
3. Test locally: `markdownlint docs/**/*.md`
4. Commit and push
5. Create Pull Request
6. Pipeline validates automatically
7. After merge to `main`, Wiki syncs automatically

### Documentation Checklist

Before submitting PR:

- [ ] Markdown is properly formatted
- [ ] All links work (internal and external)
- [ ] Code blocks have language specified
- [ ] SQL queries are valid
- [ ] C# code compiles (if complete examples)
- [ ] Related work items are linked
- [ ] Screenshots/diagrams added if needed

## Scripts

### `sync-to-wiki.ps1`

Syncs all Markdown files from `docs/` to Azure DevOps Wiki.

```powershell
.\docs\scripts\sync-to-wiki.ps1 `
    -Organization "https://dev.azure.com/aharttn" `
    -Project "Hartonomous" `
    -PAT "your-pat"
```

### `generate-work-items.ps1`

Creates Epic/Feature/Task hierarchy from documentation structure.

```powershell
.\docs\scripts\generate-work-items.ps1 `
    -Organization "https://dev.azure.com/aharttn" `
    -Project "Hartonomous" `
    -PAT "your-pat"
```

**?? Warning:** This creates many work items. Run once per project.

## Maintenance

### Weekly Tasks

- Review outdated documentation alerts
- Update work item progress
- Sync implementation status to Wiki
- Check for broken links

### Monthly Tasks

- Refresh materialized views in Wiki
- Archive old release notes
- Update architecture diagrams
- Review and update benchmarks

### Release Process

1. Documentation freeze 1 week before release
2. Generate versioned documentation snapshot
3. Create release Wiki page with changelog
4. Tag documentation version in Git
5. Publish release notes to Wiki

## Troubleshooting

### Wiki sync fails

**Error:** "Wiki not found"

**Solution:**
```powershell
az devops wiki create --name "Hartonomous-Documentation" --type projectwiki --mapped-path /docs --repository Hartonomous
```

### Pipeline fails on validation

**Error:** "markdownlint errors"

**Solution:**
```bash
markdownlint docs/**/*.md --config docs/.markdownlint.json --fix
```

### Work items not linking

**Error:** "Target work item not found"

**Solution:** Ensure parent Epic/Feature exists before creating children.

## Resources

- [Azure DevOps Documentation](https://docs.microsoft.com/en-us/azure/devops/)
- [Azure CLI DevOps Extension](https://docs.microsoft.com/en-us/cli/azure/ext/azure-devops/)
- [Markdown Guide](https://www.markdownguide.org/)
- [markdownlint Rules](https://github.com/DavidAnson/markdownlint/blob/main/doc/Rules.md)

## Support

- **Issues:** Create in Azure DevOps Boards
- **Questions:** Post in Team channel
- **Suggestions:** Create Feature request in Boards

---

**Last Updated:** 2025-01-20  
**Documentation Version:** 1.0.0  
**Project:** Hartonomous Atomic Content-Addressable Storage System
