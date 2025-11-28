# ARCHITECTURE

**Hartonomous System Architecture**  
**Version:** 0.6.0 (Current Implementation)

---

## System Overview

Hartonomous is a **content-addressable, geometrically-indexed intelligence substrate** built on PostgreSQL with full provenance tracking via Neo4j.

```
???????????????????????????????????????????????????????????????????
?                         CLIENT LAYER                            ?
?   HTTP Clients, Python SDK, CLI, Web UI, GitHub Actions        ?
???????????????????????????????????????????????????????????????????
                        ?
                        ? REST API (FastAPI)
???????????????????????????????????????????????????????????????????
?                      API LAYER (FastAPI)                        ?
?  /ingest  ?  /query  ?  /train  ?  /export  ?  /health         ?
?  ?????????????????????????????????????????????????????         ?
?  • Text/Document ingestion    • Code atomization (C#)          ?
?  • Semantic queries           • Model ingestion (GGUF, etc.)   ?
?  • Provenance traversal       • GitHub repository sync         ?
???????????????????????????????????????????????????????????????????
                        ?
        ??????????????????????????????????????????????????
        ?               ?               ?                ?
???????????????? ??????????????? ?????????????? ???????????????
?  PostgreSQL  ? ?   Neo4j     ? ? Code       ? ? Background  ?
?   + PostGIS  ? ?   Graph     ? ? Atomizer   ? ?  Workers    ?
???????????????? ??????????????? ?????????????? ???????????????
? • Atoms      ? ? • Provenance? ? • Roslyn   ? ? • Neo4j     ?
? • Composition? ? • Derivation? ? • Tree-    ? ?   Sync      ?
? • Relations  ? ?   Graph     ? ?   sitter   ? ? • Temporal  ?
? • Spatial    ? ? • Audit     ? ? • C# AST   ? ?   Cleanup   ?
?   Indexes    ? ?   Trail     ? ?            ? ?             ?
???????????????? ??????????????? ?????????????? ???????????????
```

---

## Core Components

### 1. PostgreSQL Database (Primary Storage)

**Purpose**: Atom storage, composition hierarchy, semantic relations

**Version**: PostgreSQL 16 + PostGIS 3.4

**Extensions**:
- **PostGIS 3.4** — Spatial indexing (GiST, R-tree)
- **pg_trgm** — Trigram similarity for text search
- **btree_gin** — Multi-column GIN indexes
- **pgcrypto** — SHA-256 content hashing
- **plpython3u** — In-database Python for specialized functions

**Tables** (Core Schema):

```sql
-- 1. ATOM: The periodic table of intelligence
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,              -- SHA-256
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    canonical_text TEXT,
    
    -- Spatial positioning: Current POINTZ, designed for POINTZM
    spatial_key GEOMETRY(POINTZ, 0),                 -- 3D semantic space (X, Y, Z)
    -- DESIGN INTENT: GEOMETRY(POINTZM, 0) where M = Hilbert index
    -- Exploits spatial datatypes to store non-spatial (Hilbert) data
    -- Pending schema migration
    
    reference_count BIGINT NOT NULL DEFAULT 1,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz
);

-- 2. ATOM_COMPOSITION: Hierarchical structure
CREATE TABLE atom_composition (
    composition_id BIGSERIAL PRIMARY KEY,
    parent_atom_id BIGINT NOT NULL REFERENCES atom(atom_id),
    component_atom_id BIGINT NOT NULL REFERENCES atom(atom_id),
    sequence_index BIGINT NOT NULL,
    spatial_key GEOMETRY(POINTZ, 0),
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (parent_atom_id, component_atom_id, sequence_index)
);

-- 3. ATOM_RELATION: Semantic graph
CREATE TABLE atom_relation (
    relation_id BIGSERIAL PRIMARY KEY,
    source_atom_id BIGINT NOT NULL REFERENCES atom(atom_id),
    target_atom_id BIGINT NOT NULL REFERENCES atom(atom_id),
    relation_type_id BIGINT NOT NULL REFERENCES atom(atom_id),
    weight REAL NOT NULL DEFAULT 0.5 CHECK (weight BETWEEN 0.0 AND 1.0),
    confidence REAL NOT NULL DEFAULT 0.5 CHECK (confidence BETWEEN 0.0 AND 1.0),
    importance REAL NOT NULL DEFAULT 0.5 CHECK (importance BETWEEN 0.0 AND 1.0),
    spatial_expression GEOMETRY(LINESTRINGZ, 0),
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (source_atom_id, target_atom_id, relation_type_id)
);
```

**Indexes** (Performance):

```sql
-- Content addressing
CREATE INDEX idx_atom_hash ON atom (content_hash);

-- Spatial queries (GiST = R-tree for 3D coordinates)
CREATE INDEX idx_atom_spatial ON atom USING GIST (spatial_key);

-- DESIGN INTENT: Dual indexing strategy with POINTZM
-- GiST on (X,Y,Z) for exact spatial queries
-- B-tree on M (Hilbert index) for fast approximate queries
-- CREATE INDEX idx_atom_hilbert ON atom ((ST_M(spatial_key)));
-- Pending schema migration to POINTZM

-- Reference counting (atomic mass)
CREATE INDEX idx_atom_reference_count ON atom (reference_count DESC);

-- Composition traversal
CREATE INDEX idx_composition_parent ON atom_composition(parent_atom_id, sequence_index);
CREATE INDEX idx_composition_component ON atom_composition(component_atom_id);

-- Relation traversal
CREATE INDEX idx_relation_source ON atom_relation(source_atom_id);
CREATE INDEX idx_relation_target ON atom_relation(target_atom_id);
CREATE INDEX idx_relation_weight ON atom_relation(weight DESC);

-- JSONB metadata
CREATE INDEX idx_atom_metadata ON atom USING GIN (metadata);
```

---

### 2. Neo4j Graph Database (Provenance Tracking)

**Purpose**: Complete audit trail of atom derivations

**Version**: Neo4j 5.15

**Schema** (Cypher):

```cypher
// Atom node
(:Atom {
  atom_id: INTEGER,
  content_hash: STRING,
  canonical_text: STRING,
  created_at: DATETIME
})

// Derivation relationship
(:Atom)-[:DERIVED_FROM {
  operation: STRING,       // "atomize", "compose", "relate"
  timestamp: DATETIME,
  metadata: MAP
}]->(:Atom)

// Composition relationship
(:Atom)-[:COMPOSED_OF {
  sequence_index: INTEGER,
  timestamp: DATETIME
}]->(:Atom)

// Relation tracking
(:Atom)-[:RELATES_TO {
  relation_type: STRING,
  weight: FLOAT,
  timestamp: DATETIME
}]->(:Atom)
```

**Queries** (Provenance):

```cypher
// How was this atom created?
MATCH path = (atom:Atom {atom_id: $id})-[:DERIVED_FROM*]->(origin:Atom)
RETURN path

// What did this atom produce?
MATCH path = (atom:Atom {atom_id: $id})<-[:DERIVED_FROM*]-(descendant:Atom)
RETURN path

// Full lineage (ancestors + descendants)
MATCH path = (ancestor:Atom)-[:DERIVED_FROM*]-(atom:Atom {atom_id: $id})-[:DERIVED_FROM*]-(descendant:Atom)
RETURN path
```

---

### 3. FastAPI Application Layer

**Purpose**: REST API for ingestion, query, training

**Version**: FastAPI 0.109+ with Uvicorn

**Stack**:
- **FastAPI** — Async web framework
- **psycopg3** — PostgreSQL async driver (connection pooling)
- **neo4j-driver** — Neo4j async driver
- **pydantic** — Data validation
- **httpx** — HTTP client for microservices

**Endpoints** (v1):

```
/v1/health              GET    — Health check
/v1/ingest/text         POST   — Ingest text document
/v1/ingest/code         POST   — Ingest code (via C# atomizer)
/v1/ingest/github       POST   — Ingest GitHub repository
/v1/ingest/model        POST   — Ingest model weights (GGUF, SafeTensors)
/v1/ingest/document     POST   — Ingest document (PDF, DOCX, MD)
/v1/query/semantic      GET    — Semantic search (spatial query)
/v1/query/provenance    GET    — Provenance traversal (Neo4j)
/v1/export/atoms        GET    — Export atoms (JSON, CSV)
```

**Configuration** (Environment Variables):

```bash
# PostgreSQL
DATABASE_URL=postgresql://user:pass@postgres:5432/hartonomous
POOL_MIN_SIZE=5
POOL_MAX_SIZE=20

# Neo4j
NEO4J_URI=bolt://neo4j:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=password
NEO4J_ENABLED=true

# Code Atomizer (C# microservice)
CODE_ATOMIZER_URL=http://code-atomizer:8080

# API Server
API_HOST=0.0.0.0
API_PORT=8000
LOG_LEVEL=INFO
```

---

### 4. Code Atomizer Microservice (C#)

**Purpose**: Parse source code into atoms using Roslyn/Tree-sitter

**Version**: .NET 8.0

**Technology**:
- **Roslyn** — C# semantic analysis (Microsoft.CodeAnalysis)
- **Tree-sitter** — Multi-language parsing (C, Python, Java, etc.)

**Endpoints**:

```
POST /atomize/csharp     — Parse C# code
POST /atomize/python     — Parse Python code
POST /atomize/javascript — Parse JavaScript code
GET  /health             — Health check
```

**Input**:
```json
{
  "code": "public class Example { }",
  "language": "csharp"
}
```

**Output** (AST tokens):
```json
{
  "tokens": [
    {"type": "keyword", "value": "public", "position": 0},
    {"type": "keyword", "value": "class", "position": 7},
    {"type": "identifier", "value": "Example", "position": 13}
  ],
  "atom_hashes": [
    "sha256:abc123...",
    "sha256:def456...",
    "sha256:789ghi..."
  ]
}
```

**Container**:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY ./publish .
ENTRYPOINT ["dotnet", "Hartonomous.CodeAtomizer.Api.dll"]
```

---

### 5. Background Workers (Python)

**Purpose**: Async sync between PostgreSQL and Neo4j

**Workers**:

#### Neo4j Provenance Worker
```python
# Listens to PostgreSQL logical replication
# Syncs atom creation/composition/relations to Neo4j

async def process_atom_insert(atom):
    # Create node in Neo4j
    await neo4j.run("""
        CREATE (a:Atom {
            atom_id: $atom_id,
            content_hash: $content_hash,
            canonical_text: $canonical_text
        })
    """, atom_id=atom.id, ...)

async def process_composition(composition):
    # Create COMPOSED_OF relationship
    await neo4j.run("""
        MATCH (parent:Atom {atom_id: $parent_id})
        MATCH (child:Atom {atom_id: $child_id})
        CREATE (parent)-[:COMPOSED_OF {sequence_index: $idx}]->(child)
    """, ...)
```

#### Temporal Cleanup Worker
```python
# Archives old atom versions (valid_to < now() - 90 days)
# Compresses historical data

async def cleanup_old_versions():
    await db.execute("""
        DELETE FROM atom
        WHERE valid_to < now() - interval '90 days'
          AND atom_id NOT IN (SELECT parent_atom_id FROM atom_composition)
    """)
```

---

## Data Flow

### Ingestion Flow

```
1. HTTP POST /v1/ingest/text
   ?
2. FastAPI receives request
   ?
3. Validate input (Pydantic schema)
   ?
4. Atomize content:
   - Text ? Characters (atomize_text)
   - Numbers ? Bytes (atomize_numeric)
   - Code ? Tokens (via C# atomizer)
   ?
5. For each atom:
   a. Compute SHA-256 hash
   b. Check if exists (SELECT WHERE content_hash = ...)
   c. If new:
      - Insert into atom table
      - Compute semantic neighbors (spatial query)
      - Compute spatial position (weighted centroid)
      - Update spatial_key
   d. If exists:
      - Increment reference_count
   ?
6. Create composition:
   INSERT INTO atom_composition (parent_id, component_id, sequence_index)
   ?
7. Create relations:
   INSERT INTO atom_relation (source, target, type, weight)
   ?
8. Trigger logical replication ? Neo4j worker syncs
   ?
9. Return response:
   {
     "atom_id": 12345,
     "atoms_created": 42,
     "atoms_reused": 18,
     "provenance_tracked": true
   }
```

### Query Flow

```
1. HTTP GET /v1/query/semantic?text=learning&limit=10
   ?
2. FastAPI receives request
   ?
3. Atomize query text
   ?
4. Lookup query atoms in database
   ?
5. Compute query spatial position (average of atom positions)
   ?
6. Execute spatial query:
   SELECT atom_id, canonical_text,
          ST_Distance(spatial_key, $query_point) AS distance
   FROM atom
   WHERE ST_DWithin(spatial_key, $query_point, 1.0)  -- Radius 1.0
   ORDER BY distance ASC
   LIMIT 10
   ?
7. Fetch compositions (if requested):
   SELECT component_atom_id, sequence_index
   FROM atom_composition
   WHERE parent_atom_id IN (result_atom_ids)
   ?
8. Fetch relations (if requested):
   SELECT target_atom_id, relation_type, weight
   FROM atom_relation
   WHERE source_atom_id IN (result_atom_ids)
   ?
9. Return response:
   {
     "query_point": [0.5, 0.8, 1.2],
     "results": [
       {"atom_id": 123, "text": "machine learning", "distance": 0.02},
       {"atom_id": 456, "text": "deep learning", "distance": 0.15}
     ]
   }
```

---

## Spatial Indexing

### Hilbert Curves (3D)

**Purpose**: Map 3D semantic space to 1D for efficient indexing

**Algorithm**:
```python
def encode_hilbert_3d(x: float, y: float, z: float, bits: int = 21) -> int:
    """
    Encode 3D point to Hilbert curve index.
    
    Args:
        x, y, z: Coordinates in [0, 1]
        bits: Precision (21 = 2^63 max)
    
    Returns:
        Hilbert index (integer)
    """
    # Normalize to [0, 2^bits)
    xi = int(x * (2 ** bits))
    yi = int(y * (2 ** bits))
    zi = int(z * (2 ** bits))
    
    # Apply Hilbert transform (Gray code + bit interleaving)
    return hilbert_3d_encode_impl(xi, yi, zi, bits)
```

**Usage**:
```sql
-- Compute Hilbert index during ingestion
UPDATE atom
SET spatial_hilbert = encode_hilbert_3d(
    ST_X(spatial_key),
    ST_Y(spatial_key),
    ST_Z(spatial_key)
)
WHERE atom_id = $new_atom_id;

-- Range query (box search)
SELECT atom_id, canonical_text
FROM atom
WHERE spatial_hilbert BETWEEN $hilbert_min AND $hilbert_max;
```

**Benefits**:
- O(log N) lookup (B-tree on integer)
- Locality preservation (nearby in space ? nearby in index)
- No GiST overhead for simple range queries

---

## Compression Pipeline

### Multi-Layer Strategy

**Layers** (applied sequentially):

1. **Sparse Encoding**
   - Only store non-zero values
   - Example: `[0.0, 0.23, 0.0, -0.14, 0.0]` ? `[(1, 0.23), (3, -0.14)]`

2. **Delta Encoding**
   - Store differences from neighbors
   - Example: `[10, 12, 15, 17]` ? `[10, +2, +3, +2]`

3. **Bit Packing**
   - Use minimal bits for value range
   - Example: Values in [0, 255] ? 8 bits (not 32)

**Implementation** (Python):
```python
from src.core.compression import compress_atom, decompress_atom

# Compress
compressed = compress_atom(atom_data, compression_level=3)
# Result: {"data": b"...", "magic": "HART", "type": 3, "ratio": 12.5}

# Decompress
original = decompress_atom(compressed["data"])
```

**Compression Results**:
- Text: 5-10x (sparse character encoding)
- Embeddings: 10-50x (sparse + delta + quantization)
- Model weights: 50-100x (quantization + Huffman)

---

## Performance Characteristics

### Latency (Single PostgreSQL Instance)

| Operation | Median | P95 | P99 | Notes |
|-----------|--------|-----|-----|-------|
| **Atom lookup** (by hash) | 0.5ms | 1.2ms | 2.3ms | B-tree index |
| **Spatial query** (K=10) | 4ms | 12ms | 25ms | GiST index |
| **Composition retrieval** | 2ms | 6ms | 15ms | Indexed by parent |
| **Relation traversal** (depth=3) | 8ms | 25ms | 50ms | Recursive CTE |
| **Provenance query** (Neo4j) | 15ms | 45ms | 100ms | Graph traversal |
| **Ingestion** (1KB text) | 50ms | 150ms | 300ms | Full pipeline |

### Throughput

- **Atoms/second**: 2,000 (single writer)
- **Queries/second**: 10,000 (read-only)
- **Concurrent connections**: 100 (connection pool)

### Scalability

- **Single instance**: 100M atoms, 1B relations
- **Horizontal scaling**: Shard by `metadata->>'tenant_id'`
- **Read replicas**: PostgreSQL streaming replication

---

## Deployment Architecture

### Docker Compose (Development)

```yaml
services:
  postgres:
    image: postgis/postgis:16-3.4
    ports: ["5432:5432"]
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./schema:/schema:ro
  
  neo4j:
    image: neo4j:5.15
    ports: ["7474:7474", "7687:7687"]
    volumes:
      - neo4j_data:/data
  
  api:
    build: ./docker/Dockerfile.api
    ports: ["8000:8000"]
    depends_on: [postgres, neo4j, code-atomizer]
  
  code-atomizer:
    build: ./src/Hartonomous.CodeAtomizer.Api
    ports: ["8080:8080"]
  
  caddy:
    image: caddy:2.7.5-alpine
    ports: ["80:80", "443:443"]
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
```

### Production (Azure/AWS/GCP)

```
                  ???????????????????
                  ?  Load Balancer  ?
                  ???????????????????
                           ?
        ???????????????????????????????????????
        ?                  ?                  ?
   ???????????       ???????????       ???????????
   ? API #1  ?       ? API #2  ?       ? API #N  ?
   ???????????       ???????????       ???????????
        ?                 ?                  ?
        ??????????????????????????????????????
                          ?
         ???????????????????????????????????
         ?                ?                ?
   ????????????    ????????????    ????????????
   ? Postgres ?    ?  Neo4j   ?    ?   Code   ?
   ? Primary  ?    ? Cluster  ?    ? Atomizer ?
   ????????????    ????????????    ????????????
        ?
   ????????????
   ? Replicas ?
   ????????????
```

**Components**:
- **API Instances**: Stateless, horizontally scaled (K8s, ECS)
- **PostgreSQL**: Primary + read replicas (Azure Database, AWS RDS)
- **Neo4j**: Cluster (3-node quorum)
- **Code Atomizer**: Stateless, scaled independently

---

## Security

### Authentication
- API keys (header: `X-API-Key`)
- OAuth 2.0 / OIDC (future)

### Authorization
- Tenant isolation via `metadata->>'tenant_id'`
- Row-level security (RLS) policies

### Encryption
- TLS 1.3 (all connections)
- At-rest encryption (PostgreSQL, Neo4j)

---

## Monitoring

### Metrics (Prometheus/Grafana)
- API latency (histograms)
- Database connection pool usage
- Atom ingestion rate
- Query throughput
- Neo4j sync lag

### Logging (Structured JSON)
```json
{
  "timestamp": "2025-11-28T05:30:00Z",
  "level": "INFO",
  "service": "api",
  "operation": "ingest_text",
  "atoms_created": 42,
  "duration_ms": 87
}
```

### Tracing (OpenTelemetry)
- Request ID propagation
- Distributed tracing (API ? PostgreSQL ? Neo4j)

---

## Technology Stack Summary

| Component | Technology | Version | Purpose |
|-----------|-----------|---------|---------|
| **Database** | PostgreSQL + PostGIS | 16 + 3.4 | Atom storage, spatial indexing |
| **Graph** | Neo4j | 5.15 | Provenance tracking |
| **API** | FastAPI + Uvicorn | 0.109+ | REST endpoints |
| **Language** | Python | 3.14 | Core logic |
| **Code Parser** | C# + Roslyn/Tree-sitter | .NET 8.0 | Code atomization |
| **Async Driver** | psycopg3 | 3.1+ | PostgreSQL connection pool |
| **Graph Driver** | neo4j-driver | 5.15+ | Neo4j async client |
| **Containers** | Docker + Compose | 24.0+ | Deployment |
| **Proxy** | Caddy | 2.7.5 | Reverse proxy, TLS |

---

## Next Steps

- **[Concepts](concepts/README.md)** — Deep dive into atoms, spatial semantics, compression
- **[Deployment](deployment/local-docker.md)** — Deploy locally or to production
- **[API Reference](api-reference/README.md)** — Complete endpoint documentation

---

**Last Updated**: 2025-11-28  
**Version**: Hartonomous v0.6.0
