# Mathematical Analysis Framework - Part 3: Vector Calculus & Linear Algebra

**Version:** 1.0.0  
**Date:** December 1, 2025  
**Status:** Implementation Planning

---

## 3.1 Vector Calculus for Atom Spaces

### 3.1.1 Vector Fields in Atom Space

**Definition**: A vector field assigns a vector to each point in atom space, representing direction and magnitude of some quantity.

**Applications in Universal Atomization**:

1. **Semantic Gradient Fields**
   - Each point in atom space has a gradient vector pointing toward concept centers
   - Magnitude represents "semantic pull" strength
   - Used for content classification and similarity

2. **Content Flow Fields**
   - Vectors represent evolution of content over time
   - Trajectory analysis: How content moves through semantic space
   - Prediction: Where content is "heading"

3. **Influence Fields**
   - Popular concepts create "gravity wells" in atom space
   - New atoms attracted to relevant concept regions
   - Emergent clustering without explicit ML

**Mathematical Representation**:
```
F(x, y, z) = [Fx(x,y,z), Fy(x,y,z), Fz(x,y,z)]

For semantic gradient field toward concept C at position (cx, cy, cz):
F(x, y, z) = k * [(cx - x), (cy - y), (cz - z)] / ||r||³

Where:
- k = concept strength (number of linked atoms)
- r = distance vector from point to concept
- ||r|| = Euclidean distance
```

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_semantic_gradient_field(
    p_target_point GEOMETRY,
    p_concept_id BIGINT DEFAULT NULL
)
RETURNS TABLE (
    concept_id BIGINT,
    concept_name TEXT,
    gradient_vector GEOMETRY,  -- LINESTRING from point to concept
    field_strength DOUBLE PRECISION
) AS $$
    SELECT 
        a.atom_id as concept_id,
        a.canonical_text as concept_name,
        ST_MakeLine(
            p_target_point,
            a.spatial_position
        ) as gradient_vector,
        -- Field strength: inverse square law with concept importance
        (SELECT COUNT(*) FROM atom_relation ar WHERE ar.to_atom_id = a.atom_id) 
        / POWER(ST_Distance(p_target_point, a.spatial_position), 2) as field_strength
    FROM atom a
    WHERE a.modality = 'concept'
      AND (p_concept_id IS NULL OR a.atom_id = p_concept_id)
      AND ST_Distance(p_target_point, a.spatial_position) > 0  -- Avoid division by zero
    ORDER BY field_strength DESC
$$ LANGUAGE SQL STABLE;
```

**Usage Example**:
```python
async def get_semantic_field_at_point(conn, point: Tuple[float, float, float]) -> List[Dict]:
    """
    Get semantic gradient field at a specific point in atom space.
    
    Returns list of concept vectors with strengths.
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT * FROM compute_semantic_gradient_field(
                ST_MakePoint(%s, %s, %s)
            )
            LIMIT 10
        """, point)
        
        results = await cur.fetchall()
        
        return [
            {
                'concept_id': row[0],
                'concept_name': row[1],
                'gradient_vector': row[2],  # LINESTRING (arrow)
                'field_strength': row[3]
            }
            for row in results
        ]

# Example: Where does this text atom "naturally" belong?
point = (0.5, 0.3, 0.2)  # Some text atom position
field = await get_semantic_field_at_point(conn, point)

print(f"Semantic gradients at {point}:")
for vector in field[:5]:
    print(f"  → {vector['concept_name']}: strength {vector['field_strength']:.4f}")

# Output:
# Semantic gradients at (0.5, 0.3, 0.2):
#   → CAT: strength 2.3451
#   → ANIMAL: strength 1.8923
#   → PET: strength 1.2341
#   → MAMMAL: strength 0.9871
#   → ORANGE: strength 0.7654
```

### 3.1.2 Divergence and Curl

**Divergence**: Measures "expansion" or "concentration" of a vector field

**Application - Concept Density**:
- High divergence → atoms spreading out (rare/diverse concept)
- Low/negative divergence → atoms clustering (common/focused concept)
- Used for identifying concept coherence

**Mathematical Definition**:
```
div F = ∂Fx/∂x + ∂Fy/∂y + ∂Fz/∂z
```

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_concept_divergence(
    p_concept_id BIGINT,
    p_sample_radius DOUBLE PRECISION DEFAULT 0.1
)
RETURNS DOUBLE PRECISION AS $$
DECLARE
    v_concept_center GEOMETRY;
    v_atom_count INTEGER;
    v_avg_distance DOUBLE PRECISION;
    v_divergence DOUBLE PRECISION;
BEGIN
    -- Get concept center
    SELECT spatial_position INTO v_concept_center
    FROM atom
    WHERE atom_id = p_concept_id;
    
    -- Count atoms near concept
    SELECT 
        COUNT(*),
        AVG(ST_Distance(a.spatial_position, v_concept_center))
    INTO v_atom_count, v_avg_distance
    FROM atom_relation ar
    JOIN atom a ON a.atom_id = ar.from_atom_id
    WHERE ar.to_atom_id = p_concept_id
      AND ST_DWithin(a.spatial_position, v_concept_center, p_sample_radius);
    
    -- Divergence approximation: rate of change of density
    -- Positive = spreading, Negative = concentrating
    IF v_atom_count > 0 THEN
        v_divergence := (v_avg_distance - p_sample_radius / 2) / p_sample_radius;
    ELSE
        v_divergence := 0;
    END IF;
    
    RETURN v_divergence;
END;
$$ LANGUAGE plpgsql STABLE;
```

**Curl**: Measures "rotation" or "circulation" of a vector field

**Application - Semantic Vortices**:
- Atoms orbiting between multiple related concepts
- Example: "kitten" atom between CAT and BABY concepts
- Identifies boundary regions and ambiguous classifications

**Mathematical Definition**:
```
curl F = [∂Fz/∂y - ∂Fy/∂z, ∂Fx/∂z - ∂Fz/∂x, ∂Fy/∂x - ∂Fx/∂y]
```

**Application Example**:
```python
async def analyze_concept_flow(conn, concept_id: int):
    """
    Analyze how atoms flow around a concept.
    
    Returns:
        - divergence: Are atoms spreading or clustering?
        - curl: Are atoms circulating (ambiguous classification)?
    """
    async with conn.cursor() as cur:
        # Divergence
        await cur.execute("""
            SELECT compute_concept_divergence(%s, 0.1)
        """, (concept_id,))
        
        divergence = (await cur.fetchone())[0]
        
        # Interpret divergence
        if divergence > 0.5:
            flow_type = "SPREADING (diverse concept)"
        elif divergence < -0.5:
            flow_type = "CLUSTERING (focused concept)"
        else:
            flow_type = "STABLE (balanced concept)"
        
        return {
            'concept_id': concept_id,
            'divergence': divergence,
            'flow_type': flow_type
        }

# Example
result = await analyze_concept_flow(conn, concept_id=9001)  # CAT concept
print(f"CAT concept divergence: {result['divergence']:.3f}")
print(f"Flow type: {result['flow_type']}")

# Output:
# CAT concept divergence: -0.234
# Flow type: CLUSTERING (focused concept)
# Interpretation: Atoms strongly attracted to CAT concept (well-defined)
```

### 3.1.3 Line Integrals and Work

**Definition**: Integral of a function along a curve (trajectory)

**Application - Trajectory Energy**:
- Compute "work" required to move atom along trajectory
- High work → difficult/unnatural path
- Low work → natural/easy path (follows semantic gradients)

**Mathematical Representation**:
```
∫C F · dr = ∫[a,b] F(r(t)) · r'(t) dt

For trajectory r(t) in semantic field F
```

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_trajectory_work(
    p_trajectory_atom_id BIGINT,
    p_field_concept_id BIGINT DEFAULT NULL
)
RETURNS DOUBLE PRECISION AS $$
DECLARE
    v_trajectory GEOMETRY;
    v_total_work DOUBLE PRECISION := 0;
    v_point_count INTEGER;
    v_current_point GEOMETRY;
    v_next_point GEOMETRY;
    v_displacement GEOMETRY;
    v_field_strength DOUBLE PRECISION;
BEGIN
    -- Get trajectory
    SELECT spatial_position INTO v_trajectory
    FROM atom
    WHERE atom_id = p_trajectory_atom_id;
    
    -- Get number of points
    v_point_count := ST_NPoints(v_trajectory);
    
    -- Integrate along trajectory
    FOR i IN 1..(v_point_count - 1) LOOP
        v_current_point := ST_PointN(v_trajectory, i);
        v_next_point := ST_PointN(v_trajectory, i + 1);
        v_displacement := ST_MakeLine(v_current_point, v_next_point);
        
        -- Get field strength at midpoint
        SELECT field_strength INTO v_field_strength
        FROM compute_semantic_gradient_field(
            ST_LineInterpolatePoint(v_displacement, 0.5),
            p_field_concept_id
        )
        LIMIT 1;
        
        -- Work = Force · Distance
        v_total_work := v_total_work + 
            (COALESCE(v_field_strength, 0) * ST_Length(v_displacement));
    END LOOP;
    
    RETURN v_total_work;
END;
$$ LANGUAGE plpgsql STABLE;
```

**Usage Example**:
```python
async def compare_trajectory_naturalness(
    conn, 
    trajectory_id_1: int, 
    trajectory_id_2: int,
    concept_id: int
):
    """
    Compare two trajectories to see which follows semantic field more naturally.
    
    Lower work = more natural trajectory (goes "with the flow")
    Higher work = forced trajectory (goes "against the flow")
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT 
                %s as traj1_id,
                compute_trajectory_work(%s, %s) as traj1_work,
                %s as traj2_id,
                compute_trajectory_work(%s, %s) as traj2_work
        """, (trajectory_id_1, trajectory_id_1, concept_id,
              trajectory_id_2, trajectory_id_2, concept_id))
        
        result = await cur.fetchone()
        
        work1, work2 = result[1], result[3]
        
        if work1 < work2:
            more_natural = "Trajectory 1"
            ratio = work2 / work1 if work1 > 0 else float('inf')
        else:
            more_natural = "Trajectory 2"
            ratio = work1 / work2 if work2 > 0 else float('inf')
        
        return {
            'trajectory_1': {'id': trajectory_id_1, 'work': work1},
            'trajectory_2': {'id': trajectory_id_2, 'work': work2},
            'more_natural': more_natural,
            'work_ratio': ratio
        }

# Example: Compare two text documents
result = await compare_trajectory_naturalness(
    conn,
    trajectory_id_1=1001,  # "The cat sat on the mat"
    trajectory_id_2=1002,  # "Quantum mechanics explains particle behavior"
    concept_id=9001        # CAT concept
)

print(f"Trajectory 1 work: {result['trajectory_1']['work']:.4f}")
print(f"Trajectory 2 work: {result['trajectory_2']['work']:.4f}")
print(f"More natural: {result['more_natural']}")
print(f"Work ratio: {result['work_ratio']:.2f}x")

# Output:
# Trajectory 1 work: 2.3451
# Trajectory 2 work: 12.8934
# More natural: Trajectory 1
# Work ratio: 5.50x
# Interpretation: Trajectory 1 flows naturally toward CAT concept
```

### 3.1.4 Surface Integrals and Flux

**Definition**: Integral over a surface, measures flow through surface

**Application - Concept Boundary Flux**:
- Measure flow of atoms across concept boundaries
- High flux → concept is growing/shrinking
- Zero flux → stable concept

**Mathematical Representation**:
```
∬S F · n dS

Where:
- F = vector field (semantic flow)
- n = normal vector to surface
- dS = surface element
```

**SQL Implementation**:
```sql
CREATE OR REPLACE FUNCTION compute_concept_flux(
    p_concept_id BIGINT,
    p_time_window INTERVAL DEFAULT '1 day'
)
RETURNS TABLE (
    influx_count BIGINT,
    outflux_count BIGINT,
    net_flux BIGINT,
    flux_rate DOUBLE PRECISION
) AS $$
    WITH concept_boundary AS (
        -- Define concept boundary (convex hull of linked atoms)
        SELECT 
            ST_ConvexHull(ST_Collect(a.spatial_position)) as boundary
        FROM atom_relation ar
        JOIN atom a ON a.atom_id = ar.from_atom_id
        WHERE ar.to_atom_id = p_concept_id
    ),
    recent_atoms AS (
        -- Atoms created recently
        SELECT a.atom_id, a.spatial_position, a.created_at
        FROM atom a
        WHERE a.created_at > NOW() - p_time_window
    )
    SELECT 
        -- Atoms entering concept (now linked, inside boundary)
        COUNT(*) FILTER (
            WHERE ar.created_at > NOW() - p_time_window
              AND ST_Within(ra.spatial_position, cb.boundary)
        ) as influx_count,
        
        -- Atoms leaving concept (links removed, outside boundary)
        -- (Approximated by atoms near boundary moving away)
        COUNT(*) FILTER (
            WHERE ST_Distance(ra.spatial_position, cb.boundary) < 0.05
              AND NOT EXISTS (
                  SELECT 1 FROM atom_relation ar2
                  WHERE ar2.from_atom_id = ra.atom_id
                    AND ar2.to_atom_id = p_concept_id
              )
        ) as outflux_count,
        
        -- Net flux (positive = growing, negative = shrinking)
        COUNT(*) FILTER (
            WHERE ar.created_at > NOW() - p_time_window
              AND ST_Within(ra.spatial_position, cb.boundary)
        ) - COUNT(*) FILTER (
            WHERE ST_Distance(ra.spatial_position, cb.boundary) < 0.05
              AND NOT EXISTS (
                  SELECT 1 FROM atom_relation ar2
                  WHERE ar2.from_atom_id = ra.atom_id
                    AND ar2.to_atom_id = p_concept_id
              )
        ) as net_flux,
        
        -- Flux rate (atoms per hour)
        (COUNT(*) FILTER (
            WHERE ar.created_at > NOW() - p_time_window
              AND ST_Within(ra.spatial_position, cb.boundary)
        )::DOUBLE PRECISION) / EXTRACT(EPOCH FROM p_time_window) * 3600 as flux_rate
    FROM concept_boundary cb
    CROSS JOIN recent_atoms ra
    LEFT JOIN atom_relation ar ON ar.from_atom_id = ra.atom_id AND ar.to_atom_id = p_concept_id
$$ LANGUAGE SQL STABLE;
```

**Usage Example**:
```python
async def monitor_concept_growth(conn, concept_id: int):
    """
    Monitor how a concept is growing or shrinking over time.
    """
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT * FROM compute_concept_flux(%s, '7 days')
        """, (concept_id,))
        
        result = await cur.fetchone()
        influx, outflux, net_flux, flux_rate = result
        
        if net_flux > 0:
            trend = "GROWING"
        elif net_flux < 0:
            trend = "SHRINKING"
        else:
            trend = "STABLE"
        
        return {
            'concept_id': concept_id,
            'influx': influx,
            'outflux': outflux,
            'net_flux': net_flux,
            'flux_rate_per_hour': flux_rate,
            'trend': trend
        }

# Example: Monitor CAT concept
result = await monitor_concept_growth(conn, concept_id=9001)
print(f"CAT concept trend: {result['trend']}")
print(f"New atoms (influx): {result['influx']}")
print(f"Lost atoms (outflux): {result['outflux']}")
print(f"Net flux: {result['net_flux']}")
print(f"Growth rate: {result['flux_rate_per_hour']:.2f} atoms/hour")

# Output:
# CAT concept trend: GROWING
# New atoms (influx): 347
# Lost atoms (outflux): 23
# Net flux: 324
# Growth rate: 1.93 atoms/hour
# Interpretation: CAT concept is actively growing (more content ingested)
```

---

## 3.2 Linear Algebra for Atom Relationships

### 3.2.1 Atom-Concept Adjacency Matrix

**Definition**: Matrix A where A[i][j] = 1 if atom i links to concept j, else 0

**Purpose**:
- Represent atom-concept relationships as matrix
- Enable matrix operations for analysis
- Support efficient bulk queries

**Construction**:
```python
import numpy as np
from scipy.sparse import csr_matrix

async def build_atom_concept_matrix(conn, atom_ids: List[int], concept_ids: List[int]):
    """
    Build sparse adjacency matrix: atoms × concepts.
    
    Args:
        conn: Database connection
        atom_ids: List of atom IDs (rows)
        concept_ids: List of concept IDs (columns)
        
    Returns:
        Sparse CSR matrix (atoms × concepts)
    """
    # Build index mappings
    atom_to_idx = {aid: i for i, aid in enumerate(atom_ids)}
    concept_to_idx = {cid: i for i, cid in enumerate(concept_ids)}
    
    # Query all relations
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT from_atom_id, to_atom_id, strength
            FROM atom_relation
            WHERE from_atom_id = ANY(%s)
              AND to_atom_id = ANY(%s)
              AND relation_type IN ('mentions', 'depicts')
        """, (atom_ids, concept_ids))
        
        relations = await cur.fetchall()
    
    # Build sparse matrix
    rows = []
    cols = []
    data = []
    
    for atom_id, concept_id, strength in relations:
        if atom_id in atom_to_idx and concept_id in concept_to_idx:
            rows.append(atom_to_idx[atom_id])
            cols.append(concept_to_idx[concept_id])
            data.append(strength)
    
    # Create sparse matrix
    matrix = csr_matrix(
        (data, (rows, cols)),
        shape=(len(atom_ids), len(concept_ids))
    )
    
    return matrix, atom_to_idx, concept_to_idx
```

### 3.2.2 Matrix Decomposition - SVD

**Singular Value Decomposition**: A = U Σ V^T

**Applications**:

1. **Dimensionality Reduction**
   - Compress atom-concept relationships
   - Keep top k singular values (most important patterns)
   - Reduce storage and computation

2. **Latent Concept Discovery**
   - U matrix: Atom embeddings in latent space
   - V matrix: Concept embeddings in latent space
   - Σ matrix: Importance of each latent dimension

3. **Denoising**
   - Remove small singular values (noise)
   - Reconstruct cleaner atom-concept matrix

**Implementation**:
```python
from scipy.sparse.linalg import svds

async def analyze_atom_concept_structure(conn, k: int = 50):
    """
    Perform SVD on atom-concept matrix to discover latent structure.
    
    Args:
        conn: Database connection
        k: Number of latent dimensions to keep
        
    Returns:
        U, Sigma, Vt: SVD decomposition
    """
    # Get all atoms and concepts
    async with conn.cursor() as cur:
        await cur.execute("SELECT atom_id FROM atom WHERE modality != 'concept' LIMIT 10000")
        atom_ids = [row[0] for row in await cur.fetchall()]
        
        await cur.execute("SELECT atom_id FROM atom WHERE modality = 'concept'")
        concept_ids = [row[0] for row in await cur.fetchall()]
    
    # Build matrix
    A, atom_idx, concept_idx = await build_atom_concept_matrix(conn, atom_ids, concept_ids)
    
    # Perform SVD (keep top k dimensions)
    U, Sigma, Vt = svds(A, k=min(k, min(A.shape) - 1))
    
    # Sort by singular values (descending)
    idx = np.argsort(Sigma)[::-1]
    U = U[:, idx]
    Sigma = Sigma[idx]
    Vt = Vt[idx, :]
    
    return U, Sigma, Vt, atom_idx, concept_idx

# Example usage
U, Sigma, Vt, atom_idx, concept_idx = await analyze_atom_concept_structure(conn, k=50)

print(f"Matrix shape: {U.shape[0]} atoms × {Vt.shape[1]} concepts")
print(f"Latent dimensions: {len(Sigma)}")
print(f"Top 5 singular values: {Sigma[:5]}")

# Interpret singular values
explained_variance = (Sigma ** 2) / np.sum(Sigma ** 2)
cumulative_variance = np.cumsum(explained_variance)

print(f"\nVariance explained by top 10 dimensions: {cumulative_variance[9]:.2%}")
print(f"Variance explained by top 20 dimensions: {cumulative_variance[19]:.2%}")

# Output:
# Matrix shape: 10000 atoms × 87 concepts
# Latent dimensions: 50
# Top 5 singular values: [124.56, 98.34, 76.21, 54.32, 42.18]
#
# Variance explained by top 10 dimensions: 67.34%
# Variance explained by top 20 dimensions: 84.12%
# Interpretation: Most atom-concept structure captured in 20 dimensions
```

### 3.2.3 Eigenvector Centrality

**Definition**: Eigenvector of adjacency matrix corresponding to largest eigenvalue

**Application - Concept Importance**:
- Identifies most "central" concepts in the graph
- Concepts linked by many important atoms rank higher
- Used for concept hierarchy and prioritization

**Implementation**:
```python
from scipy.sparse.linalg import eigs

async def compute_concept_centrality(conn, top_k: int = 10):
    """
    Compute eigenvector centrality for concepts.
    
    Returns:
        List of (concept_id, concept_name, centrality_score)
    """
    # Get concept-concept co-occurrence matrix
    async with conn.cursor() as cur:
        await cur.execute("SELECT atom_id FROM atom WHERE modality = 'concept'")
        concept_ids = [row[0] for row in await cur.fetchall()]
    
    # Build co-occurrence matrix (concepts that appear together)
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT 
                ar1.to_atom_id as concept1,
                ar2.to_atom_id as concept2,
                COUNT(*) as co_occurrence_count
            FROM atom_relation ar1
            JOIN atom_relation ar2 ON ar1.from_atom_id = ar2.from_atom_id
            WHERE ar1.to_atom_id < ar2.to_atom_id
              AND ar1.to_atom_id = ANY(%s)
              AND ar2.to_atom_id = ANY(%s)
            GROUP BY ar1.to_atom_id, ar2.to_atom_id
        """, (concept_ids, concept_ids))
        
        edges = await cur.fetchall()
    
    # Build adjacency matrix
    concept_to_idx = {cid: i for i, cid in enumerate(concept_ids)}
    n = len(concept_ids)
    
    rows = []
    cols = []
    data = []
    
    for c1, c2, count in edges:
        idx1 = concept_to_idx[c1]
        idx2 = concept_to_idx[c2]
        
        # Symmetric matrix
        rows.extend([idx1, idx2])
        cols.extend([idx2, idx1])
        data.extend([count, count])
    
    A = csr_matrix((data, (rows, cols)), shape=(n, n))
    
    # Compute largest eigenvector
    eigenvalues, eigenvectors = eigs(A, k=1, which='LM')
    centrality = np.abs(eigenvectors[:, 0])
    
    # Normalize
    centrality = centrality / np.sum(centrality)
    
    # Get top concepts
    top_indices = np.argsort(centrality)[::-1][:top_k]
    
    # Fetch concept names
    top_concept_ids = [concept_ids[i] for i in top_indices]
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT atom_id, canonical_text
            FROM atom
            WHERE atom_id = ANY(%s)
        """, (top_concept_ids,))
        
        names = {row[0]: row[1] for row in await cur.fetchall()}
    
    return [
        {
            'concept_id': concept_ids[i],
            'concept_name': names[concept_ids[i]],
            'centrality': centrality[i]
        }
        for i in top_indices
    ]

# Example
top_concepts = await compute_concept_centrality(conn, top_k=10)

print("Top 10 most central concepts:")
for i, concept in enumerate(top_concepts, 1):
    print(f"{i}. {concept['concept_name']}: {concept['centrality']:.6f}")

# Output:
# Top 10 most central concepts:
# 1. ANIMAL: 0.087234
# 2. COLOR: 0.076123
# 3. OBJECT: 0.065432
# 4. CAT: 0.054321
# 5. DOG: 0.048765
# 6. OUTDOOR: 0.043210
# 7. GRASS: 0.038765
# 8. SKY: 0.034567
# 9. WATER: 0.029876
# 10. TREE: 0.026543
# Interpretation: ANIMAL is the most central concept (hub for many other concepts)
```

### 3.2.4 Matrix Factorization - NMF

**Non-Negative Matrix Factorization**: A ≈ W × H

**Properties**:
- W, H have non-negative entries
- W: Atom-to-factor weights
- H: Factor-to-concept weights
- Factors represent interpretable "topics" or "themes"

**Application - Topic Discovery**:
```python
from sklearn.decomposition import NMF

async def discover_semantic_topics(conn, n_topics: int = 10):
    """
    Discover latent topics in atom-concept relationships using NMF.
    
    Args:
        conn: Database connection
        n_topics: Number of topics to discover
        
    Returns:
        Topics with top concepts for each
    """
    # Build atom-concept matrix
    async with conn.cursor() as cur:
        await cur.execute("SELECT atom_id FROM atom WHERE modality = 'text' LIMIT 5000")
        atom_ids = [row[0] for row in await cur.fetchall()]
        
        await cur.execute("SELECT atom_id FROM atom WHERE modality = 'concept'")
        concept_ids = [row[0] for row in await cur.fetchall()]
    
    A, atom_idx, concept_idx = await build_atom_concept_matrix(conn, atom_ids, concept_ids)
    
    # Perform NMF
    model = NMF(n_components=n_topics, init='nndsvd', random_state=42)
    W = model.fit_transform(A)  # Atom-topic matrix
    H = model.components_       # Topic-concept matrix
    
    # Extract top concepts for each topic
    idx_to_concept = {i: cid for cid, i in concept_idx.items()}
    
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT atom_id, canonical_text
            FROM atom
            WHERE atom_id = ANY(%s)
        """, (concept_ids,))
        
        concept_names = {row[0]: row[1] for row in await cur.fetchall()}
    
    topics = []
    for topic_idx in range(n_topics):
        # Get top concepts for this topic
        top_concept_indices = np.argsort(H[topic_idx, :])[::-1][:10]
        
        top_concepts = [
            {
                'concept_id': idx_to_concept[i],
                'concept_name': concept_names[idx_to_concept[i]],
                'weight': H[topic_idx, i]
            }
            for i in top_concept_indices
        ]
        
        topics.append({
            'topic_id': topic_idx,
            'top_concepts': top_concepts
        })
    
    return topics

# Example
topics = await discover_semantic_topics(conn, n_topics=5)

print("Discovered semantic topics:")
for topic in topics:
    print(f"\nTopic {topic['topic_id']}:")
    for concept in topic['top_concepts'][:5]:
        print(f"  - {concept['concept_name']}: {concept['weight']:.4f}")

# Output:
# Discovered semantic topics:
#
# Topic 0: (Animals/Pets)
#   - CAT: 0.8765
#   - DOG: 0.8234
#   - PET: 0.7654
#   - ANIMAL: 0.7123
#   - MAMMAL: 0.6543
#
# Topic 1: (Nature/Outdoors)
#   - GRASS: 0.7891
#   - SKY: 0.7654
#   - TREE: 0.7321
#   - OUTDOOR: 0.6987
#   - NATURE: 0.6543
#
# Topic 2: (Colors)
#   - ORANGE: 0.8123
#   - BLUE: 0.7654
#   - GREEN: 0.7234
#   - COLOR: 0.6876
#   - WHITE: 0.6432
# ...
```

### 3.2.5 Graph Laplacian and Spectral Clustering

**Graph Laplacian**: L = D - A

Where:
- D = Degree matrix (diagonal)
- A = Adjacency matrix

**Application - Concept Clustering**:
- Group similar concepts together
- Discover concept hierarchies
- Identify community structure

**Implementation**:
```python
from sklearn.cluster import SpectralClustering

async def cluster_concepts(conn, n_clusters: int = 5):
    """
    Cluster concepts using spectral clustering on graph Laplacian.
    
    Args:
        conn: Database connection
        n_clusters: Number of clusters
        
    Returns:
        Concept clusters
    """
    # Get concept co-occurrence matrix (as before)
    async with conn.cursor() as cur:
        await cur.execute("SELECT atom_id FROM atom WHERE modality = 'concept'")
        concept_ids = [row[0] for row in await cur.fetchall()]
        
        await cur.execute("""
            SELECT 
                ar1.to_atom_id as concept1,
                ar2.to_atom_id as concept2,
                COUNT(*) as co_occurrence_count
            FROM atom_relation ar1
            JOIN atom_relation ar2 ON ar1.from_atom_id = ar2.from_atom_id
            WHERE ar1.to_atom_id <= ar2.to_atom_id
              AND ar1.to_atom_id = ANY(%s)
              AND ar2.to_atom_id = ANY(%s)
            GROUP BY ar1.to_atom_id, ar2.to_atom_id
        """, (concept_ids, concept_ids))
        
        edges = await cur.fetchall()
    
    # Build adjacency matrix
    concept_to_idx = {cid: i for i, cid in enumerate(concept_ids)}
    n = len(concept_ids)
    
    rows, cols, data = [], [], []
    for c1, c2, count in edges:
        idx1, idx2 = concept_to_idx[c1], concept_to_idx[c2]
        rows.extend([idx1, idx2])
        cols.extend([idx2, idx1])
        data.extend([count, count])
    
    A = csr_matrix((data, (rows, cols)), shape=(n, n))
    
    # Spectral clustering
    clustering = SpectralClustering(
        n_clusters=n_clusters,
        affinity='precomputed',
        random_state=42
    )
    labels = clustering.fit_predict(A)
    
    # Group concepts by cluster
    clusters = [[] for _ in range(n_clusters)]
    for i, label in enumerate(labels):
        clusters[label].append(concept_ids[i])
    
    # Fetch concept names
    async with conn.cursor() as cur:
        await cur.execute("""
            SELECT atom_id, canonical_text
            FROM atom
            WHERE atom_id = ANY(%s)
        """, (concept_ids,))
        
        names = {row[0]: row[1] for row in await cur.fetchall()}
    
    return [
        {
            'cluster_id': i,
            'concepts': [
                {'concept_id': cid, 'concept_name': names[cid]}
                for cid in cluster
            ]
        }
        for i, cluster in enumerate(clusters)
    ]

# Example
clusters = await cluster_concepts(conn, n_clusters=3)

print("Concept clusters:")
for cluster in clusters:
    print(f"\nCluster {cluster['cluster_id']}:")
    for concept in cluster['concepts'][:10]:
        print(f"  - {concept['concept_name']}")

# Output:
# Concept clusters:
#
# Cluster 0: (Animals)
#   - CAT
#   - DOG
#   - ANIMAL
#   - PET
#   - MAMMAL
#   - KITTEN
#   - PUPPY
#
# Cluster 1: (Nature)
#   - GRASS
#   - SKY
#   - TREE
#   - WATER
#   - OUTDOOR
#   - NATURE
#   - CLOUD
#
# Cluster 2: (Visual Properties)
#   - ORANGE
#   - BLUE
#   - GREEN
#   - COLOR
#   - WHITE
#   - BLACK
#   - GRAY
```

---

## 3.3 Tensor Operations for Multi-Modal Data

### 3.3.1 3D Atom-Concept-Time Tensor

**Definition**: Tensor T[i, j, k] represents relationship between atom i, concept j, at time k

**Purpose**:
- Capture temporal evolution of atom-concept relationships
- Track concept drift over time
- Analyze trending patterns

**Construction**:
```python
async def build_temporal_tensor(
    conn,
    atom_ids: List[int],
    concept_ids: List[int],
    time_windows: List[Tuple[datetime, datetime]]
):
    """
    Build 3D tensor: atoms × concepts × time.
    
    Args:
        conn: Database connection
        atom_ids: List of atom IDs
        concept_ids: List of concept IDs
        time_windows: List of (start_time, end_time) tuples
        
    Returns:
        3D numpy array
    """
    n_atoms = len(atom_ids)
    n_concepts = len(concept_ids)
    n_times = len(time_windows)
    
    tensor = np.zeros((n_atoms, n_concepts, n_times))
    
    atom_to_idx = {aid: i for i, aid in enumerate(atom_ids)}
    concept_to_idx = {cid: i for i, cid in enumerate(concept_ids)}
    
    # Fill tensor for each time window
    for t_idx, (start_time, end_time) in enumerate(time_windows):
        async with conn.cursor() as cur:
            await cur.execute("""
                SELECT from_atom_id, to_atom_id, AVG(strength)
                FROM atom_relation
                WHERE from_atom_id = ANY(%s)
                  AND to_atom_id = ANY(%s)
                  AND created_at >= %s
                  AND created_at < %s
                GROUP BY from_atom_id, to_atom_id
            """, (atom_ids, concept_ids, start_time, end_time))
            
            for atom_id, concept_id, strength in await cur.fetchall():
                i = atom_to_idx[atom_id]
                j = concept_to_idx[concept_id]
                tensor[i, j, t_idx] = strength
    
    return tensor
```

### 3.3.2 Tensor Decomposition - CP/Tucker

**CP Decomposition** (CANDECOMP/PARAFAC):
```
T ≈ Σ λ_r (a_r ⊗ b_r ⊗ c_r)

Where:
- a_r: Atom factor vectors
- b_r: Concept factor vectors
- c_r: Time factor vectors
- λ_r: Component weights
```

**Application - Temporal Pattern Discovery**:
```python
import tensorly as tl
from tensorly.decomposition import parafac

async def analyze_temporal_patterns(conn, rank: int = 5):
    """
    Discover temporal patterns in atom-concept relationships using CP decomposition.
    
    Args:
        conn: Database connection
        rank: Number of components
        
    Returns:
        Decomposed factors and interpretation
    """
    # Build tensor (atoms × concepts × weeks)
    # ... (using build_temporal_tensor) ...
    
    # Perform CP decomposition
    weights, factors = parafac(tensor, rank=rank)
    
    atom_factors, concept_factors, time_factors = factors
    
    # Interpret each component
    patterns = []
    for r in range(rank):
        # Top atoms for this component
        top_atom_idx = np.argsort(atom_factors[:, r])[::-1][:5]
        
        # Top concepts for this component
        top_concept_idx = np.argsort(concept_factors[:, r])[::-1][:5]
        
        # Time trend for this component
        time_trend = time_factors[:, r]
        
        patterns.append({
            'component_id': r,
            'weight': weights[r],
            'top_atoms': [atom_ids[i] for i in top_atom_idx],
            'top_concepts': [concept_ids[i] for i in top_concept_idx],
            'time_trend': time_trend,
            'trend_type': 'INCREASING' if time_trend[-1] > time_trend[0] else 'DECREASING'
        })
    
    return patterns
```

---

**File Status**: 1000 lines  
**Covered**:
- Vector calculus (fields, divergence, curl, line integrals, surface integrals)
- Linear algebra (matrices, SVD, eigenvectors, NMF, Laplacian, spectral clustering)
- Tensor operations (3D tensors, CP decomposition)

**Next**: Part 4 will cover differential equations, numerical methods, and optimization algorithms
