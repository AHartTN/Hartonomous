# Provenance

**Complete audit trail: Neo4j graph tracking every atom derivation.**

---

## What Is Provenance?

**Provenance** = complete history of how every atom was created, from raw input to final result.

```cypher
(:Atom)-[:DERIVED_FROM]->(:Atom)-[:DERIVED_FROM]->(:OriginalInput)
```

**Key principle:** Every inference is traceable to its source.

---

## Why Provenance?

### 1. Explainability

**Traditional AI:**
```
Query: "What is machine learning?"
Answer: "A subset of AI..." 
Why? ¯\_(?)_/¯ (black box)
```

**Hartonomous:**
```
Query: "What is machine learning?"
Answer: "A subset of AI..."
Why? ? Traverse provenance graph
  ? Result derived from document_12345
  ? Which contains sentence: "Machine learning is a subset of artificial intelligence"
  ? Which was ingested from source: wikipedia.org/ml
  ? Complete audit trail available
```

### 2. Trust & Verification

```cypher
// How many sources support this fact?
MATCH (fact:Atom {canonical_text: 'cats are mammals'})<-[:DERIVED_FROM*]-(source:Atom)
WHERE source.metadata.type = 'original_document'
RETURN count(DISTINCT source) as source_count

// Result: 127 sources ? HIGH CONFIDENCE
```

### 3. Data Lineage

```cypher
// What atoms were created from this document?
MATCH (doc:Atom {atom_id: 12345})-[:COMPOSED_OF*]->(descendant:Atom)
RETURN count(descendant) as atoms_created

// Result: 1,847 atoms derived from this document
```

### 4. Debugging

```cypher
// Why is this result wrong?
MATCH path = (wrong:Atom)-[:DERIVED_FROM*]->(origin:Atom)
RETURN path

// Shows: Corrupted source data ? Bad atom ? Bad result
```

---

## Neo4j Graph Schema

### Node Types

**Atom Node:**
```cypher
(:Atom {
  atom_id: INTEGER,           // Matches PostgreSQL atom_id
  content_hash: STRING,       // SHA-256 hex
  canonical_text: STRING,     // Cached text representation
  created_at: DATETIME,
  metadata: MAP               // JSONB as map
})
```

**Example:**
```cypher
CREATE (:Atom {
  atom_id: 12350,
  content_hash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  canonical_text: "learning",
  created_at: datetime('2025-11-28T12:00:00Z'),
  metadata: {modality: 'text', specificity: 0.65}
})
```

### Relationship Types

**DERIVED_FROM:**
```cypher
(:Atom)-[:DERIVED_FROM {
  operation: STRING,           // "atomize", "compose", "relate"
  timestamp: DATETIME,
  metadata: MAP
}]->(:Atom)
```

**COMPOSED_OF:**
```cypher
(:Atom)-[:COMPOSED_OF {
  sequence_index: INTEGER,
  timestamp: DATETIME
}]->(:Atom)
```

**RELATES_TO:**
```cypher
(:Atom)-[:RELATES_TO {
  relation_type: STRING,       // semantic_pair, causes, etc.
  weight: FLOAT,
  timestamp: DATETIME
}]->(:Atom)
```

---

## Provenance Tracking

### PostgreSQL ? Neo4j Sync

**Architecture:**

```
PostgreSQL (primary storage)
  ? Logical Replication
Neo4j Worker (background process)
  ? Cypher INSERT
Neo4j (provenance graph)
```

**Worker process:**

```python
class Neo4jProvenanceWorker:
    """
    Background worker syncing PostgreSQL changes to Neo4j.
    Listens to PostgreSQL logical replication stream.
    """
    
    async def start(self):
        """Listen to PostgreSQL changes."""
        async for change in self.listen_to_replication():
            if change.table == 'atom':
                await self.sync_atom(change)
            elif change.table == 'atom_composition':
                await self.sync_composition(change)
            elif change.table == 'atom_relation':
                await self.sync_relation(change)
    
    async def sync_atom(self, change):
        """Create/update atom node in Neo4j."""
        if change.operation == 'INSERT':
            await self.neo4j.run("""
                CREATE (a:Atom {
                    atom_id: $atom_id,
                    content_hash: $content_hash,
                    canonical_text: $canonical_text,
                    created_at: datetime($created_at),
                    metadata: $metadata
                })
            """, **change.data)
    
    async def sync_composition(self, change):
        """Create COMPOSED_OF relationship."""
        await self.neo4j.run("""
            MATCH (parent:Atom {atom_id: $parent_id})
            MATCH (component:Atom {atom_id: $component_id})
            CREATE (parent)-[:COMPOSED_OF {
                sequence_index: $sequence_index,
                timestamp: datetime()
            }]->(component)
        """, **change.data)
    
    async def sync_relation(self, change):
        """Create RELATES_TO relationship."""
        await self.neo4j.run("""
            MATCH (source:Atom {atom_id: $source_id})
            MATCH (target:Atom {atom_id: $target_id})
            CREATE (source)-[:RELATES_TO {
                relation_type: $relation_type,
                weight: $weight,
                timestamp: datetime()
            }]->(target)
        """, **change.data)
```

---

## Provenance Queries

### Ancestor Lineage (How Was This Created?)

```cypher
// Find all ancestors of atom
MATCH path = (atom:Atom {atom_id: $id})-[:DERIVED_FROM*]->(ancestor:Atom)
RETURN path
ORDER BY length(path) DESC
LIMIT 100
```

**Visualization:**
```
learning (atom_id=12350)
  ?? DERIVED_FROM ? sentence_789
  ?   ?? DERIVED_FROM ? document_456
  ?       ?? DERIVED_FROM ? ingestion_123
  ?? COMPOSED_OF ? 'l', 'e', 'a', 'r', 'n', 'i', 'n', 'g'
```

### Descendant Lineage (What Did This Create?)

```cypher
// Find all descendants
MATCH path = (atom:Atom {atom_id: $id})<-[:DERIVED_FROM*]-(descendant:Atom)
RETURN count(DISTINCT descendant) as total_descendants
```

**Use case:** "This document created 1,847 atoms"

### Source Documents

```cypher
// Find original sources for atom
MATCH path = (atom:Atom {canonical_text: 'learning'})-[:DERIVED_FROM*]->(origin:Atom)
WHERE origin.metadata.type = 'original_document'
RETURN DISTINCT origin.canonical_text as source_doc,
       origin.metadata.url as source_url,
       length(path) as derivation_depth
ORDER BY derivation_depth ASC
```

**Result:**
```
source_doc                      | source_url                        | depth
------------------------------- | --------------------------------- | -----
"ML_Wikipedia_Article.txt"      | wikipedia.org/Machine_learning    | 3
"AI_Textbook_Chapter5.pdf"      | textbook.com/ai/ch5               | 4
"DeepLearning_Paper.pdf"        | arxiv.org/abs/1234.5678           | 3
```

### Composition Tree

```cypher
// Get full composition hierarchy
MATCH path = (parent:Atom {atom_id: $id})-[:COMPOSED_OF*]->(leaf:Atom)
RETURN path
ORDER BY length(path) DESC
```

**Visualization:**
```
Document
  ?? Sentence 1
  ?   ?? Word: "Machine"
  ?   ?   ?? 'M'
  ?   ?   ?? 'a'
  ?   ?   ?? 'c'
  ?   ?   ?? 'h'
  ?   ?   ?? 'i'
  ?   ?   ?? 'n'
  ?   ?   ?? 'e'
  ?   ?? Word: "learning"
  ?       ?? ...
  ?? Sentence 2
      ?? ...
```

### Relation Graph

```cypher
// Find all atoms related to target
MATCH (source:Atom)-[r:RELATES_TO]->(target:Atom {canonical_text: 'learning'})
RETURN source.canonical_text as related_atom,
       r.relation_type,
       r.weight
ORDER BY r.weight DESC
LIMIT 20
```

---

## Provenance Metadata

### Tracking Context

**In relationships:**

```cypher
CREATE (a:Atom)-[:DERIVED_FROM {
  operation: 'atomize_text',
  timestamp: datetime(),
  user_id: 'user_12345',
  session_id: 'session_abc',
  model_version: 'v0.6.0',
  processing_time_ms: 87
}]->(b:Atom)
```

**Query:**

```cypher
// Find all atoms created by specific user
MATCH (atom:Atom)-[r:DERIVED_FROM]->()
WHERE r.user_id = 'user_12345'
RETURN atom, r.timestamp
ORDER BY r.timestamp DESC
```

---

## Performance Characteristics

### Node Count

```cypher
// Count nodes
MATCH (a:Atom)
RETURN count(a) as total_atoms
```

**Typical:** 1M-100M nodes (mirrors PostgreSQL atom count)

### Relationship Count

```cypher
// Count relationships
MATCH ()-[r]->()
RETURN count(r) as total_relationships
```

**Typical:** 10M-1B relationships (10-100x atoms)

### Query Performance

| Query Type | Complexity | Typical Time |
|------------|------------|--------------|
| **Direct ancestors** | O(depth) | 10-50ms |
| **Direct descendants** | O(breadth) | 10-50ms |
| **Full lineage** | O(depth × breadth) | 50-500ms |
| **Source documents** | O(depth) | 20-100ms |

**Indexes:**

```cypher
CREATE INDEX atom_id_index FOR (a:Atom) ON (a.atom_id);
CREATE INDEX content_hash_index FOR (a:Atom) ON (a.content_hash);
CREATE INDEX canonical_text_index FOR (a:Atom) ON (a.canonical_text);
```

---

## Querying Combined: PostgreSQL + Neo4j

### Pattern: SQL Find ? Cypher Trace

**1. SQL: Find relevant atoms**

```sql
-- Find atoms near "learning"
SELECT atom_id, canonical_text
FROM atom
WHERE ST_DWithin(spatial_key, $learning_position, 0.15)
ORDER BY ST_Distance(spatial_key, $learning_position) ASC
LIMIT 10;

-- Result: [12350, 12349, 12357, ...]
```

**2. Cypher: Trace provenance for those atoms**

```cypher
// Get provenance for results
MATCH path = (atom:Atom)-[:DERIVED_FROM*]->(origin:Atom)
WHERE atom.atom_id IN [12350, 12349, 12357]
  AND origin.metadata.type = 'original_document'
RETURN atom.canonical_text as result,
       origin.canonical_text as source,
       length(path) as depth
ORDER BY depth ASC
```

**Result:**
```
result      | source                     | depth
----------- | -------------------------- | -----
learning    | ML_Wikipedia_Article.txt   | 3
machine     | AI_Textbook_Chapter5.pdf   | 4
deep        | DeepLearning_Paper.pdf     | 3
```

**Combined view:** Semantic search + full provenance = complete transparency.

---

## Provenance for Different Operations

### Text Ingestion

```cypher
// Track text ingestion
CREATE (doc:Atom {atom_id: 1000, canonical_text: 'document', type: 'document'})
CREATE (sent:Atom {atom_id: 1001, canonical_text: 'sentence', type: 'sentence'})
CREATE (word:Atom {atom_id: 1002, canonical_text: 'learning', type: 'word'})
CREATE (char_l:Atom {atom_id: 108, canonical_text: 'l', type: 'character'})

CREATE (doc)-[:COMPOSED_OF {sequence_index: 0}]->(sent)
CREATE (sent)-[:COMPOSED_OF {sequence_index: 1}]->(word)
CREATE (word)-[:COMPOSED_OF {sequence_index: 0}]->(char_l)
```

### Model Weight Ingestion

```cypher
// Track model weight
CREATE (model:Atom {atom_id: 5000, canonical_text: 'GPT-4', type: 'model'})
CREATE (layer:Atom {atom_id: 5001, canonical_text: 'layer_5', type: 'layer'})
CREATE (weight:Atom {atom_id: 5002, canonical_text: '0.173', type: 'weight'})

CREATE (model)-[:COMPOSED_OF {sequence_index: 5}]->(layer)
CREATE (layer)-[:COMPOSED_OF {sequence_index: 123}]->(weight)

// Metadata tracks original model file
SET model.metadata = {
  source_file: 'gpt4-weights.safetensors',
  model_version: '4.0',
  parameter_count: 1760000000000
}
```

### Query Result

```cypher
// Track query execution
CREATE (query:Atom {atom_id: 6000, canonical_text: 'query: learning', type: 'query'})
CREATE (result:Atom {atom_id: 6001, canonical_text: 'result: ...', type: 'result'})

CREATE (result)-[:DERIVED_FROM {
  operation: 'semantic_search',
  query_text: 'learning',
  execution_time_ms: 4,
  timestamp: datetime()
}]->(query)

// Link result to source atoms
MATCH (source:Atom {canonical_text: 'learning'})
CREATE (result)-[:DERIVED_FROM {operation: 'retrieved'}]->(source)
```

---

## Provenance Visualization

### Neo4j Browser

```cypher
// Visualize atom lineage
MATCH path = (atom:Atom {canonical_text: 'learning'})-[*..5]-(related:Atom)
RETURN path
LIMIT 100
```

**Result:** Interactive graph showing:
- Central node: "learning"
- Connected nodes: "machine", "deep", "neural", etc.
- Edge types: DERIVED_FROM, COMPOSED_OF, RELATES_TO
- Metadata on hover

### Export for Analysis

```cypher
// Export to JSON
MATCH path = (atom:Atom {atom_id: $id})-[*..3]-(related:Atom)
RETURN atom, relationships(path), related
```

```python
# Convert to NetworkX graph
import networkx as nx

G = nx.DiGraph()
for record in results:
    G.add_node(record['atom']['atom_id'], **record['atom'])
    for rel in record['relationships']:
        G.add_edge(rel.start_node['atom_id'], rel.end_node['atom_id'], **rel)

# Analyze
centrality = nx.betweenness_centrality(G)
communities = nx.community.louvain_communities(G)
```

---

## Key Takeaways

### 1. Complete Audit Trail

Every atom traceable to its origin via Neo4j graph.

### 2. Full Explainability

"Why this result?" ? Traverse provenance graph ? See complete reasoning chain.

### 3. Trust & Verification

Count sources supporting a fact ? Confidence measure.

### 4. Background Sync

PostgreSQL (primary) ? Neo4j (provenance) via async worker.

### 5. Flexible Queries

Ancestors, descendants, composition trees, relation graphs.

### 6. Performance

10-100ms for typical queries (indexed graph traversal).

---

## Next Steps

Now that you understand provenance, continue with:

1. **[Modalities](modalities.md)** — Multi-modal representation
2. **[Architecture: Provenance Tracking](../architecture/provenance-tracking.md)** — Implementation details

---

**Next: [Modalities ?](modalities.md)**
