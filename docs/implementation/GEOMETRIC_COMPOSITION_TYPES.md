# Geometric Composition Types: Complete Integration

**Status**: Core architectural component  
**Dependencies**: Phase 1 (POINTZM foundation)  
**Integration**: Phases 2, 3, 5  

---

## Overview

While POINTZM represents **atomic constants**, compositional structures require **complex geometric types**:

| Geometry Type | Use Case | Example |
|---------------|----------|---------|
| **POINTZM** | Atomic constant | Single byte/atom in 4D space |
| **MULTIPOINTZM** | Unordered collections | Embedding vectors, atom sets |
| **LINESTRINGZM** | Ordered sequences | BPE tokens, compositions, paths |
| **POLYGONZM** | Bounded regions | Document boundaries, content hulls |
| **MULTILINESTRINGZM** | Parallel sequences | Neural network weight matrices |
| **MULTIPOLYGONZM** | Disjoint regions | Multi-document boundaries |
| **GEOMETRYCOLLECTIONZM** | Hierarchical structures | Complex compositions, nested content |

---

## 1. MULTIPOINTZM: Unordered Collections

### Use Cases

**1.1 Embedding Vectors**
- Each dimension → POINTZM(dimension_index, embedding_value, 0, 0)
- 768-dim BERT embedding → MULTIPOINTZM with 768 points
- Enables geometric similarity: ST_Distance between embedding geometries

**1.2 Atom Sets (Unordered)**
- Document as set of atoms without sequence
- Bag-of-words representation
- Enables set operations: ST_Intersection, ST_Union

**1.3 Cluster Centroids**
- K-means cluster centers as MULTIPOINTZM
- Each point = cluster centroid in 4D space

### Entity Model

```csharp
public class Embedding : BaseEntity
{
    public Guid ConstantId { get; private set; }
    
    /// <summary>Embedding vector as MULTIPOINTZM (one point per dimension)</summary>
    public MultiPoint VectorGeometry { get; private set; } = null!;
    
    /// <summary>Dimensionality (e.g., 768 for BERT)</summary>
    public int Dimensions { get; private set; }
    
    /// <summary>Model that generated embedding (e.g., "BERT", "GPT")</summary>
    public string ModelName { get; private set; } = null!;
    
    /// <summary>Timestamp of embedding generation</summary>
    public DateTime GeneratedAt { get; private set; }
    
    // Navigation
    public Constant Constant { get; private set; } = null!;
    
    public static Embedding Create(Guid constantId, float[] vector, string modelName)
    {
        if (vector.Length == 0)
            throw new ArgumentException("Vector cannot be empty", nameof(vector));
        
        // Convert float[] to MULTIPOINTZM
        var points = vector.Select((value, index) => new Point(
            index,           // X: dimension index
            value,           // Y: embedding value
            0,               // Z: reserved
            0                // M: reserved
        )).ToArray();
        
        var multiPoint = new MultiPoint(points) { SRID = 4326 };
        
        return new Embedding
        {
            Id = Guid.NewGuid(),
            ConstantId = constantId,
            VectorGeometry = multiPoint,
            Dimensions = vector.Length,
            ModelName = modelName,
            GeneratedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>Extract float array from MULTIPOINTZM</summary>
    public float[] ToVector()
    {
        return VectorGeometry.Geometries
            .Cast<Point>()
            .OrderBy(p => p.X) // Dimension index
            .Select(p => (float)p.Y) // Embedding value
            .ToArray();
    }
    
    /// <summary>Cosine similarity using ST_Distance</summary>
    public double CosineSimilarity(Embedding other)
    {
        // PostGIS distance in high-dimensional space approximates cosine
        double distance = VectorGeometry.Distance(other.VectorGeometry);
        return 1.0 - (distance / Math.Sqrt(Dimensions));
    }
}
```

### EF Configuration

```csharp
public class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.ToTable("embeddings");
        
        builder.Property(e => e.VectorGeometry)
            .HasColumnName("vector_geometry")
            .HasColumnType("geometry(MultiPointZM, 4326)")
            .IsRequired();
        
        // Index for k-NN similarity search
        builder.HasIndex(e => e.VectorGeometry)
            .HasMethod("gist")
            .HasDatabaseName("idx_embeddings_vector_gist");
        
        // Foreign key to constant
        builder.HasOne(e => e.Constant)
            .WithMany()
            .HasForeignKey(e => e.ConstantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### Migration

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "embeddings",
        columns: table => new
        {
            id = table.Column<Guid>(nullable: false),
            constant_id = table.Column<Guid>(nullable: false),
            vector_geometry = table.Column<MultiPoint>(type: "geometry(MultiPointZM, 4326)", nullable: false),
            dimensions = table.Column<int>(nullable: false),
            model_name = table.Column<string>(maxLength: 100, nullable: false),
            generated_at = table.Column<DateTime>(nullable: false),
            created_at = table.Column<DateTime>(nullable: false),
            updated_at = table.Column<DateTime>(nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey("pk_embeddings", x => x.id);
            table.ForeignKey("fk_embeddings_constants", x => x.constant_id, "constants", "id", onDelete: ReferentialAction.Cascade);
        });
    
    migrationBuilder.CreateIndex(
        name: "idx_embeddings_vector_gist",
        table: "embeddings",
        column: "vector_geometry")
        .Annotation("Npgsql:IndexMethod", "gist");
}
```

### Queries

```csharp
// Find k nearest embeddings (semantic similarity)
public async Task<List<Embedding>> FindSimilarAsync(Embedding target, int k)
{
    return await _dbContext.Embeddings
        .OrderBy(e => e.VectorGeometry.Distance(target.VectorGeometry))
        .Take(k)
        .ToListAsync();
}

// Find embeddings within similarity threshold
public async Task<List<Embedding>> FindWithinDistanceAsync(Embedding target, double threshold)
{
    return await _dbContext.Embeddings
        .Where(e => e.VectorGeometry.Distance(target.VectorGeometry) < threshold)
        .ToListAsync();
}
```

---

## 2. LINESTRINGZM: Ordered Sequences

### Use Cases

**2.1 BPE Token Compositions**
- Ordered sequence of atoms forming a token
- Preserves spatial order along Hilbert curve
- Each coordinate = (X, Y, Z, M) of constituent atom

**2.2 Content Reconstruction Paths**
- A* pathfinding result as LINESTRINGZM
- Shortest path through 4D space
- Each vertex = waypoint constant

**2.3 Temporal Sequences**
- Time-series data as ordered atoms
- Each atom = observation at timestamp

### Entity Model (Update BPEToken)

```csharp
public class BPEToken : AggregateRoot
{
    public int TokenId { get; private set; }
    public Hash256 Hash { get; private set; } = null!;
    
    /// <summary>Ordered sequence of constant IDs</summary>
    public List<Guid> ConstantSequence { get; private set; } = new();
    
    /// <summary>LINESTRINGZM geometry representing composition path</summary>
    public LineString CompositionGeometry { get; private set; } = null!;
    
    /// <summary>Total geometric length of composition path</summary>
    public double PathLength { get; private set; }
    
    public int TotalSize { get; private set; }
    public int SequenceLength { get; private set; }
    public long Frequency { get; private set; }
    public int MergeLevel { get; private set; }
    public bool IsActive { get; private set; }
    
    // Navigation
    public ICollection<Constant> Constants { get; private set; } = new List<Constant>();
    
    public static BPEToken Create(
        int tokenId,
        List<Constant> orderedConstants,
        long frequency = 1)
    {
        if (!orderedConstants.Any())
            throw new ArgumentException("Must have at least one constant", nameof(orderedConstants));
        
        // Build LINESTRINGZM from ordered constants
        var coordinates = orderedConstants.Select(c => new CoordinateZM(
            c.Location.X,
            c.Location.Y,
            c.Location.Z,
            c.Location.M
        )).ToArray();
        
        var lineString = new LineString(coordinates) { SRID = 4326 };
        
        // Compute combined hash
        var allData = orderedConstants.SelectMany(c => c.Data).ToArray();
        var hash = Hash256.Compute(allData);
        
        return new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = hash,
            ConstantSequence = orderedConstants.Select(c => c.Id).ToList(),
            CompositionGeometry = lineString,
            PathLength = lineString.Length,
            TotalSize = orderedConstants.Sum(c => c.Data.Length),
            SequenceLength = orderedConstants.Count,
            Frequency = frequency,
            MergeLevel = 0,
            IsActive = true
        };
    }
    
    /// <summary>Get start point of composition</summary>
    public Point StartPoint => CompositionGeometry.StartPoint;
    
    /// <summary>Get end point of composition</summary>
    public Point EndPoint => CompositionGeometry.EndPoint;
    
    /// <summary>Check if token contains a specific constant</summary>
    public bool Contains(Guid constantId)
    {
        return ConstantSequence.Contains(constantId);
    }
}
```

### EF Configuration (Update)

```csharp
public class BPETokenConfiguration : IEntityTypeConfiguration<BPEToken>
{
    public void Configure(EntityTypeBuilder<BPEToken> builder)
    {
        builder.ToTable("bpe_tokens");
        
        // NEW: LINESTRINGZM geometry
        builder.Property(t => t.CompositionGeometry)
            .HasColumnName("composition_geometry")
            .HasColumnType("geometry(LineStringZM, 4326)")
            .IsRequired();
        
        builder.Property(t => t.PathLength)
            .HasColumnName("path_length")
            .IsRequired();
        
        // Index for spatial queries
        builder.HasIndex(t => t.CompositionGeometry)
            .HasMethod("gist")
            .HasDatabaseName("idx_bpe_tokens_composition_gist");
        
        // Existing fields...
        builder.Property(t => t.TokenId).IsRequired();
        builder.HasIndex(t => t.TokenId).IsUnique();
        
        builder.Property(t => t.ConstantSequence)
            .HasColumnName("constant_sequence")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions)null!)!)
            .IsRequired();
    }
}
```

### Migration

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Add new columns to existing bpe_tokens table
    migrationBuilder.AddColumn<LineString>(
        name: "composition_geometry",
        table: "bpe_tokens",
        type: "geometry(LineStringZM, 4326)",
        nullable: true); // Nullable initially for migration
    
    migrationBuilder.AddColumn<double>(
        name: "path_length",
        table: "bpe_tokens",
        nullable: true);
    
    // Populate geometries from existing constant sequences
    migrationBuilder.Sql(@"
        UPDATE bpe_tokens t
        SET composition_geometry = (
            SELECT ST_MakeLine(
                ARRAY_AGG(c.location ORDER BY idx.ordinality)
            )
            FROM UNNEST(t.constant_sequence) WITH ORDINALITY idx(constant_id, ordinality)
            JOIN constants c ON c.id = idx.constant_id
        ),
        path_length = ST_Length((
            SELECT ST_MakeLine(
                ARRAY_AGG(c.location ORDER BY idx.ordinality)
            )
            FROM UNNEST(t.constant_sequence) WITH ORDINALITY idx(constant_id, ordinality)
            JOIN constants c ON c.id = idx.constant_id
        ))
        WHERE composition_geometry IS NULL;
    ");
    
    // Make required after population
    migrationBuilder.AlterColumn<LineString>(
        name: "composition_geometry",
        table: "bpe_tokens",
        type: "geometry(LineStringZM, 4326)",
        nullable: false);
    
    migrationBuilder.AlterColumn<double>(
        name: "path_length",
        table: "bpe_tokens",
        nullable: false);
    
    // Create spatial index
    migrationBuilder.CreateIndex(
        name: "idx_bpe_tokens_composition_gist",
        table: "bpe_tokens",
        column: "composition_geometry")
        .Annotation("Npgsql:IndexMethod", "gist");
}
```

### Queries

```csharp
// Find tokens containing a specific constant
public async Task<List<BPEToken>> FindTokensContainingAsync(Guid constantId)
{
    var constant = await _dbContext.Constants.FindAsync(constantId);
    
    return await _dbContext.BPETokens
        .Where(t => t.CompositionGeometry.Contains(constant.Location))
        .ToListAsync();
}

// Find tokens within geometric distance of point
public async Task<List<BPEToken>> FindTokensNearAsync(Point location, double distance)
{
    return await _dbContext.BPETokens
        .Where(t => t.CompositionGeometry.Distance(location) < distance)
        .OrderBy(t => t.CompositionGeometry.Distance(location))
        .ToListAsync();
}

// Find tokens that intersect a region
public async Task<List<BPEToken>> FindTokensInRegionAsync(Polygon region)
{
    return await _dbContext.BPETokens
        .Where(t => t.CompositionGeometry.Intersects(region))
        .ToListAsync();
}

// Find longest composition paths
public async Task<List<BPEToken>> FindLongestPathsAsync(int limit)
{
    return await _dbContext.BPETokens
        .OrderByDescending(t => t.PathLength)
        .Take(limit)
        .ToListAsync();
}
```

---

## 3. POLYGONZM: Bounded Regions

### Use Cases

**3.1 Document Boundaries**
- Convex hull of all atoms in a document
- Represents document "footprint" in 4D space
- Enables document similarity: ST_Area(ST_Intersection(hull1, hull2))

**3.2 Content Categories**
- Voronoi cells defining natural content regions
- Each cell = spatial region for content type
- Enables classification by containment

**3.3 Query Regions**
- User-defined spatial filters
- "Find all atoms within this 4D region"

### Entity Model

```csharp
public class ContentBoundary : BaseEntity
{
    /// <summary>Foreign key to content ingestion</summary>
    public Guid ContentIngestionId { get; private set; }
    
    /// <summary>Convex hull containing all atoms in content</summary>
    public Polygon BoundaryGeometry { get; private set; } = null!;
    
    /// <summary>4D "area" of boundary (hypersurface measure)</summary>
    public double BoundaryArea { get; private set; }
    
    /// <summary>Number of atoms within boundary</summary>
    public int AtomCount { get; private set; }
    
    /// <summary>Density: atoms per unit area</summary>
    public double Density { get; private set; }
    
    /// <summary>Centroid of boundary</summary>
    public Point Centroid { get; private set; } = null!;
    
    /// <summary>Timestamp of boundary computation</summary>
    public DateTime ComputedAt { get; private set; }
    
    // Navigation
    public ContentIngestion ContentIngestion { get; private set; } = null!;
    
    public static ContentBoundary Create(
        Guid contentIngestionId,
        List<Constant> atoms)
    {
        if (atoms.Count < 3)
            throw new ArgumentException("Need at least 3 atoms for convex hull", nameof(atoms));
        
        // Collect all atom locations
        var multiPoint = new MultiPoint(
            atoms.Select(a => a.Location).ToArray()
        ) { SRID = 4326 };
        
        // Compute convex hull
        var convexHull = (Polygon)multiPoint.ConvexHull();
        convexHull.SRID = 4326;
        
        var centroid = convexHull.Centroid;
        centroid.SRID = 4326;
        
        return new ContentBoundary
        {
            Id = Guid.NewGuid(),
            ContentIngestionId = contentIngestionId,
            BoundaryGeometry = convexHull,
            BoundaryArea = convexHull.Area,
            AtomCount = atoms.Count,
            Density = atoms.Count / convexHull.Area,
            Centroid = centroid,
            ComputedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>Check if a constant is within this boundary</summary>
    public bool Contains(Constant constant)
    {
        return BoundaryGeometry.Contains(constant.Location);
    }
    
    /// <summary>Compute overlap with another boundary (Jaccard similarity)</summary>
    public double OverlapWith(ContentBoundary other)
    {
        var intersection = BoundaryGeometry.Intersection(other.BoundaryGeometry);
        var union = BoundaryGeometry.Union(other.BoundaryGeometry);
        
        return intersection.Area / union.Area;
    }
}
```

### EF Configuration

```csharp
public class ContentBoundaryConfiguration : IEntityTypeConfiguration<ContentBoundary>
{
    public void Configure(EntityTypeBuilder<ContentBoundary> builder)
    {
        builder.ToTable("content_boundaries");
        
        builder.Property(b => b.BoundaryGeometry)
            .HasColumnName("boundary_geometry")
            .HasColumnType("geometry(PolygonZM, 4326)")
            .IsRequired();
        
        builder.Property(b => b.Centroid)
            .HasColumnName("centroid")
            .HasColumnType("geometry(PointZM, 4326)")
            .IsRequired();
        
        // Spatial indexes
        builder.HasIndex(b => b.BoundaryGeometry)
            .HasMethod("gist")
            .HasDatabaseName("idx_content_boundaries_geometry_gist");
        
        builder.HasIndex(b => b.Centroid)
            .HasMethod("gist")
            .HasDatabaseName("idx_content_boundaries_centroid_gist");
        
        // Foreign key
        builder.HasOne(b => b.ContentIngestion)
            .WithOne()
            .HasForeignKey<ContentBoundary>(b => b.ContentIngestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### Queries

```csharp
// Find similar documents by boundary overlap
public async Task<List<ContentBoundary>> FindSimilarDocumentsAsync(
    Guid contentIngestionId,
    double minOverlap = 0.5,
    int limit = 10)
{
    var target = await _dbContext.ContentBoundaries
        .FirstAsync(b => b.ContentIngestionId == contentIngestionId);
    
    return await _dbContext.ContentBoundaries
        .Where(b => b.Id != target.Id)
        .Where(b => b.BoundaryGeometry.Intersects(target.BoundaryGeometry))
        .OrderByDescending(b => 
            b.BoundaryGeometry.Intersection(target.BoundaryGeometry).Area)
        .Take(limit)
        .ToListAsync();
}

// Find all documents containing a specific point
public async Task<List<ContentBoundary>> FindDocumentsContainingAsync(Point location)
{
    return await _dbContext.ContentBoundaries
        .Where(b => b.BoundaryGeometry.Contains(location))
        .ToListAsync();
}

// Find densest documents
public async Task<List<ContentBoundary>> FindDensestAsync(int limit)
{
    return await _dbContext.ContentBoundaries
        .OrderByDescending(b => b.Density)
        .Take(limit)
        .ToListAsync();
}
```

---

## 4. MULTILINESTRINGZM: Parallel Sequences

### Use Cases

**4.1 Neural Network Weights**
- Each layer's weight matrix as MULTILINESTRINGZM
- Each linestring = neuron's weight vector
- Enables geometric model versioning

**4.2 Parallel Compositions**
- Multiple BPE paths between same start/end
- Alternative reconstruction routes
- Enables path diversity analysis

### Entity Model

```csharp
public class NeuralNetworkLayer : BaseEntity
{
    /// <summary>Foreign key to model</summary>
    public Guid ModelId { get; private set; }
    
    /// <summary>Layer index (0-based)</summary>
    public int LayerIndex { get; private set; }
    
    /// <summary>Layer name (e.g., "dense_1")</summary>
    public string LayerName { get; private set; } = null!;
    
    /// <summary>Weight matrix as MULTILINESTRINGZM (one line per neuron)</summary>
    public MultiLineString WeightGeometry { get; private set; } = null!;
    
    /// <summary>Number of neurons in layer</summary>
    public int NeuronCount { get; private set; }
    
    /// <summary>Input dimensionality</summary>
    public int InputDim { get; private set; }
    
    /// <summary>Bias vector as LINESTRINGZM</summary>
    public LineString? BiasGeometry { get; private set; }
    
    /// <summary>Timestamp of layer creation</summary>
    public DateTime CreatedAt { get; private set; }
    
    public static NeuralNetworkLayer Create(
        Guid modelId,
        int layerIndex,
        string layerName,
        float[,] weights,
        float[]? biases = null)
    {
        int neurons = weights.GetLength(0);
        int inputDim = weights.GetLength(1);
        
        // Convert weight matrix to MULTILINESTRINGZM
        var lineStrings = new List<LineString>();
        for (int n = 0; n < neurons; n++)
        {
            var coords = new CoordinateZM[inputDim];
            for (int i = 0; i < inputDim; i++)
            {
                coords[i] = new CoordinateZM(
                    i,              // X: input index
                    weights[n, i],  // Y: weight value
                    n,              // Z: neuron index
                    0               // M: reserved
                );
            }
            lineStrings.Add(new LineString(coords) { SRID = 4326 });
        }
        
        var multiLineString = new MultiLineString(lineStrings.ToArray()) { SRID = 4326 };
        
        // Convert bias vector to LINESTRINGZM
        LineString? biasGeometry = null;
        if (biases != null)
        {
            var biasCoords = biases.Select((b, i) => new CoordinateZM(
                i,  // X: neuron index
                b,  // Y: bias value
                0,  // Z: reserved
                0   // M: reserved
            )).ToArray();
            biasGeometry = new LineString(biasCoords) { SRID = 4326 };
        }
        
        return new NeuralNetworkLayer
        {
            Id = Guid.NewGuid(),
            ModelId = modelId,
            LayerIndex = layerIndex,
            LayerName = layerName,
            WeightGeometry = multiLineString,
            NeuronCount = neurons,
            InputDim = inputDim,
            BiasGeometry = biasGeometry,
            CreatedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>Extract weight matrix from geometry</summary>
    public float[,] GetWeights()
    {
        var weights = new float[NeuronCount, InputDim];
        
        for (int n = 0; n < NeuronCount && n < WeightGeometry.Count; n++)
        {
            var lineString = (LineString)WeightGeometry[n];
            for (int i = 0; i < lineString.Count && i < InputDim; i++)
            {
                weights[n, i] = (float)lineString.Coordinates[i].Y;
            }
        }
        
        return weights;
    }
}
```

---

## 5. GEOMETRYCOLLECTIONZM: Hierarchical Structures

### Use Cases

**5.1 Complex Documents**
- Combine multiple geometry types
- Document = GEOMETRYCOLLECTION(boundary:POLYGON, atoms:MULTIPOINT, sequences:MULTILINESTRING)

**5.2 Hierarchical Compositions**
- Parent-child relationships
- Nested structures (chapters → sections → paragraphs)

### Entity Model

```csharp
public class HierarchicalContent : BaseEntity
{
    public Guid ContentIngestionId { get; private set; }
    
    /// <summary>Complete geometric representation</summary>
    public GeometryCollection CompleteGeometry { get; private set; } = null!;
    
    /// <summary>Hierarchy level (0 = document, 1 = chapter, 2 = section...)</summary>
    public int HierarchyLevel { get; private set; }
    
    /// <summary>Parent content ID (null for root)</summary>
    public Guid? ParentId { get; private set; }
    
    /// <summary>Child contents</summary>
    public ICollection<HierarchicalContent> Children { get; private set; } = new List<HierarchicalContent>();
}
```

---

## Integration Checklist

### Phase 1 Updates (Core Geometry)
- [ ] Add MULTIPOINTZM to SpatialCoordinate value object
- [ ] Add LINESTRINGZM to SpatialCoordinate value object
- [ ] Add POLYGONZM to SpatialCoordinate value object
- [ ] Test multi-geometry creation and conversion

### Phase 2 Updates (BPE Redesign)
- [x] Already specified: LINESTRINGZM for compositions
- [ ] Add CompositionGeometry field to BPEToken entity
- [ ] Update BPEService to create LINESTRINGZM
- [ ] Add spatial queries for token finding

### Phase 3 Updates (Universal Properties)
- [ ] Extend UniversalGeometryFactory for multi-geometries
- [ ] Add geometry type parameter to factory methods

### Phase 5 Updates (Advanced Features)
- [x] Already specified: MULTIPOINTZM for embeddings
- [ ] Add Embedding entity with VectorGeometry
- [ ] Add ContentBoundary entity with convex hull
- [ ] Add NeuralNetworkLayer entity with weight geometry
- [ ] Add HierarchicalContent entity

### New Migrations Required
- [ ] `AddCompositionGeometryToBPETokens`
- [ ] `CreateEmbeddingsTable`
- [ ] `CreateContentBoundariesTable`
- [ ] `CreateNeuralNetworkLayersTable`
- [ ] `CreateHierarchicalContentTable`

### Query Extension Methods
- [ ] Add LINESTRINGZM query extensions
- [ ] Add POLYGONZM query extensions
- [ ] Add MULTIPOINTZM query extensions
- [ ] Add GEOMETRYCOLLECTIONZM query extensions

---

## Performance Considerations

### Index Strategy per Geometry Type

| Geometry | Index Method | Query Pattern | Cost |
|----------|-------------|---------------|------|
| POINTZM | GIST (4D R-tree) | k-NN, within radius | O(log N) |
| MULTIPOINTZM | GIST | Set similarity | O(log N) |
| LINESTRINGZM | GIST | Path intersection, containment | O(log N) |
| POLYGONZM | GIST | Region overlap, containment | O(log N) |
| MULTILINESTRINGZM | GIST | Multi-path queries | O(log N) |
| GEOMETRYCOLLECTIONZM | GIST | Complex spatial queries | O(log N) |

### Storage Overhead

| Geometry | Storage | Example |
|----------|---------|---------|
| POINTZM | 32 bytes | Single atom |
| MULTIPOINTZM (768-dim) | ~25 KB | BERT embedding |
| LINESTRINGZM (10 atoms) | ~320 bytes | BPE token |
| POLYGONZM (100 vertices) | ~3.2 KB | Document boundary |
| MULTILINESTRINGZM (1000 neurons) | ~320 KB | Neural network layer |

---

## Testing Strategy

### Unit Tests
- [ ] MULTIPOINTZM creation from float arrays
- [ ] LINESTRINGZM creation from ordered constants
- [ ] POLYGONZM convex hull computation
- [ ] Geometry conversion methods
- [ ] Cosine similarity for embeddings
- [ ] Overlap calculation for boundaries

### Integration Tests
- [ ] Save/retrieve MULTIPOINTZM from database
- [ ] Spatial queries on LINESTRINGZM
- [ ] Boundary overlap queries
- [ ] Multi-geometry GIST index usage

### Performance Tests
- [ ] k-NN on 10K MULTIPOINTZM embeddings (<100ms)
- [ ] Token finding by containment (<50ms)
- [ ] Boundary overlap on 1K documents (<200ms)

---

**Status**: Complete geometric type system specification

**Next Steps**:
1. Update Phase 1 to include multi-geometry support
2. Update Phase 2 with BPEToken.CompositionGeometry implementation
3. Update Phase 5 with Embedding/ContentBoundary entities
4. Create migrations for all new tables
5. Implement spatial query extensions

**Last Updated**: December 4, 2025
