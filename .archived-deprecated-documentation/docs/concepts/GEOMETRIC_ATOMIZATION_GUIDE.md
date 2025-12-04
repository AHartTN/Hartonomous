# Geometric Embedding Exploitation

## The Insight: Embeddings ARE Shapes

**Core Realization**: We're storing everything in `GEOMETRY GEOMETRYZM (X, Y, Z, M)`, but we've been thinking too primitively. Different types of content map naturally to different PostGIS geometric types.

## Mapping Content to Geometry

### 1. POINT (Primitive Atoms)
```sql
-- Single constant value
CREATE TABLE atom (
    atom_id UUID PRIMARY KEY,
    canonical_text TEXT,
    content_hash BYTEA,
    spatial_key GEOMETRY(POINTZM, 4326),  -- Single point in 4D space
    ...
);

-- Example: 'Cat'
INSERT INTO atom (canonical_text, spatial_key) VALUES (
    'Cat',
    ST_MakePoint(0.8, 0.3, 0.5, 12345)  -- X, Y, Z, Hilbert-M
);
```

**Use cases**:
- Individual tokens
- Individual neurons
- Scalar constants
- Single concepts

### 2. LINESTRING (Sequences/Trajectories)

```sql
-- Composition: Sequence of atoms
CREATE TABLE atom (
    ...
    composition_ids UUID[],
    spatial_key GEOMETRY(LINESTRINGZM, 4326),  -- Path through semantic space
    ...
);

-- Example: "The Cat sat" → LINESTRING connecting 3 atoms
INSERT INTO atom (canonical_text, composition_ids, spatial_key) VALUES (
    'The Cat sat',
    ARRAY[uuid_the, uuid_cat, uuid_sat],
    ST_MakeLine(ARRAY[
        ST_MakePoint(0.1, 0.2, 0.3, 100),     -- 'The'
        ST_MakePoint(0.8, 0.3, 0.5, 12345),   -- 'Cat'
        ST_MakePoint(0.6, 0.7, 0.2, 45678)    -- 'sat'
    ])
);

-- Relation: Edge between atoms
CREATE TABLE atom_relation (
    ...
    spatial_key GEOMETRY(LINESTRINGZM, 4326),  -- Edge as 2-point linestring
    ...
);

-- Example: Weight from neuron_A to neuron_B
INSERT INTO atom_relation (from_atom_id, to_atom_id, weight, spatial_key) VALUES (
    neuron_a_id,
    neuron_b_id,
    0.42,
    ST_MakeLine(
        (SELECT spatial_key FROM atom WHERE atom_id = neuron_a_id),
        (SELECT spatial_key FROM atom WHERE atom_id = neuron_b_id)
    )
);
```

**Use cases**:
- Text sequences (sentences, paragraphs)
- Code sequences (AST paths)
- Neural pathways (neuron → neuron)
- Trajectories (time-series data)
- **EMBEDDINGS** (768D as sequence of points)

### 3. MULTIPOINT (Chunked High-Dimensional Data)

```sql
-- Embedding: 768D vector → 256 chunks of 3D points
CREATE OR REPLACE FUNCTION embedding_to_multipoint(
    embedding FLOAT[],  -- [768 floats]
    chunk_size INT DEFAULT 3
) RETURNS GEOMETRY AS $$
DECLARE
    points GEOMETRY[];
    chunk FLOAT[];
    i INT;
BEGIN
    -- Chunk embedding into 3D points
    FOR i IN 1..ARRAY_LENGTH(embedding, 1) BY chunk_size LOOP
        chunk := embedding[i:i+chunk_size-1];
        
        points := ARRAY_APPEND(points, ST_MakePoint(
            chunk[1],  -- X
            chunk[2],  -- Y
            chunk[3],  -- Z
            i          -- M (dimension index)
        ));
    END LOOP;
    
    RETURN ST_Collect(points);  -- MULTIPOINT
END;
$$ LANGUAGE plpgsql;

-- Example: Store BERT embedding as MULTIPOINT
INSERT INTO atom (canonical_text, spatial_key) VALUES (
    'Cat is a feline animal',
    embedding_to_multipoint(
        bert_encode('Cat is a feline animal')  -- [768 floats]
    )
);
```

**Use cases**:
- High-dimensional embeddings (BERT, GPT, etc.)
- Sparse representations
- Multi-modal data (image + text + audio)
- Clustering centroids

### 4. POLYGON (Concept Spaces / Convex Hulls)

```sql
-- Pattern: Repeated subgraph → Convex hull of constituent atoms
CREATE TABLE atom_pattern (
    ...
    constituent_atom_ids UUID[],
    spatial_key GEOMETRY(POLYGONZM, 4326),  -- Boundary of concept space
    ...
);

-- Example: "Animal" concept space (convex hull of all animal atoms)
INSERT INTO atom_pattern (pattern_type, constituent_atom_ids, spatial_key) VALUES (
    'concept',
    ARRAY[uuid_cat, uuid_dog, uuid_bird, uuid_fish, ...],
    ST_ConvexHull(
        ST_Collect(
            SELECT spatial_key 
            FROM atom 
            WHERE canonical_text IN ('Cat', 'Dog', 'Bird', 'Fish', ...)
        )
    )
);

-- Example: Embedding convex hull (top-K dimensions)
WITH significant_dims AS (
    SELECT unnest(
        select_top_k_dimensions(embedding, 10)  -- Top 10 most significant
    ) as point
)
INSERT INTO atom (canonical_text, spatial_key) VALUES (
    'Cat (embedding hull)',
    ST_ConvexHull(ST_Collect(point)) FROM significant_dims
);
```

**Use cases**:
- Concept spaces (all animals, all verbs, etc.)
- Topic boundaries
- Attention regions
- Semantic neighborhoods

### 5. MULTILINESTRING (Graph Substructures)

```sql
-- Complex pattern: Multiple paths/edges
CREATE TABLE atom_pattern (
    ...
    edge_relation_ids UUID[],
    spatial_key GEOMETRY(MULTILINESTRINGZM, 4326),  -- Set of edges
    ...
);

-- Example: Repeated neural motif (feed-forward + skip connection)
INSERT INTO atom_pattern (pattern_type, edge_relation_ids, spatial_key) VALUES (
    'neural_motif',
    ARRAY[edge1_id, edge2_id, edge3_id],
    ST_Collect(ARRAY[
        (SELECT spatial_key FROM atom_relation WHERE relation_id = edge1_id),
        (SELECT spatial_key FROM atom_relation WHERE relation_id = edge2_id),
        (SELECT spatial_key FROM atom_relation WHERE relation_id = edge3_id)
    ])
);
```

**Use cases**:
- Graph motifs (repeated subgraphs)
- Code patterns (AST substructures)
- Neural circuits
- Data flow patterns

## PostGIS Operations Become Semantic Operations

### Distance = Similarity

```sql
-- Find sequences similar to "The Cat sat"
SELECT 
    canonical_text,
    ST_Distance(spatial_key, :target_linestring) as similarity
FROM atom
WHERE ST_GeometryType(spatial_key) = 'ST_LineString'
ORDER BY similarity ASC
LIMIT 100;

-- Result: Similar sentences with close trajectories
-- "The Dog sat", "The Cat ran", "A Cat sat", etc.
```

### Intersection = Shared Structure

```sql
-- Find common substructure between two patterns
SELECT 
    ST_Intersection(p1.spatial_key, p2.spatial_key) as shared_region,
    ST_Area(ST_Intersection(p1.spatial_key, p2.spatial_key)) / 
    ST_Area(ST_Union(p1.spatial_key, p2.spatial_key)) as jaccard_similarity
FROM atom_pattern p1, atom_pattern p2
WHERE p1.pattern_id = :pattern_a
  AND p2.pattern_id = :pattern_b;

-- Result: Overlapping concept space (shared atoms)
```

### Containment = Membership

```sql
-- Is this atom part of this composition?
SELECT 
    comp.canonical_text,
    atom.canonical_text,
    ST_Contains(comp.spatial_key, atom.spatial_key) as is_member
FROM atom comp, atom atom
WHERE comp.composition_ids IS NOT NULL
  AND comp.canonical_text = 'The Cat sat'
  AND atom.canonical_text = 'Cat';

-- Result: TRUE (Cat is spatially within "The Cat sat" linestring)
```

### Buffer = Neighborhood

```sql
-- Find all atoms within radius 0.5 of "Cat"
SELECT 
    a.canonical_text,
    ST_Distance(a.spatial_key, cat.spatial_key) as distance
FROM atom a,
     (SELECT spatial_key FROM atom WHERE canonical_text = 'Cat') cat
WHERE ST_DWithin(a.spatial_key, cat.spatial_key, 0.5)
ORDER BY distance ASC;

-- Result: Voronoi neighborhood
-- 'Kitten', 'Feline', 'Meow', 'Whiskers', 'Dog', ...
```

### Convex Hull = Concept Space

```sql
-- What is the "animal concept space"?
SELECT 
    ST_ConvexHull(
        ST_Collect(spatial_key)
    ) as animal_space
FROM atom
WHERE metadata->>'category' = 'animals';

-- Result: POLYGON enclosing all animal atoms
-- Any new atom within this polygon is "animal-like"
```

### Clustering = Natural Grouping

```sql
-- Find 100 semantic clusters
SELECT 
    ST_ClusterKMeans(spatial_key, 100) OVER () as cluster_id,
    canonical_text,
    metadata->>'category' as category
FROM atom
WHERE metadata->>'domain' = 'natural_language';

-- Result: 100 clusters discovered by spatial proximity
-- Cluster 1: animals, Cluster 2: verbs, Cluster 3: colors, etc.
```

## Embedding Storage Strategies

### Option 1: LINESTRING (Sequential)

**Pros**:
- Natural trajectory through semantic space
- Preserves dimensional order
- Works with existing Hilbert indexing

**Cons**:
- 768 points for a 768D embedding (large)
- Less efficient for very high dimensions

```python
def embedding_to_linestring(embedding: np.ndarray) -> str:
    """Convert 768D embedding to LINESTRING ZM."""
    points = []
    for i, value in enumerate(embedding):
        # Map dimension to 3D space + sequence
        x, y, z = dimension_to_3d(i, value)
        m = i  # M coordinate = dimension index
        points.append(f"{x} {y} {z} {m}")
    
    return f"LINESTRING ZM ({', '.join(points)})"

# Example
embedding = bert_encode("Cat is an animal")  # [768 floats]
linestring = embedding_to_linestring(embedding)
# Result: LINESTRING ZM (0.1 0.2 0.3 0, 0.4 0.5 0.6 1, ..., xn yn zn 767)
```

### Option 2: MULTIPOINT (Chunked)

**Pros**:
- More compact (256 points for 768D)
- Natural for sparse representations
- Easy to add/remove dimensions

**Cons**:
- Loses sequential order within chunks
- Requires chunk aggregation for similarity

```python
def embedding_to_multipoint(embedding: np.ndarray, chunk_size: int = 3) -> str:
    """Convert 768D embedding to MULTIPOINT ZM (256 chunks of 3D)."""
    points = []
    for i in range(0, len(embedding), chunk_size):
        chunk = embedding[i:i+chunk_size]
        
        # Pad if needed
        if len(chunk) < chunk_size:
            chunk = np.pad(chunk, (0, chunk_size - len(chunk)))
        
        x, y, z = chunk[0], chunk[1], chunk[2]
        m = i  # M coordinate = chunk start index
        points.append(f"{x} {y} {z} {m}")
    
    return f"MULTIPOINT ZM ({', '.join(points)})"

# Example
embedding = bert_encode("Cat is an animal")  # [768 floats]
multipoint = embedding_to_multipoint(embedding, chunk_size=3)
# Result: MULTIPOINT ZM ((0.1 0.2 0.3 0), (0.4 0.5 0.6 3), ..., (xn yn zn 765))
```

### Option 3: POLYGON (Convex Hull)

**Pros**:
- Most compact (only significant dimensions)
- Natural concept space representation
- Works well for clustering

**Cons**:
- Lossy (discards most dimensions)
- Requires dimensionality reduction

```python
def embedding_to_polygon(embedding: np.ndarray, top_k: int = 10) -> str:
    """Convert 768D embedding to POLYGON ZM (convex hull of top-K dimensions)."""
    # Select top-K most significant dimensions
    top_indices = np.argsort(np.abs(embedding))[-top_k:]
    top_values = embedding[top_indices]
    
    # Map to 3D points
    points = []
    for idx, value in zip(top_indices, top_values):
        x, y, z = dimension_to_3d(idx, value)
        m = idx
        points.append(f"{x} {y} {z} {m}")
    
    # Close the polygon (first point = last point)
    points.append(points[0])
    
    return f"POLYGON ZM (({', '.join(points)}))"

# Example
embedding = bert_encode("Cat is an animal")  # [768 floats]
polygon = embedding_to_polygon(embedding, top_k=10)
# Result: POLYGON ZM ((x1 y1 z1 m1, x2 y2 z2 m2, ..., x10 y10 z10 m10, x1 y1 z1 m1))
```

## Vector Arithmetic with Geometry

### King - Man + Woman = Queen

```sql
-- Geometric vector arithmetic
WITH vector_math AS (
    SELECT 
        -- King position (centroid if MULTIPOINT)
        ST_Centroid((SELECT spatial_key FROM atom WHERE canonical_text = 'King')) as king_pos,
        
        -- Man position
        ST_Centroid((SELECT spatial_key FROM atom WHERE canonical_text = 'Man')) as man_pos,
        
        -- Woman position
        ST_Centroid((SELECT spatial_key FROM atom WHERE canonical_text = 'Woman')) as woman_pos
),
result_position AS (
    SELECT ST_MakePoint(
        ST_X(king_pos) - ST_X(man_pos) + ST_X(woman_pos),
        ST_Y(king_pos) - ST_Y(man_pos) + ST_Y(woman_pos),
        ST_Z(king_pos) - ST_Z(man_pos) + ST_Z(woman_pos),
        ST_M(king_pos)  -- Keep King's M coordinate
    ) as result_point
    FROM vector_math
)
SELECT canonical_text
FROM atom, result_position
ORDER BY ST_Distance(ST_Centroid(spatial_key), result_point) ASC
LIMIT 1;

-- Result: "Queen"
```

### Cosine Similarity via Dot Product

```sql
-- Cosine similarity between two LINESTRING embeddings
CREATE OR REPLACE FUNCTION cosine_similarity_geometric(
    geom1 GEOMETRY,
    geom2 GEOMETRY
) RETURNS FLOAT AS $$
DECLARE
    vec1 FLOAT[];
    vec2 FLOAT[];
    dot_product FLOAT := 0;
    norm1 FLOAT := 0;
    norm2 FLOAT := 0;
    i INT;
BEGIN
    -- Extract coordinates from LINESTRING
    vec1 := ARRAY(
        SELECT ST_X(geom) FROM ST_DumpPoints(geom1)
    );
    vec2 := ARRAY(
        SELECT ST_X(geom) FROM ST_DumpPoints(geom2)
    );
    
    -- Calculate dot product and norms
    FOR i IN 1..ARRAY_LENGTH(vec1, 1) LOOP
        dot_product := dot_product + (vec1[i] * vec2[i]);
        norm1 := norm1 + (vec1[i] * vec1[i]);
        norm2 := norm2 + (vec2[i] * vec2[i]);
    END LOOP;
    
    -- Cosine similarity
    RETURN dot_product / (SQRT(norm1) * SQRT(norm2));
END;
$$ LANGUAGE plpgsql;

-- Usage
SELECT 
    a1.canonical_text,
    a2.canonical_text,
    cosine_similarity_geometric(a1.spatial_key, a2.spatial_key) as similarity
FROM atom a1, atom a2
WHERE a1.canonical_text = 'Cat'
  AND a2.canonical_text = 'Dog';

-- Result: 0.87 (high similarity)
```

## Performance: PostGIS vs Milvus

### PostGIS Advantages

1. **Unified Storage**
   - No separate sync between databases
   - Atomic transactions across atoms, relations, embeddings
   - Single source of truth

2. **Hilbert Indexing**
   - Works on ALL geometry types (POINT, LINESTRING, POLYGON, etc.)
   - Preserves spatial locality
   - Efficient for range queries

3. **Rich Geometric Operations**
   - ST_Distance, ST_Intersection, ST_ConvexHull, ST_Buffer
   - ST_ClusterKMeans (native clustering)
   - ST_Centroid, ST_Union, ST_Difference

4. **True to Vision**
   - Embeddings ARE atoms with coordinates
   - No special treatment for "vectors"
   - Everything atomizable

### Milvus Disadvantages

1. **Separate System**
   - Requires sync between Postgres and Milvus
   - Dual writes, eventual consistency issues
   - Extra infrastructure

2. **Violates Philosophy**
   - Treats embeddings as special "vectors"
   - Not atomizable (external storage)
   - Breaks self-contained principle

3. **Limited Operations**
   - Only similarity search (nearest neighbor)
   - No geometric operations (intersection, hull, containment)
   - No graph queries

## Migration Path

### Phase 1: Atomic Embeddings (Current)

```sql
-- Store embeddings as POINT (centroid only)
INSERT INTO atom (canonical_text, spatial_key) VALUES (
    'Cat',
    ST_MakePoint(mean(embedding), 0, 0, 0)  -- Lossy: 1D projection
);
```

### Phase 2: Geometric Embeddings (Target)

```sql
-- Store embeddings as LINESTRING or MULTIPOINT
INSERT INTO atom (canonical_text, spatial_key) VALUES (
    'Cat',
    embedding_to_linestring(bert_encode('Cat'))  -- Full 768D as geometry
);
```

### Phase 3: Hybrid Representations (Advanced)

```sql
-- Store multiple geometric representations
CREATE TABLE atom (
    ...
    spatial_key GEOMETRY(GEOMETRYZM, 4326),           -- Primary: POINT or LINESTRING
    spatial_key_hull GEOMETRY(POLYGONZM, 4326),       -- Concept space hull
    spatial_key_chunks GEOMETRY(MULTIPOINTZM, 4326),  -- Chunked dimensions
    ...
);

-- Query with appropriate representation
-- For similarity: use spatial_key (full resolution)
-- For clustering: use spatial_key_hull (concept space)
-- For sparse data: use spatial_key_chunks
```

## Example: Full Workflow

### 1. Ingest Text

```python
from transformers import AutoTokenizer, AutoModel
import numpy as np

# Load model
tokenizer = AutoTokenizer.from_pretrained("bert-base-uncased")
model = AutoModel.from_pretrained("bert-base-uncased")

# Generate embedding
text = "Cat is a feline animal"
inputs = tokenizer(text, return_tensors="pt")
outputs = model(**inputs)
embedding = outputs.last_hidden_state.mean(dim=1).squeeze().detach().numpy()  # [768]

# Convert to LINESTRING
linestring = embedding_to_linestring(embedding)

# Insert as atom
cursor.execute("""
    INSERT INTO atom (canonical_text, content_hash, spatial_key)
    VALUES (%s, %s, ST_GeomFromText(%s, 4326))
    RETURNING atom_id
""", (text, hash(text), linestring))
```

### 2. Query Similar Atoms

```sql
-- Find atoms similar to "Cat is a feline animal"
SELECT 
    canonical_text,
    ST_Distance(spatial_key, :cat_linestring) as distance
FROM atom
WHERE ST_GeometryType(spatial_key) = 'ST_LineString'
ORDER BY distance ASC
LIMIT 10;

-- Result:
-- "Dog is a canine animal" (distance: 0.12)
-- "Cat is a domestic animal" (distance: 0.15)
-- "Tiger is a feline predator" (distance: 0.18)
-- ...
```

### 3. Cluster Concepts

```sql
-- Find semantic clusters
WITH clusters AS (
    SELECT 
        atom_id,
        canonical_text,
        ST_ClusterKMeans(spatial_key, 50) OVER () as cluster_id
    FROM atom
    WHERE metadata->>'domain' = 'animals'
)
SELECT 
    cluster_id,
    ARRAY_AGG(canonical_text) as members
FROM clusters
GROUP BY cluster_id
ORDER BY cluster_id;

-- Result:
-- Cluster 1: ['Cat', 'Dog', 'Mouse', ...]  -- Domestic animals
-- Cluster 2: ['Lion', 'Tiger', 'Bear', ...]  -- Wild animals
-- Cluster 3: ['Eagle', 'Hawk', 'Owl', ...]  -- Birds
-- ...
```

### 4. Concept Space Boundaries

```sql
-- Define "feline" concept space
INSERT INTO atom_pattern (pattern_type, constituent_atom_ids, spatial_key)
SELECT 
    'concept_space',
    ARRAY_AGG(atom_id),
    ST_ConvexHull(ST_Collect(spatial_key))
FROM atom
WHERE canonical_text IN ('Cat', 'Lion', 'Tiger', 'Leopard', 'Cheetah', 'Panther');

-- Test if "Jaguar" is a feline
SELECT ST_Contains(
    (SELECT spatial_key FROM atom_pattern WHERE pattern_type = 'concept_space' LIMIT 1),
    (SELECT spatial_key FROM atom WHERE canonical_text = 'Jaguar')
) as is_feline;

-- Result: TRUE (Jaguar is within feline convex hull)
```

## Conclusion

**Embeddings are not separate "vectors" — they ARE geometric shapes living in our 4D space (X, Y, Z, M).**

By exploiting PostGIS geometry types:
- POINT: Atoms
- LINESTRING: Sequences, relations, embeddings
- MULTIPOINT: Chunked high-dimensional data
- POLYGON: Concept spaces, convex hulls
- MULTILINESTRING: Graph substructures

We get:
- ✅ Unified storage (no Milvus needed)
- ✅ Rich geometric operations (distance, intersection, hull, etc.)
- ✅ Native clustering (ST_ClusterKMeans)
- ✅ Vector arithmetic (via coordinate manipulation)
- ✅ Spatial indexing (Hilbert curves on ALL geometries)
- ✅ True to vision (everything atomizable)

**This is the way. 🚀**
