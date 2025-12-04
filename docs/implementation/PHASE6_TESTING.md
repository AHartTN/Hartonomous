# Phase 6: Testing & Quality Assurance

**Duration**: 3-4 days  
**Dependencies**: All previous phases (1-5) must be implemented  
**Critical Path**: Yes - blocks production deployment

---

## Overview

Comprehensive testing strategy covering unit tests, integration tests, performance benchmarks, load testing, and test data generation. Target: >80% code coverage across all projects.

---

## Objectives

1. Achieve >80% code coverage across all projects
2. Create comprehensive unit test suites per project
3. Create integration test suites for cross-layer operations
4. Establish performance benchmarks with automated regression detection
5. Implement load testing framework
6. Create test data generators for realistic scenarios
7. Set up continuous testing in CI/CD pipeline

---

## Test Strategy Overview

### Test Pyramid
```
           /\
          /E2E\        - 5% End-to-End (API integration, full workflows)
         /------\
        /Integration\ - 25% Integration (cross-service, database)
       /------------\
      /   Unit       \ - 70% Unit (pure logic, isolated components)
     /----------------\
```

### Coverage Targets by Project
| Project | Target | Focus Areas |
|---------|--------|-------------|
| Hartonomous.Core | 90% | Domain logic, value objects, services |
| Hartonomous.Data | 85% | Repositories, EF configurations, spatial queries |
| Hartonomous.Infrastructure | 80% | Caching, quantization, current user |
| Hartonomous.API | 75% | Controllers, middleware, validation |
| Hartonomous.Worker | 70% | Background jobs, scheduling |

---

## Task Breakdown

### Task 6.1: Hartonomous.Core.Tests - Unit Tests (12 hours)

**Test Suites**:

#### 1. SpatialCoordinate Tests (4 hours)
```csharp
public class SpatialCoordinateTests
{
    [Theory]
    [InlineData(0UL, 0, 0, 0)]
    [InlineData(ulong.MaxValue, 2_097_151, 2_097_151, 2_097_151)]
    [InlineData(12345UL, 1_500_000, 800_000, 50_000)]
    public void FromUniversalProperties_CreatesValidCoordinate(
        ulong expectedHilbert,
        int expectedY,
        int expectedZ,
        int expectedM)
    {
        // Arrange
        var hash = CreateHash(expectedHilbert);
        var data = CreateDataWithEntropy(expectedY);
        var referenceCount = ComputeReferenceCount(expectedM);
        
        // Act
        var coordinate = SpatialCoordinate.FromUniversalProperties(
            hash,
            data,
            referenceCount,
            _quantizationService);
        
        // Assert
        Assert.Equal(expectedHilbert, coordinate.HilbertIndex);
        Assert.InRange(coordinate.QuantizedY, expectedY * 0.9, expectedY * 1.1);
        Assert.InRange(coordinate.QuantizedZ, expectedZ * 0.9, expectedZ * 1.1);
        Assert.InRange(coordinate.QuantizedM, expectedM * 0.9, expectedM * 1.1);
    }
    
    [Fact]
    public void ToPoint_CreatesPOINTZM()
    {
        // Arrange
        var coordinate = CreateTestCoordinate();
        
        // Act
        var point = coordinate.ToPoint();
        
        // Assert
        Assert.NotNull(point);
        Assert.Equal(3, point.Coordinate.Z); // Has Z
        Assert.True(point.Coordinate is CoordinateZM); // Has M
    }
    
    [Fact]
    public void ToCartesian_ReturnsValidTuple()
    {
        // Arrange
        var coordinate = CreateTestCoordinate();
        
        // Act
        var (x, y, z, m) = coordinate.ToCartesian();
        
        // Assert
        Assert.InRange(x, 0, 1);
        Assert.InRange(y, 0, 1);
        Assert.InRange(z, 0, 1);
        Assert.InRange(m, 0, 1);
    }
}
```

#### 2. QuantizationService Tests (3 hours)
```csharp
public class QuantizationServiceTests
{
    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 }, 0)] // Zero entropy
    [InlineData(new byte[] { 0, 1, 2, 3 }, 2_000_000)] // High entropy
    public void QuantizeEntropy_ReturnsExpectedRange(byte[] data, int expectedApprox)
    {
        // Act
        var quantized = _service.QuantizeEntropy(data);
        
        // Assert
        Assert.InRange(quantized, 0, 2_097_151);
        if (expectedApprox > 0)
        {
            Assert.InRange(quantized, expectedApprox * 0.8, expectedApprox * 1.2);
        }
    }
    
    [Fact]
    public void QuantizeCompressibility_HighlyCompressibleData_ReturnsHighValue()
    {
        // Arrange: Highly repetitive data
        var data = Enumerable.Repeat((byte)0, 10000).ToArray();
        
        // Act
        var quantized = _service.QuantizeCompressibility(data);
        
        // Assert: High compressibility = high Z value
        Assert.True(quantized > 1_500_000);
    }
    
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 693147)]
    [InlineData(1000, 996578)]
    [InlineData(2_097_151, 2_097_151)]
    public void QuantizeConnectivity_LogarithmicScaling(long refCount, int expected)
    {
        // Act
        var quantized = _service.QuantizeConnectivity(refCount);
        
        // Assert
        Assert.InRange(quantized, expected * 0.95, expected * 1.05);
    }
}
```

#### 3. BPEService Tests (5 hours)
```csharp
public class BPEServiceTests
{
    [Fact]
    public async Task LearnVocabularyAsync_CreatesTokensWithGeometry()
    {
        // Arrange
        await SeedTestConstantsAsync(1000);
        
        // Act
        var vocabulary = await _service.LearnVocabularyAsync(
            ContentType.Text,
            maxVocabularySize: 100,
            minFrequency: 2);
        
        // Assert
        Assert.NotEmpty(vocabulary);
        Assert.All(vocabulary, token =>
        {
            Assert.NotNull(token.CompositionGeometry);
            Assert.Equal(GeometryType.LineString, token.CompositionGeometry.GeometryType);
        });
    }
    
    [Fact]
    public void DetectHilbertGaps_WithSparseData_IdentifiesGaps()
    {
        // Arrange
        var constants = new List<Constant>
        {
            CreateConstant(100UL),
            CreateConstant(200UL),
            CreateConstant(5000UL), // Gap
            CreateConstant(5100UL)
        };
        
        // Act
        var gaps = _service.DetectHilbertGaps(constants, 1000);
        
        // Assert
        Assert.Single(gaps);
        Assert.Equal(4800UL, gaps[0].GapSize);
    }
}
```

**Coverage Target**: 90% (Core domain logic is critical)

---

### Task 6.2: Hartonomous.Data.Tests - Integration Tests (10 hours)

#### 1. Repository Tests (4 hours)
```csharp
public class ConstantRepositoryTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task AddAsync_SavesConstantWithPOINTZM()
    {
        // Arrange
        var constant = CreateTestConstant();
        
        // Act
        await _repository.AddAsync(constant);
        await _unitOfWork.SaveChangesAsync();
        
        // Assert
        var retrieved = await _repository.GetByIdAsync(constant.Id);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Location);
        Assert.Equal(3, retrieved.Location.Coordinate.Z);
    }
    
    [Fact]
    public async Task KNearestNeighbors_Returns10Nearest()
    {
        // Arrange
        await SeedTestConstantsAsync(100);
        var target = await _repository.GetAllAsync().FirstAsync();
        
        // Act
        var neighbors = await _repository
            .KNearestNeighbors(target.Location, k: 10)
            .ToListAsync();
        
        // Assert
        Assert.Equal(10, neighbors.Count);
        Assert.All(neighbors, n => Assert.NotEqual(target.Id, n.Id));
    }
}
```

#### 2. Spatial Query Tests (3 hours)
```csharp
public class SpatialQueryTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task WithinRadius_FindsNeighbors()
    {
        // Arrange
        var center = CreateTestConstant();
        await _repository.AddAsync(center);
        
        var neighbors = CreateConstantsWithinRadius(center.Location, 100);
        await _repository.AddRangeAsync(neighbors);
        await _unitOfWork.SaveChangesAsync();
        
        // Act
        var results = await _repository
            .WithinRadius(center.Location, 100)
            .ToListAsync();
        
        // Assert
        Assert.Equal(neighbors.Count, results.Count);
    }
}
```

#### 3. Migration Tests (3 hours)
```csharp
public class MigrationTests
{
    [Fact]
    public async Task Migration_RefactorToPointZM_Succeeds()
    {
        // Arrange: Database with PointZ data
        await SeedLegacyPointZDataAsync();
        
        // Act: Apply migration
        await _dbContext.Database.MigrateAsync();
        
        // Assert: All geometries are POINTZM
        var constants = await _dbContext.Constants.ToListAsync();
        Assert.All(constants, c =>
        {
            Assert.NotNull(c.Location);
            Assert.True(c.Location.Coordinate is CoordinateZM);
        });
    }
}
```

**Coverage Target**: 85%

---

### Task 6.3: Performance Benchmarks (8 hours)

**Using BenchmarkDotNet**:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class SpatialQueryBenchmarks
{
    private ApplicationDbContext _dbContext = null!;
    private IConstantRepository _repository = null!;
    
    [GlobalSetup]
    public async Task Setup()
    {
        _dbContext = CreateInMemoryContext();
        _repository = new ConstantRepository(_dbContext);
        
        // Seed 10K constants
        var constants = Enumerable.Range(0, 10_000)
            .Select(_ => CreateRandomConstant())
            .ToList();
        
        await _repository.AddRangeAsync(constants);
        await _dbContext.SaveChangesAsync();
    }
    
    [Benchmark]
    public async Task<List<Constant>> KNN_10Neighbors()
    {
        var target = await _repository.GetAllAsync().FirstAsync();
        return await _repository
            .KNearestNeighbors(target.Location, k: 10)
            .ToListAsync();
    }
    
    [Benchmark]
    public async Task<List<Constant>> WithinRadius_100Units()
    {
        var target = await _repository.GetAllAsync().FirstAsync();
        return await _repository
            .WithinRadius(target.Location, radius: 100)
            .ToListAsync();
    }
    
    [Benchmark]
    public async Task<Dictionary<Guid, double>> PageRank_10KGraph()
    {
        return await _importanceScoringService.ComputePageRankAsync();
    }
}
```

**Benchmark Targets**:
| Operation | Target | Baseline |
|-----------|--------|----------|
| k-NN (k=10, 10K db) | <100ms | TBD |
| Within radius (10K db) | <150ms | TBD |
| PageRank (10K graph) | <10s | TBD |
| BPE Learn (1K constants) | <5s | TBD |
| A* Pathfinding (1K graph) | <1s | TBD |

**Regression Detection**: Fail CI if performance degrades >10% from baseline.

---

### Task 6.4: Load Testing (6 hours)

**Using NBomber**:

```csharp
public class ApiLoadTests
{
    [Fact]
    public void AtomizeEndpoint_SustainsThroughput()
    {
        var scenario = Scenario.Create("atomize_text", async context =>
        {
            var request = Http.CreateRequest("POST", "http://localhost:7001/api/atomize/text")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(JsonSerializer.Serialize(new
                {
                    text = "The quick brown fox jumps over the lazy dog"
                })));
            
            var response = await Http.Send(request, context);
            return response;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(10))
        .WithLoadSimulations(
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // Assert: P95 latency <500ms, 0 errors
        var scen = stats.ScenarioStats[0];
        Assert.True(scen.Ok.Latency.Percent95 < 500);
        Assert.Equal(0, scen.Fail.Request.Count);
    }
}
```

**Load Test Scenarios**:
1. Atomization endpoint: 50 req/s for 1 min
2. k-NN search endpoint: 100 req/s for 1 min
3. BPE vocabulary learning: 10 concurrent jobs
4. Concurrent writes: 20 parallel atomization operations

**Acceptance Criteria**:
- ✅ P95 latency <500ms under load
- ✅ 0% error rate
- ✅ Database connection pool stable
- ✅ Memory usage <2GB under load

---

### Task 6.5: Test Data Generators (6 hours)

```csharp
public static class TestDataGenerator
{
    public static List<Constant> GenerateTextConstants(int count)
    {
        var words = new[] { "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog" };
        var constants = new List<Constant>();
        
        for (int i = 0; i < count; i++)
        {
            var word = words[i % words.Length];
            var bytes = Encoding.UTF8.GetBytes(word);
            var hash = ComputeHash(bytes);
            var coordinate = SpatialCoordinate.FromUniversalProperties(
                hash,
                bytes,
                referenceCount: i % 100,
                _quantizationService);
            
            constants.Add(new Constant
            {
                Id = Guid.NewGuid(),
                Hash = hash,
                Data = bytes,
                Coordinate = coordinate,
                Location = coordinate.ToPoint()
            });
        }
        
        return constants;
    }
    
    public static List<Constant> GenerateImageConstants(int count)
    {
        // Generate constants with low entropy, low compressibility (typical of images)
        var constants = new List<Constant>();
        
        for (int i = 0; i < count; i++)
        {
            var pixels = GenerateImagePixels(64, 64); // 64x64 image
            var hash = ComputeHash(pixels);
            var coordinate = SpatialCoordinate.FromUniversalProperties(
                hash,
                pixels,
                referenceCount: i % 50,
                _quantizationService);
            
            constants.Add(new Constant
            {
                Id = Guid.NewGuid(),
                Hash = hash,
                Data = pixels,
                Coordinate = coordinate,
                Location = coordinate.ToPoint()
            });
        }
        
        return constants;
    }
    
    private static byte[] GenerateImagePixels(int width, int height)
    {
        var pixels = new byte[width * height * 3]; // RGB
        var random = new Random();
        
        // Generate gradient (low entropy)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var index = (y * width + x) * 3;
                pixels[index] = (byte)(x * 255 / width);     // R
                pixels[index + 1] = (byte)(y * 255 / height); // G
                pixels[index + 2] = 128;                      // B
            }
        }
        
        return pixels;
    }
}
```

**Generated Datasets**:
- 10K text constants (high entropy, high compressibility)
- 10K image constants (low entropy, low compressibility)
- 10K audio constants (medium entropy, medium compressibility)
- 1K BPE tokens with LINESTRINGZM geometries
- 100 landmarks (composites)

---

### Task 6.6: CI/CD Integration (4 hours)

**Update `azure-pipelines.yml`**:

```yaml
stages:
  - stage: Test
    jobs:
      - job: UnitTests
        pool:
          vmImage: 'ubuntu-latest'
        steps:
          - task: UseDotNet@2
            inputs:
              version: '10.x'
          
          - script: dotnet test --configuration Release --collect:"XPlat Code Coverage" --logger trx
            displayName: 'Run Unit Tests'
          
          - task: PublishTestResults@2
            inputs:
              testResultsFormat: 'VSTest'
              testResultsFiles: '**/*.trx'
          
          - task: PublishCodeCoverageResults@1
            inputs:
              codeCoverageTool: 'Cobertura'
              summaryFileLocation: '**/coverage.cobertura.xml'
              failIfCoverageEmpty: true
          
          # Fail if coverage <80%
          - script: |
              coverage=$(grep -oP 'line-rate="\K[^"]+' **/coverage.cobertura.xml | head -1)
              if (( $(echo "$coverage < 0.80" | bc -l) )); then
                echo "Coverage $coverage is below 80% threshold"
                exit 1
              fi
            displayName: 'Check Coverage Threshold'
      
      - job: IntegrationTests
        dependsOn: UnitTests
        pool:
          vmImage: 'ubuntu-latest'
        services:
          postgres:
            image: postgis/postgis:16-3.4
            env:
              POSTGRES_PASSWORD: test
        steps:
          - script: dotnet test Hartonomous.Data.Tests --configuration Release
            displayName: 'Run Integration Tests'
      
      - job: PerformanceBenchmarks
        dependsOn: UnitTests
        pool:
          vmImage: 'ubuntu-latest'
        steps:
          - script: dotnet run --project Hartonomous.Benchmarks --configuration Release
            displayName: 'Run Benchmarks'
          
          - script: |
              # Compare with baseline, fail if regression >10%
              python scripts/compare-benchmarks.py --baseline baseline.json --current results.json --threshold 1.10
            displayName: 'Check Performance Regression'
```

**Acceptance Criteria**:
- ✅ All tests run in CI/CD pipeline
- ✅ Coverage reports published
- ✅ Build fails if coverage <80%
- ✅ Build fails if performance regresses >10%

---

## Test Execution Plan

### Daily Testing (During Development)
```powershell
# Run fast unit tests
dotnet test --filter Category=Unit

# Run specific project tests
dotnet test Hartonomous.Core.Tests
```

### Pre-Commit Testing
```powershell
# Run all tests
dotnet test --configuration Release

# Check coverage
dotnet test /p:CollectCoverage=true /p:CoverageReportsFormat=cobertura
```

### Weekly Full Suite
```powershell
# Run all tests + benchmarks + load tests
dotnet test --configuration Release
dotnet run --project Hartonomous.Benchmarks
dotnet run --project Hartonomous.LoadTests
```

---

## Acceptance Criteria (Phase Exit)

- ✅ >80% code coverage across all projects
- ✅ All unit tests passing (>200 tests)
- ✅ All integration tests passing (>50 tests)
- ✅ Performance benchmarks documented
- ✅ Load tests demonstrate stability under load
- ✅ Test data generators operational
- ✅ CI/CD pipeline enforces quality gates
- ✅ 0 failing tests in main branch

---

**Next Phase**: [PHASE7_DOCUMENTATION.md](./PHASE7_DOCUMENTATION.md) - Architecture documentation and knowledge transfer

**Status**: 📋 Ready for implementation after Phase 5

**Last Updated**: December 4, 2025
