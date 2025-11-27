# API REFERENCE

Core functions and procedures.

---

## Atomization

### `atomize_value()`
```sql
atomize_value(
    p_value BYTEA,
    p_canonical_text TEXT DEFAULT NULL,
    p_metadata JSONB DEFAULT '{}'
) RETURNS BIGINT

-- Example
SELECT atomize_value('\x48'::bytea, 'H', '{"modality":"byte"}');
-- Returns: atom_id
```

### `atomize_text()`
```sql
atomize_text(p_text TEXT) RETURNS BIGINT[]

-- Example
SELECT atomize_text('Hello');
-- Returns: [72, 101, 108, 108, 111] (atom_ids for H,e,l,l,o)
```

---

## Spatial

### `compute_spatial_position()`
```sql
compute_spatial_position(p_atom_id BIGINT) RETURNS GEOMETRY

-- Computes 3D position via weighted neighbor averaging
```

### `spatial_knn()`
```sql
spatial_knn(
    p_query_point GEOMETRY,
    p_top_k INT DEFAULT 10,
    p_max_distance FLOAT DEFAULT NULL
) RETURNS TABLE(atom_id BIGINT, distance FLOAT)

-- Example
SELECT * FROM spatial_knn(ST_MakePoint(0.5, 0.3, 0.2), 10);
```

---

## Relations

### `reinforce_synapse()`
```sql
reinforce_synapse(
    p_source_id BIGINT,
    p_target_id BIGINT,
    p_relation_type TEXT DEFAULT 'semantic_correlation',
    p_learning_rate FLOAT DEFAULT 0.05
) RETURNS VOID

-- Hebbian learning: strengthen connection
```

### `create_relation()`
```sql
create_relation(
    p_source_id BIGINT,
    p_target_id BIGINT,
    p_relation_type_id BIGINT,
    p_weight REAL DEFAULT 0.5
) RETURNS BIGINT

-- Returns: relation_id
```

---

## Composition

### `create_composition()`
```sql
create_composition(
    p_parent_id BIGINT,
    p_component_id BIGINT,
    p_sequence_index BIGINT
) RETURNS BIGINT

-- Returns: composition_id
```

### `reconstruct_atom()`
```sql
reconstruct_atom(p_atom_id BIGINT) RETURNS TEXT

-- Recursively reconstructs from components
-- Example: reconstruct_atom(doc_id) returns full document text
```

---

## OODA Loop

### `ooda_observe()`
```sql
ooda_observe() RETURNS TABLE(issue TEXT, metric REAL)

-- Identifies performance issues, heavy atoms, etc.
```

### `ooda_act()`
```sql
ooda_act(p_optimization_ddl TEXT) RETURNS TEXT

-- Executes optimization (CREATE INDEX, etc.)
```

---

## Queries

### Multi-Model Query
```sql
SELECT
    metadata->>'model_name' AS model,
    canonical_text,
    ST_Distance(spatial_key, @query_point) AS distance
FROM atom
WHERE ST_DWithin(spatial_key, @query_point, 1.0)
ORDER BY distance
LIMIT 100;
```

### Truth Clustering
```sql
SELECT
    canonical_text,
    COUNT(*) AS cluster_size,
    AVG(ST_Distance(spatial_key, @reference_point)) AS avg_distance
FROM atom
WHERE ST_DWithin(spatial_key, @reference_point, 0.1)
GROUP BY canonical_text
HAVING COUNT(*) > 10
ORDER BY cluster_size DESC;
```

### Temporal Query
```sql
SELECT * FROM atom_history
WHERE atom_id = @id
  AND valid_from <= @timestamp
  AND valid_to > @timestamp;
```

---

## GPU Functions (Optional)

### `gpu_batch_hash()`
```sql
gpu_batch_hash(p_values BYTEA[]) RETURNS BYTEA[]

-- GPU-accelerated SHA-256 hashing
```

### `gpu_project_to_3d()`
```sql
gpu_project_to_3d(p_embeddings REAL[][])
RETURNS TABLE(x REAL, y REAL, z REAL)

-- UMAP/PCA projection (GPU if available)
```

---

## Metadata Patterns

```sql
-- Modality filtering
WHERE metadata->>'modality' = 'text'

-- Tenant isolation
WHERE (metadata->>'tenantId')::INT = @tenant_id

-- Model filtering
WHERE metadata->>'model_name' = 'gpt-4'

-- Confidence thresholding
WHERE (metadata->>'confidence')::REAL > 0.8
```

---

## Performance Tips

- Use `EXPLAIN ANALYZE` to verify index usage
- Batch atomization (insert 1000s at once)
- Partition large tables by modality or tenant
- Materialize frequently-used spatial positions
- Use connection pooling (PgBouncer)

---

**See Also**: [02-ARCHITECTURE.md](02-ARCHITECTURE.md) for schema details
