# Phase 5: Advanced Features

**Duration**: 4-5 days  
**Dependencies**: Phase 1 (POINTZM), Phase 3 (Universal properties), Phase 4 (Graph algorithms)  
**Critical Path**: No - can be partially implemented in parallel with testing

---

## Overview

Implement advanced geometric features: embedding storage as MULTIPOINTZM, neural network parameter storage, content as geometric objects (convex hulls), Borsuk-Ulam antipodal analysis, and topological continuity verification.

---

## Objectives

1. Store embeddings as MULTIPOINTZM geometries
2. Store neural network parameters in POINTZM space
3. Represent content as geometric objects (convex hulls, polygons)
4. Implement Borsuk-Ulam antipodal analysis
5. Implement topological continuity verification
6. Create `TopologyAnalysisService` for advanced geometric analysis

---

## Task Breakdown

### Task 5.1: Embedding Storage as MULTIPOINTZM (8 hours)

**Purpose**: Store vector embeddings (e.g., from transformers) as multi-dimensional points in POINTZM space.

**Files to Modify**:
- `Hartonomous.Core/Domain/Entities/Embedding.cs` (NEW)
- `Hartonomous.Data/Configurations/EmbeddingConfiguration.cs` (NEW)

```csharp
// Embedding.cs
public sealed class Embedding : BaseEntity
{
    private MultiPoint? _vectorGeometry;
    
    public Guid ConstantId { get; private set; }
    public Constant Constant { get; private set; } = null!;
    
    public string ModelName { get; private set; } = string.Empty; // e.g., "sentence-transformers/all-MiniLM-L6-v2"
    public int Dimensions { get; private set; } // e.g., 384, 768, 1536
    
    // Store embedding as MULTIPOINTZM (one point per dimension)
    public MultiPoint VectorGeometry
    {
        get => _vectorGeometry ?? throw new InvalidOperationException("VectorGeometry not initialized");
        private set => _vectorGeometry = value;
    }
    
    // Factory method from float array
    public static Embedding CreateFromVector(
        Guid constantId,
        string modelName,
        float[] vector,
        IQuantizationService quantizationService)
    {
        if (vector == null || vector.Length == 0)
            throw new ArgumentException("Vector cannot be empty", nameof(vector));
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var points = new Point[vector.Length];
        
        // Each dimension becomes a POINTZM
        for (int i = 0; i < vector.Length; i++)
        {
            var value = vector[i];
            
            // Quantize float [-1, 1] to 21-bit integer
            var quantized = QuantizeFloat(value);
            
            // Create POINTZM: X = dimension index, Y = quantized value, Z = 0, M = 0
            points[i] = geometryFactory.CreatePoint(new CoordinateZM(
                x: i,
                y: quantized,
                z: 0,
                m: 0
            ));
        }
        
        var multiPoint = geometryFactory.CreateMultiPoint(points);
        
        return new Embedding
        {
            Id = Guid.NewGuid(),
            ConstantId = constantId,
            ModelName = modelName,
            Dimensions = vector.Length,
            VectorGeometry = multiPoint
        };
    }
    
    public float[] ToVector()
    {
        var vector = new float[Dimensions];
        
        for (int i = 0; i < Dimensions; i++)
        {
            var point = VectorGeometry.Geometries[i] as Point;
            if (point != null)
            {
                // Dequantize Y coordinate back to float
                var quantized = (int)point.Y;
                vector[i] = DequantizeFloat(quantized);
            }
        }
        
        return vector;
    }
    
    private static int QuantizeFloat(float value)
    {
        // Clamp to [-1, 1]
        value = Math.Clamp(value, -1f, 1f);
        
        // Map [-1, 1] → [0, 2,097,151]
        var normalized = (value + 1f) / 2f; // [0, 1]
        return (int)(normalized * 2_097_151);
    }
    
    private static float DequantizeFloat(int quantized)
    {
        // Map [0, 2,097,151] → [-1, 1]
        var normalized = quantized / 2_097_151.0;
        return (float)(normalized * 2.0 - 1.0);
    }
}

// EmbeddingConfiguration.cs
public sealed class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.ToTable("embeddings");
        
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").IsRequired();
        
        builder.Property(e => e.ConstantId).HasColumnName("constant_id").IsRequired();
        builder.Property(e => e.ModelName).HasColumnName("model_name").IsRequired().HasMaxLength(200);
        builder.Property(e => e.Dimensions).HasColumnName("dimensions").IsRequired();
        
        // MULTIPOINTZM geometry
        builder.Property(e => e.VectorGeometry)
            .HasColumnName("vector_geometry")
            .HasColumnType("geometry(MultiPointZM, 4326)")
            .IsRequired();
        
        // Index on constant_id for lookups
        builder.HasIndex(e => e.ConstantId).HasDatabaseName("ix_embeddings_constant_id");
        
        // Index on model_name for filtering
        builder.HasIndex(e => e.ModelName).HasDatabaseName("ix_embeddings_model_name");
        
        // Foreign key
        builder.HasOne(e => e.Constant)
            .WithMany()
            .HasForeignKey(e => e.ConstantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Database Migration**:
```csharp
// 20251204_AddEmbeddingTable.cs
public partial class AddEmbeddingTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "embeddings",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                constant_id = table.Column<Guid>(type: "uuid", nullable: false),
                model_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                dimensions = table.Column<int>(type: "integer", nullable: false),
                vector_geometry = table.Column<MultiPoint>(type: "geometry(MultiPointZM, 4326)", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_embeddings", x => x.id);
                table.ForeignKey(
                    name: "fk_embeddings_constants",
                    column: x => x.constant_id,
                    principalTable: "constants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
        
        migrationBuilder.CreateIndex(
            name: "ix_embeddings_constant_id",
            table: "embeddings",
            column: "constant_id");
        
        migrationBuilder.CreateIndex(
            name: "ix_embeddings_model_name",
            table: "embeddings",
            column: "model_name");
    }
    
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "embeddings");
    }
}
```

**Acceptance Criteria**:
- ✅ Embeddings stored as MULTIPOINTZM
- ✅ Float vectors quantized to 21-bit integers
- ✅ ToVector() correctly reconstructs original embedding
- ✅ Performance: <10ms for 768-dimensional embedding

---

### Task 5.2: Neural Network Parameter Storage (6 hours)

**Purpose**: Store neural network weights/biases as POINTZM coordinates for geometric analysis.

**File**: `Hartonomous.Core/Domain/Entities/NeuralParameter.cs` (NEW)

```csharp
public sealed class NeuralParameter : BaseEntity
{
    public string ModelName { get; private set; } = string.Empty; // e.g., "gpt-2", "bert-base"
    public string LayerName { get; private set; } = string.Empty; // e.g., "transformer.layer.0.attention"
    public string ParameterName { get; private set; } = string.Empty; // e.g., "weight", "bias"
    
    public Point Location { get; private set; } = null!; // POINTZM
    public SpatialCoordinate Coordinate { get; private set; } = null!;
    
    public float Value { get; private set; }
    
    public static NeuralParameter CreateFromWeight(
        string modelName,
        string layerName,
        string parameterName,
        float value,
        IQuantizationService quantizationService)
    {
        // Compute hash from fully qualified name
        var fqn = $"{modelName}.{layerName}.{parameterName}";
        var hash = ComputeHash(Encoding.UTF8.GetBytes(fqn));
        
        // Create coordinate from universal properties
        var coordinate = SpatialCoordinate.FromUniversalProperties(
            hash,
            BitConverter.GetBytes(value),
            referenceCount: 0,
            quantizationService);
        
        return new NeuralParameter
        {
            Id = Guid.NewGuid(),
            ModelName = modelName,
            LayerName = layerName,
            ParameterName = parameterName,
            Value = value,
            Coordinate = coordinate,
            Location = coordinate.ToPoint()
        };
    }
    
    private static Hash256 ComputeHash(byte[] data)
    {
        using var sha = SHA256.Create();
        return new Hash256(sha.ComputeHash(data));
    }
}
```

**Use Case**: Geometric analysis of neural network structure
- Find "similar" parameters (k-NN in POINTZM space)
- Detect pruning opportunities (low-entropy parameters)
- Analyze weight distribution (clustering in YZM space)

**Acceptance Criteria**:
- ✅ Parameters stored as POINTZM
- ✅ Can query by model/layer/parameter name
- ✅ k-NN finds similar weights
- ✅ Performance: <1s to store 100M parameters

---

### Task 5.3: Content as Geometric Objects (8 hours)

**Purpose**: Represent entire documents/images as convex hulls or polygons in POINTZM space.

**File**: `Hartonomous.Core/Domain/ValueObjects/ContentGeometry.cs` (NEW)

```csharp
public sealed class ContentGeometry
{
    public Geometry Shape { get; }
    public string ShapeType { get; }
    public int ConstituentsCount { get; }
    
    private ContentGeometry(Geometry shape, string shapeType, int constituentsCount)
    {
        Shape = shape;
        ShapeType = shapeType;
        ConstituentsCount = constituentsCount;
    }
    
    public static ContentGeometry CreateConvexHull(IEnumerable<Constant> constants)
    {
        var coordinates = constants
            .Select(c => new Coordinate(
                c.Location.X,
                c.Location.Y,
                c.Location.Z))
            .ToArray();
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var multiPoint = geometryFactory.CreateMultiPoint(
            coordinates.Select(c => geometryFactory.CreatePoint(c)).ToArray());
        
        var convexHull = multiPoint.ConvexHull();
        
        return new ContentGeometry(
            convexHull,
            convexHull.GeometryType,
            constants.Count());
    }
    
    public static ContentGeometry CreateBoundingBox(IEnumerable<Constant> constants)
    {
        var coordinates = constants
            .Select(c => c.Location.Coordinate)
            .ToArray();
        
        var envelope = new Envelope();
        foreach (var coord in coordinates)
        {
            envelope.ExpandToInclude(coord);
        }
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var polygon = geometryFactory.ToGeometry(envelope);
        
        return new ContentGeometry(
            polygon,
            "Polygon",
            constants.Count());
    }
    
    public bool Contains(Point location)
    {
        return Shape.Contains(location);
    }
    
    public double DistanceTo(Point location)
    {
        return Shape.Distance(location);
    }
    
    public double Area => Shape.Area;
    public double Perimeter => Shape is Polygon poly ? poly.Length : Shape.Length;
}
```

**Use Cases**:
- Document similarity: Compare convex hull overlap
- Content classification: Analyze hull shape (elongated vs. spherical)
- Anomaly detection: Points outside typical content hull

**Acceptance Criteria**:
- ✅ Convex hulls computed correctly
- ✅ Bounding boxes computed correctly
- ✅ Contains() and DistanceTo() working
- ✅ Performance: <100ms for 10K constants

---

### Task 5.4: Borsuk-Ulam Antipodal Analysis (8 hours)

**Purpose**: Implement Borsuk-Ulam theorem verification for topological continuity.

**File**: `Hartonomous.Core/Application/Services/TopologyAnalysisService.cs` (NEW)

```csharp
public interface ITopologyAnalysisService
{
    Task<AntipodalPair?> FindAntipodalPairAsync(
        Point location,
        CancellationToken cancellationToken = default);
    
    Task<bool> VerifyTopologicalContinuityAsync(
        IEnumerable<Constant> constants,
        CancellationToken cancellationToken = default);
}

public sealed class TopologyAnalysisService : ITopologyAnalysisService
{
    private readonly IConstantRepository _constantRepository;
    private readonly ILogger<TopologyAnalysisService> _logger;
    
    public async Task<AntipodalPair?> FindAntipodalPairAsync(
        Point location,
        CancellationToken cancellationToken = default)
    {
        // Borsuk-Ulam: For continuous f: S^n → R^n, exists x where f(x) = f(-x)
        // In POINTZM space: find point at "opposite" location with same YZM properties
        
        var constant = await _constantRepository
            .Query()
            .Where(c => c.Location.Distance(location) < 1.0)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (constant == null)
            return null;
        
        // Compute antipodal point in Hilbert space (flip bits)
        var antipodalHilbert = ~constant.Coordinate.HilbertIndex;
        
        // Find nearest constant to antipodal point
        var antipodalConstant = await _constantRepository
            .Query()
            .OrderBy(c => Math.Abs((long)c.Coordinate.HilbertIndex - (long)antipodalHilbert))
            .FirstOrDefaultAsync(cancellationToken);
        
        if (antipodalConstant == null)
            return null;
        
        // Check if YZM properties match (within tolerance)
        var yDiff = Math.Abs(constant.Coordinate.QuantizedY - antipodalConstant.Coordinate.QuantizedY);
        var zDiff = Math.Abs(constant.Coordinate.QuantizedZ - antipodalConstant.Coordinate.QuantizedZ);
        var mDiff = Math.Abs(constant.Coordinate.QuantizedM - antipodalConstant.Coordinate.QuantizedM);
        
        const int Tolerance = 10_000; // ~0.5% of max value
        var isAntipodal = yDiff < Tolerance && zDiff < Tolerance && mDiff < Tolerance;
        
        return new AntipodalPair
        {
            Location = constant.Location,
            AntipodalLocation = antipodalConstant.Location,
            IsVerified = isAntipodal,
            YDifference = yDiff,
            ZDifference = zDiff,
            MDifference = mDiff
        };
    }
    
    public async Task<bool> VerifyTopologicalContinuityAsync(
        IEnumerable<Constant> constants,
        CancellationToken cancellationToken = default)
    {
        // Verify that mapping from Hilbert space (X) to YZM space is continuous
        // Check: nearby points in X-space have nearby YZM values
        
        var constantsList = constants.ToList();
        if (constantsList.Count < 2)
            return true; // Trivially continuous
        
        // Sort by Hilbert index
        var sorted = constantsList.OrderBy(c => c.Coordinate.HilbertIndex).ToList();
        
        int discontinuities = 0;
        const double MaxJump = 0.1; // 10% of max value
        
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var current = sorted[i];
            var next = sorted[i + 1];
            
            // Compute YZM distance
            var yJump = Math.Abs(current.Coordinate.QuantizedY - next.Coordinate.QuantizedY) / 2_097_151.0;
            var zJump = Math.Abs(current.Coordinate.QuantizedZ - next.Coordinate.QuantizedZ) / 2_097_151.0;
            var mJump = Math.Abs(current.Coordinate.QuantizedM - next.Coordinate.QuantizedM) / 2_097_151.0;
            
            if (yJump > MaxJump || zJump > MaxJump || mJump > MaxJump)
            {
                discontinuities++;
                _logger.LogWarning(
                    "Discontinuity detected: Y={Y:F4}, Z={Z:F4}, M={M:F4}",
                    yJump, zJump, mJump);
            }
        }
        
        var continuityRatio = 1.0 - (discontinuities / (double)sorted.Count);
        _logger.LogInformation(
            "Topological continuity: {Ratio:P2} ({Discontinuities}/{Total})",
            continuityRatio, discontinuities, sorted.Count);
        
        return discontinuities == 0;
    }
}

public record AntipodalPair
{
    public Point Location { get; init; } = null!;
    public Point AntipodalLocation { get; init; } = null!;
    public bool IsVerified { get; init; }
    public int YDifference { get; init; }
    public int ZDifference { get; init; }
    public int MDifference { get; init; }
}
```

**Acceptance Criteria**:
- ✅ Antipodal pairs correctly identified
- ✅ Topological continuity verified
- ✅ Discontinuities logged with details
- ✅ Performance: <1s for 10K constants

---

### Task 5.5: Integration with Existing Services (4 hours)

**Update atomization services to use advanced features**:

```csharp
// TextAtomizationService.cs - Add embedding storage
public async Task<IEnumerable<Constant>> AtomizeAsync(
    string text,
    bool generateEmbeddings = false,
    CancellationToken cancellationToken = default)
{
    var constants = new List<Constant>();
    
    foreach (var word in SplitIntoWords(text))
    {
        var bytes = Encoding.UTF8.GetBytes(word);
        var hash = ComputeHash(bytes);
        var coordinate = _geometryFactory.CreateCoordinate(hash, bytes, 0);
        
        var constant = new Constant
        {
            Id = Guid.NewGuid(),
            Hash = hash,
            Data = bytes,
            Coordinate = coordinate,
            Location = coordinate.ToPoint()
        };
        
        constants.Add(constant);
        
        // Generate embedding if requested
        if (generateEmbeddings)
        {
            var vector = await _embeddingService.GenerateEmbeddingAsync(word, cancellationToken);
            var embedding = Embedding.CreateFromVector(
                constant.Id,
                "sentence-transformers/all-MiniLM-L6-v2",
                vector,
                _quantizationService);
            
            await _embeddingRepository.AddAsync(embedding);
        }
    }
    
    return constants;
}
```

**Acceptance Criteria**:
- ✅ Atomization services optionally generate embeddings
- ✅ Embeddings stored alongside constants
- ✅ No performance degradation when embeddings disabled

---

## Integration Tests

### Test Scenario 1: Embedding Storage and Retrieval
```csharp
[Fact]
public async Task CreateEmbedding_StoresAndRetrievesCorrectly()
{
    // Arrange
    var constant = CreateTestConstant();
    var vector = new float[] { 0.1f, -0.5f, 0.9f, 0.0f };
    
    // Act
    var embedding = Embedding.CreateFromVector(
        constant.Id,
        "test-model",
        vector,
        _quantizationService);
    
    await _embeddingRepository.AddAsync(embedding);
    var retrieved = await _embeddingRepository.GetByIdAsync(embedding.Id);
    
    // Assert
    Assert.NotNull(retrieved);
    var retrievedVector = retrieved.ToVector();
    for (int i = 0; i < vector.Length; i++)
    {
        Assert.InRange(retrievedVector[i], vector[i] - 0.01f, vector[i] + 0.01f);
    }
}
```

### Test Scenario 2: Convex Hull Creation
```csharp
[Fact]
public void CreateConvexHull_ForDocumentConstants_ReturnsValidGeometry()
{
    // Arrange
    var constants = CreateTestConstants(count: 100);
    
    // Act
    var contentGeometry = ContentGeometry.CreateConvexHull(constants);
    
    // Assert
    Assert.Equal("Polygon", contentGeometry.ShapeType);
    Assert.True(contentGeometry.Area > 0);
    Assert.Equal(100, contentGeometry.ConstituentsCount);
}
```

---

## Performance Benchmarks

| Operation | Target | Measured |
|-----------|--------|----------|
| Store 768D embedding | <10ms | TBD |
| Convex hull (10K points) | <100ms | TBD |
| Antipodal pair search | <100ms | TBD |
| Continuity verification (10K) | <1s | TBD |

---

## Acceptance Criteria (Phase Exit)

- ✅ Embeddings stored as MULTIPOINTZM
- ✅ Neural parameters stored in POINTZM space
- ✅ Content represented as geometric objects
- ✅ Borsuk-Ulam antipodal analysis working
- ✅ Topological continuity verification working
- ✅ TopologyAnalysisService operational
- ✅ All tests passing (>80% coverage)
- ✅ Performance benchmarks met

---

**Next Phase**: [PHASE6_TESTING.md](./PHASE6_TESTING.md) - Comprehensive testing strategy

**Status**: 📋 Ready for implementation after Phase 4

**Last Updated**: December 4, 2025
