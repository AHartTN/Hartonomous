# Hartonomous AI Coding Instructions

## Core Philosophy: Universal Geometric Knowledge Substrate

**Hartonomous is NOT a database with spatial features - it IS a geometric knowledge substrate where everything exists as geometry in 4D POINTZM space.**

### The Fundamental Architecture

**Everything is Geometry:**
- **Atoms** (bytes, characters, words, sentences, files, projects) = POINTZM points
- **Compositions** (sequences, hierarchies) = LINESTRINGZM paths through atoms
- **Relationships** (references, calls, connections) = LINESTRINGZM between atoms
- **Documents/Boundaries** (files, repos, knowledge domains) = POLYGONZM regions

**Everything is Atomizable:**
Content decomposes recursively at EVERY level:
```
Repository → Projects → Files → AST Nodes → Statements → Tokens → Words → Characters → Bytes
     ↓          ↓         ↓          ↓            ↓           ↓        ↓          ↓          ↓
  POINT     POINT     POINT      POINT        POINT       POINT    POINT      POINT      POINT
```
**Each level is an atom pointing to constituent atoms. Each level deduplicated by Hash256.**

**Everything is Deduplicated:**
- "Hello World" appearing 1 trillion times = 108 bytes storage (atoms + counter)
- After warm-up (10K+ documents), 99%+ deduplication rate
- Ingestion becomes: hash lookup → counter increment (milliseconds)
- Common patterns stored ONCE: `{ get; set; }`, `public static void`, `Lorem ipsum...`

**Geometry Encodes Semantics:**
- Position in POINTZM space = intrinsic meaning
- Distance = similarity (no embeddings needed for basic queries)
- Intersection = relationship
- Containment = composition
- Clustering = emergent modality (text vs images vs code discovered via Voronoi/k-means)

**The Database IS the Model:**
- PostGIS spatial queries directly answer knowledge questions
- Voronoi tessellation = natural neighborhoods/clusters
- Delaunay triangulation = connectivity graph
- MST = most important relationships
- A* pathfinding = knowledge graph traversal
- No external ML needed for basic reasoning

### The POINTZM Space (4D Coordinate System)

**Target State** (see `POINTZM_MASTER_IMPLEMENTATION_PLAN.md`):
- **X**: Hilbert index from SHA-256 hash (content-addressable spatial position)
- **Y**: Shannon entropy, quantized to 21-bit [0, 2,097,151] (information density)
- **Z**: Kolmogorov complexity via gzip ratio, 21-bit (compressibility)
- **M**: Graph connectivity/reference count, 21-bit (importance/usage frequency)

**Emergent Modality** - No `ContentType` enum:
- Low Y + Low Z → Images/video (random pixels, already compressed)
- High Y + High Z → Source code (structured, highly compressible patterns)
- Medium Y + Medium Z → Natural language text
- Clusters discovered through k-means/DBSCAN on YZM subspace

### Project Structure
- **Hartonomous.Core** - Pure domain logic, zero dependencies. CQRS with MediatR. All entities inherit `BaseEntity` (audit, soft delete, domain events).
- **Hartonomous.Data** - EF Core 10 + PostgreSQL/PostGIS. POINTZM geometry, Hilbert indexing, Voronoi/Delaunay/MST algorithms.
- **Hartonomous.Infrastructure** - QuantizationService (21-bit universal properties), caching (Redis), current user context.
- **Hartonomous.API** - REST API. Zero Trust (Entra ID), rate limiting (token bucket), comprehensive health checks.
- **Hartonomous.Worker** - Background BPE learning using Hilbert-sorted sequences, gap detection, MST vocabulary construction.
- **Hartonomous.AppHost** - .NET Aspire orchestration. Service discovery, OpenTelemetry, centralized configuration.

## Multi-Level Atomic Deduplication

**Every level of composition is itself an atom, deduplicated by Hash256:**

```
Byte 0x48          → POINT(hilbert_hash, entropy, compressibility, connectivity)
  ↓ stored once
Character "H"      → POINT(hilbert_hash, ...) points to [Byte(0x48)]
  ↓ stored once
Word "Hello"       → POINT(hilbert_hash, ...) points to [H, e, l, l, o]
  ↓ stored once
Sentence "Hello World" → POINT(...) points to [Hello, " ", World]
  ↓ stored once
Method HelloWorld()    → POINT(...) points to [declaration, body, statements]
  ↓ stored once
File Program.cs        → POINT(...) points to [using_statements, classes]
  ↓ stored once
Project MyApp          → POINT(...) points to [Program.cs, Utils.cs, ...]
  ↓ stored once
Repository             → POINT(...) points to [MyApp, Tests, Docs]
```

**Deduplication Cascade Example - "Hello World":**

**First Ingestion:**
- Create 11 character atoms: `H`, `e`, `l`, `l`, `o`, ` `, `W`, `o`, `r`, `l`, `d`
- Create 2 word atoms: `"Hello"` → [H,e,l,l,o], `"World"` → [W,o,r,l,d]
- Create 1 sentence atom: `"Hello World"` → [Hello, " ", World]
- **Storage**: ~100 bytes (atoms) + metadata

**Next 999,999,999,999 Ingestions:**
```sql
-- Hash lookup (B-tree index, O(log n))
SELECT id, reference_count, frequency FROM constants 
WHERE hash IN ($hello_hash, $world_hash, $sentence_hash);

-- Found all 3 atoms → Batch increment counters
UPDATE constants 
SET reference_count = reference_count + 999999999999,
    frequency = frequency + 999999999999
WHERE id IN ($hello_id, $world_id, $sentence_id);
```
- **Additional storage**: 8 bytes (counter) × 3 atoms = 24 bytes
- **Compression ratio**: 11 TB → 124 bytes = **88,709,677,419:1**

**Why Ingestion Stays Fast (99%+ Deduplication After Warm-Up):**

**Cold Start (First 1,000 documents):**
- Creating new atoms frequently
- 30-40% deduplication rate
- Building indexes

**Warm State (After 10,000 documents):**
- Common patterns exist: `public static void`, `{ get; set; }`, `Lorem ipsum...`
- 90-95% deduplication rate
- Most operations: hash lookup + counter increment

**Hot State (After 1M+ documents):**
- 99%+ deduplication rate
- Ingesting entire new codebase:
  - Compute hashes (deterministic, fast)
  - Batch query: `WHERE hash IN (...)` → 99%+ hits
  - Batch increment counters (in-place update)
  - Insert only 1% new atoms
- **Total time: Milliseconds** for documents that would be megabytes traditionally

**Common Patterns Stored ONCE:**
- Programming: `{ get; set; }`, `public static void Main`, `import React from`, `console.log()`
- Natural language: `"the"`, `"and"`, `"of"`, `"Lorem ipsum dolor sit amet..."`
- Code patterns: Factory methods, CRUD operations, try-catch blocks
- Documentation: Standard sections, legal disclaimers, boilerplate

## Compositional Hierarchy as Geometry

**Three geometric types encode all relationships:**

### Atoms (POINTZM) - Individual entities
```csharp
public class Constant : BaseEntity
{
    public Point Location { get; set; }  // GEOMETRY(POINTZM, 4326)
    // Location.X = Hilbert index from Hash256
    // Location.Y = Shannon entropy (21-bit quantized)
    // Location.Z = Kolmogorov complexity (gzip ratio, 21-bit)
    // Location.M = Graph connectivity (reference count, 21-bit)
    
    public Hash256 Hash { get; private set; }  // Content address
    public byte[] Data { get; private set; }   // Raw content
    public long ReferenceCount { get; private set; }  // Deduplication counter
}
```

### Compositions (LINESTRINGZM) - Ordered sequences through space
```csharp
public class BPEToken : BaseEntity
{
    public LineString CompositionGeometry { get; set; }  // GEOMETRY(LINESTRINGZM, 4326)
    // Path through constituent atoms in POINTZM space
    // Gaps in Hilbert sequence (X coordinate) = compression boundaries
    
    public List<Guid> ConstantSequence { get; private set; }  // Ordered atom IDs
    public long Frequency { get; private set; }  // Usage count
}
```

**Example - "Hello World" as LINESTRINGZM:**
```sql
-- Sentence composition = path through word atoms
SELECT ST_MakeLine(ARRAY[
    (SELECT location FROM constants WHERE hash = $hello_hash),
    (SELECT location FROM constants WHERE hash = $space_hash),
    (SELECT location FROM constants WHERE hash = $world_hash)
]) as sentence_geometry;

-- Result: LINESTRING(
--   POINTZM(hilbert_hello, entropy_hello, compress_hello, refs_hello),
--   POINTZM(hilbert_space, entropy_space, compress_space, refs_space),
--   POINTZM(hilbert_world, entropy_world, compress_world, refs_world)
-- )
```

### Boundaries (POLYGONZM) - Convex hulls around content
```csharp
public class ContentIngestion : BaseEntity
{
    public Polygon? BoundaryGeometry { get; set; }  // GEOMETRY(POLYGONZM, 4326)
    // Convex hull around all constituent atoms
    
    public List<Guid> ConstantIds { get; private set; }  // Atoms in this content
}
```

**File-as-Atom Concept:**
- Source file `Program.cs` is itself an atom with POINTZM coordinates
- Its `BoundaryGeometry` = convex hull around all constituent atoms (using statements, classes, methods)
- Its `ConstantSequence` = ordered list pointing to class atoms
- Each class atom points to method atoms
- Each method atom points to statement atoms
- **Recursive decomposition to arbitrary depth**

## Relationship Geometry

**Relationships are geometric objects connecting atoms in POINTZM space:**

### References as LINESTRINGZM
```csharp
// Method A calls Method B
var callRelationship = new BPEToken
{
    CompositionGeometry = ST_MakeLine(
        methodA.Location,  // Start point
        methodB.Location   // End point
    ),
    ConstantSequence = new List<Guid> { methodA.Id, methodB.Id }
};

// Distance in POINTZM space = semantic similarity
var semanticDistance = ST_Distance(methodA.Location, methodB.Location);
```

### Inheritance/Composition Hierarchies
```sql
-- Class inheritance chain as LINESTRINGZM
SELECT ST_MakeLine(
    ARRAY_AGG(location ORDER BY hierarchy_level)
) FROM constants
WHERE id IN (base_class_id, derived_class1_id, derived_class2_id);
```

### Knowledge Graph Relations
```csharp
// Concept A "depends on" Concept B
var dependencyRelation = new BPEToken
{
    CompositionGeometry = ST_MakeLine(conceptA.Location, conceptB.Location),
    Metadata = new { RelationType = "DependsOn" }
};
```

**Why This Works:**
- Euclidean distance in YZM subspace = semantic similarity
- Intersection of LINESTRINGZMs = shared concepts/dependencies
- Shortest path through space = knowledge graph traversal
- MST edges = most important relationships in domain

## Critical Architectural Patterns

### 1. Universal Quantization (Phase 1)

All Y/Z/M dimensions MUST be quantized to [0, 2,097,151] (21-bit) for unified comparison:

```csharp
// IQuantizationService in Infrastructure
public interface IQuantizationService
{
    int QuantizeEntropy(byte[] data);         // Shannon H(X) = -Σ p(x)log₂p(x) → [0, 2^21-1]
    int QuantizeCompressibility(byte[] data); // gzip ratio → [0, 2^21-1]
    int QuantizeConnectivity(int refCount);   // log₂(count+1) → [0, 2^21-1]
    double DequantizeValue(int quantized, double min, double max);
}
```

**Entropy Interpretation**:
- Y < 500K: Highly repetitive (images, video frames)
- Y = 500K-1.5M: Structured data (code, compressed files)
- Y > 1.5M: Random/encrypted data (high information density)

**Compressibility Interpretation**:
- Z < 500K: Already compressed (JPEG, MP3, .zip)
- Z = 500K-1.5M: Moderately compressible (HTML, source code)
- Z > 1.5M: Highly compressible (repeated patterns, text)

**Connectivity Interpretation**:
- M < 100K: Rarely referenced (specialized atoms)
- M = 100K-1M: Normal usage (common patterns)
- M > 1M: Hot atoms (fundamental constants like space, newline)

### 2. Repository + UnitOfWork Pattern (CRITICAL)

**NEVER call SaveChanges on repositories**. Always use `IUnitOfWork.SaveChangesAsync()`:

```csharp
// ❌ WRONG - No transaction control
await _repository.AddAsync(entity);
await _repository.SaveChangesAsync(); // DON'T DO THIS

// ✅ CORRECT - Single transaction, domain events dispatched
await _repository.AddAsync(entity);
await _unitOfWork.SaveChangesAsync(); // Always use UnitOfWork
```

`ApplicationDbContext.SaveChangesAsync()` handles:
- Audit field population (CreatedAt/By, UpdatedAt/By)
- Soft delete enforcement (IsDeleted check)
- Domain event dispatch via MediatR after commit
- Transaction management

### 3. BPE Algorithm - Inline Ingestion vs Background Analysis (Phase 2)

**Inline Ingestion (Fast - Milliseconds):**
```csharp
// During content ingestion - create atoms via deduplication
public async Task<List<Constant>> IngestAsync(byte[] data)
{
    // 1. Atomize content (bytes → chars → words → phrases)
    var atoms = AtomizeContent(data);
    
    // 2. Compute Hash256 for each atom
    var hashes = atoms.Select(a => SHA256.HashData(a)).ToList();
    
    // 3. Batch lookup existing atoms (B-tree index, O(log n))
    var existing = await _dbContext.Constants
        .Where(c => hashes.Contains(c.Hash))
        .ToListAsync();
    
    // 4. For existing atoms: increment counters (in-place update)
    foreach (var atom in existing)
    {
        atom.IncrementReferenceCount();
        atom.IncrementFrequency();
    }
    
    // 5. For new atoms (typically <1% after warm-up): insert
    var newAtoms = atoms.Where(a => !existing.Any(e => e.Hash == a.Hash));
    await _dbContext.Constants.AddRangeAsync(newAtoms);
    
    // 6. Single transaction commit
    await _unitOfWork.SaveChangesAsync();
    
    // Total time: Milliseconds (99%+ hash hits after warm-up)
    return existing.Concat(newAtoms).ToList();
}
```

**Background BPE Analysis (Expensive - Minutes/Hours):**
```csharp
// Scheduled off-peak (2 AM daily) - analyze ENTIRE corpus geometry
public async Task LearnVocabularyAsync()
{
    // 1. Hilbert-sort ALL constants (global spatial ordering)
    var hilbertSorted = await _dbContext.Constants
        .OrderBy(c => c.Location.X) // X = Hilbert index
        .ToListAsync(); // Could be millions/billions of points
    
    // 2. Detect compression boundaries (Hilbert gaps)
    var gaps = DetectHilbertGaps(hilbertSorted, threshold: 1000);
    
    // 3. Compute Voronoi tessellation (spatial partitioning)
    var voronoi = await ComputeVoronoiTessellation(hilbertSorted);
    // PostGIS operation on full dataset - expensive
    
    // 4. Build Delaunay triangulation (connectivity graph)
    var delaunay = ComputeDelaunayTriangulation(voronoi);
    
    // 5. Compute MST (minimum spanning tree = most important edges)
    var mst = ComputeMinimumSpanningTree(delaunay);
    // Graph algorithm on millions of edges - expensive
    
    // 6. Extract vocabulary from MST edges (frequent patterns)
    var vocabulary = LearnVocabularyFromMST(mst, minFrequency, maxVocabSize);
    
    // 7. Store compositions as LINESTRINGZM
    foreach (var pattern in vocabulary)
    {
        var token = new BPEToken {
            CompositionGeometry = new LineString(pattern.Atoms.Select(a => a.Location))
        };
        await _dbContext.BPETokens.AddAsync(token);
    }
    
    // Total time: Minutes to hours depending on corpus size
}
```

**Why Separate?**
- **Ingestion creates atoms** → Must be fast (milliseconds) for real-time use
- **BPE discovers patterns** → Can be slow (hours) because it's global analysis
- **Deduplication ensures ingestion stays fast** → 99%+ hash hits = no database writes
- **Background worker discovers geometric structure** → Voronoi/MST require full corpus
- **Atoms exist before patterns** → Can query/search immediately after ingestion
- **Patterns optimize queries** → BPE tokens are performance shortcuts, not required for correctness

**Key Insight**: Gaps in Hilbert sequence indicate natural compression boundaries. Don't merge across gaps.

### 4. Spatial Entity Conventions

```csharp
// POINTZM for single atoms
public class Constant : BaseEntity, ISpatialEntity
{
    public Point Location { get; set; } // GEOMETRY(POINTZM, 4326)
    // Location.X = Hilbert, .Y = Entropy, .Z = Compressibility, .M = Connectivity
}

// LINESTRINGZM for sequences/compositions
public class BPEToken : BaseEntity
{
    public LineString? CompositionGeometry { get; set; } // Ordered atoms with gaps
}

// MULTIPOINTZM for embeddings (768D → 256×POINTZM)
public class Embedding : BaseEntity
{
    public MultiPoint VectorGeometry { get; set; } // High-dim projection
}
```

**SRID**: Always 4326. Always specify: `new Point(x, y) { SRID = 4326 }` (SRID 0 = non-geographic).

## PostGIS Integration & Indexing Strategy (Phase 8)

### Spatial Queries = Knowledge Queries

**Critical Insight**: PostGIS geometric operations directly answer semantic questions about knowledge.

**Distance Queries → Find Similar Content:**
```csharp
// Find 10 most similar atoms to target (k-NN)
var similar = await _dbContext.Constants
    .OrderBy(c => c.Location.Distance(targetAtom.Location))
    .Take(10)
    .ToListAsync();

// SQL equivalent
SELECT * FROM constants
ORDER BY location <-> ST_MakePoint($x, $y, $z, $m)::geometry
LIMIT 10;
// <-> operator uses GIST index for fast k-NN
```

**Intersection Queries → Find Relationships:**
```csharp
// Find all atoms whose composition paths intersect target region
var related = await _dbContext.BPETokens
    .Where(t => t.CompositionGeometry.Intersects(searchRegion))
    .ToListAsync();

// Find shared concepts between two documents
var sharedConcepts = await _dbContext.Database.ExecuteSqlRawAsync(@"
    SELECT ST_Intersection(doc1.boundary_geometry, doc2.boundary_geometry)
    FROM content_ingestions doc1, content_ingestions doc2
    WHERE doc1.id = $doc1_id AND doc2.id = $doc2_id
");
```

**Containment Queries → Find Compositions:**
```csharp
// Find all atoms contained within a file's boundary
var fileAtoms = await _dbContext.Constants
    .Where(c => file.BoundaryGeometry.Contains(c.Location))
    .ToListAsync();

// Find all methods in a class
var methods = await _dbContext.BPETokens
    .Where(t => classAtom.BoundaryGeometry.Contains(t.CompositionGeometry))
    .ToListAsync();
```

**Voronoi Tessellation → Discover Clusters/Modalities:**
```csharp
// Partition POINTZM space into natural neighborhoods
var voronoi = await _dbContext.Database.ExecuteSqlRawAsync(@"
    SELECT 
        (ST_Dump(ST_VoronoiPolygons(ST_Collect(location)))).geom as cell,
        COUNT(*) as atom_count,
        AVG(ST_Y(location)) as avg_entropy,
        AVG(ST_Z(location)) as avg_compressibility
    FROM constants
    WHERE NOT is_deleted
    GROUP BY cell
    ORDER BY atom_count DESC
");

// Clusters with low Y+Z = images/video (random, compressed)
// Clusters with high Y+Z = source code (structured, compressible)
// Clusters with medium Y+Z = natural language text
```

**Delaunay Triangulation → Connectivity Graph:**
```csharp
// Build connectivity graph from spatial proximity
var delaunay = await _dbContext.Database.ExecuteSqlRawAsync(@"
    SELECT ST_DelaunayTriangles(ST_Collect(location)) as connectivity_graph
    FROM constants
");

// Edges = natural relationships between atoms
// Used for MST computation (most important relationships)
```

**A* Pathfinding → Knowledge Graph Traversal:**
```csharp
// Find shortest semantic path between two concepts
var path = await _pathfindingService.FindOptimalPathAsync(
    startAtom: conceptA,
    goalAtom: conceptB,
    heuristic: (current, goal) => {
        // Euclidean distance in YZM space
        var dy = current.Location.Y - goal.Location.Y;
        var dz = current.Location.Z - goal.Location.Z;
        var dm = current.Location.M - goal.Location.M;
        return Math.Sqrt(dy*dy + dz*dz + dm*dm);
    }
);
```

**Minimum Spanning Tree → Most Important Relationships:**
```csharp
// Compute MST from Delaunay edges
var mst = ComputeMSTFromDelaunay(delaunayEdges);

// MST edges = strongest/most important connections
// Used for BPE vocabulary (frequent patterns)
// High M dimension = high importance
```

### Hybrid Indexing for Performance

**Critical**: Use B-tree for Hilbert (X), GIST for POINTZM spatial queries:

```sql
-- B-tree for Hilbert range queries (O(log n))
CREATE INDEX CONCURRENTLY idx_constants_hilbert_btree
    ON constants USING btree ((ST_X(location)::bigint));

-- GIST for k-NN and radius queries (O(log n))
CREATE INDEX CONCURRENTLY idx_constants_location_gist
    ON constants USING gist (location);
```

**Query Pattern Selection**:
```csharp
// Use B-tree for Hilbert range (sequential scan)
var constants = await _dbContext.Constants
    .Where(c => c.Location.X >= hilbertStart && c.Location.X <= hilbertEnd)
    .ToListAsync(); // Query plan: Index Scan using btree

// Use GIST for k-NN (nearest neighbors)
var nearest = await _dbContext.Constants
    .OrderBy(c => c.Location.Distance(targetPoint))
    .Take(k)
    .ToListAsync(); // Query plan: Index Scan using gist
```

### PostGIS Geometric Algorithms (Phase 4)

```csharp
// Voronoi tessellation (natural neighborhoods)
var voronoi = await _dbContext.Database.ExecuteSqlRawAsync(@"
    SELECT ST_VoronoiPolygons(ST_Collect(location))
    FROM constants
    WHERE NOT is_deleted
");

// Delaunay triangulation (connectivity graph)
var delaunay = await _dbContext.Database.ExecuteSqlRawAsync(@"
    SELECT ST_DelaunayTriangles(ST_Collect(location))
    FROM constants
");

// Minimum Spanning Tree (BPE vocabulary)
// Compute in C# from Delaunay edges using Prim's algorithm
var mst = ComputeMSTFromDelaunay(delaunayEdges);
```

### Required PostgreSQL Extensions

```sql
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";  -- UUID generation
CREATE EXTENSION IF NOT EXISTS "postgis";     -- Spatial types/functions
CREATE EXTENSION IF NOT EXISTS "plpython3u";  -- GPU-accelerated functions
```

## Mathematical Algorithms (Phase 4)

### A* Pathfinding for Content Reconstruction

```csharp
// Find optimal path through POINTZM space to reconstruct content
var path = await _pathfindingService.FindOptimalPathAsync(
    targetId: contentId,
    maxDepth: 100,
    cancellationToken);

// Heuristic: Euclidean distance in YZM space
private double Heuristic(Constant current, Constant goal)
{
    var dy = current.Location.Y - goal.Location.Y;
    var dz = current.Location.Z - goal.Location.Z;
    var dm = current.Location.M - goal.Location.M;
    return Math.Sqrt(dy*dy + dz*dz + dm*dm);
}
```

### PageRank for Atom Importance

```csharp
// Score constants by graph connectivity (M dimension basis)
var scores = await _importanceScoringService.ComputePageRankAsync(
    dampingFactor: 0.85,
    maxIterations: 100,
    convergenceThreshold: 1e-6);

// Update M dimension with importance scores
foreach (var (constantId, score) in scores)
{
    var m = _quantizationService.QuantizeConnectivity((int)(score * 1000));
    await UpdateLocationM(constantId, m);
}
```

### Laplace Operator for Diffusion

```csharp
// Measure information flow smoothness
var laplacian = await _topologyService.ComputeLaplacianAsync();

// High Laplacian = discontinuity in YZM space = potential compression boundary
var boundaries = laplacian.Where(l => l.Value > threshold).Select(l => l.Key);
```

### Blossom/Hungarian Matching for Deduplication

```csharp
// Find near-duplicate atoms within epsilon ball
var duplicates = await _dbContext.Constants
    .Where(c => c.Location.Distance(targetPoint) < epsilon)
    .ToListAsync();

// Perfect matching to merge duplicates
var matching = ComputeMinimumWeightMatching(duplicates);
await MergeDuplicates(matching);
```

## Geometric Composition Types (Phase 5)

### Embeddings as MULTIPOINTZM

```csharp
// Store 768D embedding as 256×POINTZM (3D chunks)
public sealed class Embedding : BaseEntity
{
    private MultiPoint? _vectorGeometry;
    
    public MultiPoint VectorGeometry 
    { 
        get => _vectorGeometry ??= FromVector(Vector);
        set => _vectorGeometry = value;
    }
    
    private MultiPoint FromVector(float[] vector)
    {
        var points = new Point[vector.Length / 3];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Point(
                vector[i*3], vector[i*3+1], vector[i*3+2]) 
                { SRID = 4326 };
        }
        return new MultiPoint(points) { SRID = 4326 };
    }
}
```

### Neural Network Parameters as POINTZM

```csharp
// Store weights/biases geometrically
public sealed class NeuralParameter : BaseEntity
{
    public Point WeightLocation { get; set; } // POINTZM
    
    // X = Hilbert(hash(weight_value))
    // Y = Shannon entropy of weight distribution
    // Z = Compressibility of weight matrix
    // M = Layer depth + neuron position
}
```

### Content as Geometric Objects

```csharp
// Document = convex hull of constituent atoms
public sealed class Content : BaseEntity
{
    public Polygon ConvexHull { get; set; }      // POLYGONZM boundary
    public MultiLineString Sequences { get; set; } // MULTILINESTRINGZM internal structure
    
    // Query: "Find documents with similar geometric shape"
    var similar = await _dbContext.Contents
        .OrderBy(c => ST.HausdorffDistance(c.ConvexHull, targetHull))
        .Take(10)
        .ToListAsync();
}
```

## Build & Development Workflow

### Local Development with Aspire
```powershell
# Start all services with Aspire dashboard
dotnet run --project Hartonomous.AppHost
```
Opens dashboard at `http://localhost:15xxx` showing all services, logs, traces, metrics.

### Database Migrations
```powershell
# From Hartonomous.Data directory
dotnet ef migrations add <MigrationName> --startup-project ../Hartonomous.API
dotnet ef database update --startup-project ../Hartonomous.API
```
**Required PostgreSQL extensions**: `uuid-ossp`, `postgis`, `plpython3u`

### Build Artifacts
Centralized in `artifacts/` directory (not individual project `bin/obj`). See `Directory.Build.props` for configuration.

## Security & Authentication

### Zero Trust by Default
All API endpoints require authentication by default (`RequireAuthorization()` on controller mapping). Use `[AllowAnonymous]` to opt out.

### Authentication Uses Microsoft Entra ID
JWT Bearer tokens validated against Entra ID (formerly Azure AD). Configuration in `appsettings.json`:
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "...",
    "ClientId": "...",
    "CallbackPath": "/signin-oidc"
  }
}
```
**Development tokens**: System accepts symmetric key-signed tokens in Development environment (see `AuthenticationConfiguration.cs`). Production requires Entra ID tokens.

### Authorization Policies
Defined in `AuthenticationConfiguration.cs`:
- `AdminPolicy` - requires Admin/Administrator role + `api.admin` scope
- `UserPolicy` - requires User/Reader role + `api.read` scope  
- `WritePolicy` - requires `api.write` scope
- `ApiScopePolicy` - requires `api.access` scope

Use: `[Authorize(Policy = "AdminPolicy")]`

### Rate Limiting
Configured per-endpoint via `RateLimitingConfiguration.cs`. Uses token bucket algorithm.

### Security Headers
Automatically applied in `Program.cs`: `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Strict-Transport-Security`, etc.

## Configuration & Environment Management

### Environment Mapping (`Directory.Build.props`)
```xml
<EnvironmentName Condition="'$(Configuration)' == 'Debug'">Local</EnvironmentName>
<EnvironmentName Condition="'$(Configuration)' == 'Dev'">Development</EnvironmentName>
<EnvironmentName Condition="'$(Configuration)' == 'Staging'">Staging</EnvironmentName>
<EnvironmentName Condition="'$(Configuration)' == 'Release'">Production</EnvironmentName>
```

### Preprocessor Directives
```csharp
#if LOCAL    // Debug builds - dev tokens, verbose logging
#if DEV      // Development configuration
#if STAGING  // Staging configuration - production-like
#if PRODUCTION // Production/Release - performance optimized
```

### Namespaces Follow Folder Structure
`Hartonomous.{ProjectName}.{FolderPath}` - e.g., `Hartonomous.Core.Domain.Entities`, `Hartonomous.Data.Repositories`

## Data Access Patterns

### Soft Delete Query Filters
Global query filter in `ApplicationDbContext` automatically excludes `IsDeleted == true` entities. To include deleted entities, use `.IgnoreQueryFilters()` in LINQ queries.

### Domain Events Lifecycle
Entities raise domain events via `AddDomainEvent(IDomainEvent)`. Events dispatched after `SaveChangesAsync()` via MediatR. Clear events with `ClearDomainEvents()` post-processing.

### Audit Field Population
`ApplicationDbContext.SaveChangesAsync()` automatically populates:
- `CreatedAt`/`CreatedBy` on EntityState.Added
- `UpdatedAt`/`UpdatedBy` on EntityState.Modified
- `DeletedAt`/`DeletedBy` on soft delete (IsDeleted = true)

### Spatial Queries with PostGIS
Entities can inherit from `SpatialEntity` for geometry/geography support:
```csharp
// Use NetTopologySuite types
public Point Location { get; set; } // POINT(x y)
```
Use `SpatialQueryExtensions.cs` for k-NN, distance, containment queries.

### PL/Python GPU Functions
Located in `Hartonomous.Data/Functions/PlPython/`. Define SQL functions in C# that map to PL/Python implementations for GPU-accelerated operations (CuPy, TensorFlow, PyTorch).

## Deployment

### Azure Pipelines CI/CD
`azure-pipelines.yml` orchestrates:
1. **Build** - Compiles Core → Data → Infrastructure → ServiceDefaults → API/Worker
2. **Package** - Creates NuGet packages for main branch/tags
3. **Deploy** - Uses Azure Arc Run Command to deploy to on-premises servers

### Deployment Targets
- `hart-server` (Linux/Windows) - Production/Staging
- `hart-desktop` (Linux/Windows) - Development
- Azure-hosted VMs

### Infrastructure Setup
**Linux**: systemd services + nginx reverse proxy  
**Windows**: IIS app pools + sites

Scripts in `deploy/` directory handle infrastructure provisioning and app deployment per environment.

### Health Checks
- `/health` - All checks
- `/health/live` - Liveness (is process running?)
- `/health/ready` - Readiness (can accept traffic?)

## Testing

### Run Tests
```powershell
dotnet test --configuration Release
```
Tests should exist in `**/*Tests.csproj` projects (not yet created in structure).

## Important Patterns & Pitfalls

### Soft Delete Query Filters
Global query filter in `ApplicationDbContext` automatically excludes `IsDeleted == true` entities. To include deleted entities, use `.IgnoreQueryFilters()` in LINQ queries.

### Aspire Service Discovery
Projects registered in `AppHost.cs` get automatic service discovery via `builder.AddProject<Projects.Hartonomous_Api>("hartonomous-api")`. Use project names in configuration to reference services.

### Domain Events
Entities raise domain events via `AddDomainEvent(IDomainEvent)`. Events dispatched after `SaveChangesAsync()` via MediatR. Clear events with `ClearDomainEvents()` post-processing.

### Configuration Binding
Aspire adds `ServiceDefaults` which configures OpenTelemetry, health checks automatically. Environment-specific settings override via `appsettings.{Environment}.json` based on `Directory.Build.props` environment mapping.

### PostGIS SRID
Default SRID is 4326 (WGS 84). Always specify SRID when creating geometry: `new Point(x, y) { SRID = 4326 }`. Use `Location` property from `SpatialEntity` for consistent spatial handling.

## Common Tasks

### Add New Entity
1. Create entity in `Hartonomous.Core/Domain/Entities/` inheriting `BaseEntity` (or `SpatialEntity` for spatial data)
2. Create EF configuration in `Hartonomous.Data/Configurations/`
3. Add `DbSet<T>` to `ApplicationDbContext`
4. Create repository interface in `Hartonomous.Core/Application/Interfaces/`
5. Implement repository in `Hartonomous.Data/Repositories/`
6. Register in DI: `Hartonomous.Data/Extensions/DataLayerExtensions.cs`
7. Create migration: `dotnet ef migrations add Add{Entity} --startup-project Hartonomous.API`
8. Create controller in `Hartonomous.API/Controllers/`

### Add CQRS Handler
1. Create command/query class in `Core/Application/Commands|Queries/{Feature}/`
2. Create handler implementing `IRequestHandler<TRequest, TResponse>`
3. Inject required repositories and `IUnitOfWork`
4. For commands: Call `await _unitOfWork.SaveChangesAsync()` after repository modifications
5. Handlers auto-registered by MediatR in `AddApplicationLayer()`

### Add Background Job
Implement `BackgroundService` in `Hartonomous.Worker/Jobs/` and register in `Program.cs`.

### Add Cache Layer
Use `ICacheService` from Infrastructure. Backed by Redis in production.

## Important Files

- `Directory.Build.props` - Global MSBuild properties (versions, environment, artifacts path)
- `Hartonomous.slnx` - Solution file (XML format)
- `Hartonomous.API/Program.cs` - API startup with middleware pipeline
- `Hartonomous.Data/Context/ApplicationDbContext.cs` - EF Core DbContext
- `Hartonomous.Data/Extensions/DataLayerExtensions.cs` - Data layer DI registration

## Target Framework

All projects target **net10.0** (.NET 10).
