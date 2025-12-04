# Relations

**Semantic connections: How atoms relate, Hebbian learning, and the knowledge graph.**

---

## What Are Relations?

**Relations** define semantic connections between atoms via typed, weighted edges.

```sql
Source Atom --[Relation Type, Weight]--> Target Atom
```

**Examples:**
- `'machine'` --[semantic_pair, 0.95]--> `'learning'`
- `'cat'` --[semantic_similar, 0.70]--> `'dog'`
- `QueryAtom` --[produced_result, 1.0]--> `ResultAtom`
- `NeuronA` --[temporal_precedes, 0.80]--> `NeuronB`

**Key principle:** Relations capture **how atoms connect semantically**, forming a knowledge graph.

---

## Relation Table Schema

### Complete DDL

```sql
CREATE TABLE atom_relation (
    -- Identity
    relation_id BIGSERIAL PRIMARY KEY,
    
    -- Source and target atoms
    source_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    target_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    
    -- Relation type is itself an atom!
    relation_type_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE RESTRICT,
    
    -- Synaptic weights (Hebbian learning)
    weight REAL NOT NULL DEFAULT 0.5 CHECK (weight >= 0.0 AND weight <= 1.0),
    confidence REAL NOT NULL DEFAULT 0.5 CHECK (confidence >= 0.0 AND confidence <= 1.0),
    importance REAL NOT NULL DEFAULT 0.5 CHECK (importance >= 0.0 AND importance <= 1.0),
    
    -- Geometric path through semantic space
    spatial_expression GEOMETRY(LINESTRINGZ, 0),
    
    -- Flexible metadata
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Tracking
    last_accessed TIMESTAMPTZ NOT NULL DEFAULT now(),
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz,
    
    -- Uniqueness: one relation of each type between same atoms
    UNIQUE (source_atom_id, target_atom_id, relation_type_id)
);

-- Critical indexes
CREATE INDEX idx_relation_source ON atom_relation(source_atom_id);
CREATE INDEX idx_relation_target ON atom_relation(target_atom_id);
CREATE INDEX idx_relation_weight ON atom_relation(weight DESC);
CREATE INDEX idx_relation_type ON atom_relation(relation_type_id);
```

### Field Descriptions

| Field | Type | Purpose |
|-------|------|---------|
| `relation_id` | BIGSERIAL | Unique identifier |
| `source_atom_id` | BIGINT | Origin atom of relation |
| `target_atom_id` | BIGINT | Destination atom of relation |
| `relation_type_id` | BIGINT | Type of relation (itself an atom!) |
| `weight` | REAL | Synaptic efficacy (0.0-1.0) |
| `confidence` | REAL | Confidence in relation (0.0-1.0) |
| `importance` | REAL | Importance/relevance (0.0-1.0) |
| `spatial_expression` | GEOMETRY(LINESTRINGZ) | Geometric path (source ? target) |
| `last_accessed` | TIMESTAMPTZ | Last traversal time (for decay) |

---

## Relation Types

### Relation Types Are Atoms

**Key insight:** Relation types are stored in the `atom` table.

```sql
-- Create relation type atoms
INSERT INTO atom (content_hash, canonical_text, metadata)
VALUES
    (sha256('semantic_pair'), 'semantic_pair', '{"type":"relation_type"}'),
    (sha256('semantic_similar'), 'semantic_similar', '{"type":"relation_type"}'),
    (sha256('causes'), 'causes', '{"type":"relation_type"}'),
    (sha256('produced_result'), 'produced_result', '{"type":"relation_type"}');
```

**Benefits:**
- Relation types are first-class entities
- Can have their own relations (meta-relations)
- Can be positioned in semantic space
- Can be queried like any atom

### Common Relation Types

| Type | Meaning | Example |
|------|---------|---------|
| **semantic_pair** | Strong co-occurrence | `'machine'` ? `'learning'` |
| **semantic_similar** | Semantic similarity | `'cat'` ? `'dog'` |
| **semantic_opposite** | Antonym | `'hot'` ? `'cold'` |
| **causes** | Causal relationship | `'rain'` ? `'wet'` |
| **produced_result** | Provenance | `QueryAtom` ? `ResultAtom` |
| **temporal_precedes** | Temporal order | `EventA` ? `EventB` |
| **is_a** | Taxonomy | `'dog'` ? `'animal'` |
| **has_a** | Composition | `'car'` ? `'wheel'` |
| **part_of** | Membership | `'wheel'` ? `'car'` |

---

## Hebbian Learning

### "Neurons That Fire Together, Wire Together"

**Principle:** Relations strengthen with repeated co-occurrence.

```sql
-- Initial relation
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
VALUES ($machine_id, $learning_id, $semantic_pair_id, 0.5);
-- weight = 0.5 (default)

-- After co-occurrence #1
UPDATE atom_relation
SET weight = weight * 1.1, last_accessed = now()
WHERE source_atom_id = $machine_id AND target_atom_id = $learning_id;
-- weight = 0.55

-- After co-occurrence #2
UPDATE atom_relation
SET weight = weight * 1.1, last_accessed = now()
WHERE source_atom_id = $machine_id AND target_atom_id = $learning_id;
-- weight = 0.605

-- After 10 co-occurrences
-- weight ? 0.95 (strong connection)
```

**Formula:** `new_weight = min(1.0, old_weight � (1.0 + learning_rate))`

### Synaptic Decay

**Unused relations weaken over time:**

```sql
-- Decay relations not accessed in 30 days
UPDATE atom_relation
SET weight = weight * 0.9
WHERE last_accessed < now() - interval '30 days'
  AND weight > 0.1;

-- Delete very weak relations
DELETE FROM atom_relation
WHERE weight < 0.05;
```

**This implements synaptic pruning** (forget irrelevant connections).

---

## Weights, Confidence, Importance

### Weight (Synaptic Efficacy)

**Meaning:** Strength of connection (how often atoms co-occur).

```sql
-- Strong relation
weight = 0.95  -- "machine" + "learning" co-occur frequently

-- Weak relation
weight = 0.15  -- Rarely co-occur
```

**Usage:** Ranking, filtering, traversal decisions.

### Confidence (Certainty)

**Meaning:** How certain we are about this relation.

```sql
-- High confidence (observed 1000+ times)
confidence = 0.98

-- Low confidence (observed 2 times, might be noise)
confidence = 0.20
```

**Usage:** Filter unreliable relations.

### Importance (Relevance)

**Meaning:** How important this relation is (domain-specific).

```sql
-- Important for query context
importance = 0.90

-- Tangential relation
importance = 0.30
```

**Usage:** Prioritize relevant relations.

### Combined Scoring

```sql
-- Rank relations by combined score
SELECT 
    t.canonical_text AS target,
    ar.weight,
    ar.confidence,
    ar.importance,
    (ar.weight * 0.4 + ar.confidence * 0.3 + ar.importance * 0.3) AS combined_score
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ar.source_atom_id = $source_id
ORDER BY combined_score DESC;
```

---

## Spatial Expressions

### Geometric Paths

`spatial_expression` = LINESTRING from source to target in 3D space.

```sql
-- Create relation with geometric path
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight, spatial_expression)
VALUES (
    $machine_id,
    $learning_id,
    $semantic_pair_id,
    0.95,
    ST_MakeLine(
        (SELECT spatial_key FROM atom WHERE atom_id = $machine_id),
        (SELECT spatial_key FROM atom WHERE atom_id = $learning_id)
    )
);
```

**Visualize as:**
```
'machine' (0.521, 0.845, 1.199)
    |
    | LINESTRING (geometric path)
    ?
'learning' (0.519, 0.843, 1.197)
```

### Query by Path Length

```sql
-- Find short paths (close atoms)
SELECT 
    s.canonical_text AS source,
    t.canonical_text AS target,
    ST_Length(ar.spatial_expression) AS path_length
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ST_Length(ar.spatial_expression) < 0.1
ORDER BY path_length ASC;

-- Result: Atoms very close in space (strong semantic similarity)
```

---

## Querying Relations

### Outgoing Relations (Source ? Targets)

```sql
-- What is "machine" related to?
SELECT 
    t.canonical_text AS target,
    rt.canonical_text AS relation_type,
    ar.weight,
    ar.confidence
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
JOIN atom rt ON rt.atom_id = ar.relation_type_id
WHERE ar.source_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'machine')
ORDER BY ar.weight DESC;

-- Result:
-- learning  | semantic_pair     | 0.95 | 0.98
-- automation| semantic_related  | 0.72 | 0.85
-- computer  | semantic_similar  | 0.68 | 0.80
```

### Incoming Relations (Sources ? Target)

```sql
-- What relates to "learning"?
SELECT 
    s.canonical_text AS source,
    rt.canonical_text AS relation_type,
    ar.weight
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom rt ON rt.atom_id = ar.relation_type_id
WHERE ar.target_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'learning')
ORDER BY ar.weight DESC;

-- Result:
-- machine   | semantic_pair    | 0.95
-- deep      | semantic_related | 0.78
-- supervised| semantic_related | 0.65
```

### Bidirectional Relations

```sql
-- Find all atoms connected to "machine" (either direction)
SELECT DISTINCT
    CASE 
        WHEN ar.source_atom_id = $machine_id THEN t.canonical_text
        ELSE s.canonical_text
    END AS related_atom,
    ar.weight
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ar.source_atom_id = $machine_id OR ar.target_atom_id = $machine_id
ORDER BY ar.weight DESC;
```

---

## Graph Traversal

### Single Hop

```sql
SELECT t.canonical_text
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ar.source_atom_id = $start_id
  AND ar.weight > 0.5;
```

### Multi-Hop (Recursive)

```sql
-- Find all atoms reachable within 3 hops
WITH RECURSIVE path AS (
    -- Base: Start atom
    SELECT 
        atom_id,
        canonical_text,
        0 AS depth,
        ARRAY[atom_id] AS path_ids
    FROM atom
    WHERE canonical_text = 'machine'
    
    UNION ALL
    
    -- Recursive: Follow relations
    SELECT 
        a.atom_id,
        a.canonical_text,
        p.depth + 1,
        p.path_ids || a.atom_id
    FROM path p
    JOIN atom_relation ar ON ar.source_atom_id = p.atom_id
    JOIN atom a ON a.atom_id = ar.target_atom_id
    WHERE p.depth < 3  -- Max 3 hops
      AND ar.weight > 0.5  -- Strong relations only
      AND a.atom_id != ALL(p.path_ids)  -- Avoid cycles
)
SELECT DISTINCT canonical_text, depth
FROM path
ORDER BY depth, canonical_text;

-- Result:
-- machine (depth=0)
-- learning, automation, computer (depth=1)
-- deep, neural, algorithm (depth=2)
-- network, layer, training (depth=3)
```

### Shortest Path

```sql
-- Find shortest path from "machine" to "neural"
WITH RECURSIVE path AS (
    SELECT 
        atom_id,
        canonical_text,
        0 AS depth,
        ARRAY[atom_id] AS path_ids,
        ARRAY[canonical_text] AS path_text
    FROM atom
    WHERE canonical_text = 'machine'
    
    UNION ALL
    
    SELECT 
        a.atom_id,
        a.canonical_text,
        p.depth + 1,
        p.path_ids || a.atom_id,
        p.path_text || a.canonical_text
    FROM path p
    JOIN atom_relation ar ON ar.source_atom_id = p.atom_id
    JOIN atom a ON a.atom_id = ar.target_atom_id
    WHERE p.depth < 10
      AND a.atom_id != ALL(p.path_ids)
      AND a.canonical_text != 'neural'  -- Stop when reached
)
SELECT path_text, depth
FROM path
WHERE canonical_text = 'neural'
ORDER BY depth ASC
LIMIT 1;

-- Result: ['machine', 'learning', 'deep', 'neural'] (3 hops)
```

---

## Combining Relations + Spatial Queries

### Semantic Expansion

**Query:** Find atoms near "learning" that are strongly related to "machine"

```sql
WITH machine_relations AS (
    SELECT target_atom_id, weight
    FROM atom_relation
    WHERE source_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'machine')
      AND weight > 0.6
),
learning_spatial AS (
    SELECT atom_id, canonical_text
    FROM atom
    WHERE ST_DWithin(
        spatial_key,
        (SELECT spatial_key FROM atom WHERE canonical_text = 'learning'),
        0.15
    )
)
SELECT 
    ls.canonical_text,
    mr.weight AS relation_weight,
    ST_Distance(
        ls.spatial_key,
        (SELECT spatial_key FROM atom WHERE canonical_text = 'learning')
    ) AS spatial_distance
FROM learning_spatial ls
JOIN machine_relations mr ON mr.target_atom_id = ls.atom_id
ORDER BY (mr.weight * 0.7 + (1.0 / (1.0 + spatial_distance)) * 0.3) DESC;

-- Combines: Spatial proximity + Relation strength
```

---

## Relation Metadata

### Provenance Tracking

```json
{
  "created_by": "user_query",
  "source_document": "doc_12345",
  "extraction_method": "co-occurrence",
  "co_occurrence_count": 127,
  "first_seen": "2025-11-01T00:00:00Z",
  "last_reinforced": "2025-11-28T12:00:00Z"
}
```

### Temporal Information

```json
{
  "valid_from_date": "2020-01-01",
  "valid_to_date": "2023-12-31",
  "temporal_context": "historical"
}
```

### Domain-Specific

```json
{
  "domain": "machine_learning",
  "confidence_source": "expert_labeled",
  "contradiction_count": 0
}
```

---

## Common Patterns

### Upsert Relation

```sql
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
VALUES ($source, $target, $type, 0.5)
ON CONFLICT (source_atom_id, target_atom_id, relation_type_id) DO UPDATE SET
    weight = LEAST(1.0, atom_relation.weight * 1.1),  -- Strengthen
    last_accessed = now(),
    metadata = atom_relation.metadata || EXCLUDED.metadata;
```

### Batch Create Relations

```sql
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
SELECT 
    $source_id,
    neighbor.atom_id,
    $semantic_neighbor_type_id,
    1.0 / (1.0 + ST_Distance($source_position, neighbor.spatial_key))  -- Inverse distance
FROM atom neighbor
WHERE ST_DWithin(neighbor.spatial_key, $source_position, 0.2)
  AND neighbor.atom_id != $source_id
ORDER BY ST_Distance(neighbor.spatial_key, $source_position) ASC
LIMIT 100;
```

### Prune Weak Relations

```sql
DELETE FROM atom_relation
WHERE weight < 0.1
  AND last_accessed < now() - interval '90 days';
```

### Find Mutual Relations

```sql
-- Atoms that relate to each other (bidirectional)
SELECT 
    a.canonical_text AS atom_a,
    b.canonical_text AS atom_b,
    r1.weight AS a_to_b_weight,
    r2.weight AS b_to_a_weight
FROM atom_relation r1
JOIN atom_relation r2 ON 
    r2.source_atom_id = r1.target_atom_id 
    AND r2.target_atom_id = r1.source_atom_id
JOIN atom a ON a.atom_id = r1.source_atom_id
JOIN atom b ON b.atom_id = r1.target_atom_id
WHERE r1.relation_type_id = r2.relation_type_id
ORDER BY (r1.weight + r2.weight) DESC;
```

---

## Performance Characteristics

### Index Usage

**Outgoing relations:**
```sql
-- Uses idx_relation_source
SELECT * FROM atom_relation WHERE source_atom_id = $id;
-- O(log N)
```

**Incoming relations:**
```sql
-- Uses idx_relation_target
SELECT * FROM atom_relation WHERE target_atom_id = $id;
-- O(log N)
```

**Strong relations:**
```sql
-- Uses idx_relation_weight
SELECT * FROM atom_relation WHERE weight > 0.8 ORDER BY weight DESC;
-- O(log N)
```

### Recursive Query Limits

```sql
-- Set max recursion depth
SET max_recursion_depth = 50;

-- Limit results
LIMIT 1000;

-- Break on target found
WHERE canonical_text != $target
```

---

## Key Takeaways

### 1. Relations = Semantic Graph

Atoms connected via typed, weighted edges forming knowledge graph.

### 2. Hebbian Learning

Weights strengthen with co-occurrence ("fire together, wire together").

### 3. Relation Types Are Atoms

First-class entities, can have their own relations and positions.

### 4. Three Weights

- `weight`: Synaptic efficacy (connection strength)
- `confidence`: Certainty in relation
- `importance`: Relevance/priority

### 5. Spatial Expressions

Geometric paths through semantic space (LINESTRING).

### 6. Graph Traversal

Multi-hop queries via recursive CTEs (shortest path, reachability).

### 7. Combined Queries

Spatial proximity + Relation strength = Rich semantic search.

---

## Next Steps

Now that you understand relations, continue with:

1. **[Spatial Semantics](spatial-semantics.md)** — How positions are computed
2. **[Compression](compression.md)** — Multi-layer encoding
3. **[Provenance](provenance.md)** — Neo4j tracking

---

## Advanced Relation Patterns

### Relation Strength Prediction

Predict future relation strength using historical patterns:

```python
from dataclasses import dataclass
from datetime import datetime, timedelta
import numpy as np

@dataclass
class RelationHistory:
    """Historical strength measurements for a relation."""
    timestamps: list[datetime]
    weights: list[float]

class RelationStrengthPredictor:
    """Predict future relation strength based on historical data."""
    
    def __init__(self, decay_factor: float = 0.95):
        self.decay_factor = decay_factor
    
    def predict_future_weight(
        self,
        history: RelationHistory,
        days_ahead: int = 7
    ) -> float:
        """Predict relation weight N days in the future."""
        if not history.weights:
            return 0.5  # Default weight
        
        # Fit exponential decay model
        current_weight = history.weights[-1]
        
        # Calculate decay rate from historical data
        if len(history.weights) > 1:
            decay_rate = self._estimate_decay_rate(history)
        else:
            decay_rate = 1.0 - self.decay_factor
        
        # Predict future weight
        predicted_weight = current_weight * (self.decay_factor ** days_ahead)
        
        return max(0.0, min(1.0, predicted_weight))
    
    def _estimate_decay_rate(self, history: RelationHistory) -> float:
        """Estimate decay rate from historical measurements."""
        if len(history.weights) < 2:
            return 0.0
        
        # Calculate average decay between consecutive measurements
        decays = []
        for i in range(1, len(history.weights)):
            time_diff = (history.timestamps[i] - history.timestamps[i-1]).days
            if time_diff > 0:
                weight_ratio = history.weights[i] / history.weights[i-1]
                decay = 1.0 - weight_ratio
                decays.append(decay / time_diff)
        
        return np.mean(decays) if decays else 0.0
    
    def should_strengthen(
        self,
        history: RelationHistory,
        threshold: float = 0.3
    ) -> bool:
        """Determine if relation needs strengthening."""
        predicted = self.predict_future_weight(history, days_ahead=7)
        return predicted < threshold

# Usage
history = RelationHistory(
    timestamps=[
        datetime.now() - timedelta(days=30),
        datetime.now() - timedelta(days=15),
        datetime.now()
    ],
    weights=[0.9, 0.7, 0.6]
)

predictor = RelationStrengthPredictor(decay_factor=0.95)
future_weight = predictor.predict_future_weight(history, days_ahead=7)
print(f"Predicted weight in 7 days: {future_weight:.2f}")
```

### Multi-Hop Relation Traversal

Navigate through chains of relations:

```python
from typing import List, Set, Tuple
from collections import deque

class RelationPathFinder:
    """Find paths between atoms through relation chains."""
    
    def __init__(self, db_pool):
        self.pool = db_pool
    
    async def find_shortest_path(
        self,
        source_atom_id: int,
        target_atom_id: int,
        max_hops: int = 5
    ) -> List[Tuple[int, str, float]]:
        """Find shortest path between two atoms.
        
        Returns: List of (atom_id, relation_type, weight) tuples
        """
        # BFS to find shortest path
        queue = deque([(source_atom_id, [])])
        visited: Set[int] = {source_atom_id}
        
        while queue:
            current_atom, path = queue.popleft()
            
            if len(path) >= max_hops:
                continue
            
            if current_atom == target_atom_id:
                return path
            
            # Get neighbors
            neighbors = await self._get_neighbors(current_atom)
            
            for next_atom, relation_type, weight in neighbors:
                if next_atom not in visited:
                    visited.add(next_atom)
                    new_path = path + [(next_atom, relation_type, weight)]
                    queue.append((next_atom, new_path))
        
        return []  # No path found
    
    async def find_all_paths(
        self,
        source_atom_id: int,
        target_atom_id: int,
        max_hops: int = 5
    ) -> List[List[Tuple[int, str, float]]]:
        """Find all paths between two atoms."""
        paths = []
        
        async def dfs(current: int, path: List, visited: Set):
            if len(path) >= max_hops:
                return
            
            if current == target_atom_id:
                paths.append(path.copy())
                return
            
            neighbors = await self._get_neighbors(current)
            
            for next_atom, relation_type, weight in neighbors:
                if next_atom not in visited:
                    visited.add(next_atom)
                    path.append((next_atom, relation_type, weight))
                    await dfs(next_atom, path, visited)
                    path.pop()
                    visited.remove(next_atom)
        
        await dfs(source_atom_id, [], {source_atom_id})
        return paths
    
    async def _get_neighbors(
        self,
        atom_id: int
    ) -> List[Tuple[int, str, float]]:
        """Get all atoms connected to given atom."""
        query = """
            SELECT target_atom_id, rt.canonical_text, ar.weight
            FROM atom_relation ar
            JOIN atom rt ON rt.atom_id = ar.relation_type_id
            WHERE source_atom_id = $1
            AND weight > 0.1
            ORDER BY weight DESC
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (atom_id,))
                return await cursor.fetchall()

# Usage
path_finder = RelationPathFinder(db_pool)
path = await path_finder.find_shortest_path(
    source_atom_id=100,
    target_atom_id=200,
    max_hops=5
)
print(f"Found path with {len(path)} hops")
```

### Relation Clustering

Group related atoms using community detection:

```python
from typing import Dict, Set
import networkx as nx

class RelationClusterAnalyzer:
    """Detect communities in the relation graph."""
    
    def __init__(self, db_pool):
        self.pool = db_pool
    
    async def detect_communities(
        self,
        min_weight: float = 0.5
    ) -> Dict[int, Set[int]]:
        """Detect communities using Louvain algorithm.
        
        Returns: Dict mapping community_id to set of atom_ids
        """
        # Build graph
        graph = await self._build_graph(min_weight)
        
        # Detect communities using Louvain
        communities = nx.community.louvain_communities(graph, resolution=1.0)
        
        # Convert to dict
        result = {}
        for i, community in enumerate(communities):
            result[i] = community
        
        return result
    
    async def find_central_atoms(
        self,
        community_id: int,
        communities: Dict[int, Set[int]],
        top_k: int = 10
    ) -> List[Tuple[int, float]]:
        """Find most central atoms in a community."""
        if community_id not in communities:
            return []
        
        community_atoms = communities[community_id]
        
        # Build subgraph for this community
        subgraph = await self._build_subgraph(community_atoms)
        
        # Calculate centrality
        centrality = nx.degree_centrality(subgraph)
        
        # Sort by centrality
        sorted_atoms = sorted(
            centrality.items(),
            key=lambda x: x[1],
            reverse=True
        )
        
        return sorted_atoms[:top_k]
    
    async def _build_graph(self, min_weight: float) -> nx.Graph:
        """Build NetworkX graph from relations."""
        query = """
            SELECT source_atom_id, target_atom_id, weight
            FROM atom_relation
            WHERE weight >= $1
        """
        
        graph = nx.Graph()
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (min_weight,))
                rows = await cursor.fetchall()
                
                for source, target, weight in rows:
                    graph.add_edge(source, target, weight=weight)
        
        return graph
    
    async def _build_subgraph(self, atom_ids: Set[int]) -> nx.Graph:
        """Build graph from subset of atoms."""
        query = """
            SELECT source_atom_id, target_atom_id, weight
            FROM atom_relation
            WHERE source_atom_id = ANY($1)
            AND target_atom_id = ANY($1)
        """
        
        graph = nx.Graph()
        atom_list = list(atom_ids)
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (atom_list,))
                rows = await cursor.fetchall()
                
                for source, target, weight in rows:
                    graph.add_edge(source, target, weight=weight)
        
        return graph

# Usage
analyzer = RelationClusterAnalyzer(db_pool)
communities = await analyzer.detect_communities(min_weight=0.5)
print(f"Detected {len(communities)} communities")

central_atoms = await analyzer.find_central_atoms(
    community_id=0,
    communities=communities,
    top_k=10
)
print(f"Top 10 central atoms: {central_atoms}")
```

### Relation Confidence Scoring

Score relation confidence based on multiple signals:

```python
from enum import Enum
from typing import List

class ConfidenceSignal(Enum):
    """Signals contributing to relation confidence."""
    CO_OCCURRENCE = "co_occurrence"  # How often atoms co-occur
    TEMPORAL_STABILITY = "temporal_stability"  # Weight stability over time
    GRAPH_CENTRALITY = "graph_centrality"  # Position in graph
    PROVENANCE_QUALITY = "provenance_quality"  # Source data quality

@dataclass
class ConfidenceScore:
    """Confidence score breakdown."""
    overall: float
    signals: Dict[ConfidenceSignal, float]

class RelationConfidenceScorer:
    """Calculate confidence scores for relations."""
    
    def __init__(self, db_pool):
        self.pool = db_pool
    
    async def calculate_confidence(
        self,
        relation_id: int
    ) -> ConfidenceScore:
        """Calculate overall confidence score."""
        signals = {}
        
        # Co-occurrence frequency
        signals[ConfidenceSignal.CO_OCCURRENCE] = \
            await self._score_co_occurrence(relation_id)
        
        # Temporal stability
        signals[ConfidenceSignal.TEMPORAL_STABILITY] = \
            await self._score_temporal_stability(relation_id)
        
        # Graph centrality
        signals[ConfidenceSignal.GRAPH_CENTRALITY] = \
            await self._score_graph_centrality(relation_id)
        
        # Provenance quality
        signals[ConfidenceSignal.PROVENANCE_QUALITY] = \
            await self._score_provenance_quality(relation_id)
        
        # Weighted average
        weights = {
            ConfidenceSignal.CO_OCCURRENCE: 0.3,
            ConfidenceSignal.TEMPORAL_STABILITY: 0.3,
            ConfidenceSignal.GRAPH_CENTRALITY: 0.2,
            ConfidenceSignal.PROVENANCE_QUALITY: 0.2
        }
        
        overall = sum(
            signals[signal] * weights[signal]
            for signal in signals
        )
        
        return ConfidenceScore(overall=overall, signals=signals)
    
    async def _score_co_occurrence(self, relation_id: int) -> float:
        """Score based on co-occurrence frequency."""
        query = """
            SELECT COUNT(*) as count
            FROM atom_composition ac1
            JOIN atom_composition ac2 ON ac1.parent_atom_id = ac2.parent_atom_id
            JOIN atom_relation ar ON ar.relation_id = $1
            WHERE ac1.component_atom_id = ar.source_atom_id
            AND ac2.component_atom_id = ar.target_atom_id
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (relation_id,))
                row = await cursor.fetchone()
                count = row[0] if row else 0
                
                # Normalize to [0, 1]
                return min(1.0, count / 100.0)
    
    async def _score_temporal_stability(self, relation_id: int) -> float:
        """Score based on weight stability over time."""
        query = """
            SELECT weight
            FROM atom_relation_history
            WHERE relation_id = $1
            ORDER BY created_at DESC
            LIMIT 10
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (relation_id,))
                rows = await cursor.fetchall()
                
                if not rows:
                    return 0.5
                
                weights = [row[0] for row in rows]
                variance = np.var(weights)
                
                # Lower variance = higher stability
                return max(0.0, 1.0 - variance)
    
    async def _score_graph_centrality(self, relation_id: int) -> float:
        """Score based on graph centrality."""
        # Simplified: just check degree
        query = """
            SELECT source_atom_id, target_atom_id
            FROM atom_relation
            WHERE relation_id = $1
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (relation_id,))
                row = await cursor.fetchone()
                
                if not row:
                    return 0.5
                
                source_id, target_id = row
                
                # Count connections for both atoms
                degree_query = """
                    SELECT COUNT(*) FROM atom_relation
                    WHERE source_atom_id = $1 OR target_atom_id = $1
                """
                
                await cursor.execute(degree_query, (source_id,))
                source_degree = (await cursor.fetchone())[0]
                
                await cursor.execute(degree_query, (target_id,))
                target_degree = (await cursor.fetchone())[0]
                
                avg_degree = (source_degree + target_degree) / 2
                
                # Normalize
                return min(1.0, avg_degree / 50.0)
    
    async def _score_provenance_quality(self, relation_id: int) -> float:
        """Score based on provenance metadata quality."""
        query = """
            SELECT metadata
            FROM atom_relation
            WHERE relation_id = $1
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (relation_id,))
                row = await cursor.fetchone()
                
                if not row or not row[0]:
                    return 0.5
                
                metadata = row[0]
                
                # Score based on metadata completeness
                quality_signals = [
                    'source' in metadata,
                    'created_by' in metadata,
                    'confidence' in metadata,
                    'provenance_id' in metadata
                ]
                
                return sum(quality_signals) / len(quality_signals)

# Usage
scorer = RelationConfidenceScorer(db_pool)
confidence = await scorer.calculate_confidence(relation_id=123)
print(f"Overall confidence: {confidence.overall:.2f}")
print(f"Signal breakdown: {confidence.signals}")
```

---

**Next: [Spatial Semantics →](spatial-semantics.md)**
