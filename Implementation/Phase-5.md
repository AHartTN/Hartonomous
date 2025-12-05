---
title: "Phase 5: Advanced Geometric Features"
author: "Hartonomous Development Team"
date: "2025-12-05"
version: "1.0"
status: "Planning"
---

# Phase 5: Advanced Geometric Features

## Table of Contents
- [Overview](#overview)
- [Phase Details](#phase-details)
- [Objectives](#objectives)
- [Implementation Tasks](#implementation-tasks)
  - [5.1: High-Dimensional Embeddings](#51-high-dimensional-embeddings)
  - [5.2: Neural Network Parameters](#52-neural-network-parameters)
  - [5.3: Multi-Modal Content](#53-multi-modal-content)
  - [5.4: Geometric Transformations](#54-geometric-transformations)
- [Success Criteria](#success-criteria)
- [Quality Gates](#quality-gates)
- [Risks & Mitigation](#risks--mitigation)
- [Dependencies](#dependencies)
- [Next Steps](#next-steps)

---

## Overview

**Phase 5** extends the geometric substrate to support advanced content types including high-dimensional embeddings, neural network parameters, and multi-modal content. This phase demonstrates the universal applicability of the POINTZM space by storing traditionally non-spatial data geometrically.

**Duration**: 3-5 days  
**Complexity**: High  
**Dependencies**: Phases 1, 2, 3, 4  
**Prerequisites**: All core geometric infrastructure operational

---

## Phase Details

### Timeline
- **Start**: After Phase 4 completion
- **Duration**: 3-5 days
- **Parallelizable**: Tasks 5.1, 5.2, 5.3 can run concurrently after task design
- **Critical Path**: No - Optional advanced features

### Resource Requirements
- **Development**: 1-2 senior engineers with ML/embedding experience
- **Testing**: Integration testing with real embeddings/models
- **Infrastructure**: GPU support for embedding generation (optional)

---

## Objectives

1. **Store embeddings as MULTIPOINTZM** - Project 768D vectors into geometric space
2. **Represent neural networks geometrically** - Store weights/biases as POINTZM
3. **Handle multi-modal content** - Images, audio, video as geometry
4. **Enable geometric transformations** - Rotation, scaling, translation in POINTZM space
5. **Demonstrate universal substrate** - Show ANY data can be geometric

---

## Implementation Tasks

### 5.1: High-Dimensional Embeddings

**Goal**: Store embedding vectors (768D, 1536D, etc.) as MULTIPOINTZM geometry.

<details>
<summary><strong>5.1.1: Create Embedding Entity</strong> (1 day)</summary>

**Create `Embedding.cs` in Core/Domain/Entities:**

```csharp
namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents a high-dimensional embedding vector stored as MULTIPOINTZM.
/// Each POINTZM stores 3 dimensions, so 768D = 256 points.
/// </summary>
public sealed class Embedding : BaseEntity, ISpatialEntity
{
    private MultiPoint? _vectorGeometry;
    private float[]? _vectorCache;
    
    /// <summary>
    /// Source content this embedding represents
    /// </summary>
    public Guid ContentId { get; private set; }
    
    /// <summary>
    /// Embedding model used (e.g., "text-embedding-ada-002", "all-MiniLM-L6-v2")
    /// </summary>
    public string ModelName { get; private set; } = string.Empty;
    
    /// <summary>
    /// Original vector dimensions (768, 1536, etc.)
    /// </summary>
    public int Dimensions { get; private set; }
    
    /// <summary>
    /// Vector stored as MULTIPOINTZM (3D chunks)
    /// </summary>
    public MultiPoint VectorGeometry 
    { 
        get => _vectorGeometry ??= FromVector(Vector);
        set => _vectorGeometry = value;
    }
    
    /// <summary>
    /// Cached vector array for efficient access
    /// </summary>
    public float[] Vector
    {
        get => _vectorCache ??= ToVector(VectorGeometry);
        private set => _vectorCache = value;
    }
    
    // Factory method
    public static Embedding Create(
        Guid contentId, 
        string modelName, 
        float[] vector)
    {
        if (vector.Length % 3 != 0)
            throw new ArgumentException("Vector dimensions must be divisible by 3 for POINTZM storage");
            
        return new Embedding
        {
            Id = Guid.NewGuid(),
            ContentId = contentId,
            ModelName = modelName,
            Dimensions = vector.Length,
            Vector = vector
        };
    }
    
    /// <summary>
    /// Convert vector array to MULTIPOINTZM
    /// </summary>
    private MultiPoint FromVector(float[] vector)
    {
        var pointCount = vector.Length / 3;
        var points = new Point[pointCount];
        
        for (int i = 0; i < pointCount; i++)
        {
            points[i] = new Point(
                vector[i * 3],       // X
                vector[i * 3 + 1],   // Y
                vector[i * 3 + 2]    // Z
            ) 
            { 
                SRID = 4326,
                M = i  // M = position in sequence
            };
        }
        
        return new MultiPoint(points) { SRID = 4326 };
    }
    
    /// <summary>
    /// Convert MULTIPOINTZM to vector array
    /// </summary>
    private float[] ToVector(MultiPoint geometry)
    {
        var points = geometry.Geometries.Cast<Point>().ToArray();
        var vector = new float[points.Length * 3];
        
        for (int i = 0; i < points.Length; i++)
        {
            vector[i * 3] = (float)points[i].X;
            vector[i * 3 + 1] = (float)points[i].Y;
            vector[i * 3 + 2] = (float)points[i].Z;
        }
        
        return vector;
    }
    
    /// <summary>
    /// Compute cosine similarity with another embedding
    /// </summary>
    public double CosineSimilarity(Embedding other)
    {
        if (Dimensions != other.Dimensions)
            throw new ArgumentException("Embeddings must have same dimensions");
            
        var dotProduct = 0.0;
        var normA = 0.0;
        var normB = 0.0;
        
        for (int i = 0; i < Dimensions; i++)
        {
            dotProduct += Vector[i] * other.Vector[i];
            normA += Vector[i] * Vector[i];
            normB += other.Vector[i] * other.Vector[i];
        }
        
        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
```

**Create EF Core Configuration:**

```csharp
namespace Hartonomous.Data.Configurations;

public sealed class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.ToTable("embeddings");
        
        builder.Property(e => e.ContentId)
            .IsRequired();
            
        builder.Property(e => e.ModelName)
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(e => e.Dimensions)
            .IsRequired();
            
        builder.Property(e => e.VectorGeometry)
            .HasColumnType("geometry(MULTIPOINTZM, 4326)")
            .IsRequired();
            
        // Ignore computed property
        builder.Ignore(e => e.Vector);
        
        // Indexes
        builder.HasIndex(e => e.ContentId);
        builder.HasIndex(e => e.ModelName);
        
        // GIST index for spatial queries
        builder.HasIndex(e => e.VectorGeometry)
            .HasMethod("GIST");
    }
}
```

**Testing**:
```csharp
[Fact]
public async Task Embedding_Should_Store_768D_Vector_As_256_Points()
{
    // Arrange
    var vector = GenerateRandomVector(768);
    var embedding = Embedding.Create(contentId, "test-model", vector);
    
    // Act
    await _repository.AddAsync(embedding);
    await _unitOfWork.SaveChangesAsync();
    var retrieved = await _repository.GetByIdAsync(embedding.Id);
    
    // Assert
    retrieved.Should().NotBeNull();
    retrieved.Dimensions.Should().Be(768);
    retrieved.VectorGeometry.Geometries.Count.Should().Be(256);
    retrieved.Vector.Should().BeEquivalentTo(vector);
}
```

</details>

<details>
<summary><strong>5.1.2: Implement Embedding Search Service</strong> (1 day)</summary>

**Create `IEmbeddingSearchService.cs`:**

```csharp
namespace Hartonomous.Core.Application.Interfaces;

public interface IEmbeddingSearchService
{
    /// <summary>
    /// Find k nearest embeddings using PostGIS k-NN
    /// </summary>
    Task<List<(Embedding Embedding, double Distance)>> FindNearestAsync(
        float[] queryVector,
        int k = 10,
        string? modelName = null,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Find embeddings within distance threshold
    /// </summary>
    Task<List<Embedding>> FindWithinRadiusAsync(
        float[] queryVector,
        double radius,
        string? modelName = null,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Compute similarity matrix for batch of embeddings
    /// </summary>
    Task<double[,]> ComputeSimilarityMatrixAsync(
        List<Guid> embeddingIds,
        CancellationToken cancellationToken = default);
}
```

**Implementation**:

```csharp
namespace Hartonomous.Infrastructure.Services;

public sealed class EmbeddingSearchService : IEmbeddingSearchService
{
    private readonly ApplicationDbContext _dbContext;
    
    public async Task<List<(Embedding, double)>> FindNearestAsync(
        float[] queryVector,
        int k,
        string? modelName,
        CancellationToken cancellationToken)
    {
        // Convert query vector to MULTIPOINTZM
        var queryGeometry = VectorToMultiPoint(queryVector);
        
        // PostGIS k-NN query using <-> operator
        var query = _dbContext.Embeddings
            .Where(e => modelName == null || e.ModelName == modelName)
            .OrderBy(e => e.VectorGeometry.Distance(queryGeometry))
            .Take(k)
            .Select(e => new 
            { 
                Embedding = e,
                Distance = e.VectorGeometry.Distance(queryGeometry)
            });
            
        var results = await query.ToListAsync(cancellationToken);
        
        return results
            .Select(r => (r.Embedding, r.Distance))
            .ToList();
    }
    
    public async Task<List<Embedding>> FindWithinRadiusAsync(
        float[] queryVector,
        double radius,
        string? modelName,
        CancellationToken cancellationToken)
    {
        var queryGeometry = VectorToMultiPoint(queryVector);
        
        return await _dbContext.Embeddings
            .Where(e => modelName == null || e.ModelName == modelName)
            .Where(e => e.VectorGeometry.Distance(queryGeometry) <= radius)
            .ToListAsync(cancellationToken);
    }
    
    private MultiPoint VectorToMultiPoint(float[] vector)
    {
        var pointCount = vector.Length / 3;
        var points = new Point[pointCount];
        
        for (int i = 0; i < pointCount; i++)
        {
            points[i] = new Point(
                vector[i * 3],
                vector[i * 3 + 1],
                vector[i * 3 + 2]
            ) { SRID = 4326, M = i };
        }
        
        return new MultiPoint(points) { SRID = 4326 };
    }
}
```

</details>

---

### 5.2: Neural Network Parameters

**Goal**: Store neural network weights and biases geometrically.

<details>
<summary><strong>5.2.1: Create NeuralParameter Entity</strong> (1 day)</summary>

```csharp
namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents a neural network parameter (weight or bias) as POINTZM.
/// X = Hilbert(hash(value)), Y = entropy, Z = compressibility, M = importance
/// </summary>
public sealed class NeuralParameter : BaseEntity, ISpatialEntity
{
    public Guid ModelId { get; private set; }
    public string LayerName { get; private set; } = string.Empty;
    public string ParameterType { get; private set; } = string.Empty; // "weight" or "bias"
    public int[] Shape { get; private set; } = Array.Empty<int>();
    public float Value { get; private set; }
    
    /// <summary>
    /// Geometric location based on parameter value
    /// </summary>
    public Point Location { get; set; } = null!;
    
    public static NeuralParameter Create(
        Guid modelId,
        string layerName,
        string parameterType,
        int[] shape,
        float value,
        IQuantizationService quantization,
        IHilbertIndexService hilbert)
    {
        var valueBytes = BitConverter.GetBytes(value);
        var hash = SHA256.HashData(valueBytes);
        
        var x = hilbert.ComputeIndex(hash);
        var y = quantization.QuantizeEntropy(valueBytes);
        var z = quantization.QuantizeCompressibility(valueBytes);
        var m = ComputeImportance(layerName, shape);
        
        return new NeuralParameter
        {
            Id = Guid.NewGuid(),
            ModelId = modelId,
            LayerName = layerName,
            ParameterType = parameterType,
            Shape = shape,
            Value = value,
            Location = new Point(x, y, z) { SRID = 4326, M = m }
        };
    }
    
    private static int ComputeImportance(string layerName, int[] shape)
    {
        // Early layers more important, larger shapes more important
        var layerDepth = ExtractLayerDepth(layerName);
        var shapeSize = shape.Aggregate(1, (a, b) => a * b);
        var importance = (1.0 / (layerDepth + 1)) * Math.Log(shapeSize + 1);
        return (int)(importance * 100000);
    }
}
```

</details>

---

### 5.3: Multi-Modal Content

**Goal**: Handle images, audio, video as geometric objects.

<details>
<summary><strong>5.3.1: Image Storage as POINTZM Grid</strong> (1 day)</summary>

```csharp
/// <summary>
/// Store image pixels as MULTIPOINTZM where each point = (R, G, B, position)
/// </summary>
public sealed class ImageContent : BaseEntity
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public MultiPoint PixelGeometry { get; set; } = null!;
    
    public static ImageContent FromBitmap(Bitmap image)
    {
        var pixels = new List<Point>();
        
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                pixels.Add(new Point(pixel.R, pixel.G, pixel.B)
                {
                    SRID = 4326,
                    M = y * image.Width + x  // Linear position
                });
            }
        }
        
        return new ImageContent
        {
            Width = image.Width,
            Height = image.Height,
            PixelGeometry = new MultiPoint(pixels) { SRID = 4326 }
        };
    }
}
```

**Query Similar Images**:
```csharp
// Find images with similar color distribution
var similar = await _dbContext.ImageContents
    .OrderBy(i => i.PixelGeometry.Distance(targetImage.PixelGeometry))
    .Take(10)
    .ToListAsync();
```

</details>

---

### 5.4: Geometric Transformations

**Goal**: Support rotation, scaling, translation in POINTZM space.

<details>
<summary><strong>5.4.1: Create GeometricTransformationService</strong> (1 day)</summary>

```csharp
public interface IGeometricTransformationService
{
    Point Rotate(Point point, double angleX, double angleY, double angleZ);
    Point Scale(Point point, double factorX, double factorY, double factorZ);
    Point Translate(Point point, double deltaX, double deltaY, double deltaZ);
    MultiPoint Transform(MultiPoint geometry, Matrix4x4 transformMatrix);
}
```

**Use Cases**:
- Normalize embeddings to unit sphere
- Align coordinate systems
- Apply learned transformations
- Implement geometric attention mechanisms

</details>

---

## Success Criteria

- [ ] **768D embeddings stored as 256×POINTZM** with lossless roundtrip
- [ ] **k-NN embedding search** completes in <100ms for 1M embeddings
- [ ] **Neural network parameters** stored geometrically with importance scores
- [ ] **Image content** stored and queried by color similarity
- [ ] **Geometric transformations** implemented with unit tests
- [ ] **All spatial queries use GIST indexes** (confirmed via EXPLAIN ANALYZE)

---

## Quality Gates

### Code Review Checklist
- [ ] All entities inherit `BaseEntity` or `SpatialEntity`
- [ ] SRID = 4326 specified for all geometries
- [ ] Vector serialization is lossless (roundtrip tests pass)
- [ ] Spatial indexes created for all geometry columns
- [ ] Performance tests show <100ms query times

### Testing Requirements
- [ ] Unit tests for Embedding.FromVector/ToVector
- [ ] Integration tests for k-NN search
- [ ] Load tests with 1M+ embeddings
- [ ] Roundtrip tests for all geometric types

---

## Risks & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **High memory usage** for large embeddings | High | Medium | Use streaming serialization, lazy loading |
| **Slow k-NN queries** on millions of embeddings | High | Medium | Ensure GIST indexes, consider HNSW |
| **Loss of precision** in float→POINTZM conversion | Medium | Low | Use double precision, validation tests |
| **Complex transformation math** | Medium | Medium | Use proven libraries (System.Numerics) |

---

## Dependencies

### Upstream (Required)
- **Phase 1**: POINTZM infrastructure, quantization
- **Phase 3**: Universal properties (entropy, complexity)
- **Phase 4**: Geometric algorithms (distance, k-NN)

### Downstream (Impacts)
- **Phase 6**: Testing will validate embedding search performance
- **Phase 7**: Documentation will include embedding API examples

---

## Next Steps

After completing Phase 5:

1. **Proceed to Phase 6** - Testing & Quality Assurance
2. **Document embedding API** - Add examples to wiki
3. **Benchmark performance** - Compare with vector databases
4. **Explore advanced features**:
   - HNSW indexes for faster k-NN
   - GPU-accelerated distance computation
   - Learned geometric transformations

**See**: [Phase 6: Testing & Quality Assurance](Phase-6.md)

---

**Navigation**:  
← [Phase 4: Mathematical Algorithms](Phase-4.md) | [Master Plan](Master-Plan.md) | [Phase 6: Testing & QA](Phase-6.md) →
