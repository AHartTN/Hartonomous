# Hartonomous Architecture

**Universal Geometric Knowledge Substrate** - Everything is geometry in 4D POINTZM space.

## Core Paradigm

Hartonomous is NOT a database with spatial features. It IS a geometric knowledge substrate where:

- **Everything is Geometry**: Atoms (POINTZM), compositions (LINESTRINGZM), relationships (LINESTRINGZM), boundaries (POLYGONZM)
- **Everything is Atomizable**: Content decomposes recursively at every level (bytes → chars → words → files → projects → repositories)
- **Everything is Deduplicated**: Hash256 content-addressing with 99%+ deduplication after warm-up
- **Geometry Encodes Semantics**: Position IS meaning, distance IS similarity, no external ML needed
- **Database IS the Model**: PostGIS spatial queries directly answer knowledge questions

## 4D POINTZM Space

- **X**: Hilbert index from SHA-256 hash (content-addressable spatial position)
- **Y**: Shannon entropy, 21-bit quantized [0, 2,097,151] (information density)
- **Z**: Kolmogorov complexity (gzip ratio), 21-bit (compressibility)
- **M**: Graph connectivity/reference count, 21-bit (usage frequency/importance)

## Emergent Intelligence

- **Modality discovered through clustering** - No ContentType enum needed
- **Voronoi tessellation** = natural neighborhoods
- **Delaunay triangulation** = connectivity graph
- **MST** = most important relationships
- **A* pathfinding** = knowledge graph traversal
- **PageRank** = importance scoring

## Detailed Documentation

### Architecture & Design
- **[docs/architecture/ENTERPRISE_ARCHITECTURE.md](docs/architecture/ENTERPRISE_ARCHITECTURE.md)** - Complete solution structure and DDD patterns
- **[docs/architecture/PHASE1_4D_SPATIAL_ARCHITECTURE_DECISION.md](docs/architecture/PHASE1_4D_SPATIAL_ARCHITECTURE_DECISION.md)** - ADR for 4D geometry
- **[docs/SYSTEM_ARCHITECTURE.md](docs/SYSTEM_ARCHITECTURE.md)** - Component design and data flow
- **[docs/HILBERT_ARCHITECTURE.md](docs/HILBERT_ARCHITECTURE.md)** - Hilbert curve indexing details

### Implementation
- **[POINTZM_MASTER_IMPLEMENTATION_PLAN.md](POINTZM_MASTER_IMPLEMENTATION_PLAN.md)** - 8-phase transformation roadmap
- **[docs/implementation/](docs/implementation/)** - Phase-by-phase implementation guides

### Technical Details
- **[.github/copilot-instructions.md](.github/copilot-instructions.md)** - Comprehensive AI coding guidelines
- **[docs/api/](docs/api/)** - API reference
- **[docs/database/](docs/database/)** - Database schema and PostGIS setup
- **[docs/deployment/](docs/deployment/)** - Deployment guides

## Technology Stack

- **.NET 10** - Modern C# with latest features
- **PostgreSQL 14+ + PostGIS 3.3+** - Spatial database with POINTZM support
- **EF Core 10** - ORM with spatial types (NetTopologySuite)
- **.NET Aspire** - Orchestration and service discovery
- **Azure Arc** - Hybrid cloud deployment
- **Clean Architecture + DDD + CQRS** - Enterprise patterns

## Quick Reference

```csharp
// Everything is a POINT in 4D space
public class Constant : BaseEntity
{
    public Point Location { get; set; }  // GEOMETRY(POINTZM, 4326)
    public Hash256 Hash { get; set; }    // Content-addressable ID
    public byte[] Data { get; set; }     // Raw content
}

// Compositions are LINESTRINGZM paths through atoms
public class BPEToken : BaseEntity
{
    public LineString CompositionGeometry { get; set; }  // Path through POINTZM space
    public List<Guid> ConstantSequence { get; set; }     // Ordered atom IDs
}

// Spatial queries = Knowledge queries
var similar = await dbContext.Constants
    .OrderBy(c => c.Location.Distance(target.Location))
    .Take(10)
    .ToListAsync();  // Find 10 most similar atoms
```

## License

Copyright © 2025 Anthony Hart. All Rights Reserved.
