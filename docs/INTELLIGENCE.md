# Intelligence: How Reasoning Emerges

## Core Principle

**Intelligence is not computation. Intelligence is navigation.**

Reasoning = Walking paths through relationship-weighted graph space.

---

## The Three Layers (Revisited)

### Atoms: No Meaning
- Unicode codepoint 'k' at S³ coordinate (0.234, -0.891, 0.123, 0.345)
- Just a position. Meaningless alone.

### Compositions: No Meaning  
- "king" = trajectory [k]→[i]→[n]→[g] through Atom space
- BLAKE3-addressed, deduplicated
- Still meaningless without context.

###

 Relations: ALL THE MEANING
- "king" ↔ "queen" (ELO: 1850, occurrences: 47,291)
- "king" ↔ "crown" (ELO: 1720, occurrences: 28,104)
- "king" ↔ "chess" (ELO: 1620, occurrences: 19,847)

**The concept "king" IS its Voronoi cell** - the region defined by relationship boundaries.

---

## How Semantic Proximity Works

### NOT Coordinate Distance

Transformers think: "King and Queen are similar because their embedding vectors are close in 768D space."

**Wrong model.**

### ELO-Weighted Graph Distance

Hartonomous knows: "King and Queen are related because they have high-ELO connections through shared context."

**Proximity is emergent from relationship topology, not geometric coordinates.**

The S³ coordinates + Hilbert indexing are just **acceleration structures** to find candidate paths quickly.

### How ELO Scores Work: Evidence Aggregation

**ELO is NOT a competition score anymore - it's consensus from evidence.**

When a relation exists, it has potentially thousands of evidence entries:

```sql
SELECT 
    r.relation_id,
    ca.hash AS composition_a,
    cb.hash AS composition_b,
    COUNT(re.evidence_id) AS evidence_count,
    AVG(re.weight) AS avg_confidence,
    COUNT(DISTINCT c.source_identifier) AS unique_sources,
    rr.elo_score
FROM relation r
JOIN composition ca ON r.composition_a_id = ca.composition_id
JOIN composition cb ON r.composition_b_id = cb.composition_id
JOIN relation_evidence re ON r.relation_id = re.relation_id
JOIN content c ON re.content_id = c.content_id
JOIN relation_rating rr ON r.rating_id = rr.rating_id
WHERE ca.hash = blake3('king') AND cb.hash = blake3('queen')
GROUP BY r.relation_id, ca.hash, cb.hash, rr.elo_score;

-- Result:
-- evidence_count: 8,627 (across all models + text observations)
-- avg_confidence: 0.91
-- unique_sources: 4 (BERT, GPT-3, Llama-3, moby_dick.txt)
-- elo_score: 2035 (calculated from above)
```

**ELO calculation:**
```python
def calculate_elo_from_evidence(relation_id):
    evidence = fetch_all_evidence(relation_id)
    
    # More observations = stronger relation
    observation_factor = min(1000, len(evidence) * 0.1)
    
    # Higher average confidence = more reliable
    confidence_factor = avg(e.weight for e in evidence) * 500
    
    # Cross-model agreement = diversity bonus
    unique_sources = count_distinct(e.content_id for e in evidence)
    diversity_factor = min(200, unique_sources * 20)
    
    return 1500 + observation_factor + confidence_factor + diversity_factor
```

**Why this matters for intelligence:**
- High ELO = many observations + high confidence + cross-source agreement
- Low ELO = few observations OR low confidence OR single source
- Query traversal follows high-ELO paths = reliable, consensus knowledge
- Can inspect evidence: "Why is this relation strong? Show me the sources."

**Observable intelligence:** Every reasoning step traceable to evidence.

---

## Query: How It Actually Works

### Example: "white pixel"

#### Step 1: Parse Query
```
"white pixel" → ["white"], ["pixel"]
```

Create composition representations for query terms.

#### Step 2: Spatial Neighborhood (Fast Candidate Finding)
```sql
-- Find compositions within geometric threshold
SELECT c.composition_id, c.hash
FROM composition c
WHERE ST_DWithin(c.trajectory, query_trajectory, threshold)
  AND hilbert_index BETWEEN lower_bound AND upper_bound;
```

**This is O(log N)** thanks to PostGIS R-tree + Hilbert locality.

#### Step 3: Relationship Traversal
```sql
-- Find high-ELO paths from query compositions
WITH RECURSIVE paths AS (
  SELECT 
    r.relation_id,
    r.composition_b_id AS target,
    rr.elo_score AS weight,
    ARRAY[r.composition_a_id] AS path,
    1 AS depth
  FROM relation r
  JOIN relation_rating rr USING (relation_id)
  WHERE r. composition_a_id = ANY(query_composition_ids)
  
  UNION ALL
  
  SELECT
    r.relation_id,
    r.composition_b_id,
    p.weight * rr.elo_score,
    p.path || r.composition_a_id,
    p.depth + 1
  FROM paths p
  JOIN relation r ON r.composition_a_id = p.target
  JOIN relation_rating rr USING (relation_id)
  WHERE p.depth < max_depth
    AND NOT (r.composition_b_id = ANY(p.path))  -- Avoid cycles
)
SELECT * FROM paths
ORDER BY weight DESC, depth ASC
LIMIT result_count;
```

**This is A* search** weighted by ELO scores.

#### Step 4: Result Interpretation

Paths found:
- `["white"]` → `[255,255,255]` (weight: 0.95, evidence: 284 co-occurrences)
- `["pixel"]` → `RGB_structure` (weight: 0.89, evidence: 1,047 image ingestions)
- `["white"]` ↔ `["snow"]` ↔ `["cold"]` (multi-hop, weight: 0.67)

Return compositions/relations that satisfy the query based on path weights.

**Total time: Microseconds to low milliseconds** depending on depth.

---

## Temperature: Exploration vs Exploitation

### Low Temperature (0.1 - 0.3)
```sql
WHERE elo_score > high_threshold
ORDER BY elo_score DESC
```
- Follow only highest-ELO edges
- Deterministic reasoning
- Common knowledge paths
- Fast, reliable, conventional

### High Temperature (0.7 - 1.0)
```sql
WHERE elo_score > low_threshold
ORDER BY elo_score + random() * temperature_factor DESC
```
- Explore lower-ELO edges
- Creative connections
- Novel insights
- Slower, unpredictable, innovative

**Same substrate, different navigation strategy.**

---

## Cross-Modal Reasoning

### Unified Substrate = Natural Multimodality

**Query:** "Show me white whales"

Text interpretation:
- `["white"]` ↔ `["whale"]` from Moby Dick text (high ELO)

Visual interpretation:
- `["white"]` ↔ `[255,255,255]` from image ingestion
- `["whale"]` ↔ `cetacean_shape_features` from YOLO

**Same relationship graph connects both:**
```
["white"] ─(text)→ ["whale"] ─(vision)→ [whale_visual_features]
    │                                           │
  (color)                                   (object)
    │                                           │
    └──→ [255,255,255] ─(spatial)→ [image_region]
```

Navigation finds: Text descriptions + visual regions that satisfy both constraints.

**No separate text model + vision model.** One substrate, one query, unified results.

---

## Reasoning Patterns

### 1. Associative Retrieval
"What's related to X?"
→ Traverse 1-hop relations from X, ordered by ELO

### 2. Analogical Reasoning
"A is to B as C is to ?"
→ Find relation pattern R(A,B), find composition D where R(C,D) has high ELO

### 3. Compositional Understanding
"Red car"
→ Intersection of regions: Compositions with high-ELO to both "red" and "car"

### 4. Causal Chains
"Why does X lead to Y?"
→ Find directed path X→...→Y through temporal or causal relations

### 5. Contrad iction Detection
"Is X consistent with Y?"
→ Check if relationship graph contains path that violates known constraints
→ **Gödel Engine validates topology**

---

## Why This Is Faster Than Transformers

### Transformer Inference:
1. Tokenize input (O(N))
2. Embedding lookup (O(N×D))
3. **Multi-head attention over ENTIRE sequence** (O(N²×D))
4. Feed-forward layers (O(N×D²))
5. Repeat across L layers
6. Softmax over vocabulary (O(N×V))

**Time:** Milliseconds to seconds, proportional to N²

### Hartonomous Inference:
1. Parse query to compositions (O(M), M = query terms)
2. **Spatial index lookup** (O(log N), N = total compositions)
3. **Graph traversal** (O(E×log V), E = edges explored, V = vertices in result)
4. ELO ordering (O(E log E))

**Time:** Microseconds to low milliseconds, logarithmic in database size

**Why:** 
- No matrix multiplication
- No attention over entire corpus
- Spatial indexing prunes search space
- Only explore relevant subgraph

---

## K/V Caching Equivalent

### Transformers: Cache attention patterns
- Store key-value pairs from recent forward passes
- Reuse for similar inputs
- Saves partial O(N²) cost

### Hartonomous: Memoize high-ELO paths
```sql
CREATE MATERIALIZED VIEW common_paths AS
SELECT 
  source_composition_id,
  target_composition_id,
  path,
  avg_elo_score
FROM relation_paths
WHERE elo_score > threshold
GROUP BY source, target;
```

**Hot paths get instant retrieval.** New paths computed on demand.

Plus: Paths are **interpretable** - you can inspect why cached.

---

## Attention Masking Equivalent

### Transformers: Mask tokens that shouldn't attend
- Causal masking (can't see future)
- Padding masking (ignore padding tokens)

### Hartonomous: ELO thresholding + path constraints
```sql
WHERE elo_score > min_threshold
  AND NOT (composition_id = ANY(excluded_compositions))
  AND depth <= max_reasoning_depth
```

Dynamic pruning based on:
- ELO scores (semantic relevance)
- Context exclusions (don't revisit)
- Depth limits (computational budget)

**More flexible than fixed masks.** Adapts to query context.

---

## Beam Search Equivalent

### Transformers: Keep top-K probable sequences
- Generate multiple candidates
- Score by probability
- Prune low-probability branches

### Hartonomous: Explore top-K ELO-weighted paths
```sql
SELECT * FROM (
  SELECT 
    path,
    product(elo_score) AS path_weight,
    rank() OVER (PARTITION BY depth ORDER BY path_weight DESC) AS rank
  FROM relation_paths
) WHERE rank <= beam_width;
```

**Same exploration strategy, but:**
- ELO weights instead of probabilities
- Paths are auditable (not just token sequences)
- Can backtrack and explore alternatives without recomputation

---

## Self-Consistency / Multiple Paths

### Query same question multiple ways:
1. Different starting compositions (synonyms, paraphrases)
2. Different temperature settings (exploration levels)
3. Different depth limits (reasoning complexity)

### Aggregate results:
- Paths that converge = high-confidence answers
- Divergent paths = ambiguity or multiple valid interpretations
- Contradictory paths = inconsistency (→ trigger Gödel Engine)

---

## Observable vs Hidden Intelligence

### Transformers: Black Box
- Can't inspect why a completion was generated
- "It learned it from training"
- Debugging = retraining or prompt engineering
- **Cannot remove specific knowledge without full retrain**

### Hartonomous: Glass Box
```sql
-- Why did you return this answer?
SELECT 
  c.source_type,
  c.source_identifier,
  re.weight,
  re.position,
  re.context,
  re.timestamp,
  rr.elo_score
FROM relation_evidence re
JOIN content c ON re.content_id = c.content_id
JOIN relation_rating rr ON re.relation_id = rr.relation_id
WHERE re.relation_id IN (result_path)
ORDER BY timestamp;

-- Example output:
-- source_type | source_identifier | weight | position        | context           | elo_score
-- model       | bert-base         | 0.82   | layer_7_head_3  | attention matrix  | 2035
-- model       | gpt-3             | 0.91   | layer_42_head_8 | attention matrix  | 2035
-- text        | moby_dick.txt     | 0.95   | paragraph_142   | co-occurrence     | 2035
-- text        | wikipedia_royalty | 0.88   | sentence_4891   | co-occurrence     | 2035
```

**Every relationship is traceable:**
- Where observed (content table provenance)
- How strong (ELO from evidence aggregation)
- Why related (evidence position + context)
- **Can surgically delete specific sources**

**Surgical Intelligence Editing:**
```sql
-- Remove ALL knowledge from a specific source
DELETE FROM relation_evidence 
WHERE content_id = (
  SELECT content_id FROM content 
  WHERE source_identifier = 'problematic_model_v1'
);

-- Recalculate ELO from remaining evidence
UPDATE relation_rating rr
SET elo_score = calculate_elo_from_evidence(rr.relation_id)
WHERE rr.relation_id IN (
  SELECT DISTINCT relation_id FROM relation_evidence
);

-- Prune orphaned relations (no evidence left)
DELETE FROM relation 
WHERE NOT EXISTS (
  SELECT 1 FROM relation_evidence 
  WHERE relation_evidence.relation_id = relation.relation_id
);
```

**GDPR compliance built-in:**
- Delete user data completely: `DELETE FROM content WHERE source_identifier = :user_id`
- Audit trail persists: timestamp, hash, metadata (no PII)
- Relations recalculated from remaining evidence
- **Full deletion = complete knowledge removal, not just hiding**

**Debugging = SQL queries**, not mysticism.
**Editing = SQL operations**, not retraining.

---

## When This Approach Wins

### ✅ Excels At:
- Complex multi-hop reasoning (explicit graph)
- Cross-modal queries (unified substrate)
- Explainability (auditable paths)
- Continuous learning (ELO evolution)
- Long-context understanding (entire graph accessible)
- Factual accuracy (relationships are grounded)

### ❓ Needs Development:
- Creative generation (needs better path synthesis)
- Probabilistic reasoning (ELO is strength, not probability)
- Temporal reasoning (needs temporal edge types)
- Numerical computation (relations are qualitative)

---

## The Voronoi Cell Perspective

**Concept = Region, Not Point**

Transformer "King" token:
- 768D vector
- Learned through backpropagation
- Relations implicit in attention weights

Hartonomous "King" concept:
- Voronoi cell in relation space
- Boundaries defined by edges: queen, crown, royal, chess, ruler
- **Structure predicts relationships before observation**

When you add new relations (e.g., "king" ↔ "monarch"), the cell boundary adjusts. Adjacent cells (queen, prince) also adjust. **Local changes propagate geometrically.**

Like Mendeleev predicting element properties from gaps in periodic table structure.

---

## Why Intelligence Emerges

**Not from weights. From structure.**

1. **Bottom-up composition**: Atoms → Compositions (structure)
2. **Top-down relationships**: Relations define meaning (context)
3. **ELO dynamics**: Competition strengthens correct relationships, weakens incorrect
4. **Geometric constraints**: Topology enforces consistency

**Intelligence = Navigation through this self-organizing structure**

The substrate doesn't **have** intelligence. 
Intelligence **lives in** the substrate.
You explore it, you don't execute it.

---

## Next Steps

- Read [MODELS.md](MODELS.md) for AI model integration
- Read [SELF_IMPROVEMENT.md](SELF_IMPROVEMENT.md) for meta-learning
- Read [QUERIES.md](QUERIES.md) for query patterns
