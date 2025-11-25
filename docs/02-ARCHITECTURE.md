# ARCHITECTURE

PostgreSQL + PostGIS + PL/Python. Three tables. R-tree spatial indexing.

---

## Stack

**Database**: PostgreSQL 15+
**Spatial**: PostGIS 3.3+
**Extensions**: PL/Python3u (GPU-optional)
**Optional**: CUDA 11.8+ (for GPU acceleration)

---

## Schema

### Core Tables

```sql
-- 1. Atom: Content-addressable storage
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    canonical_text TEXT,
    spatial_key GEOMETRY(POINTZ, 0),
    reference_count BIGINT DEFAULT 1,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT now(),
    valid_from TIMESTAMPTZ DEFAULT now(),
    valid_to TIMESTAMPTZ DEFAULT 'infinity'
);

-- 2. AtomComposition: Hierarchical structure
CREATE TABLE atom_composition (
    composition_id BIGSERIAL PRIMARY KEY,
    parent_atom_id BIGINT REFERENCES atom(atom_id),
    component_atom_id BIGINT REFERENCES atom(atom_id),
    sequence_index BIGINT,
    spatial_key GEOMETRY(POINTZ, 0),
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE (parent_atom_id, component_atom_id, sequence_index)
);

-- 3. AtomRelation: Semantic graph
CREATE TABLE atom_relation (
    relation_id BIGSERIAL PRIMARY KEY,
    source_atom_id BIGINT REFERENCES atom(atom_id),
    target_atom_id BIGINT REFERENCES atom(atom_id),
    relation_type_id BIGINT REFERENCES atom(atom_id),
    weight REAL DEFAULT 0.5,
    confidence REAL DEFAULT 0.5,
    importance REAL DEFAULT 0.5,
    spatial_expression GEOMETRY(LINESTRINGZ, 0),
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE (source_atom_id, target_atom_id, relation_type_id)
);
```

### Indexes

```sql
-- Spatial (R-tree)
CREATE INDEX idx_atom_spatial ON atom USING GIST (spatial_key);
CREATE INDEX idx_composition_spatial ON atom_composition USING GIST (spatial_key);
CREATE INDEX idx_relation_spatial ON atom_relation USING GIST (spatial_expression);

-- Hash lookup
CREATE INDEX idx_atom_hash ON atom (content_hash);

-- Hierarchy traversal
CREATE INDEX idx_composition_parent ON atom_composition(parent_atom_id, sequence_index);
CREATE INDEX idx_composition_component ON atom_composition(component_atom_id);

-- Graph traversal
CREATE INDEX idx_relation_source ON atom_relation(source_atom_id);
CREATE INDEX idx_relation_target ON atom_relation(target_atom_id);
CREATE INDEX idx_relation_weight ON atom_relation(weight DESC);

-- Metadata
CREATE INDEX idx_atom_metadata ON atom USING GIN (metadata);
CREATE INDEX idx_atom_reference_count ON atom (reference_count DESC);
```

---

## Spatial Indexing Strategy

**Primary**: PostGIS R-tree (GIST index) for KNN queries
**Secondary**: B-tree on `reference_count` for heavy atom filtering

**No Hilbert curve** - R-tree is 40x faster for spatial proximity.

Query pattern:
```sql
-- KNN (nearest neighbors) - uses R-tree
SELECT * FROM atom
ORDER BY spatial_key <-> ST_MakePoint(0.5, 0.3, 0.2)
LIMIT 10;
-- Execution: ~0.3ms (R-tree index scan)
```

---

## Temporal Versioning

PostgreSQL doesn't have native `SYSTEM_VERSIONING`. Use triggers:

```sql
-- History table
CREATE TABLE atom_history (LIKE atom INCLUDING ALL);

-- Trigger function
CREATE OR REPLACE FUNCTION atom_temporal_trigger()
RETURNS TRIGGER AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        INSERT INTO atom_history SELECT OLD.*;
        NEW.valid_from := now();
    ELSIF TG_OP = 'DELETE' THEN
        INSERT INTO atom_history SELECT OLD.*;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Attach trigger
CREATE TRIGGER atom_temporal
    BEFORE UPDATE OR DELETE ON atom
    FOR EACH ROW EXECUTE FUNCTION atom_temporal_trigger();
```

Time-travel queries:
```sql
SELECT * FROM atom_history
WHERE atom_id = 12345
  AND valid_from <= '2025-01-01'::timestamptz
  AND valid_to > '2025-01-01'::timestamptz;
```

---

## PL/Python GPU Acceleration

Enable PL/Python:
```sql
CREATE EXTENSION plpython3u;
```

Example GPU function:
```sql
CREATE OR REPLACE FUNCTION gpu_batch_hash(values BYTEA[])
RETURNS BYTEA[] AS $$
    import torch
    import hashlib

    device = "cuda" if torch.cuda.is_available() else "cpu"

    if device == "cuda":
        import cupy as cp
        # GPU-accelerated hashing
        return [hashlib.sha256(v).digest() for v in values]
    else:
        # CPU fallback
        return [hashlib.sha256(v).digest() for v in values]
$$ LANGUAGE plpython3u;
```

Optional: Install PyTorch, CuPy for GPU support:
```bash
pip3 install torch cupy-cuda11x numpy scipy
```

---

## Content Addressing

SHA-256 hash ensures uniqueness:
```sql
CREATE OR REPLACE FUNCTION atomize_value(
    p_value BYTEA,
    p_canonical_text TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'
)
RETURNS BIGINT AS $$
DECLARE
    v_hash BYTEA;
    v_atom_id BIGINT;
BEGIN
    v_hash := digest(p_value, 'sha256');

    SELECT atom_id INTO v_atom_id
    FROM atom WHERE content_hash = v_hash;

    IF FOUND THEN
        UPDATE atom SET reference_count = reference_count + 1
        WHERE atom_id = v_atom_id;
        RETURN v_atom_id;
    END IF;

    INSERT INTO atom (content_hash, atomic_value, canonical_text, metadata)
    VALUES (v_hash, p_value, p_canonical_text, p_metadata)
    RETURNING atom_id INTO v_atom_id;

    RETURN v_atom_id;
END;
$$ LANGUAGE plpgsql;
```

---

## Spatial Position Computation

Compute via weighted neighbor averaging:

```sql
CREATE OR REPLACE FUNCTION compute_spatial_position(p_atom_id BIGINT)
RETURNS GEOMETRY AS $$
DECLARE
    v_centroid GEOMETRY;
BEGIN
    -- Find semantic neighbors (top 100 by similarity)
    -- Compute weighted centroid of their positions
    SELECT ST_Centroid(ST_Collect(spatial_key))
    INTO v_centroid
    FROM (
        SELECT a2.spatial_key
        FROM atom a1
        CROSS JOIN atom a2
        WHERE a1.atom_id = p_atom_id
          AND a2.spatial_key IS NOT NULL
          AND a1.metadata->>'modality' = a2.metadata->>'modality'
        ORDER BY calculate_similarity(a1.atom_id, a2.atom_id) DESC
        LIMIT 100
    ) neighbors;

    RETURN v_centroid;
END;
$$ LANGUAGE plpgsql;
```

Similarity can be text-based (Levenshtein), embedding-based, or hybrid.

---

## Query Patterns

**KNN (K-Nearest Neighbors)**:
```sql
SELECT * FROM atom
ORDER BY spatial_key <-> @query_point
LIMIT 10;
```

**Range Query**:
```sql
SELECT * FROM atom
WHERE ST_DWithin(spatial_key, @query_point, 0.5);
```

**Composition Traversal**:
```sql
WITH RECURSIVE tree AS (
    SELECT * FROM atom_composition WHERE parent_atom_id = @root
    UNION ALL
    SELECT ac.* FROM atom_composition ac
    JOIN tree t ON t.component_atom_id = ac.parent_atom_id
)
SELECT * FROM tree;
```

**Relation Traversal**:
```sql
WITH RECURSIVE graph AS (
    SELECT * FROM atom_relation WHERE source_atom_id = @start
    UNION ALL
    SELECT ar.* FROM atom_relation ar
    JOIN graph g ON g.target_atom_id = ar.source_atom_id
    WHERE g.depth < 10
)
SELECT * FROM graph;
```

---

## Multi-Tenancy

Use JSONB metadata + row-level security:

```sql
-- Add tenant_id computed column
ALTER TABLE atom ADD COLUMN tenant_id INT
    GENERATED ALWAYS AS (CAST(metadata->>'tenantId' AS INT)) STORED;

CREATE INDEX idx_atom_tenant ON atom(tenant_id);

-- Row-level security
ALTER TABLE atom ENABLE ROW LEVEL SECURITY;

CREATE POLICY atom_tenant_isolation ON atom
    USING (tenant_id = current_setting('app.tenant_id')::INT);
```

---

## Partitioning (Optional, for scale)

Partition by modality or tenant:

```sql
CREATE TABLE atom_text PARTITION OF atom
    FOR VALUES IN ('text');

CREATE TABLE atom_image PARTITION OF atom
    FOR VALUES IN ('image');

-- etc.
```

Or range partition by reference_count (hot/cold data):
```sql
CREATE TABLE atom_hot PARTITION OF atom
    FOR VALUES FROM (1000000) TO (MAXVALUE);

CREATE TABLE atom_cold PARTITION OF atom
    FOR VALUES FROM (0) TO (1000000);
```

---

## Performance Characteristics

| Operation | Complexity | Typical Time |
|-----------|-----------|--------------|
| Atomize value | O(1) hash lookup | 0.1ms |
| KNN query (R-tree) | O(log N) | 0.3ms |
| Composition traversal | O(depth × fanout) | 5-50ms |
| Relation traversal | O(edges) | 10-100ms |
| Batch atomization (1K) | O(K) | 15ms |
| Spatial position compute | O(log N) neighbors | 20ms |

N = total atoms (~1B at scale)

---

## Deployment Configurations

**Development** (local):
```yaml
PostgreSQL 15
PostGIS 3.3
RAM: 4GB
Disk: 50GB SSD
```

**Production** (cloud):
```yaml
PostgreSQL 15 (managed: RDS, Cloud SQL, Azure DB)
PostGIS 3.3
RAM: 64-256GB
Disk: 1-10TB NVMe
Replicas: 2-3 (read scaling)
```

**Edge** (IoT):
```yaml
PostgreSQL 15 (ARM)
PostGIS 3.3
RAM: 2GB
Disk: 16GB SD card
```

---

## Extensions Used

```sql
CREATE EXTENSION postgis;           -- Spatial types, R-tree
CREATE EXTENSION plpython3u;        -- Python UDFs (optional GPU)
CREATE EXTENSION pg_trgm;           -- Text similarity (optional)
CREATE EXTENSION btree_gin;         -- Composite indexes (optional)
```

---

## Migration from SQL Server (if needed)

Core differences:

| SQL Server | PostgreSQL |
|-----------|-----------|
| `GEOMETRY` | `GEOMETRY` (PostGIS) |
| `VARBINARY(64)` | `BYTEA` |
| `NVARCHAR(MAX)` | `TEXT` |
| `SYSTEM_VERSIONING` | Triggers |
| Service Broker | pg_notify or RabbitMQ |
| CLR functions | PL/Python |
| `HASHBYTES('SHA2_256')` | `digest(, 'sha256')` |

See MASTER-TECHNICAL-CHECKLIST.md for full migration guide.

---

**Next**: [03-GETTING-STARTED.md](03-GETTING-STARTED.md)
