# Phase 3: Universal Properties Implementation

**Duration**: 2-3 days  
**Dependencies**: Phase 1 (QuantizationService), Phase 2 (BPE redesign)  
**Critical Path**: Yes - enables emergent modality and universal atomization

---

## Overview

Replace explicit `ContentType` enumeration with emergent modality derived from universal properties: Shannon entropy (Y), Kolmogorov complexity (Z), and graph connectivity (M). Modalities cluster naturally in YZM space without manual labeling.

---

## Current vs. Target Architecture

### Current (WRONG)
```csharp
public enum ContentType
{
    Text = 0,
    Image = 1,
    Audio = 2,
    Video = 3,
    Unknown = 99
}

// Explicitly set during atomization
var constant = new Constant
{
    ContentType = ContentType.Text, // ❌ MANUAL CLASSIFICATION
    Data = bytes
};
```

### Target (CORRECT)
```csharp
// NO enum - modality emerges from YZM clustering
var entropy = _quantizationService.QuantizeEntropy(bytes);
var compressibility = _quantizationService.QuantizeCompressibility(bytes);
var connectivity = _quantizationService.QuantizeConnectivity(referenceCount);

var coordinate = SpatialCoordinate.FromUniversalProperties(
    hash256,
    bytes,
    referenceCount,
    _quantizationService);

// Query: "Find all constants near (Y=1.5M, Z=800K, M=50)"
// Returns: Text-like atoms (high entropy, high compressibility, low connectivity)
```

---

## Objectives

1. Implement Shannon entropy calculation
2. Implement Kolmogorov complexity approximation (gzip ratio)
3. Implement graph degree connectivity metric
4. Make `ContentType` emergent from YZM clustering
5. Create `UniversalGeometryFactory` for consistent coordinate creation
6. Update all atomization services to use universal properties
7. Create cluster analysis tools for visualizing modality separation

---

## Task Breakdown

### Task 3.1: Shannon Entropy Implementation (6 hours)

**File**: `Hartonomous.Infrastructure/Services/QuantizationService.cs`

```csharp
public int QuantizeEntropy(byte[] data)
{
    if (data == null || data.Length == 0)
        return 0;
    
    // Shannon entropy: H(X) = -Σ p(x) log₂ p(x)
    var entropy = CalculateShannonEntropy(data);
    
    // Normalize to [0, 8] bits (max entropy for byte)
    var normalizedEntropy = entropy / 8.0;
    
    // Scale to [0, 2,097,151] (21-bit)
    var quantized = (int)(normalizedEntropy * MaxQuantizedValue);
    
    _logger.LogTrace("Entropy: raw={RawEntropy:F4}, quantized={Quantized}",
        entropy, quantized);
    
    return Math.Clamp(quantized, 0, MaxQuantizedValue);
}

private double CalculateShannonEntropy(byte[] data)
{
    // Count byte frequencies
    var frequencies = new int[256];
    foreach (var b in data)
    {
        frequencies[b]++;
    }
    
    // Calculate probabilities and entropy
    var totalBytes = data.Length;
    var entropy = 0.0;
    
    for (int i = 0; i < 256; i++)
    {
        if (frequencies[i] == 0)
            continue;
        
        var probability = (double)frequencies[i] / totalBytes;
        entropy -= probability * Math.Log2(probability);
    }
    
    return entropy;
}
```

**Interpretation**:
- **Low entropy (Y < 500K)**: Highly repetitive data (images, video frames)
- **Medium entropy (Y = 500K-1.5M)**: Structured data (compressed files, code)
- **High entropy (Y > 1.5M)**: Random or encrypted data (high information density)

**Acceptance Criteria**:
- ✅ Returns [0, 8] bits for entropy calculation
- ✅ Quantizes to [0, 2,097,151] range
- ✅ Performance: <1ms for 1MB data
- ✅ Handles edge cases (empty, single byte, uniform distribution)

---

### Task 3.2: Kolmogorov Complexity Approximation (6 hours)

**File**: `Hartonomous.Infrastructure/Services/QuantizationService.cs`

```csharp
public int QuantizeCompressibility(byte[] data)
{
    if (data == null || data.Length == 0)
        return 0;
    
    // Approximate Kolmogorov complexity via gzip compression ratio
    var compressedSize = CompressData(data);
    var compressionRatio = (double)compressedSize / data.Length;
    
    // Lower ratio = more compressible = lower complexity
    // Invert so high compressibility = high Z value
    var complexity = 1.0 - compressionRatio;
    
    // Scale to [0, 2,097,151] (21-bit)
    var quantized = (int)(complexity * MaxQuantizedValue);
    
    _logger.LogTrace(
        "Compressibility: original={Original}, compressed={Compressed}, ratio={Ratio:F4}, quantized={Quantized}",
        data.Length, compressedSize, compressionRatio, quantized);
    
    return Math.Clamp(quantized, 0, MaxQuantizedValue);
}

private int CompressData(byte[] data)
{
    using var outputStream = new MemoryStream();
    using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true))
    {
        gzipStream.Write(data, 0, data.Length);
    }
    return (int)outputStream.Length;
}
```

**Interpretation**:
- **Low compressibility (Z < 500K)**: Highly redundant data (solid color images, silence)
- **Medium compressibility (Z = 500K-1.5M)**: Structured patterns (natural language, music)
- **High compressibility (Z > 1.5M)**: Already compressed or encrypted data

**Acceptance Criteria**:
- ✅ Uses gzip for compression
- ✅ Quantizes to [0, 2,097,151] range
- ✅ Performance: <10ms for 1MB data
- ✅ Handles incompressible data (ratio ≈ 1.0)

---

### Task 3.3: Graph Connectivity Metric (4 hours)

**File**: `Hartonomous.Infrastructure/Services/QuantizationService.cs`

```csharp
public int QuantizeConnectivity(long referenceCount)
{
    if (referenceCount < 0)
        throw new ArgumentOutOfRangeException(nameof(referenceCount), "Must be non-negative");
    
    // Logarithmic scale: log₂(count + 1)
    // +1 to handle zero references
    var logCount = Math.Log2(referenceCount + 1);
    
    // Max reference count ≈ 2^21 (2M references)
    // log₂(2M) ≈ 20.93 bits
    var maxLogCount = 21.0;
    
    // Normalize to [0, 1]
    var normalized = logCount / maxLogCount;
    
    // Scale to [0, 2,097,151] (21-bit)
    var quantized = (int)(normalized * MaxQuantizedValue);
    
    _logger.LogTrace("Connectivity: count={Count}, log={Log:F4}, quantized={Quantized}",
        referenceCount, logCount, quantized);
    
    return Math.Clamp(quantized, 0, MaxQuantizedValue);
}
```

**Interpretation**:
- **Low connectivity (M < 100K)**: Rare atoms (specialized vocabulary, unique images)
- **Medium connectivity (M = 100K-1M)**: Common atoms (frequent words, common patterns)
- **High connectivity (M > 1M)**: Universal atoms (spaces, common punctuation, solid colors)

**Acceptance Criteria**:
- ✅ Uses logarithmic scaling
- ✅ Handles zero references gracefully
- ✅ Quantizes to [0, 2,097,151] range
- ✅ Performance: O(1) - instant computation

---

### Task 3.4: Emergent Modality from YZM Clustering (8 hours)

**File**: `Hartonomous.Core/Application/Services/ModalityAnalysisService.cs` (NEW)

```csharp
public interface IModalityAnalysisService
{
    Task<ModalityCluster> IdentifyModalityAsync(Point location, CancellationToken cancellationToken = default);
    Task<IEnumerable<ModalityCluster>> DiscoverClustersAsync(int maxClusters = 10, CancellationToken cancellationToken = default);
    Task<string> GetModalityNameAsync(Point location, CancellationToken cancellationToken = default);
}

public sealed class ModalityAnalysisService : IModalityAnalysisService
{
    private readonly IConstantRepository _constantRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ModalityAnalysisService> _logger;
    
    public async Task<ModalityCluster> IdentifyModalityAsync(
        Point location,
        CancellationToken cancellationToken = default)
    {
        // Find k nearest neighbors in YZM space
        var neighbors = await _constantRepository
            .KNearestNeighbors(location, k: 100)
            .ToListAsync(cancellationToken);
        
        if (!neighbors.Any())
        {
            return ModalityCluster.Unknown;
        }
        
        // Extract YZM coordinates
        var yValues = neighbors.Select(c => c.Coordinate.QuantizedY).ToList();
        var zValues = neighbors.Select(c => c.Coordinate.QuantizedZ).ToList();
        var mValues = neighbors.Select(c => c.Coordinate.QuantizedM).ToList();
        
        // Compute cluster centroid
        var centroid = new
        {
            Y = yValues.Average(),
            Z = zValues.Average(),
            M = mValues.Average()
        };
        
        // Compute cluster variance
        var yVariance = Variance(yValues);
        var zVariance = Variance(zValues);
        var mVariance = Variance(mValues);
        
        // Classify based on centroid position
        var modalityName = ClassifyModality(centroid.Y, centroid.Z, centroid.M);
        
        return new ModalityCluster
        {
            Name = modalityName,
            CentroidY = centroid.Y,
            CentroidZ = centroid.Z,
            CentroidM = centroid.M,
            VarianceY = yVariance,
            VarianceZ = zVariance,
            VarianceM = mVariance,
            SampleSize = neighbors.Count
        };
    }
    
    private string ClassifyModality(double y, double z, double m)
    {
        const double Third = MaxQuantizedValue / 3.0;
        const double TwoThirds = 2.0 * MaxQuantizedValue / 3.0;
        
        // High entropy + high compressibility + low connectivity = Text
        if (y > TwoThirds && z > TwoThirds && m < Third)
            return "Text";
        
        // Low entropy + low compressibility + medium connectivity = Image
        if (y < Third && z < Third && m > Third && m < TwoThirds)
            return "Image";
        
        // Medium entropy + medium compressibility + low connectivity = Audio
        if (y > Third && y < TwoThirds && z > Third && z < TwoThirds && m < Third)
            return "Audio";
        
        // High entropy + low compressibility + high connectivity = Video
        if (y > TwoThirds && z < Third && m > TwoThirds)
            return "Video";
        
        // Medium across all dimensions = Code
        if (y > Third && y < TwoThirds && z > Third && z < TwoThirds && m > Third && m < TwoThirds)
            return "Code";
        
        // High entropy + high compressibility + high connectivity = Compressed
        if (y > TwoThirds && z > TwoThirds && m > TwoThirds)
            return "Compressed";
        
        return "Unknown";
    }
    
    public async Task<IEnumerable<ModalityCluster>> DiscoverClustersAsync(
        int maxClusters = 10,
        CancellationToken cancellationToken = default)
    {
        // Use DBSCAN or k-means clustering in YZM space
        var sql = @"
            WITH yzm_coords AS (
                SELECT 
                    quantized_y as y,
                    quantized_z as z,
                    quantized_m as m
                FROM constants
            ),
            clusters AS (
                SELECT 
                    ST_ClusterDBSCAN(
                        ST_MakePoint(y, z, m),
                        eps := 100000,
                        minpoints := 10
                    ) OVER () as cluster_id,
                    y, z, m
                FROM yzm_coords
            )
            SELECT 
                cluster_id,
                AVG(y) as centroid_y,
                AVG(z) as centroid_z,
                AVG(m) as centroid_m,
                VARIANCE(y) as variance_y,
                VARIANCE(z) as variance_z,
                VARIANCE(m) as variance_m,
                COUNT(*) as sample_size
            FROM clusters
            WHERE cluster_id IS NOT NULL
            GROUP BY cluster_id
            ORDER BY sample_size DESC
            LIMIT @maxClusters
        ";
        
        var clusters = await _dbContext.Database
            .SqlQueryRaw<ClusterResult>(sql, new NpgsqlParameter("@maxClusters", maxClusters))
            .ToListAsync(cancellationToken);
        
        return clusters.Select(c => new ModalityCluster
        {
            Name = ClassifyModality(c.CentroidY, c.CentroidZ, c.CentroidM),
            CentroidY = c.CentroidY,
            CentroidZ = c.CentroidZ,
            CentroidM = c.CentroidM,
            VarianceY = c.VarianceY,
            VarianceZ = c.VarianceZ,
            VarianceM = c.VarianceM,
            SampleSize = c.SampleSize
        });
    }
    
    public async Task<string> GetModalityNameAsync(
        Point location,
        CancellationToken cancellationToken = default)
    {
        var cluster = await IdentifyModalityAsync(location, cancellationToken);
        return cluster.Name;
    }
    
    private double Variance(IEnumerable<int> values)
    {
        var mean = values.Average();
        return values.Average(v => Math.Pow(v - mean, 2));
    }
}

public record ModalityCluster
{
    public string Name { get; init; } = "Unknown";
    public double CentroidY { get; init; }
    public double CentroidZ { get; init; }
    public double CentroidM { get; init; }
    public double VarianceY { get; init; }
    public double VarianceZ { get; init; }
    public double VarianceM { get; init; }
    public int SampleSize { get; init; }
    
    public static ModalityCluster Unknown => new();
}

private record ClusterResult
{
    public int ClusterId { get; init; }
    public double CentroidY { get; init; }
    public double CentroidZ { get; init; }
    public double CentroidM { get; init; }
    public double VarianceY { get; init; }
    public double VarianceZ { get; init; }
    public double VarianceM { get; init; }
    public int SampleSize { get; init; }
}
```

**Acceptance Criteria**:
- ✅ k-NN clustering in YZM space working
- ✅ DBSCAN clustering discovers natural clusters
- ✅ Modality classification heuristics reasonable
- ✅ Performance: <100ms for modality identification
- ✅ Performance: <5s for full cluster discovery

---

### Task 3.5: UniversalGeometryFactory (6 hours)

**File**: `Hartonomous.Core/Domain/Factories/UniversalGeometryFactory.cs` (NEW)

```csharp
public interface IUniversalGeometryFactory
{
    SpatialCoordinate CreateCoordinate(
        Hash256 hash,
        byte[] data,
        long referenceCount);
    
    Task<Point> CreateLocationAsync(
        Hash256 hash,
        byte[] data,
        long referenceCount,
        CancellationToken cancellationToken = default);
}

public sealed class UniversalGeometryFactory : IUniversalGeometryFactory
{
    private readonly IQuantizationService _quantizationService;
    private readonly ILogger<UniversalGeometryFactory> _logger;
    
    public SpatialCoordinate CreateCoordinate(
        Hash256 hash,
        byte[] data,
        long referenceCount)
    {
        return SpatialCoordinate.FromUniversalProperties(
            hash,
            data,
            referenceCount,
            _quantizationService);
    }
    
    public async Task<Point> CreateLocationAsync(
        Hash256 hash,
        byte[] data,
        long referenceCount,
        CancellationToken cancellationToken = default)
    {
        var coordinate = CreateCoordinate(hash, data, referenceCount);
        
        return await Task.FromResult(coordinate.ToPoint());
    }
}
```

**Usage in Atomization Services**:

```csharp
// TextAtomizationService.cs
public async Task<IEnumerable<Constant>> AtomizeAsync(
    string text,
    CancellationToken cancellationToken = default)
{
    var constants = new List<Constant>();
    
    foreach (var word in SplitIntoWords(text))
    {
        var bytes = Encoding.UTF8.GetBytes(word);
        var hash = ComputeHash(bytes);
        
        // Universal coordinate creation - NO ContentType enum
        var coordinate = _geometryFactory.CreateCoordinate(
            hash,
            bytes,
            referenceCount: 0); // Will be computed later
        
        var constant = new Constant
        {
            Id = Guid.NewGuid(),
            Hash = hash,
            Data = bytes,
            Coordinate = coordinate,
            Location = coordinate.ToPoint(),
            // NO ContentType field!
        };
        
        constants.Add(constant);
    }
    
    return constants;
}
```

**Acceptance Criteria**:
- ✅ Factory creates coordinates consistently
- ✅ All atomization services use factory
- ✅ No manual `ContentType` assignment anywhere
- ✅ Coordinates include all 4 dimensions (X, Y, Z, M)

---

### Task 3.6: Update All Atomization Services (4 hours)

**Files to Modify**:
- `Hartonomous.Data/Services/TextAtomizationService.cs`
- `Hartonomous.Data/Services/ImageAtomizationService.cs`
- `Hartonomous.Data/Services/AudioAtomizationService.cs`
- `Hartonomous.Data/Services/VideoAtomizationService.cs`

**Pattern**:
```csharp
// BEFORE
var constant = new Constant
{
    ContentType = ContentType.Text, // ❌ REMOVE THIS
    Data = bytes,
    Location = coordinate.ToPoint()
};

// AFTER
var coordinate = _geometryFactory.CreateCoordinate(hash, bytes, referenceCount);
var constant = new Constant
{
    Data = bytes,
    Coordinate = coordinate,
    Location = coordinate.ToPoint()
    // Modality will emerge from YZM clustering
};
```

**Acceptance Criteria**:
- ✅ All atomization services updated
- ✅ No `ContentType` enum usage anywhere
- ✅ All use `UniversalGeometryFactory`
- ✅ Tests updated to remove `ContentType` assertions

---

## Visualization Tools

### Cluster Visualization Query
```sql
-- Visualize YZM clusters in 3D space
SELECT 
    quantized_y / 1000000.0 as y_mega,
    quantized_z / 1000000.0 as z_mega,
    quantized_m / 1000000.0 as m_mega,
    COUNT(*) as count
FROM constants
GROUP BY 
    quantized_y / 1000000,
    quantized_z / 1000000,
    quantized_m / 1000000
HAVING COUNT(*) > 10
ORDER BY count DESC;
```

### Modality Separation Analysis
```csharp
[Fact]
public async Task DiscoverClusters_ShowsModalitySeparation()
{
    // Arrange: Seed diverse data
    await SeedTextConstants(1000);
    await SeedImageConstants(1000);
    await SeedAudioConstants(1000);
    
    // Act: Discover clusters
    var clusters = await _modalityService.DiscoverClustersAsync(maxClusters: 10);
    
    // Assert: At least 3 distinct clusters
    Assert.True(clusters.Count() >= 3);
    Assert.Contains(clusters, c => c.Name == "Text");
    Assert.Contains(clusters, c => c.Name == "Image");
    Assert.Contains(clusters, c => c.Name == "Audio");
}
```

---

## Integration Tests

### Test Scenario 1: Universal Property Calculation
```csharp
[Theory]
[InlineData("Hello World", 1.5e6, 1.8e6, 0)] // Text: high entropy, high compressibility
[InlineData(new byte[] { 0, 0, 0, 0 }, 0, 0, 0)] // Zeros: zero entropy, zero compressibility
public void QuantizeUniversalProperties_ReturnsExpectedRanges(
    byte[] data,
    double expectedY,
    double expectedZ,
    double expectedM)
{
    // Act
    var y = _quantizationService.QuantizeEntropy(data);
    var z = _quantizationService.QuantizeCompressibility(data);
    var m = _quantizationService.QuantizeConnectivity(0);
    
    // Assert
    Assert.InRange(y, 0, 2_097_151);
    Assert.InRange(z, 0, 2_097_151);
    Assert.InRange(m, 0, 2_097_151);
    Assert.InRange(y, expectedY * 0.8, expectedY * 1.2); // ±20% tolerance
}
```

### Test Scenario 2: Emergent Modality
```csharp
[Fact]
public async Task IdentifyModality_ForTextConstant_ReturnsTextCluster()
{
    // Arrange: Create text constant with typical YZM values
    var textBytes = Encoding.UTF8.GetBytes("The quick brown fox");
    var hash = ComputeHash(textBytes);
    var coordinate = _geometryFactory.CreateCoordinate(hash, textBytes, 0);
    
    var constant = new Constant
    {
        Data = textBytes,
        Coordinate = coordinate,
        Location = coordinate.ToPoint()
    };
    await _constantRepository.AddAsync(constant);
    
    // Act: Identify modality
    var cluster = await _modalityService.IdentifyModalityAsync(constant.Location);
    
    // Assert: Classified as Text
    Assert.Equal("Text", cluster.Name);
    Assert.True(cluster.CentroidY > 1_500_000); // High entropy
    Assert.True(cluster.CentroidZ > 1_500_000); // High compressibility
    Assert.True(cluster.CentroidM < 500_000);   // Low connectivity
}
```

---

## Performance Benchmarks

| Operation | Target | Measured |
|-----------|--------|----------|
| Shannon entropy (1MB) | <1ms | TBD |
| Gzip compression (1MB) | <10ms | TBD |
| Connectivity quantization | <0.1ms | TBD |
| k-NN modality identification | <100ms | TBD |
| DBSCAN clustering (10K) | <5s | TBD |

---

## Acceptance Criteria (Phase Exit)

- ✅ Shannon entropy calculation working
- ✅ Kolmogorov complexity approximation working
- ✅ Graph connectivity metric working
- ✅ Modality emerges from YZM clustering (no enum)
- ✅ UniversalGeometryFactory operational
- ✅ All atomization services use universal properties
- ✅ Cluster analysis shows clear modality separation
- ✅ All tests passing (>80% coverage)
- ✅ Performance benchmarks met

---

**Next Phase**: [PHASE4_MATH_ALGORITHMS.md](./PHASE4_MATH_ALGORITHMS.md) - A*, PageRank, Laplace, Blossom, Voronoi, MST

**Status**: 📋 Ready for implementation after Phase 2

**Last Updated**: December 4, 2025
