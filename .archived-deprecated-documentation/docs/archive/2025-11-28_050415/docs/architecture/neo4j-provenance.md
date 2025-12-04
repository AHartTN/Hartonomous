# Neo4j Provenance Tracking

## Overview

Hartonomous uses **Neo4j** as the provenance graph database, tracking the complete lineage of how atoms are created, composed, and transformed over time. This provides a "living audit trail" for all data operations.

## Why Neo4j?

### Production Maturity (2025)
- **15+ years battle-tested**: Used by 84 of Fortune 100 companies
- **Active Development**: $100M investment in GenAI and agentic AI
- **Enterprise Support**: Comprehensive tooling and ecosystem
- **Regulatory Compliance**: GDPR, CCPA, HIPAA audit trail capabilities

### Architecture Fit
```
PostgreSQL (Cortex)     →  Fast reflexes, operational writes, current state
Neo4j (Hippocampus)     →  Long-term memory, provenance lineage, progression tracking
Temporal Tables         →  State snapshots at points in time
Neo4j Graph            →  The "story" of how atoms evolved
```

### Why Not Apache AGE?
Apache AGE development significantly slowed after October 2024 when Bitnine/AGEDB dismissed the entire development team. Neo4j provides:
- More stable long-term development trajectory
- Rich visualization tools (Neo4j Browser, Bloom)
- Mature ecosystem for graph analytics
- Better separation of concerns (read path doesn't impact write performance)

## Graph Schema

### Node Type: Atom
```cypher
(:Atom {
  atom_id: Integer,          // PostgreSQL atom_id
  content_hash: String,      // SHA-256 hash (hex encoded)
  canonical_text: String,    // Human-readable text (if applicable)
  metadata: Map,             // JSON metadata from PostgreSQL
  created_at: DateTime       // When atom was created
})
```

### Relationship Type: DERIVED_FROM
```cypher
(:Atom)-[:DERIVED_FROM {
  position: Integer,         // Position in composition
  created_at: DateTime       // When composition was created
}]->(:Atom)
```

### Graph Pattern
```
(parent:Atom)-[:DERIVED_FROM {position: 0}]->(child1:Atom)
(parent:Atom)-[:DERIVED_FROM {position: 1}]->(child2:Atom)
(parent:Atom)-[:DERIVED_FROM {position: 2}]->(child3:Atom)
```

**Example**: A word atom "hello" derived from 5 character atoms 'h', 'e', 'l', 'l', 'o'

## Sync Architecture

### LISTEN/NOTIFY Pattern
```
PostgreSQL Trigger  →  NOTIFY 'atom_created'
                   →  NOTIFY 'composition_created'
                          ↓
             Neo4jProvenanceWorker
                          ↓
                    Neo4j Graph Database
```

### Zero-Latency Async
- **PostgreSQL writes**: Fast, no blocking, immediate return
- **Neo4j sync**: Asynchronous via background worker
- **Eventual consistency**: Provenance graph updated within milliseconds
- **No write penalty**: Operational writes unaffected by provenance tracking

### Worker Configuration
```python
# api/config.py
neo4j_enabled: bool = True              # Enable provenance tracking
neo4j_uri: str = "bolt://localhost:7687"
neo4j_user: str = "neo4j"
neo4j_password: str = "neo4jneo4j"      # Or loaded from Azure Key Vault
neo4j_database: str = "neo4j"
```

### Environment-Specific Configuration
**HART-DESKTOP (Windows)**:
- Neo4j Desktop Edition
- Credentials: `neo4j:neo4jneo4j`
- URI: `bolt://localhost:7687`

**hart-server (Linux)**:
- Neo4j Community Edition
- Credentials: Loaded from Azure Key Vault
- Key Vault Secret: `Neo4j-hart-server-Password`
- URI: Configured in Azure App Configuration

## Provenance Query Patterns

### 1. Full Lineage (All Ancestors)
Find all atoms that contributed to creating a target atom:
```cypher
MATCH path = (target:Atom {atom_id: $target_id})-[:DERIVED_FROM*]->(ancestor:Atom)
RETURN path
ORDER BY length(path) DESC;
```

**Use Case**: "What data sources contributed to this inference?"

### 2. Immediate Parents (Direct Composition)
Find atoms directly used to compose a target atom:
```cypher
MATCH (target:Atom {atom_id: $target_id})-[r:DERIVED_FROM]->(parent:Atom)
RETURN parent, r.position
ORDER BY r.position;
```

**Use Case**: "What are the components of this word/image/audio?"

### 3. Descendants (Impact Analysis)
Find all atoms created using a source atom:
```cypher
MATCH path = (source:Atom {atom_id: $source_id})<-[:DERIVED_FROM*]-(descendant:Atom)
RETURN path
ORDER BY length(path) DESC;
```

**Use Case**: "If I update this atom, what downstream data is affected?"

### 4. Provenance Depth
Count how many generations of composition a lineage spans:
```cypher
MATCH path = (target:Atom {atom_id: $target_id})-[:DERIVED_FROM*]->(ancestor:Atom)
RETURN max(length(path)) AS depth;
```

**Use Case**: "How many layers of abstraction are in this data?"

### 5. Common Ancestors
Find atoms that contributed to multiple targets (data reuse):
```cypher
MATCH (target1:Atom {atom_id: $target1_id})-[:DERIVED_FROM*]->(ancestor:Atom)
MATCH (target2:Atom {atom_id: $target2_id})-[:DERIVED_FROM*]->(ancestor)
RETURN ancestor, ancestor.canonical_text
LIMIT 10;
```

**Use Case**: "What data is shared between these two inferences?"

### 6. Temporal Lineage (Time-Bounded)
Find provenance within a specific time range:
```cypher
MATCH path = (target:Atom {atom_id: $target_id})-[r:DERIVED_FROM*]->(ancestor:Atom)
WHERE all(rel IN relationships(path) WHERE rel.created_at > $start_time AND rel.created_at < $end_time)
RETURN path;
```

**Use Case**: "What data was incorporated during Q4 2024?"

### 7. Atom Reuse Frequency (Hot Atoms)
Find most frequently used atoms in compositions:
```cypher
MATCH (atom:Atom)<-[:DERIVED_FROM]-(parent:Atom)
RETURN atom.atom_id, atom.canonical_text, count(parent) AS reuse_count
ORDER BY reuse_count DESC
LIMIT 100;
```

**Use Case**: "What are the most commonly reused building blocks?"

### 8. Orphan Atoms (No Descendants)
Find atoms that have never been used in compositions:
```cypher
MATCH (atom:Atom)
WHERE NOT (atom)<-[:DERIVED_FROM]-()
RETURN atom.atom_id, atom.canonical_text, atom.created_at
ORDER BY atom.created_at DESC;
```

**Use Case**: "What raw data has never been incorporated?"

### 9. Composition Breadth
Find how many immediate children an atom has:
```cypher
MATCH (atom:Atom {atom_id: $atom_id})-[:DERIVED_FROM]->(child:Atom)
RETURN count(child) AS composition_size;
```

**Use Case**: "How complex is this composed atom?"

### 10. Cross-Modality Lineage
Find atoms from different modalities in provenance chain:
```cypher
MATCH path = (target:Atom {atom_id: $target_id})-[:DERIVED_FROM*]->(ancestor:Atom)
WITH path, [node IN nodes(path) | node.metadata.modality] AS modalities
RETURN path, modalities
WHERE size(apoc.coll.toSet(modalities)) > 1;
```

**Use Case**: "Does this inference use multi-modal data (text + image + audio)?"

## Compliance & Auditing

### GDPR Right to Explanation
```cypher
// Show full provenance for a specific inference
MATCH path = (inference:Atom {atom_id: $inference_id})-[:DERIVED_FROM*]->(source:Atom)
RETURN path
ORDER BY length(path);
```

### Data Deletion Impact Analysis
```cypher
// Before deleting atom X, find what would be affected
MATCH (source:Atom {atom_id: $delete_id})<-[:DERIVED_FROM*]-(affected:Atom)
RETURN count(DISTINCT affected) AS affected_count,
       collect(DISTINCT affected.atom_id)[0..10] AS sample_affected;
```

### Audit Trail for Specific Time Period
```cypher
// All compositions created in date range
MATCH (parent:Atom)-[r:DERIVED_FROM]->(child:Atom)
WHERE r.created_at >= $start_date AND r.created_at <= $end_date
RETURN parent, r, child;
```

## Performance Considerations

### Indexes
The Neo4j worker automatically creates:
```cypher
CREATE CONSTRAINT atom_id_unique IF NOT EXISTS
FOR (a:Atom) REQUIRE a.atom_id IS UNIQUE;

CREATE INDEX atom_content_hash IF NOT EXISTS
FOR (a:Atom) ON (a.content_hash);
```

### Query Optimization
1. **Use Indexes**: Always filter by `atom_id` or `content_hash` first
2. **Limit Depth**: Use `[:DERIVED_FROM*..10]` to limit traversal depth
3. **Pagination**: Use `SKIP` and `LIMIT` for large result sets
4. **Warm Cache**: Frequently accessed patterns stay in page cache

### Scaling
- **Sharding**: Neo4j Enterprise supports graph sharding
- **Read Replicas**: Multiple read-only Neo4j instances
- **Cluster Mode**: Neo4j Causal Cluster for HA

## Visualization

### Neo4j Browser
Access at `http://localhost:7474`
```cypher
// Visualize provenance tree
MATCH path = (:Atom {atom_id: 12345})-[:DERIVED_FROM*..3]->(:Atom)
RETURN path
LIMIT 100;
```

### Neo4j Bloom
- Natural language queries: "Show me the lineage of atom 12345"
- Interactive graph exploration
- Search patterns by node properties
- Custom perspectives for different use cases

## API Integration

### Python Client (Cypher Queries)
```python
from neo4j import AsyncGraphDatabase

async def get_lineage(atom_id: int):
    driver = AsyncGraphDatabase.driver(
        "bolt://localhost:7687",
        auth=("neo4j", "neo4jneo4j")
    )

    async with driver.session(database="neo4j") as session:
        result = await session.run("""
            MATCH path = (target:Atom {atom_id: $atom_id})-[:DERIVED_FROM*]->(ancestor:Atom)
            RETURN path
            ORDER BY length(path) DESC
            LIMIT 100;
        """, atom_id=atom_id)

        paths = [record["path"] async for record in result]

    await driver.close()
    return paths
```

### REST API Endpoint (Future)
```python
# api/routes/provenance.py (planned for v0.7.0)
@router.get("/v1/provenance/{atom_id}/lineage")
async def get_atom_lineage(atom_id: int, depth: Optional[int] = None):
    """Get full provenance lineage for an atom."""
    # Query Neo4j via driver
    # Return JSON representation of graph
```

## Monitoring

### Health Check
```cypher
// Verify Neo4j connectivity
RETURN 1;
```

### Sync Lag
```cypher
// Compare latest PostgreSQL atom_id vs Neo4j
MATCH (a:Atom)
RETURN max(a.atom_id) AS latest_synced;
```

Compare with PostgreSQL:
```sql
SELECT max(atom_id) FROM atom;
```

If delta > 1000, investigate worker health.

### Worker Logs
```bash
# Check Neo4j worker status
docker logs hartonomous-api -f | grep "Neo4j"
```

## Troubleshooting

### Worker Not Starting
```bash
# Check Neo4j connectivity
neo4j-admin server status

# Test connection
cypher-shell -u neo4j -p neo4jneo4j "RETURN 1;"
```

### Sync Lag Growing
1. Check Neo4j server CPU/memory
2. Verify network connectivity
3. Review worker error logs
4. Consider scaling Neo4j (read replicas)

### Constraints Failing
```cypher
// Drop and recreate constraints
DROP CONSTRAINT atom_id_unique IF EXISTS;
CREATE CONSTRAINT atom_id_unique FOR (a:Atom) REQUIRE a.atom_id IS UNIQUE;
```

## References

- [Neo4j Data Lineage Best Practices](https://neo4j.com/blog/graph-database/what-is-data-lineage/)
- [Neo4j Operations Manual](https://neo4j.com/docs/operations-manual/current/)
- [Cypher Query Language](https://neo4j.com/docs/cypher-manual/current/)
- [Neo4j Python Driver](https://neo4j.com/docs/python-manual/current/)

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
