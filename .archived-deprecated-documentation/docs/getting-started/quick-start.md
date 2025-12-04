# Quick Start

**Get Hartonomous running in 5 minutes.**

---

## Prerequisites

- Docker 24.0+ with Docker Compose
- 8GB RAM minimum
- Internet connection (for image pulls)

---

## 1. Clone Repository

```bash
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous
```

---

## 2. Start Services

```bash
docker-compose up -d
```

**Services starting:**
- `postgres` — PostgreSQL 16 + PostGIS 3.4 (port 5432)
- `neo4j` — Neo4j 5.15 (ports 7474, 7687)
- `api` — FastAPI application (port 8000)
- `code-atomizer` — C# microservice (port 8080)
- `caddy` — Reverse proxy (ports 80, 443)

**Wait for services** (~60 seconds for first startup):
```bash
docker-compose logs -f api
# Wait for: "? Hartonomous API ready"
```

---

## 3. Verify Health

```bash
curl http://localhost/v1/health
```

**Expected output:**
```json
{
  "status": "healthy",
  "version": "0.6.0",
  "database": "connected",
  "neo4j": "connected",
  "code_atomizer": "connected"
}
```

---

## 4. Ingest First Document

```bash
curl -X POST http://localhost/v1/ingest/text \
  -H "Content-Type: application/json" \
  -d '{
    "content": "Machine learning is a subset of artificial intelligence that enables computers to learn from data.",
    "metadata": {
      "source": "quick-start",
      "author": "user"
    }
  }'
```

**Response:**
```json
{
  "atom_id": 12345,
  "atoms_created": 42,
  "atoms_reused": 18,
  "compositions_created": 15,
  "relations_created": 8,
  "spatial_position": [0.523, 0.847, 1.201],
  "processing_time_ms": 87,
  "provenance_tracked": true
}
```

**What happened:**
1. Text decomposed into character atoms
2. Each atom checked for existence (SHA-256 hash)
3. New atoms inserted, existing atoms referenced
4. Semantic neighbors queried
5. Spatial position computed (weighted centroid)
6. Compositions created (sentence ? words ? characters)
7. Relations created (semantic connections)
8. Neo4j provenance graph updated

---

## 5. Query Semantic Space

```bash
curl "http://localhost/v1/query/semantic?text=learning&limit=10"
```

**Response:**
```json
{
  "query": "learning",
  "query_position": [0.519, 0.843, 1.197],
  "results": [
    {
      "atom_id": 12350,
      "canonical_text": "learning",
      "distance": 0.00,
      "reference_count": 1
    },
    {
      "atom_id": 12349,
      "canonical_text": "machine",
      "distance": 0.04,
      "reference_count": 1
    },
    {
      "atom_id": 12355,
      "canonical_text": "intelligence",
      "distance": 0.08,
      "reference_count": 1
    },
    {
      "atom_id": 12352,
      "canonical_text": "data",
      "distance": 0.12,
      "reference_count": 1
    }
  ],
  "execution_time_ms": 4
}
```

**What this shows:**
- Atoms near "learning" in 3D semantic space
- Distance = semantic dissimilarity
- Words from same document cluster together
- No embedding model needed—positions emerged from ingestion

---

## 6. View in Neo4j (Optional)

Open Neo4j Browser:
```
http://localhost:7474
```

**Login:**
- Username: `neo4j`
- Password: `neo4jneo4j`

**Query provenance:**
```cypher
MATCH path = (a:Atom)-[:DERIVED_FROM*]-(b:Atom)
WHERE a.canonical_text = 'learning'
RETURN path
LIMIT 25
```

**Visualize:**
- Nodes = atoms
- Edges = derivation relationships
- Complete audit trail from raw input to final atoms

---

## 7. Inspect Database (Optional)

Connect to PostgreSQL:
```bash
docker exec -it hartonomous-postgres psql -U hartonomous -d hartonomous
```

**Query atoms:**
```sql
SELECT atom_id, canonical_text, reference_count,
       ST_AsText(spatial_key) as position
FROM atom
WHERE canonical_text ~ '^[a-zA-Z]+$'  -- Text atoms only
ORDER BY reference_count DESC
LIMIT 10;
```

**Query compositions:**
```sql
SELECT 
    p.canonical_text AS parent,
    c.canonical_text AS component,
    ac.sequence_index
FROM atom_composition ac
JOIN atom p ON p.atom_id = ac.parent_atom_id
JOIN atom c ON c.atom_id = ac.component_atom_id
WHERE p.canonical_text = 'learning'
ORDER BY ac.sequence_index;
```

---

## What's Next?

You now have:
- ? Hartonomous running locally
- ? First document ingested and atomized
- ? Atoms positioned in semantic space
- ? Provenance tracked in Neo4j

### Continue Learning

1. **[First Ingestion Tutorial](first-ingestion.md)** — Deep dive into atomization process
2. **[First Query Tutorial](first-query.md)** — Understand spatial queries
3. **[Concepts](../concepts/atoms.md)** — What are atoms? How does spatial positioning work?
4. **[API Reference](../api-reference/ingestion.md)** — Full endpoint documentation

---

## Troubleshooting

### Services won't start
```bash
# Check Docker status
docker-compose ps

# View logs
docker-compose logs postgres
docker-compose logs neo4j
docker-compose logs api

# Restart services
docker-compose restart
```

### Health check fails
```bash
# Check PostgreSQL
docker exec hartonomous-postgres pg_isready -U hartonomous

# Check Neo4j
curl http://localhost:7474

# Check API
docker-compose logs api | grep ERROR
```

### Port conflicts
```bash
# Stop services
docker-compose down

# Edit docker-compose.yml to change ports
# Example: Change postgres "5433:5432" if 5432 is taken

# Restart
docker-compose up -d
```

---

## Clean Up

To stop and remove all services:
```bash
docker-compose down
```

To remove all data (DESTROYS DATABASE):
```bash
docker-compose down -v
```

---

**Next: [First Ingestion Tutorial](first-ingestion.md) ?**
