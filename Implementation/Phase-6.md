---
title: "Phase 6: Testing & Quality Assurance"
author: "Hartonomous Development Team"
date: "2025-12-05"
version: "1.0"
status: "Planning"
---

# Phase 6: Testing & Quality Assurance

## Table of Contents
- [Overview](#overview)
- [Phase Details](#phase-details)
- [Objectives](#objectives)
- [Implementation Tasks](#implementation-tasks)
  - [6.1: Unit Testing Strategy](#61-unit-testing-strategy)
  - [6.2: Integration Testing](#62-integration-testing)
  - [6.3: Performance Testing](#63-performance-testing)
  - [6.4: Spatial Query Validation](#64-spatial-query-validation)
- [Success Criteria](#success-criteria)
- [Quality Gates](#quality-gates)
- [Risks & Mitigation](#risks--mitigation)
- [Dependencies](#dependencies)
- [Next Steps](#next-steps)

---

## Overview

**Phase 6** establishes comprehensive testing infrastructure to validate the geometric knowledge substrate. This phase ensures correctness, performance, and reliability of all POINTZM operations before production deployment.

**Duration**: 4-6 days  
**Complexity**: High  
**Dependencies**: Phases 1-5  
**Prerequisites**: All core features implemented

---

## Phase Details

### Timeline
- **Start**: After Phase 5 completion
- **Duration**: 4-6 days
- **Parallelizable**: Test suites can be developed concurrently
- **Critical Path**: Yes - Gates Phase 8 (Production Hardening)

### Resource Requirements
- **Development**: 2 engineers for test development
- **Testing Infrastructure**: Docker containers for PostgreSQL/PostGIS
- **Performance Environment**: Isolated test environment with production-like data volumes

---

## Objectives

1. **Achieve 80%+ code coverage** across all layers
2. **Validate spatial correctness** - All PostGIS operations produce expected results
3. **Establish performance baselines** - Document query times for standard operations
4. **Create regression test suite** - Prevent future breakage
5. **Implement continuous testing** - Automated tests in CI/CD pipeline

---

## Implementation Tasks

### 6.1: Unit Testing Strategy

**Goal**: Comprehensive unit tests for all business logic and domain entities.

<details>
<summary><strong>6.1.1: Core Domain Entity Tests</strong> (1 day)</summary>

**Create `ConstantTests.cs`:**

```csharp
namespace Hartonomous.Core.Tests.Domain.Entities;

public sealed class ConstantTests
{
    private readonly IQuantizationService _quantization;
    private readonly IHilbertIndexService _hilbert;
    
    public ConstantTests()
    {
        _quantization = new QuantizationService();
        _hilbert = new HilbertIndexService();
    }
    
    [Fact]
    public void Create_Should_Generate_Valid_POINTZM_Location()
    {
        // Arrange
        var data = "Hello World"u8.ToArray();
        
        // Act
        var constant = Constant.Create(data, _quantization, _hilbert);
        
        // Assert
        constant.Should().NotBeNull();
        constant.Location.Should().NotBeNull();
        constant.Location.SRID.Should().Be(4326);
        constant.Location.X.Should().BeGreaterThan(0); // Hilbert index
        constant.Location.Y.Should().BeInRange(0, 2_097_151); // Entropy
        constant.Location.Z.Should().BeInRange(0, 2_097_151); // Compressibility
        constant.Location.M.Should().Be(0); // Initial connectivity
    }
    
    [Fact]
    public void Hash256_Should_Be_Content_Addressable()
    {
        // Arrange
        var data1 = "test"u8.ToArray();
        var data2 = "test"u8.ToArray();
        var data3 = "different"u8.ToArray();
        
        // Act
        var constant1 = Constant.Create(data1, _quantization, _hilbert);
        var constant2 = Constant.Create(data2, _quantization, _hilbert);
        var constant3 = Constant.Create(data3, _quantization, _hilbert);
        
        // Assert
        constant1.Hash.Should().Be(constant2.Hash); // Same content = same hash
        constant1.Hash.Should().NotBe(constant3.Hash); // Different content = different hash
    }
    
    [Fact]
    public void IncrementReferenceCount_Should_Update_M_Dimension()
    {
        // Arrange
        var constant = Constant.Create("test"u8.ToArray(), _quantization, _hilbert);
        var initialM = constant.Location.M;
        
        // Act
        constant.IncrementReferenceCount();
        
        // Assert
        constant.ReferenceCount.Should().Be(1);
        constant.Location.M.Should().BeGreaterThan(initialM);
    }
    
    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00 }, true)] // Low entropy
    [InlineData(new byte[] { 0xFF, 0xFE, 0xFD }, false)] // High entropy
    public void Entropy_Quantization_Should_Reflect_Randomness(byte[] data, bool isLowEntropy)
    {
        // Act
        var constant = Constant.Create(data, _quantization, _hilbert);
        
        // Assert
        if (isLowEntropy)
            constant.Location.Y.Should().BeLessThan(500_000);
        else
            constant.Location.Y.Should().BeGreaterThan(500_000);
    }
}
```

**Create `BPETokenTests.cs`:**

```csharp
public sealed class BPETokenTests
{
    [Fact]
    public void CompositionGeometry_Should_Be_LINESTRINGZM()
    {
        // Arrange
        var atom1 = CreateTestAtom("Hello");
        var atom2 = CreateTestAtom("World");
        
        // Act
        var token = BPEToken.Create(new[] { atom1, atom2 });
        
        // Assert
        token.CompositionGeometry.Should().BeOfType<LineString>();
        token.CompositionGeometry.SRID.Should().Be(4326);
        token.CompositionGeometry.NumPoints.Should().Be(2);
        token.ConstantSequence.Should().HaveCount(2);
    }
    
    [Fact]
    public void Frequency_Should_Increment_On_Usage()
    {
        // Arrange
        var token = BPEToken.Create(CreateTestAtoms());
        
        // Act
        token.IncrementFrequency();
        token.IncrementFrequency();
        
        // Assert
        token.Frequency.Should().Be(2);
    }
    
    [Fact]
    public void HilbertGaps_Should_Be_Detected()
    {
        // Arrange
        var atom1 = CreateAtomWithHilbert(1000);
        var atom2 = CreateAtomWithHilbert(5000); // Gap = 4000
        var atom3 = CreateAtomWithHilbert(5100); // Gap = 100
        
        // Act
        var token = BPEToken.Create(new[] { atom1, atom2, atom3 });
        var gaps = token.DetectHilbertGaps(threshold: 1000);
        
        // Assert
        gaps.Should().ContainSingle(); // Only gap between atom1 and atom2
        gaps.First().Should().Be(4000);
    }
}
```

</details>

<details>
<summary><strong>6.1.2: Quantization Service Tests</strong> (1 day)</summary>

```csharp
public sealed class QuantizationServiceTests
{
    private readonly IQuantizationService _service;
    
    public QuantizationServiceTests()
    {
        _service = new QuantizationService();
    }
    
    [Theory]
    [InlineData(new byte[] { 0x00 }, 0)] // No entropy
    [InlineData(new byte[] { 0x00, 0xFF }, 2_097_151)] // Max entropy (2 symbols)
    public void QuantizeEntropy_Should_Map_To_21Bit_Range(byte[] data, int expectedRange)
    {
        // Act
        var quantized = _service.QuantizeEntropy(data);
        
        // Assert
        quantized.Should().BeInRange(0, 2_097_151);
        quantized.Should().BeCloseTo(expectedRange, 100_000);
    }
    
    [Fact]
    public void QuantizeCompressibility_Should_Detect_Repetition()
    {
        // Arrange
        var repetitive = Enumerable.Repeat((byte)'A', 1000).ToArray();
        var random = RandomNumberGenerator.GetBytes(1000);
        
        // Act
        var repetitiveZ = _service.QuantizeCompressibility(repetitive);
        var randomZ = _service.QuantizeCompressibility(random);
        
        // Assert
        repetitiveZ.Should().BeGreaterThan(randomZ); // High compressibility = high Z
    }
    
    [Fact]
    public void QuantizeConnectivity_Should_Be_Logarithmic()
    {
        // Act
        var connectivity1 = _service.QuantizeConnectivity(1);
        var connectivity10 = _service.QuantizeConnectivity(10);
        var connectivity100 = _service.QuantizeConnectivity(100);
        
        // Assert
        connectivity10.Should().BeGreaterThan(connectivity1);
        connectivity100.Should().BeGreaterThan(connectivity10);
        
        // Log scale: log2(101) ≈ 6.66, log2(11) ≈ 3.46, log2(2) = 1
        var ratio = (double)connectivity100 / connectivity10;
        ratio.Should().BeApproximately(2.0, 0.5); // Logarithmic growth
    }
    
    [Fact]
    public void Roundtrip_Dequantization_Should_Preserve_Range()
    {
        // Arrange
        var originalMin = 0.0;
        var originalMax = 8.0; // Max entropy in bits
        var quantized = 1_048_576; // Middle of 21-bit range
        
        // Act
        var dequantized = _service.DequantizeValue(quantized, originalMin, originalMax);
        
        // Assert
        dequantized.Should().BeApproximately(4.0, 0.1); // Middle value
    }
}
```

</details>

---

### 6.2: Integration Testing

**Goal**: Test full workflows involving multiple layers (API → Core → Data).

<details>
<summary><strong>6.2.1: Content Ingestion Integration Tests</strong> (1 day)</summary>

```csharp
public sealed class ContentIngestionIntegrationTests : IClassFixture<DatabaseFixture>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IContentIngestionService _service;
    
    [Fact]
    public async Task IngestContent_Should_Create_Atoms_And_Deduplicate()
    {
        // Arrange
        var content = "Hello World"u8.ToArray();
        
        // Act - First ingestion
        var result1 = await _service.IngestAsync(content);
        await _unitOfWork.SaveChangesAsync();
        
        // Act - Second ingestion (duplicate)
        var result2 = await _service.IngestAsync(content);
        await _unitOfWork.SaveChangesAsync();
        
        // Assert
        result1.Atoms.Should().HaveCountGreaterThan(0);
        result2.Atoms.Should().HaveCount(result1.Atoms.Count);
        
        // Verify deduplication - same atoms referenced
        foreach (var atom in result1.Atoms)
        {
            var dbAtom = await _dbContext.Constants.FindAsync(atom.Id);
            dbAtom!.ReferenceCount.Should().Be(2); // Referenced twice
            dbAtom.Frequency.Should().Be(2);
        }
    }
    
    [Fact]
    public async Task IngestContent_Should_Compute_Boundary_Geometry()
    {
        // Arrange
        var content = GenerateTestContent(1000); // 1KB content
        
        // Act
        var result = await _service.IngestAsync(content);
        await _unitOfWork.SaveChangesAsync();
        
        // Assert
        result.BoundaryGeometry.Should().NotBeNull();
        result.BoundaryGeometry.Should().BeOfType<Polygon>();
        
        // Boundary should be convex hull of all atoms
        var allAtoms = await _dbContext.Constants
            .Where(c => result.AtomIds.Contains(c.Id))
            .ToListAsync();
            
        var expectedHull = NetTopologySuite.Algorithm.ConvexHull.Create(
            allAtoms.Select(a => a.Location).ToArray()
        );
        
        result.BoundaryGeometry.EqualsTopologically(expectedHull).Should().BeTrue();
    }
    
    [Fact]
    public async Task BPE_Learning_Should_Create_Tokens_From_Frequent_Patterns()
    {
        // Arrange - Ingest multiple documents with repeated phrase
        var phrase = "public static void"u8.ToArray();
        for (int i = 0; i < 100; i++)
        {
            await _service.IngestAsync(phrase);
        }
        await _unitOfWork.SaveChangesAsync();
        
        // Act - Run BPE learning
        await _service.LearnVocabularyAsync(minFrequency: 10);
        
        // Assert
        var tokens = await _dbContext.BPETokens
            .Where(t => t.Frequency >= 10)
            .ToListAsync();
            
        tokens.Should().NotBeEmpty();
        tokens.Should().Contain(t => 
            t.ConstantSequence.Count == 3 && // "public", "static", "void"
            t.Frequency >= 100
        );
    }
}
```

</details>

<details>
<summary><strong>6.2.2: Spatial Query Integration Tests</strong> (1 day)</summary>

```csharp
public sealed class SpatialQueryIntegrationTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task KNN_Query_Should_Find_Nearest_Atoms()
    {
        // Arrange - Create atoms with known positions
        var targetAtom = await CreateAtomAt(x: 1000, y: 500_000, z: 1_000_000);
        var nearAtom = await CreateAtomAt(x: 1001, y: 500_100, z: 1_000_100);
        var farAtom = await CreateAtomAt(x: 9999, y: 1_500_000, z: 500_000);
        
        // Act - Find 1 nearest neighbor
        var nearest = await _dbContext.Constants
            .OrderBy(c => c.Location.Distance(targetAtom.Location))
            .Skip(1) // Skip self
            .Take(1)
            .ToListAsync();
        
        // Assert
        nearest.Should().ContainSingle();
        nearest[0].Id.Should().Be(nearAtom.Id);
    }
    
    [Fact]
    public async Task Containment_Query_Should_Find_Atoms_In_Boundary()
    {
        // Arrange - Create document with boundary
        var atoms = await CreateAtomsInRegion(
            minX: 1000, maxX: 2000,
            minY: 500_000, maxY: 600_000
        );
        var boundary = CreatePolygonBoundary(1000, 2000, 500_000, 600_000);
        
        // Act - Query atoms within boundary
        var contained = await _dbContext.Constants
            .Where(c => boundary.Contains(c.Location))
            .ToListAsync();
        
        // Assert
        contained.Should().HaveCount(atoms.Count);
        contained.Select(c => c.Id).Should().BeEquivalentTo(atoms.Select(a => a.Id));
    }
    
    [Fact]
    public async Task Voronoi_Tessellation_Should_Partition_Space()
    {
        // Arrange - Create 100 random atoms
        var atoms = await CreateRandomAtoms(100);
        
        // Act - Compute Voronoi
        var voronoi = await _dbContext.Database.ExecuteSqlRawAsync(@"
            SELECT ST_VoronoiPolygons(ST_Collect(location))
            FROM constants
            WHERE id = ANY(@atomIds)
        ", new NpgsqlParameter("atomIds", atoms.Select(a => a.Id).ToArray()));
        
        // Assert
        voronoi.Should().NotBeNull();
        // Voronoi cells should cover entire space with no overlaps
    }
}
```

</details>

---

### 6.3: Performance Testing

**Goal**: Establish performance baselines and identify bottlenecks.

<details>
<summary><strong>6.3.1: Ingestion Performance Tests</strong> (1 day)</summary>

```csharp
public sealed class IngestionPerformanceTests
{
    [Fact]
    public async Task Ingestion_Should_Complete_In_Milliseconds_After_Warmup()
    {
        // Arrange - Warm up with 10K documents
        await WarmUpDatabase(10_000);
        
        var content = GenerateTestContent(10_000); // 10KB
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        await _service.IngestAsync(content);
        await _unitOfWork.SaveChangesAsync();
        stopwatch.Stop();
        
        // Assert - Should be <100ms due to 99%+ deduplication
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }
    
    [Fact]
    public async Task Deduplication_Rate_Should_Exceed_99_Percent_After_Warmup()
    {
        // Arrange - Ingest 100K documents
        var documents = GenerateTypicalCodebase(100_000);
        
        long totalBytes = 0;
        long newAtoms = 0;
        
        // Act
        foreach (var doc in documents)
        {
            totalBytes += doc.Length;
            var result = await _service.IngestAsync(doc);
            newAtoms += result.NewAtomsCreated;
        }
        
        // Assert
        var deduplicationRate = 1.0 - ((double)newAtoms / totalBytes);
        deduplicationRate.Should().BeGreaterThan(0.99); // >99% deduplicated
    }
    
    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public async Task KNN_Query_Should_Scale_Logarithmically(int atomCount)
    {
        // Arrange - Create N atoms
        await CreateRandomAtoms(atomCount);
        var targetPoint = CreateRandomPoint();
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        var nearest = await _dbContext.Constants
            .OrderBy(c => c.Location.Distance(targetPoint))
            .Take(10)
            .ToListAsync();
        stopwatch.Stop();
        
        // Assert - Should be O(log n) due to GIST index
        // 1M atoms should take <100ms
        if (atomCount >= 1_000_000)
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
        
        _output.WriteLine($"{atomCount:N0} atoms: {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

</details>

<details>
<summary><strong>6.3.2: BPE Learning Performance Tests</strong> (1 day)</summary>

```csharp
[Fact]
public async Task BPE_Learning_Should_Complete_Within_Time_Budget()
{
    // Arrange - 1M atoms
    await SeedDatabase(1_000_000);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    await _bpeService.LearnVocabularyAsync(
        minFrequency: 100,
        maxVocabSize: 10_000
    );
    stopwatch.Stop();
    
    // Assert - Should complete in <1 hour (acceptable for background job)
    stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromHours(1));
    
    _output.WriteLine($"BPE learning completed in {stopwatch.Elapsed}");
}

[Fact]
public async Task Voronoi_Computation_Should_Handle_Large_Datasets()
{
    // Arrange - 100K atoms
    await CreateRandomAtoms(100_000);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var voronoi = await _topologyService.ComputeVoronoiTessellationAsync();
    stopwatch.Stop();
    
    // Assert - GPU-accelerated should complete in <10 minutes
    stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(10));
}
```

</details>

---

### 6.4: Spatial Query Validation

**Goal**: Verify PostGIS operations produce mathematically correct results.

<details>
<summary><strong>6.4.1: Geometric Correctness Tests</strong> (1 day)</summary>

```csharp
public sealed class GeometricCorrectnessTests
{
    [Fact]
    public void Distance_Should_Be_Euclidean_In_YZM_Space()
    {
        // Arrange
        var point1 = new Point(1000, 500_000, 1_000_000) { SRID = 4326, M = 100 };
        var point2 = new Point(1000, 500_003, 1_000_004) { SRID = 4326, M = 100 };
        
        // Act
        var distance = point1.Distance(point2);
        
        // Assert - sqrt(3^2 + 4^2) = 5
        distance.Should().BeApproximately(5.0, 0.01);
    }
    
    [Fact]
    public void ConvexHull_Should_Contain_All_Points()
    {
        // Arrange
        var points = GenerateRandomPoints(100);
        var multiPoint = new MultiPoint(points) { SRID = 4326 };
        
        // Act
        var hull = multiPoint.ConvexHull();
        
        // Assert
        foreach (var point in points)
        {
            hull.Contains(point).Should().BeTrue();
        }
    }
    
    [Fact]
    public void Voronoi_Cells_Should_Partition_Space_Without_Gaps()
    {
        // Arrange
        var points = GenerateRandomPoints(50);
        
        // Act
        var voronoi = ComputeVoronoiPolygons(points);
        
        // Assert
        for (int i = 0; i < voronoi.Count - 1; i++)
        {
            for (int j = i + 1; j < voronoi.Count; j++)
            {
                // No overlaps
                voronoi[i].Overlaps(voronoi[j]).Should().BeFalse();
                
                // Adjacent cells touch
                if (AreAdjacent(points[i], points[j]))
                    voronoi[i].Touches(voronoi[j]).Should().BeTrue();
            }
        }
    }
    
    [Fact]
    public void MST_Should_Have_N_Minus_1_Edges()
    {
        // Arrange
        var points = GenerateRandomPoints(100);
        var delaunay = ComputeDelaunayTriangulation(points);
        
        // Act
        var mst = ComputeMinimumSpanningTree(delaunay);
        
        // Assert
        mst.Edges.Should().HaveCount(99); // N-1 edges for N points
    }
}
```

</details>

---

## Success Criteria

- [ ] **80%+ code coverage** across Core, Data, Infrastructure projects
- [ ] **All spatial queries validated** against known geometric properties
- [ ] **Performance baselines documented** for standard operations
- [ ] **Ingestion <100ms** after warm-up (99%+ deduplication)
- [ ] **k-NN <100ms** for 1M atoms
- [ ] **BPE learning <1 hour** for 1M atoms
- [ ] **CI/CD pipeline** runs all tests automatically

---

## Quality Gates

### Test Coverage Requirements
- [ ] Core domain entities: 90%+ coverage
- [ ] Quantization service: 95%+ coverage
- [ ] Repository implementations: 80%+ coverage
- [ ] API controllers: 70%+ coverage

### Performance Requirements
- [ ] All benchmark tests pass with documented baselines
- [ ] No query exceeds 1-second timeout
- [ ] Memory usage stays below 2GB for typical operations

---

## Risks & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Flaky spatial tests** due to floating-point precision | Medium | High | Use tolerance ranges, deterministic test data |
| **Slow integration tests** blocking CI | High | Medium | Parallelize tests, use in-memory DB for unit tests |
| **Performance regressions** undetected | High | Medium | Automated benchmark comparisons in CI |
| **Test data generation** is too slow | Medium | Low | Cache seeded databases, use factories |

---

## Dependencies

### Upstream (Required)
- **Phases 1-5**: All features implemented
- **Docker**: PostgreSQL/PostGIS test containers

### Downstream (Impacts)
- **Phase 8**: Production deployment requires passing test suite
- **CI/CD**: Tests integrated into azure-pipelines.yml

---

## Next Steps

After completing Phase 6:

1. **Proceed to Phase 7** - Documentation & Training
2. **Integrate tests into CI/CD** - Update azure-pipelines.yml
3. **Establish monitoring** - Track test execution times, flakiness
4. **Create performance dashboard** - Visualize benchmarks over time

**See**: [Phase 7: Documentation & Training](Phase-7.md)

---

**Navigation**:  
← [Phase 5: Advanced Features](Phase-5.md) | [Master Plan](Master-Plan.md) | [Phase 7: Documentation](Phase-7.md) →
