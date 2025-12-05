---
title: "Database Schema Reference"
author: "Hartonomous Development Team"
date: "2025-12-05"
version: "1.0"
status: "Active"
---

# Database Schema Reference

## Table of Contents
- [Tables](#tables)
  - [constants](#constants)
  - [bpe_tokens](#bpe_tokens)
  - [embeddings](#embeddings)
  - [content_ingestions](#content_ingestions)
  - [neural_parameters](#neural_parameters)
  - [image_contents](#image_contents)
- [Indexes](#indexes)
- [Custom Functions](#custom-functions)
  - [PL/pgSQL Functions](#plpgsql-functions)
  - [GPU-Accelerated Functions](#gpu-accelerated-functions)
- [Migrations](#migrations)
- [Performance Tuning](#performance-tuning)

---

## Tables

### constants

The core atomic entity table storing all content atoms in POINTZM space.

```sql
CREATE TABLE constants (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    location GEOMETRY(POINTZM, 4326) NOT NULL,
    hash BYTEA NOT NULL UNIQUE, -- SHA-256 hash (32 bytes)
    data BYTEA NOT NULL,
    reference_count BIGINT NOT NULL DEFAULT 1,
    frequency BIGINT NOT NULL DEFAULT 1,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(255),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(255),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(255),
    
    -- Constraints
    CONSTRAINT chk_reference_count CHECK (reference_count >= 0),
    CONSTRAINT chk_frequency CHECK (frequency >= 0),
    CONSTRAINT chk_location_srid CHECK (ST_SRID(location) = 4326)
);

-- Computed columns for easy access to dimensions
ALTER TABLE constants 
    ADD COLUMN hilbert_index BIGINT GENERATED ALWAYS AS (ST_X(location)::BIGINT) STORED,
    ADD COLUMN entropy INT GENERATED ALWAYS AS (ST_Y(location)::INT) STORED,
    ADD COLUMN compressibility INT GENERATED ALWAYS AS (ST_Z(location)::INT) STORED,
    ADD COLUMN connectivity INT GENERATED ALWAYS AS (ST_M(location)::INT) STORED;
```

**Key Columns**:
- `location` - POINTZM geometry storing (Hilbert, Entropy, Compressibility, Connectivity)
- `hash` - SHA-256 content hash for deduplication (32 bytes)
- `data` - Raw atom bytes
- `reference_count` - Number of compositions referencing this atom
- `frequency` - Total occurrences across all ingested content

**Spatial Properties**:
- `X` (Hilbert): Hilbert curve index from SHA-256 hash [0, 2^63-1]
- `Y` (Entropy): Shannon entropy quantized to [0, 2,097,151]
- `Z` (Compressibility): Kolmogorov complexity quantized to [0, 2,097,151]
- `M` (Connectivity): log₂(reference_count+1) quantized to [0, 2,097,151]

**Indexes**: See [Indexes](#indexes) section

---

### bpe_tokens

Stores learned BPE vocabulary patterns as LINESTRINGZM compositions.

```sql
CREATE TABLE bpe_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    composition_geometry GEOMETRY(LINESTRINGZM, 4326) NOT NULL,
    constant_sequence UUID[] NOT NULL, -- Ordered atom IDs
    frequency BIGINT NOT NULL DEFAULT 1,
    
    -- Metadata
    atom_count INT GENERATED ALWAYS AS (array_length(constant_sequence, 1)) STORED,
    hilbert_gap_detected BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(255),
    updated_at TIMESTAMPTZ,
    updated_by VARCHAR(255),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(255),
    
    -- Constraints
    CONSTRAINT chk_frequency_positive CHECK (frequency > 0),
    CONSTRAINT chk_atoms_not_empty CHECK (array_length(constant_sequence, 1) >= 2),
    CONSTRAINT chk_composition_srid CHECK (ST_SRID(composition_geometry) = 4326)
);
```

**Key Columns**:
- `composition_geometry` - LINESTRINGZM path through constituent atoms
- `constant_sequence` - Array of atom UUIDs in order
- `frequency` - How often this pattern occurs
- `hilbert_gap_detected` - Whether Hilbert gap exists (compression boundary)

**Geometric Interpretation**:
- Each point in LINESTRING = atom location in POINTZM space
- Gaps in Hilbert sequence (X coordinate jumps) = natural compression boundaries
- Used for vocabulary-based compression and pattern recognition

---

### embeddings

High-dimensional vector embeddings stored as MULTIPOINTZM.

```sql
CREATE TABLE embeddings (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    content_id UUID NOT NULL REFERENCES content_ingestions(id) ON DELETE CASCADE,
    vector_geometry GEOMETRY(MULTIPOINTZM, 4326) NOT NULL,
    vector FLOAT4[] NOT NULL, -- Original vector for exact distance
    model_name VARCHAR(255) NOT NULL,
    dimension_count INT NOT NULL,
    
    -- Metadata
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(255),
    
    -- Constraints
    CONSTRAINT chk_dimension_count CHECK (dimension_count > 0),
    CONSTRAINT chk_vector_length CHECK (array_length(vector, 1) = dimension_count),
    CONSTRAINT chk_vector_geometry_srid CHECK (ST_SRID(vector_geometry) = 4326)
);

CREATE INDEX idx_embeddings_model ON embeddings(model_name);
CREATE INDEX idx_embeddings_content ON embeddings(content_id);
CREATE INDEX idx_embeddings_vector_gist ON embeddings USING gist(vector_geometry);
```

**Key Columns**:
- `vector_geometry` - MULTIPOINTZM with N/3 points (3D chunks of original vector)
- `vector` - Original float array for exact cosine similarity
- `model_name` - Embedding model identifier (e.g., "text-embedding-ada-002")
- `dimension_count` - Vector dimensionality (e.g., 768, 1536)

**Usage**:
- Semantic search via k-NN on `vector_geometry`
- Exact cosine similarity via `vector` array
- Model-specific retrieval via `model_name` index

---

### content_ingestions

Tracks ingested content with geometric boundaries.

```sql
CREATE TABLE content_ingestions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    boundary_geometry GEOMETRY(POLYGONZM, 4326), -- Convex hull of atoms
    constant_ids UUID[] NOT NULL,
    
    -- Statistics
    atom_count INT GENERATED ALWAYS AS (array_length(constant_ids, 1)) STORED,
    new_atoms_created INT NOT NULL DEFAULT 0,
    deduplication_rate NUMERIC(5,4), -- 0.0000 to 1.0000
    ingestion_time_ms NUMERIC(10,2),
    
    -- Metadata
    source_metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(255),
    
    -- Constraints
    CONSTRAINT chk_atom_count_positive CHECK (array_length(constant_ids, 1) > 0),
    CONSTRAINT chk_deduplication_rate CHECK (deduplication_rate BETWEEN 0 AND 1),
    CONSTRAINT chk_boundary_srid CHECK (
        boundary_geometry IS NULL OR 
        ST_SRID(boundary_geometry) = 4326
    )
);

CREATE INDEX idx_content_ingestions_boundary ON content_ingestions 
    USING gist(boundary_geometry);
CREATE INDEX idx_content_ingestions_metadata ON content_ingestions 
    USING gin(source_metadata);
```

**Key Columns**:
- `boundary_geometry` - Convex hull (POLYGONZM) around all constituent atoms
- `constant_ids` - Array of atom UUIDs comprising this content
- `deduplication_rate` - Fraction of atoms that already existed (0 = all new, 1 = all duplicates)
- `source_metadata` - JSONB for arbitrary metadata (filename, content type, etc.)

**Geometric Queries**:
```sql
-- Find all content containing a specific atom
SELECT * FROM content_ingestions
WHERE ST_Contains(boundary_geometry, (SELECT location FROM constants WHERE id = $atom_id));

-- Find overlapping content (shared atoms)
SELECT c1.id, c2.id, ST_Area(ST_Intersection(c1.boundary_geometry, c2.boundary_geometry)) as overlap
FROM content_ingestions c1, content_ingestions c2
WHERE c1.id < c2.id AND ST_Intersects(c1.boundary_geometry, c2.boundary_geometry);
```

---

### neural_parameters

Stores neural network weights/biases as geometric points.

```sql
CREATE TABLE neural_parameters (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    weight_location GEOMETRY(POINTZM, 4326) NOT NULL,
    layer_name VARCHAR(255) NOT NULL,
    parameter_name VARCHAR(255) NOT NULL,
    value FLOAT8 NOT NULL,
    importance_score FLOAT8,
    
    -- Network metadata
    network_id UUID NOT NULL,
    layer_depth INT NOT NULL,
    neuron_position INT,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    
    -- Constraints
    CONSTRAINT chk_weight_location_srid CHECK (ST_SRID(weight_location) = 4326),
    CONSTRAINT chk_layer_depth CHECK (layer_depth >= 0)
);

CREATE INDEX idx_neural_parameters_network ON neural_parameters(network_id, layer_depth);
CREATE INDEX idx_neural_parameters_location ON neural_parameters 
    USING gist(weight_location);
```

**Key Columns**:
- `weight_location` - POINTZM encoding of weight value
- `layer_name` / `layer_depth` - Network layer identification
- `importance_score` - PageRank or gradient-based importance
- `network_id` - Groups parameters by neural network instance

**Geometric Encoding**:
- `X` (Hilbert): Hash of (network_id, layer, neuron, parameter)
- `Y` (Entropy): Distribution entropy of weights in layer
- `Z` (Compressibility): Weight matrix compressibility
- `M` (Connectivity): Layer depth + neuron position

---

### image_contents

Multi-modal image storage as MULTIPOINTZM pixel grids.

```sql
CREATE TABLE image_contents (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    pixel_geometry GEOMETRY(MULTIPOINTZM, 4326) NOT NULL,
    width INT NOT NULL,
    height INT NOT NULL,
    channel_count INT NOT NULL DEFAULT 3, -- RGB
    
    -- Metadata
    content_id UUID REFERENCES content_ingestions(id) ON DELETE CASCADE,
    format VARCHAR(50), -- JPEG, PNG, etc.
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Constraints
    CONSTRAINT chk_dimensions CHECK (width > 0 AND height > 0),
    CONSTRAINT chk_channels CHECK (channel_count IN (1, 3, 4)), -- Grayscale, RGB, RGBA
    CONSTRAINT chk_pixel_count CHECK (
        ST_NumGeometries(pixel_geometry) = width * height
    ),
    CONSTRAINT chk_pixel_geometry_srid CHECK (ST_SRID(pixel_geometry) = 4326)
);

CREATE INDEX idx_image_contents_dimensions ON image_contents(width, height);
CREATE INDEX idx_image_contents_geometry ON image_contents 
    USING gist(pixel_geometry);
```

**Key Columns**:
- `pixel_geometry` - MULTIPOINTZM with width×height points
- Each point = POINTZM(x_coord, y_coord, intensity, alpha)
- `channel_count` - 1 (grayscale), 3 (RGB), 4 (RGBA)

**Spatial Queries**:
```sql
-- Find images with similar geometric structure (shape matching)
SELECT id, ST_HausdorffDistance(pixel_geometry, $target_geometry) as distance
FROM image_contents
ORDER BY distance
LIMIT 10;
```

---

## Indexes

### Primary Indexes (constants table)

```sql
-- B-tree for Hilbert range queries (sequential scans)
CREATE INDEX CONCURRENTLY idx_constants_hilbert_btree
    ON constants USING btree (hilbert_index);

-- GIST for k-NN spatial queries
CREATE INDEX CONCURRENTLY idx_constants_location_gist
    ON constants USING gist (location);

-- Hash lookup for deduplication
CREATE UNIQUE INDEX CONCURRENTLY idx_constants_hash_unique
    ON constants (hash);

-- Composite index for YZM subspace queries
CREATE INDEX CONCURRENTLY idx_constants_yzm_composite
    ON constants (entropy, compressibility, connectivity)
    WHERE NOT is_deleted;

-- Partial index for hot atoms (high connectivity)
CREATE INDEX CONCURRENTLY idx_constants_hot_atoms
    ON constants (connectivity DESC)
    WHERE connectivity > 1000000 AND NOT is_deleted;

-- Covering index for frequently accessed fields
CREATE INDEX CONCURRENTLY idx_constants_covering
    ON constants (hilbert_index, reference_count, frequency)
    INCLUDE (hash, data)
    WHERE NOT is_deleted;
```

### BPE Token Indexes

```sql
-- GIST for composition geometry queries
CREATE INDEX CONCURRENTLY idx_bpe_tokens_composition_gist
    ON bpe_tokens USING gist (composition_geometry);

-- Frequency-based ordering
CREATE INDEX CONCURRENTLY idx_bpe_tokens_frequency
    ON bpe_tokens (frequency DESC)
    WHERE NOT is_deleted;

-- GIN index for constant_sequence array searches
CREATE INDEX CONCURRENTLY idx_bpe_tokens_sequence_gin
    ON bpe_tokens USING gin (constant_sequence);
```

### Embedding Indexes

```sql
-- GIST for k-NN vector search
CREATE INDEX CONCURRENTLY idx_embeddings_vector_gist
    ON embeddings USING gist (vector_geometry);

-- Model-specific lookups
CREATE INDEX CONCURRENTLY idx_embeddings_model
    ON embeddings (model_name, created_at DESC);
```

### Performance Monitoring

```sql
-- Check index usage statistics
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_scan as scans,
    idx_tup_read as tuples_read,
    idx_tup_fetch as tuples_fetched,
    pg_size_pretty(pg_relation_size(indexrelid)) as size
FROM pg_stat_user_indexes
WHERE schemaname = 'public'
ORDER BY idx_scan ASC;

-- Identify missing indexes (sequential scans on large tables)
SELECT 
    schemaname,
    tablename,
    seq_scan,
    seq_tup_read,
    idx_scan,
    seq_tup_read / NULLIF(seq_scan, 0) as avg_seq_read
FROM pg_stat_user_tables
WHERE schemaname = 'public'
    AND seq_scan > 0
ORDER BY seq_tup_read DESC;
```

---

## Custom Functions

### PL/pgSQL Functions

#### compute_convex_hull_from_atoms

Creates convex hull boundary from atom locations.

```sql
CREATE OR REPLACE FUNCTION compute_convex_hull_from_atoms(atom_ids UUID[])
RETURNS GEOMETRY(POLYGONZM, 4326)
LANGUAGE plpgsql
AS $$
DECLARE
    hull GEOMETRY(POLYGONZM, 4326);
BEGIN
    SELECT ST_ConvexHull(ST_Collect(location))
    INTO hull
    FROM constants
    WHERE id = ANY(atom_ids) AND NOT is_deleted;
    
    RETURN hull;
END;
$$;

-- Usage
UPDATE content_ingestions
SET boundary_geometry = compute_convex_hull_from_atoms(constant_ids)
WHERE boundary_geometry IS NULL;
```

#### compute_voronoi_tessellation

Partitions POINTZM space into Voronoi cells.

```sql
CREATE OR REPLACE FUNCTION compute_voronoi_tessellation()
RETURNS TABLE(
    cell_id INT,
    cell_geometry GEOMETRY(POLYGON, 4326),
    atom_count BIGINT,
    avg_entropy NUMERIC,
    avg_compressibility NUMERIC
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH voronoi_cells AS (
        SELECT 
            (ST_Dump(ST_VoronoiPolygons(ST_Collect(location)))).geom as cell,
            ROW_NUMBER() OVER () as cell_id
        FROM constants
        WHERE NOT is_deleted
    )
    SELECT 
        vc.cell_id::INT,
        vc.cell::GEOMETRY(POLYGON, 4326),
        COUNT(c.id)::BIGINT as atom_count,
        AVG(c.entropy)::NUMERIC as avg_entropy,
        AVG(c.compressibility)::NUMERIC as avg_compressibility
    FROM voronoi_cells vc
    LEFT JOIN constants c ON ST_Contains(vc.cell, c.location)
    WHERE NOT c.is_deleted
    GROUP BY vc.cell_id, vc.cell
    ORDER BY atom_count DESC;
END;
$$;

-- Usage
SELECT * FROM compute_voronoi_tessellation();
```

#### compute_delaunay_triangulation

Builds connectivity graph from spatial proximity.

```sql
CREATE OR REPLACE FUNCTION compute_delaunay_triangulation()
RETURNS GEOMETRY(MULTILINESTRING, 4326)
LANGUAGE plpgsql
AS $$
DECLARE
    triangulation GEOMETRY(MULTILINESTRING, 4326);
BEGIN
    SELECT ST_DelaunayTriangles(ST_Collect(location), 0.0, 1) -- flag=1 returns edges
    INTO triangulation
    FROM constants
    WHERE NOT is_deleted;
    
    RETURN triangulation;
END;
$$;
```

#### find_hilbert_gaps

Detects compression boundaries in Hilbert-sorted sequence.

```sql
CREATE OR REPLACE FUNCTION find_hilbert_gaps(
    gap_threshold BIGINT DEFAULT 1000
)
RETURNS TABLE(
    gap_start_id UUID,
    gap_end_id UUID,
    gap_size BIGINT,
    hilbert_start BIGINT,
    hilbert_end BIGINT
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    WITH hilbert_sorted AS (
        SELECT 
            id,
            hilbert_index,
            LEAD(id) OVER (ORDER BY hilbert_index) as next_id,
            LEAD(hilbert_index) OVER (ORDER BY hilbert_index) - hilbert_index as gap
        FROM constants
        WHERE NOT is_deleted
    )
    SELECT 
        id,
        next_id,
        gap,
        hilbert_index,
        hilbert_index + gap
    FROM hilbert_sorted
    WHERE gap > gap_threshold
    ORDER BY gap DESC;
END;
$$;

-- Usage
SELECT * FROM find_hilbert_gaps(1000);
```

---

### GPU-Accelerated Functions

Implemented in PL/Python using CuPy for GPU computation.

#### gpu_compute_mst

Computes Minimum Spanning Tree on GPU.

```sql
CREATE OR REPLACE FUNCTION gpu_compute_mst(edge_list FLOAT8[][])
RETURNS TABLE(source_idx INT, target_idx INT, weight FLOAT8)
LANGUAGE plpython3u
AS $$
import cupy as cp
import cupyx.scipy.sparse.csgraph as csgraph

# Convert edge list to adjacency matrix
edges = cp.array(edge_list)
n = int(edges[:, :2].max()) + 1
adj_matrix = cp.zeros((n, n))

for src, tgt, weight in edges:
    adj_matrix[int(src), int(tgt)] = weight
    adj_matrix[int(tgt), int(src)] = weight

# Compute MST using Prim's algorithm
mst = csgraph.minimum_spanning_tree(adj_matrix)

# Extract MST edges
mst_edges = []
for i in range(n):
    for j in range(i+1, n):
        if mst[i, j] > 0:
            mst_edges.append((i, j, float(mst[i, j])))

return mst_edges
$$;
```

#### gpu_batch_quantize_entropy

Quantizes Shannon entropy on GPU in batch.

```sql
CREATE OR REPLACE FUNCTION gpu_batch_quantize_entropy(data_arrays BYTEA[])
RETURNS INT[]
LANGUAGE plpython3u
AS $$
import cupy as cp
import numpy as np

def compute_entropy(data):
    counts = cp.bincount(cp.frombuffer(data, dtype=cp.uint8))
    probs = counts / counts.sum()
    probs = probs[probs > 0]  # Remove zeros
    entropy = -cp.sum(probs * cp.log2(probs))
    return entropy

# Process all data arrays on GPU
entropies = []
for data in data_arrays:
    entropy = compute_entropy(cp.frombuffer(data, dtype=cp.uint8))
    quantized = int(cp.clip(entropy * 262144, 0, 2097151))  # Scale to 21-bit
    entropies.append(quantized)

return entropies
$$;
```

#### gpu_knn_search

GPU-accelerated k-NN search in POINTZM space.

```sql
CREATE OR REPLACE FUNCTION gpu_knn_search(
    query_point FLOAT8[],
    k INT DEFAULT 10
)
RETURNS TABLE(atom_id UUID, distance FLOAT8)
LANGUAGE plpython3u
AS $$
import cupy as cp

# Fetch all locations
result = plpy.execute("""
    SELECT id, 
        ST_X(location)::FLOAT8 as x,
        ST_Y(location)::FLOAT8 as y,
        ST_Z(location)::FLOAT8 as z,
        ST_M(location)::FLOAT8 as m
    FROM constants
    WHERE NOT is_deleted
""")

# Convert to GPU arrays
ids = [row['id'] for row in result]
points = cp.array([[row['x'], row['y'], row['z'], row['m']] for row in result])
query = cp.array(query_point)

# Compute Euclidean distances on GPU
distances = cp.linalg.norm(points - query, axis=1)

# Get k nearest indices
top_k_indices = cp.argpartition(distances, k)[:k]
top_k_indices = top_k_indices[cp.argsort(distances[top_k_indices])]

# Return results
results = []
for idx in top_k_indices.get():
    results.append((ids[idx], float(distances[idx])))

return results
$$;
```

---

## Migrations

### Migration Workflow

```bash
# Create new migration
cd Hartonomous.Data
dotnet ef migrations add <MigrationName> --startup-project ../Hartonomous.API

# Preview SQL
dotnet ef migrations script --startup-project ../Hartonomous.API

# Apply migration
dotnet ef database update --startup-project ../Hartonomous.API

# Rollback migration
dotnet ef database update <PreviousMigration> --startup-project ../Hartonomous.API
```

### Zero-Downtime Migration Pattern

```csharp
// Phase 1: Add new column as nullable
migrationBuilder.AddColumn<int>(
    name: "new_column",
    table: "constants",
    nullable: true);

// Phase 2: Backfill data (separate migration)
migrationBuilder.Sql(@"
    UPDATE constants
    SET new_column = compute_new_value(old_column)
    WHERE new_column IS NULL;
");

// Phase 3: Make non-nullable (separate migration)
migrationBuilder.AlterColumn<int>(
    name: "new_column",
    table: "constants",
    nullable: false);

// Phase 4: Drop old column (separate migration)
migrationBuilder.DropColumn(
    name: "old_column",
    table: "constants");
```

### Critical Migrations

```csharp
// Initial POINTZM migration
public partial class AddPointZMSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Enable PostGIS
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");
        
        // Add POINTZM column
        migrationBuilder.Sql(@"
            ALTER TABLE constants
            ADD COLUMN location GEOMETRY(POINTZM, 4326);
        ");
        
        // Create GIST index
        migrationBuilder.Sql(@"
            CREATE INDEX CONCURRENTLY idx_constants_location_gist
            ON constants USING gist (location);
        ");
    }
}
```

---

## Performance Tuning

### PostgreSQL Configuration

```ini
# postgresql.conf optimizations for Hartonomous

# Memory settings (for 32GB RAM server)
shared_buffers = 8GB
effective_cache_size = 24GB
maintenance_work_mem = 2GB
work_mem = 128MB

# Parallelism
max_parallel_workers_per_gather = 4
max_parallel_workers = 8
max_worker_processes = 8

# Write-ahead log
wal_buffers = 16MB
checkpoint_completion_target = 0.9
max_wal_size = 4GB
min_wal_size = 1GB

# Query planner
random_page_cost = 1.1  # SSD storage
effective_io_concurrency = 200

# Autovacuum (important for high-write workload)
autovacuum_max_workers = 4
autovacuum_naptime = 10s

# Connection pooling
max_connections = 200
```

### Query Optimization Examples

```sql
-- Use covering index for fast lookups
EXPLAIN ANALYZE
SELECT hash, data, reference_count
FROM constants
WHERE hilbert_index BETWEEN 1000 AND 2000
    AND NOT is_deleted;
-- Should use idx_constants_covering

-- Batch inserts for ingestion
INSERT INTO constants (location, hash, data)
SELECT 
    ST_MakePointZM(hilbert, entropy, compressibility, connectivity),
    hash,
    data
FROM UNNEST($1::BIGINT[], $2::BYTEA[], $3::BYTEA[]) 
    AS t(hilbert, hash, data)
ON CONFLICT (hash) DO UPDATE
SET reference_count = constants.reference_count + 1,
    frequency = constants.frequency + 1;

-- Efficient k-NN with <-> operator
SELECT id, location <-> ST_MakePointZM($1, $2, $3, $4) as distance
FROM constants
WHERE NOT is_deleted
ORDER BY location <-> ST_MakePointZM($1, $2, $3, $4)
LIMIT 10;
```

### VACUUM and ANALYZE

```sql
-- Regular maintenance
VACUUM ANALYZE constants;

-- After bulk ingestion
VACUUM ANALYZE constants;
REINDEX INDEX CONCURRENTLY idx_constants_location_gist;

-- Monitor bloat
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size,
    n_live_tup,
    n_dead_tup,
    round(n_dead_tup * 100.0 / NULLIF(n_live_tup + n_dead_tup, 0), 2) as dead_ratio
FROM pg_stat_user_tables
WHERE schemaname = 'public'
ORDER BY n_dead_tup DESC;
```

---

**Navigation**:  
← [API Guide](API-Guide.md) | [Home](../Home.md) | [Deployment Guide](Deployment-Guide.md) →
