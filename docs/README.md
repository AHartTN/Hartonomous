# Hartonomous

**A Content-Addressable, Geometrically-Indexed Intelligence Substrate**

[![License: Proprietary](https://img.shields.io/badge/License-Proprietary-red.svg)](LICENSE)
[![Python 3.14+](https://img.shields.io/badge/python-3.14+-blue.svg)](https://www.python.org/downloads/)
[![PostgreSQL 16](https://img.shields.io/badge/PostgreSQL-16-blue.svg)](https://www.postgresql.org/)
[![Neo4j 5.15](https://img.shields.io/badge/Neo4j-5.15-green.svg)](https://neo4j.com/)

---

## What is Hartonomous?

Hartonomous is a **novel data architecture** that treats all information—text, code, images, audio, model weights—as **atomic, content-addressable units** positioned in **3D semantic space**. Unlike traditional AI systems that operate as "black boxes," Hartonomous provides **full traceability** for every piece of information, from its raw form to its conceptual relationships.

### Key Insight

**Everything is atoms.** A character, a number, a pixel, a model weight—all are atoms (?64 bytes) stored exactly once, globally deduplicated, and positioned geometrically based on semantic meaning.

```python
# Traditional approach: Store embeddings as opaque vectors
embedding = model.encode("machine learning")  # [0.023, -0.145, ..., 0.234] ? 1998 floats

# Hartonomous approach: Decompose to content-addressed atoms
atoms = atomize("machine learning")  # Each character ? unique atom
position = compute_spatial_position(atoms)  # Position based on semantic neighbors
```

**Result**: No black boxes. Every inference traceable. Continuous learning. CPU-first efficiency.

---

## Core Architecture

Hartonomous is built on three PostgreSQL tables that store **all knowledge**:

### 1. `atom` — The Periodic Table of Intelligence

Every unique value ?64 bytes exists exactly once, content-addressed by SHA-256.

```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,          -- SHA-256 (deduplication)
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),  -- ?64 bytes
    canonical_text TEXT,                         -- Cached for text atoms
    spatial_key GEOMETRY(POINTZ, 0),             -- 3D semantic space position
    reference_count BIGINT NOT NULL DEFAULT 1,   -- Atomic mass
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb  -- Modality, model, tenant, etc.
);
```

**Examples:**
- Character `'A'` ? 1 atom (referenced millions of times)
- Float `0.017` ? 1 atom (referenced billions of times)
- Word `"machine"` ? 1 atom (composed of character atoms)

### 2. `atom_composition` — Molecular Structure

Complex data is **hierarchical composition** of simpler atoms.

```sql
-- "Hello" is composed of 5 character atoms
Parent="Hello" ? Components=['H', 'e', 'l', 'l', 'o'] (sequence 0,1,2,3,4)

-- A 1998D embedding is composed of 1998 float atoms
Parent=EmbeddingID ? Components=[Float0, Float1, ..., Float1997] (sparse, only non-zero)
```

### 3. `atom_relation` — Semantic Forces

Atoms connect via **typed, weighted relationships** forming a knowledge graph.

```sql
-- Semantic association (Hebbian learning)
Source='machine', Target='learning', Type='semantic_pair', Weight=0.95

-- Provenance tracking
Source=QueryAtom, Target=ResultAtom, Type='produced_result', Weight=1.0
```

---

## Why Hartonomous?

| Traditional AI | Hartonomous |
|----------------|-------------|
| ? Black box inference | ? Full provenance graph |
| ? Separate training phase | ? Ingestion = learning |
| ? GPU-dependent | ? CPU-first (spatial queries) |
| ? Frozen models | ? Continuously updating |
| ? Opaque embeddings | ? Geometric semantic space |
| ? Model-specific APIs | ? Unified multi-modal queries |
| ? Expensive retraining | ? Zero training cost |

---

## Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **Primary Database** | PostgreSQL 16 + PostGIS 3.4 | Atom storage, spatial indexing |
| **Graph Database** | Neo4j 5.15 | Provenance tracking |
| **API Framework** | FastAPI + Uvicorn | HTTP REST endpoints |
| **Code Atomizer** | C# (.NET) + Roslyn/Tree-sitter | Source code parsing |
| **Compression** | Python (multi-layer sparse encoding) | Atom size reduction |
| **Spatial Indexing** | Hilbert curves (3D) | Semantic proximity search |
| **Containerization** | Docker + Docker Compose | Deployment |

---

## Quick Start

### Prerequisites

- **Docker** 24.0+ with Docker Compose
- **8GB RAM** minimum (16GB recommended)
- **10GB disk space** for initial setup

### 1. Clone Repository

```bash
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous
```

### 2. Start Services

```bash
docker-compose up -d
```

**Services started:**
- PostgreSQL (`:5432`) — Atom database
- Neo4j (`:7474`, `:7687`) — Provenance graph
- FastAPI (`:8000`) — Ingestion/query API
- Code Atomizer (`:8080`) — C# microservice
- Caddy (`:80`, `:443`) — Reverse proxy

### 3. Verify Health

```bash
curl http://localhost/v1/health
# {"status":"healthy","database":"connected","neo4j":"connected"}
```

### 4. Ingest First Document

```bash
curl -X POST http://localhost/v1/ingest \
  -H "Content-Type: application/json" \
  -d '{"content": "Machine learning is amazing", "modality": "text"}'
```

### 5. Query Semantic Space

```bash
curl http://localhost/v1/query?text=learning&limit=10
# Returns atoms near "learning" in semantic space
```

**See:** [Getting Started Guide](docs/getting-started/quick-start.md) for detailed walkthrough.

---

## Documentation

### ?? Core Documentation

- **[Vision](docs/VISION.md)** — What we're building and why
- **[Architecture](docs/ARCHITECTURE.md)** — System design and component interaction
- **[Getting Started](docs/getting-started/README.md)** — Installation, tutorials, first steps

### ?? Concepts

- **[Atoms](docs/concepts/atoms.md)** — The fundamental unit (?64 bytes)
- **[Compositions](docs/concepts/compositions.md)** — Hierarchical structures
- **[Relations](docs/concepts/relations.md)** — Semantic connections
- **[Spatial Semantics](docs/concepts/spatial-semantics.md)** — Hilbert curves, landmark projection
- **[Compression](docs/concepts/compression.md)** — Multi-layer encoding
- **[Provenance](docs/concepts/provenance.md)** — Neo4j tracking

### ??? Architecture

- **[Database Schema](docs/architecture/database-schema.md)** — PostgreSQL tables, indexes
- **[Ingestion Flow](docs/architecture/ingestion-flow.md)** — How data enters the system
- **[Query Flow](docs/architecture/query-flow.md)** — How queries are executed
- **[Code Atomizer](docs/architecture/code-atomizer.md)** — C# microservice design

### ?? Deployment

- **[Local Docker](docs/deployment/local-docker.md)** — Docker Compose setup
- **[Production Docker](docs/deployment/production-docker.md)** — Production configuration
- **[Azure Deployment](docs/deployment/azure-deployment.md)** — Cloud deployment guide
- **[Performance Tuning](docs/deployment/performance-tuning.md)** — Optimization strategies

### ??? Development

- **[Project Structure](docs/development/project-structure.md)** — Code organization
- **[Contributing](docs/development/contributing.md)** — How to contribute
- **[Testing](docs/development/testing.md)** — Test strategy
- **[Troubleshooting](docs/development/troubleshooting.md)** — Common issues

### ?? API Reference

- **[Ingestion API](docs/api-reference/ingestion.md)** — Ingest endpoints
- **[Query API](docs/api-reference/query.md)** — Search endpoints
- **[Code Atomizer API](docs/api-reference/code-atomizer.md)** — Code parsing endpoints

---

## Project Structure

```
Hartonomous/
??? api/                          # FastAPI application
?   ??? main.py                   # API entry point
?   ??? ingestion_endpoints.py    # Ingestion routes
?
??? src/
?   ??? core/
?   ?   ??? atomization/          # Atom creation & management
?   ?   ??? compression/          # Multi-layer compression
?   ?   ??? landmark/             # Spatial positioning
?   ?   ??? spatial/              # Hilbert curves, geometric ops
?   ?
?   ??? Hartonomous.CodeAtomizer.Api/  # C# microservice (Roslyn/Tree-sitter)
?       ??? Controllers/
?       ??? Services/
?
??? schema/                       # PostgreSQL DDL
?   ??? core/tables/              # Atom, composition, relation
?   ??? functions/                # Stored procedures
?   ??? indexes/                  # Spatial + B-tree indexes
?   ??? extensions/               # PostGIS, PL/Python, etc.
?
??? docker/                       # Container configuration
?   ??? Dockerfile.api            # FastAPI image
?   ??? docker-compose.yml        # Service orchestration
?
??? tests/                        # Test suite
?   ??? test_atomization.py
?   ??? test_compression.py
?   ??? test_spatial.py
?
??? docs/                         # Documentation
    ??? concepts/
    ??? architecture/
    ??? getting-started/
    ??? api-reference/
```

---

## Key Features

### ?? Content-Addressable Storage
Every atom is identified by `SHA-256(atomic_value)`, ensuring **global deduplication**. The character `'A'` exists once, regardless of how many times it appears.

### ?? Geometric Semantics
Atoms positioned in **3D space** where **proximity = similarity**. No embeddings needed—position emerges from semantic neighbor averaging.

```sql
-- Find atoms near "cat"
SELECT canonical_text, ST_Distance(spatial_key, 
    (SELECT spatial_key FROM atom WHERE canonical_text = 'cat')
) AS distance
FROM atom
ORDER BY distance ASC
LIMIT 10;

-- Results: cat, kitten, feline, dog, meow, whiskers, ...
```

### ?? Hierarchical Composition
Complex structures built from simpler atoms:
- **Text**: Sentence ? Words ? Characters
- **Code**: Function ? Statements ? Tokens
- **Images**: Image ? Pixels ? RGB values
- **Models**: Layer ? Weights ? Floats

### ?? Provenance Tracking
Every atom derivation recorded in **Neo4j graph**. Full audit trail from raw input to final result.

```cypher
// Trace how atom was created
MATCH path = (source:Atom)-[:DERIVED_FROM*]->(origin:Atom)
WHERE source.atom_id = $target_atom_id
RETURN path
```

### ?? Continuous Learning
No separate training phase. **Ingestion = Learning**. Every document updates the semantic space immediately.

### ?? Multi-Layer Compression
Atoms compressed via:
1. **Sparse encoding** (only non-zero values)
2. **Delta encoding** (differences from neighbors)
3. **Bit packing** (minimal bit representation)

Result: **10-100x compression** for common patterns.

### ?? Multi-Modal Unity
Text, images, audio, code—all occupy the **same semantic space**. Cross-modal queries work natively:

```sql
-- Text query returns images
SELECT metadata->>'image_url' AS image
FROM atom
WHERE metadata->>'modality' = 'image'
  AND ST_Distance(spatial_key, (SELECT spatial_key FROM atom WHERE canonical_text = 'sunset')) < 0.5;
```

---

## Performance Characteristics

| Operation | Latency | Notes |
|-----------|---------|-------|
| **Atom lookup** (by hash) | <1ms | B-tree index |
| **Spatial query** (K-nearest) | <10ms | GiST/R-tree index |
| **Composition retrieval** | <5ms | Indexed by parent |
| **Provenance traversal** | <50ms | Neo4j Cypher query |
| **Ingestion** (1KB text) | <100ms | Includes atomization + positioning |

**Scalability**: Tested to 100M atoms on single PostgreSQL instance. Horizontal scaling via sharding (tenant-based).

---

## Roadmap

### Current Version: v0.3 (Development)

**Implemented:**
- ? Core atom storage (PostgreSQL + PostGIS)
- ? Spatial indexing (Hilbert curves)
- ? Multi-layer compression
- ? Text atomization
- ? Code atomization (C# via Roslyn/Tree-sitter)
- ? Provenance tracking (Neo4j)
- ? FastAPI ingestion endpoints

**In Progress:**
- ?? Image atomization (pixel-level)
- ?? Audio atomization (waveform sampling)
- ?? Query optimization (caching, materialized views)
- ?? Multi-tenant support

**Planned (v0.4+):**
- ?? GPU acceleration (PG-Strom integration)
- ?? Distributed graph (Neo4j clustering)
- ?? Geometric truth detection (lie clustering)
- ?? OODA loop (self-optimization)
- ?? Multi-model integration (GPT/DALL-E/Llama atoms)

---

## Contributing

We welcome contributions! See [CONTRIBUTING.md](docs/development/contributing.md) for:
- Code style guidelines
- Development setup
- Pull request process
- Testing requirements

**Quick Start:**
1. Fork repository
2. Create feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open Pull Request

---

## License

**Proprietary License**. All rights reserved.

Copyright © 2025 Anthony Hart. Unauthorized copying, distribution, or modification prohibited.

See [LICENSE](LICENSE) for full terms.

---

## Contact

- **Author**: Anthony Hart
- **GitHub**: [@AHartTN](https://github.com/AHartTN)
- **Repository**: [Hartonomous](https://github.com/AHartTN/Hartonomous)

---

## Acknowledgments

**Technologies:**
- [PostgreSQL](https://www.postgresql.org/) — Rock-solid transactional database
- [PostGIS](https://postgis.net/) — Spatial indexing magic
- [Neo4j](https://neo4j.com/) — Graph database excellence
- [FastAPI](https://fastapi.tiangolo.com/) — Modern Python API framework
- [Roslyn](https://github.com/dotnet/roslyn) — C# compiler platform
- [Tree-sitter](https://tree-sitter.github.io/tree-sitter/) — Incremental parsing

**Inspiration:**
- Content-addressable storage (Git, IPFS)
- Spatial indexing (Hilbert curves, R-trees)
- Hebbian learning ("neurons that fire together, wire together")
- Laplace's Demon (deterministic observability)

---

**"It's atoms all the way down."**

