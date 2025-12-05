# Code Organization Cleanup Plan

## Principles

1. **Single Responsibility** - One class per file
2. **Separation of Concerns** - Clear boundaries between layers
3. **DRY** - Eliminate duplication through proper abstraction
4. **SOLID** - Follow all five principles strictly
5. **Consistent Naming** - File name matches primary type
6. **Logical Grouping** - Related types in appropriate namespaces

## Methodology

Process each file ONE AT A TIME:
1. Open file
2. Identify all types (classes, interfaces, enums, records)
3. For each type after the first:
   - Create new file named for that type
   - Move type to new file
   - Update namespace if needed
   - Fix all references
   - DO NOT Build and verify after each change... Make changes first and then build when you are at a good state
4. Commit after each extraction
5. Move to next file

**NO BATCHING**. One extraction, one verification, one commit.

## Scan Results (To Be Populated)

### Files with Multiple Types

Will scan Core, Data, Infrastructure projects systematically and list:
- File path
- Primary type
- Additional types found
- Extraction priority (High/Medium/Low)

### Common Patterns Requiring Extraction

1. **Base Classes**
   - Shared functionality currently duplicated
   - Candidate for abstract base classes

2. **Interfaces**
   - Service interfaces mixed with implementations
   - Should be in separate Interfaces/ folders

3. **Extensions**
   - Extension methods mixed with other code
   - Should be in Extensions/ namespace

4. **Helpers/Utilities**
   - Static helper methods scattered
   - Should be in Utilities/ with clear purpose

5. **Constants/Enums**
   - Magic numbers and strings
   - Should be in named constant files

## Process Template

For each file requiring cleanup:

```markdown
### File: [Path]

**Primary Type**: [Name]
**Additional Types**: 
- [ ] Type1 (Priority: High/Medium/Low)
- [ ] Type2 (Priority: High/Medium/Low)

**Action Plan**:
1. Extract Type1 to new file
2. Update references
3. Build and verify
4. Commit: "refactor: extract [Type1] from [File]"
5. Repeat for Type2

**Dependencies**: [List files that reference these types]
**Risk**: [Low/Medium/High]
```

## Execution Order

1. **Phase 1: Core Domain** (Highest priority)
   - ValueObjects
   - Entities
   - Enums
   - Domain Events

2. **Phase 2: Core Application**
   - Commands
   - Queries
   - Interfaces
   - DTOs

3. **Phase 3: Infrastructure**
   - Services
   - Extensions
   - Configurations

4. **Phase 4: Data**
   - Repositories
   - Configurations
   - Extensions

5. **Phase 5: Tests**
   - Test fixtures
   - Test utilities
   - Mock implementations

## Success Criteria

After each extraction:
- ? 0 compilation errors
- ? 0 warnings
- ? All existing tests still pass
- ? File structure follows conventions
- ? Namespaces match folder structure

## Tools/Commands

```bash
# Find files with multiple type definitions
Get-ChildItem -Path "D:\Repositories\Hartonomous\Hartonomous.Core" -Recurse -Filter "*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName
    $typeCount = ($content | Select-String "^\s*(public|internal|private)?\s*(class|interface|enum|record|struct)\s+").Count
    if ($typeCount -gt 1) {
        [PSCustomObject]@{
            File = $_.FullName
            TypeCount = $typeCount
        }
    }
}

# Build specific project
dotnet build [project].csproj --no-restore

# Run tests for specific project
dotnet test [project].csproj --no-restore --no-build
```

---

**Next Step**: Scan Core project for multiple-type files and begin systematic extraction.
