# Core Project - Files Requiring Type Extraction

## Summary
- Total files with multiple types: 24
- Total extra types to extract: ~30+
- Average types per file: 2-3

## Extraction Queue (Priority Order)

### HIGH PRIORITY - Application Layer DTOs

These are tightly coupled response/request objects. Strategy: Create dedicated DTO folders.

#### 1. Commands - Response Types
- [x] `LearnBPEVocabularyCommandResult` from LearnBPEVocabularyCommand.cs ?
- [x] `MergeBPETokensResponse` from MergeBPETokensCommand.cs ?
- [x] `MergeBytePairCommandResult` from MergeBytePairCommand.cs ?
- [x] `IngestContentResponse` from IngestContentCommand.cs ?
- [x] `IngestRepositoryResponse` from IngestRepositoryCommand.cs ?
- [x] `CreateLandmarkResponse` from CreateLandmarkCommand.cs ?

**Status**: COMPLETE (6/6) - Committed: 3735344

**Target Location**: `Hartonomous.Core/Application/Commands/{Feature}/DTOs/`

#### 2. Queries - DTO Types
- [x] `BPEStatisticsDto` from GetBPEStatisticsQuery.cs ?
- [x] `BPEVocabularyResponse` from GetBPEVocabularyQuery.cs ?
- [x] `BPETokenDto` from GetBPEVocabularyQuery.cs ?
- [x] `NearestConstantDto` from FindNearestNeighborsQuery.cs ?
- [x] `AllConstantDto` from GetAllConstantsQuery.cs ?
- [x] `ConstantDto` from GetConstantByHashQuery.cs ?
- [x] `SpatialCoordinateDto` from GetConstantByHashQuery.cs (shared) ?
- [x] `ConstantStatisticsDto` from GetConstantStatisticsQuery.cs ?
- [x] `RecentConstantDto` from GetRecentConstantsQuery.cs ?
- [x] `ContentIngestionDto` from GetContentIngestionByIdQuery.cs ?
- [x] `IngestionMetricsDto` from GetIngestionMetricsQuery.cs ?
- [x] `IngestionDto` from GetIngestionsByStatusQuery.cs ?
- [x] `IngestionStatisticsDto` from GetIngestionStatisticsQuery.cs ?
- [x] `LandmarkCandidateDto` from DetectLandmarkCandidatesQuery.cs ?
- [x] `LandmarkDto` from GetLandmarkByNameQuery.cs ?

**Status**: COMPLETE (15/15) - Committed: f0d0c78

**Target Location**: `Hartonomous.Core/Application/Queries/{Feature}/DTOs/`

### MEDIUM PRIORITY - Common Infrastructure

#### 3. Application Common Types
- [ ] `ICommand<TResponse>` (second interface) from ICommand.cs
- [ ] `Result<T>` from Result.cs

**Action**: These are actually proper - generic overloads in same file are acceptable. SKIP.

#### 4. Repository Statistics Types
- [ ] `ContentBoundaryStatistics` from IContentBoundaryRepository.cs
- [ ] `GpuCapabilities` from IGpuService.cs
- [ ] `LandmarkCandidate` from IGpuService.cs
- [ ] `HierarchicalContentTree` from IHierarchicalContentRepository.cs
- [ ] `HierarchyStatistics` from IHierarchicalContentRepository.cs
- [ ] `ModelStatistics` from INeuralNetworkLayerRepository.cs

**Target Location**: `Hartonomous.Core/Application/Interfaces/DTOs/` or keep with interface (acceptable pattern)

**Decision Required**: Are these DTOs or domain models? Need to assess each.

### LOW PRIORITY - Duplicates

#### 5. Duplicated Types
- `SpatialCoordinateDto` appears in two files:
  - GetConstantByHashQuery.cs
  - GetLandmarkByNameQuery.cs

**Action**: Extract to shared DTOs folder, update both references.

## Extraction Process (Per Type)

```markdown
### Extracting: [TypeName]

1. **Read source file**: [SourceFile.cs]
2. **Identify type definition**: Lines X-Y
3. **Create target file**: [TargetPath/TypeName.cs]
4. **Copy type with**:
   - Using statements (only what's needed)
   - Namespace
   - XML documentation
   - Type definition
5. **Remove from source file**
6. **Add using to source file** (if needed)
7. **Build source project**: `dotnet build Hartonomous.Core.csproj --no-restore`
8. **Verify 0 errors, 0 warnings**
9. **Run tests**: `dotnet test Hartonomous.Core.Tests.csproj --no-restore`
10. **Verify all tests pass**
11. **Commit**: `refactor(core): extract [TypeName] to separate file`
12. **Push**
13. **Document completion**: Check box above
```

## Start Point

Begin with: **LearnBPEVocabularyCommandResult** from LearnBPEVocabularyCommand.cs

This is a simple response type with no complex dependencies - good starting point.

---

**Next Action**: Extract LearnBPEVocabularyCommandResult
