# Spatial Query API Specification

**Base URL:** `http://localhost:8000/api/v1`  
**Authentication:** ⚠️ JWT TODO (currently bypassed for development)  
**Content-Type:** `application/json`

---

## Overview

**Spatial Query API** provides geometric and semantic search operations:

1. **KNN (K-Nearest Neighbors):** Find closest atoms by spatial distance
2. **Range Queries:** Find all atoms within radius
3. **Similarity Search:** Find atoms by content hash or semantic proximity
4. **Composition Queries:** Decompose hierarchies, find parents/children
5. **Relation Queries:** Graph traversal, shortest paths, neighborhood exploration

**Current Status:** ✅ COMPLETE (POINTZ), 🟡 Hilbert range optimization pending POINTZM migration

---

## Response Schema Standards

**All query endpoints return consistent structure:**

```json
{
  "results": [...],           // Array of result objects
  "count": 10,                // Number of results returned
  "execution_time_ms": 4.2,  // Query execution time
  "query_params": {...}       // Echo of query parameters
}
```

**Result Object Standard Fields:**

| Field | Type | Always Present | Description |
|-------|------|----------------|-------------|
| `atom_id` | int | ✅ Yes | Unique atom identifier |
| `content_hash` | string | ✅ Yes | SHA-256 content hash (64 hex chars) |
| `canonical_text` | string | ⚠️ Optional | Human-readable text (may be null) |
| `position` | string | ✅ Yes | PostGIS POINTZ string |
| `metadata` | object | ✅ Yes | Metadata JSON (may be empty `{}`) |
| `distance` | float | ⚠️ Context | Present in KNN/range queries |
| `similarity` | float | ⚠️ Context | Present in similarity queries |
| `depth` | int | ⚠️ Context | Present in graph traversal queries |

**Error Response Standard:**

```json
{
  "error": "Invalid parameter",
  "detail": "k must be between 1 and 10000",
  "status_code": 400,
  "timestamp": "2025-01-15T10:30:00Z"
}
```

---

## Endpoints

### 1. K-Nearest Neighbors (KNN)

**GET** `/query/knn`

Find K nearest atoms by spatial distance.

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `x` | float | required | Query position X coordinate |
| `y` | float | required | Query position Y coordinate |
| `z` | float | required | Query position Z coordinate |
| `k` | int | 10 | Number of neighbors to return |
| `modality` | string | null | Filter by modality (text, code, image, audio, model) |
| `metadata` | JSON | null | Filter by metadata fields (e.g., `{"language": "en"}`) |

#### Response (200 OK)

```json
{
  "query_position": "POINTZ(0.5 0.5 0.5)",
  "k": 10,
  "results": [
    {
      "atom_id": 12345,
      "distance": 0.023,
      "position": "POINTZ(0.51 0.49 0.50)",
      "content_hash": "a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e",
      "canonical_text": "Hello",
      "metadata": {
        "modality": "text",
        "type": "word"
      }
    },
    {
      "atom_id": 12346,
      "distance": 0.041,
      "position": "POINTZ(0.48 0.52 0.51)",
      "content_hash": "b7d05f6f0d9f08e...",
      "canonical_text": "world",
      "metadata": {
        "modality": "text",
        "type": "word"
      }
    }
  ],
  "execution_time_ms": 4.2
}
```

#### cURL Example

```bash
curl -X GET "http://localhost:8000/api/v1/query/knn?x=0.5&y=0.5&z=0.5&k=10&modality=text"
```

#### Python Client

```python
import httpx

async def query_knn(x: float, y: float, z: float, k: int = 10, filters: dict = None) -> dict:
    """Query K-nearest neighbors."""
    async with httpx.AsyncClient() as client:
        params = {
            "x": x,
            "y": y,
            "z": z,
            "k": k
        }
        
        if filters:
            if "modality" in filters:
                params["modality"] = filters["modality"]
            if "metadata" in filters:
                params["metadata"] = filters["metadata"]
        
        response = await client.get(
            "http://localhost:8000/api/v1/query/knn",
            params=params
        )
        
        response.raise_for_status()
        return response.json()

# Usage
results = await query_knn(0.5, 0.5, 0.5, k=10, filters={"modality": "text"})

for result in results["results"]:
    print(f"{result['canonical_text']}: distance={result['distance']:.4f}")
```

---

### 2. Range Query

**GET** `/query/range`

Find all atoms within radius of center point.

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `x` | float | required | Center position X coordinate |
| `y` | float | required | Center position Y coordinate |
| `z` | float | required | Center position Z coordinate |
| `radius` | float | required | Search radius (Euclidean distance) |
| `modality` | string | null | Filter by modality |
| `metadata` | JSON | null | Filter by metadata fields |
| `limit` | int | 1000 | Maximum results |

#### Response (200 OK)

```json
{
  "center_position": "POINTZ(0.5 0.5 0.5)",
  "radius": 0.1,
  "results": [
    {
      "atom_id": 12345,
      "distance": 0.023,
      "position": "POINTZ(0.51 0.49 0.50)",
      "canonical_text": "Hello"
    },
    {
      "atom_id": 12346,
      "distance": 0.087,
      "position": "POINTZ(0.48 0.52 0.51)",
      "canonical_text": "world"
    }
  ],
  "count": 2,
  "execution_time_ms": 8.7
}
```

#### cURL Example

```bash
curl -X GET "http://localhost:8000/api/v1/query/range?x=0.5&y=0.5&z=0.5&radius=0.1&limit=100"
```

#### Python Client

```python
async def query_range(x: float, y: float, z: float, radius: float, filters: dict = None, limit: int = 1000) -> dict:
    """Query atoms within radius."""
    async with httpx.AsyncClient() as client:
        params = {
            "x": x,
            "y": y,
            "z": z,
            "radius": radius,
            "limit": limit
        }
        
        if filters:
            if "modality" in filters:
                params["modality"] = filters["modality"]
        
        response = await client.get(
            "http://localhost:8000/api/v1/query/range",
            params=params
        )
        
        response.raise_for_status()
        return response.json()

# Usage
results = await query_range(0.5, 0.5, 0.5, radius=0.1, limit=100)
print(f"Found {results['count']} atoms within radius")
```

---

### 3. Similarity Search by Content

**POST** `/query/similar`

Find atoms similar to given content (content-addressable lookup + KNN).

#### Request

```json
{
  "content": "SGVsbG8=",  // Base64-encoded content
  "top_k": 10,
  "modality": "text"
}
```

#### Response (200 OK)

```json
{
  "query_hash": "a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e",
  "exact_match": {
    "atom_id": 12345,
    "position": "POINTZ(0.51 0.49 0.50)",
    "canonical_text": "Hello"
  },
  "similar": [
    {
      "atom_id": 12346,
      "distance": 0.041,
      "position": "POINTZ(0.48 0.52 0.51)",
      "canonical_text": "world",
      "similarity": 0.959
    }
  ],
  "execution_time_ms": 6.3
}
```

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/query/similar \
  -H "Content-Type: application/json" \
  -d '{
    "content": "SGVsbG8=",
    "top_k": 10,
    "modality": "text"
  }'
```

#### Python Client

```python
import base64

async def query_similar(content: bytes, top_k: int = 10, modality: str = None) -> dict:
    """Find similar atoms by content."""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/query/similar",
            json={
                "content": base64.b64encode(content).decode('utf-8'),
                "top_k": top_k,
                "modality": modality
            }
        )
        
        response.raise_for_status()
        return response.json()

# Usage
results = await query_similar(b"Hello", top_k=10, modality="text")

if results["exact_match"]:
    print(f"Exact match: {results['exact_match']['canonical_text']}")

print(f"Similar atoms:")
for atom in results["similar"]:
    print(f"  {atom['canonical_text']}: similarity={atom['similarity']:.3f}")
```

---

### 4. Decompose Composition

**GET** `/query/composition/{composition_id}/decompose`

Recursively decompose composition to primitives.

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `max_depth` | int | 10 | Maximum recursion depth |

#### Response (200 OK)

```json
{
  "composition_id": 12355,
  "depth": 2,
  "tree": {
    "atom_id": 12355,
    "is_composition": true,
    "canonical_text": "Hello world",
    "components": [
      {
        "atom_id": 12353,
        "is_composition": true,
        "canonical_text": "Hello",
        "components": [
          {"atom_id": 12345, "is_composition": false, "canonical_text": "H"},
          {"atom_id": 12346, "is_composition": false, "canonical_text": "e"},
          {"atom_id": 12347, "is_composition": false, "canonical_text": "l"},
          {"atom_id": 12347, "is_composition": false, "canonical_text": "l"},
          {"atom_id": 12348, "is_composition": false, "canonical_text": "o"}
        ]
      },
      {
        "atom_id": 12354,
        "is_composition": true,
        "canonical_text": "world",
        "components": [
          {"atom_id": 12350, "is_composition": false, "canonical_text": "w"},
          {"atom_id": 12348, "is_composition": false, "canonical_text": "o"},
          {"atom_id": 12351, "is_composition": false, "canonical_text": "r"},
          {"atom_id": 12347, "is_composition": false, "canonical_text": "l"},
          {"atom_id": 12352, "is_composition": false, "canonical_text": "d"}
        ]
      }
    ]
  },
  "execution_time_ms": 12.5
}
```

#### cURL Example

```bash
curl -X GET "http://localhost:8000/api/v1/query/composition/12355/decompose?max_depth=5"
```

---

### 5. Find Composition Parents

**GET** `/query/atom/{atom_id}/parents`

Find all compositions containing this atom.

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `limit` | int | 100 | Maximum results |

#### Response (200 OK)

```json
{
  "atom_id": 12347,
  "canonical_text": "l",
  "parents": [
    {
      "composition_id": 12353,
      "canonical_text": "Hello",
      "component_count": 5
    },
    {
      "composition_id": 12354,
      "canonical_text": "world",
      "component_count": 5
    },
    {
      "composition_id": 12360,
      "canonical_text": "like",
      "component_count": 4
    }
  ],
  "count": 3
}
```

#### cURL Example

```bash
curl -X GET "http://localhost:8000/api/v1/query/atom/12347/parents?limit=100"
```

---

### 6. Relation Neighborhood Query

**GET** `/query/relations/{atom_id}`

Get all relations connected to atom (1-hop neighborhood).

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `relation_type` | string | null | Filter by relation type |
| `direction` | string | "both" | `incoming`, `outgoing`, or `both` |
| `min_weight` | float | 0.0 | Minimum relation weight |

#### Response (200 OK)

```json
{
  "atom_id": 12345,
  "relations": [
    {
      "relation_id": 1001,
      "source_id": 12345,
      "target_id": 12346,
      "relation_type": "follows",
      "weight": 1.5,
      "created_at": "2025-01-15T10:30:00Z",
      "updated_at": "2025-01-15T12:45:00Z"
    },
    {
      "relation_id": 1002,
      "source_id": 12347,
      "target_id": 12345,
      "relation_type": "synonym",
      "weight": 0.8,
      "created_at": "2025-01-15T11:00:00Z",
      "updated_at": "2025-01-15T11:00:00Z"
    }
  ],
  "count": 2
}
```

#### cURL Example

```bash
curl -X GET "http://localhost:8000/api/v1/query/relations/12345?relation_type=follows&direction=outgoing"
```

#### Python Client

```python
async def query_relations(atom_id: int, relation_type: str = None, direction: str = "both", min_weight: float = 0.0) -> dict:
    """Query atom's relation neighborhood."""
    async with httpx.AsyncClient() as client:
        params = {
            "direction": direction,
            "min_weight": min_weight
        }
        
        if relation_type:
            params["relation_type"] = relation_type
        
        response = await client.get(
            f"http://localhost:8000/api/v1/query/relations/{atom_id}",
            params=params
        )
        
        response.raise_for_status()
        return response.json()

# Usage
relations = await query_relations(12345, relation_type="follows", direction="outgoing")

for rel in relations["relations"]:
    print(f"Relation {rel['relation_id']}: {rel['source_id']} --{rel['relation_type']}--> {rel['target_id']} (weight={rel['weight']:.2f})")
```

---

### 7. Multi-Hop Graph Traversal

**POST** `/query/traverse`

Traverse graph from starting atom to specified depth.

#### Request

```json
{
  "start_id": 12345,
  "max_hops": 3,
  "relation_type": null,  // Filter by relation type (null = all)
  "min_weight": 0.0,
  "limit": 1000
}
```

#### Response (200 OK)

```json
{
  "start_id": 12345,
  "max_hops": 3,
  "paths": [
    {
      "path": [12345, 12346, 12347],
      "hops": 2,
      "total_weight": 2.3,
      "relations": [
        {"relation_id": 1001, "type": "follows", "weight": 1.5},
        {"relation_id": 1003, "type": "follows", "weight": 0.8}
      ]
    },
    {
      "path": [12345, 12350, 12351, 12352],
      "hops": 3,
      "total_weight": 3.1,
      "relations": [
        {"relation_id": 1010, "type": "synonym", "weight": 1.2},
        {"relation_id": 1011, "type": "synonym", "weight": 0.9},
        {"relation_id": 1012, "type": "synonym", "weight": 1.0}
      ]
    }
  ],
  "count": 2
}
```

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/query/traverse \
  -H "Content-Type: application/json" \
  -d '{
    "start_id": 12345,
    "max_hops": 3,
    "min_weight": 0.5
  }'
```

#### Python Client

```python
async def query_traverse(start_id: int, max_hops: int = 3, relation_type: str = None, min_weight: float = 0.0) -> dict:
    """Multi-hop graph traversal."""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/query/traverse",
            json={
                "start_id": start_id,
                "max_hops": max_hops,
                "relation_type": relation_type,
                "min_weight": min_weight
            }
        )
        
        response.raise_for_status()
        return response.json()

# Usage
result = await query_traverse(12345, max_hops=3, min_weight=0.5)

for path in result["paths"]:
    print(f"Path (hops={path['hops']}, weight={path['total_weight']:.2f}): {path['path']}")
```

---

### 8. Shortest Path (Dijkstra)

**POST** `/query/shortest_path`

Find shortest weighted path between two atoms.

#### Request

```json
{
  "start_id": 12345,
  "end_id": 12352,
  "relation_type": null  // Filter by relation type
}
```

#### Response (200 OK)

```json
{
  "start_id": 12345,
  "end_id": 12352,
  "path": [12345, 12346, 12350, 12352],
  "total_weight": 2.8,
  "hops": 3,
  "relations": [
    {"relation_id": 1001, "type": "follows", "weight": 1.5},
    {"relation_id": 1005, "type": "follows", "weight": 0.7},
    {"relation_id": 1008, "type": "follows", "weight": 0.6}
  ],
  "execution_time_ms": 18.3
}
```

#### Response (404 Not Found)

```json
{
  "error": "No path found",
  "start_id": 12345,
  "end_id": 99999
}
```

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/query/shortest_path \
  -H "Content-Type: application/json" \
  -d '{
    "start_id": 12345,
    "end_id": 12352
  }'
```

#### Python Client

```python
async def query_shortest_path(start_id: int, end_id: int, relation_type: str = None) -> dict:
    """Find shortest path between atoms."""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/query/shortest_path",
            json={
                "start_id": start_id,
                "end_id": end_id,
                "relation_type": relation_type
            }
        )
        
        if response.status_code == 404:
            return None
        
        response.raise_for_status()
        return response.json()

# Usage
path = await query_shortest_path(12345, 12352)

if path:
    print(f"Shortest path: {path['path']}")
    print(f"Total weight: {path['total_weight']:.2f}")
    print(f"Hops: {path['hops']}")
else:
    print("No path found")
```

---

### 9. Voronoi Cell Query

**GET** `/query/voronoi/{atom_id}`

Find atom's Voronoi cell (nearest neighbor region).

**Status:** ⚠️ Experimental (computationally expensive for large datasets)

#### Response (200 OK)

```json
{
  "atom_id": 12345,
  "position": "POINTZ(0.51 0.49 0.50)",
  "voronoi_cell": "POLYGON((0.45 0.44 0.45, 0.56 0.44 0.45, 0.56 0.54 0.45, ...))",
  "volume": 0.0012,
  "contained_atoms": [],
  "execution_time_ms": 450.2
}
```

#### cURL Example

```bash
curl -X GET http://localhost:8000/api/v1/query/voronoi/12345
```

---

## Performance

| Endpoint | Index Used | Latency (p95) | Notes |
|----------|-----------|---------------|-------|
| `/query/knn` | GiST (POINTZ) | < 5ms (K=10) | O(log N) with spatial index |
| `/query/range` | GiST (POINTZ) | < 10ms (100 results) | O(log N + M) where M = results |
| `/query/similar` | B-tree (content_hash) | < 5ms | CAS lookup + KNN |
| `/composition/{id}/decompose` | GIN (component_ids) | < 10ms (depth 3) | O(depth × components) |
| `/atom/{id}/parents` | GIN (component_ids) | < 5ms | O(log N) |
| `/relations/{id}` | B-tree (source_id, target_id) | < 3ms (1-hop) | O(log N) |
| `/query/traverse` | B-tree (source_id, target_id) | < 50ms (3 hops) | O(E) where E = edges explored |
| `/query/shortest_path` | B-tree (source_id, target_id) | < 100ms | O(E log V) Dijkstra |
| `/query/voronoi` | GiST (POINTZ) | < 500ms | O(N) - computationally expensive |

---

## POINTZM Optimization (Future)

**Current State (POINTZ):**
- Spatial queries use GiST index on POINTZ(X, Y, Z)
- KNN: O(log N) with GiST
- Range: O(log N + M) with GiST

**Designed State (POINTZM):**
- Add M coordinate: Hilbert curve index
- KNN: O(log N) Hilbert range query + refinement
- Range: O(log N) Hilbert range query (faster than GiST for high-dimensional spaces)

**Migration Status:** ⚠️ POINTZM migration TODO (see POINTZ_TO_POINTZM_MIGRATION.md)

---

## Pagination

For large result sets, use cursor-based pagination:

#### Request

```bash
curl -X GET "http://localhost:8000/api/v1/query/range?x=0.5&y=0.5&z=0.5&radius=0.5&limit=100&cursor=eyJhdG9tX2lkIjoxMjM0NX0="
```

#### Response

```json
{
  "results": [...],
  "count": 100,
  "next_cursor": "eyJhdG9tX2lkIjoxMjQ0NX0=",
  "has_more": true
}
```

---

## Filtering Examples

### Metadata JSONB Queries

```bash
# Find atoms with specific language
curl -X GET "http://localhost:8000/api/v1/query/knn?x=0.5&y=0.5&z=0.5&k=10&metadata=%7B%22language%22%3A%22en%22%7D"

# Find atoms with type=word
curl -X GET "http://localhost:8000/api/v1/query/range?x=0.5&y=0.5&z=0.5&radius=0.1&metadata=%7B%22type%22%3A%22word%22%7D"
```

### Modality Filtering

```bash
# Find only text atoms
curl -X GET "http://localhost:8000/api/v1/query/knn?x=0.5&y=0.5&z=0.5&k=10&modality=text"

# Find only code atoms
curl -X GET "http://localhost:8000/api/v1/query/range?x=0.5&y=0.5&z=0.5&radius=0.1&modality=code"
```

---

## Query Cost Estimation (TODO)

**GET** `/query/explain`

**Status:** ⚠️ DESIGN PLANNED, IMPLEMENTATION TODO

Estimate query cost and execution plan before running expensive queries.

**Request:** Same parameters as target query (e.g., KNN).

**Response (200 OK):**
```json
{
  "query_type": "knn",
  "estimated_cost": 142.5,
  "estimated_rows": 10,
  "estimated_time_ms": 8.2,
  "index_used": "atom_spatial_idx (GiST)",
  "warnings": []
}
```

**High-Cost Warning:**
```json
{
  "estimated_cost": 98765.4,
  "estimated_rows": 500000,
  "warnings": [
    "Query will scan 50% of atom table",
    "Consider reducing radius or adding filters"
  ]
}
```

**Use Cases:** Validate query before execution, auto-optimize parameters, alert users to slow queries.

---

## Status Summary

**Production Ready:**
- ✅ KNN queries (POINTZ with GiST index)
- ✅ Range queries (ST_DWithin with GiST index)
- ✅ Similarity search (CAS lookup + spatial KNN)
- ✅ Composition decomposition (recursive GIN queries)
- ✅ Composition parent queries (reverse GIN lookups)
- ✅ Relation neighborhood (1-hop graph queries)
- ✅ Multi-hop graph traversal (recursive CTEs)
- ✅ Shortest path (Dijkstra implementation)
- ✅ Metadata filtering (JSONB queries)

**Experimental:**
- 🟡 Voronoi cell queries (computationally expensive, O(N))

**TODO:**
- ❌ JWT authentication (currently bypassed)
- ❌ POINTZM migration (Hilbert range optimization)
- ❌ Rate limiting
- ❌ Query result caching
- ❌ Spatial clustering queries (DBSCAN, K-means)

---

**This API is PRODUCTION-READY for all spatial and graph queries. Hilbert optimization requires POINTZM migration.**

---

## Edge Case Handling

**Common Edge Cases:**

### Empty Results

**Scenario:** KNN k=10 but only 3 atoms exist

```json
{"results": [{"atom_id": 1}, {"atom_id": 2}, {"atom_id": 3}], "requested": 10, "returned": 3}
```

### Zero Distance

**Scenario:** Query point matches atom exactly

```json
{"results": [{"atom_id": 42, "distance": 0.0, "exact_match": true}]}
```

### Invalid Coordinates

**400 Bad Request:**
```json
{"error": "Coordinates must be in [0.0, 1.0]", "invalid": {"x": 1.5}}
```

### Invalid k

**400 Bad Request:**
```json
{"error": "k must be positive", "provided": 0}
```

**Validation:**

```python
class SpatialQueryValidator:
    @staticmethod
    def validate_point(x, y, z):
        for coord, name in [(x, 'x'), (y, 'y'), (z, 'z')]:
            if not (0.0 <= coord <= 1.0):
                raise ValueError(f"{name}={coord} out of bounds")
    
    @staticmethod
    def validate_k(k):
        if k <= 0:
            raise ValueError(f"k must be positive")
        if k > 1000:
            raise ValueError(f"k max 1000")
```

---
