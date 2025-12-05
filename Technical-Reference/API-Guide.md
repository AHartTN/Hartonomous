---
title: "API Reference Guide"
author: "Hartonomous Development Team"
date: "2025-12-05"
version: "1.0"
status: "Active"
---

# API Reference Guide

## Table of Contents
- [Authentication](#authentication)
- [Content Ingestion](#content-ingestion)
- [Spatial Queries](#spatial-queries)
- [BPE Vocabulary](#bpe-vocabulary)
- [Embeddings](#embeddings)
- [Error Handling](#error-handling)
- [Rate Limiting](#rate-limiting)

---

## Authentication

All API endpoints require JWT Bearer authentication via Microsoft Entra ID.

### Get Access Token

```bash
# Client Credentials Flow
TOKEN=$(curl -X POST "https://login.microsoftonline.com/$TENANT_ID/oauth2/v2.0/token" \
  -d "client_id=$CLIENT_ID" \
  -d "client_secret=$CLIENT_SECRET" \
  -d "scope=api://hartonomous/.default" \
  -d "grant_type=client_credentials" \
  | jq -r '.access_token')

# Use token in requests
curl -H "Authorization: Bearer $TOKEN" https://api.hartonomous.com/api/content
```

### Required Scopes

| Endpoint | Required Scope | Description |
|----------|---------------|-------------|
| `POST /api/content/ingest` | `api.write` | Ingest content |
| `GET /api/content/*` | `api.read` | Query content |
| `GET /api/spatial/*` | `api.read` | Spatial queries |
| `POST /api/bpe/learn` | `api.admin` | Admin operations |

---

## Content Ingestion

### Ingest Text Content

**Endpoint**: `POST /api/content/ingest`

**Request**:
```json
{
  "content": "SGVsbG8gV29ybGQ=",
  "metadata": {
    "source": "example.txt",
    "contentType": "text/plain"
  }
}
```

**Response**:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "atomIds": [
    "atom-uuid-1",
    "atom-uuid-2",
    "atom-uuid-3"
  ],
  "atomCount": 3,
  "newAtomsCreated": 1,
  "deduplicationRate": 0.666,
  "boundaryGeometry": "POLYGON((1000 500000, 1100 500000, 1100 510000, 1000 510000, 1000 500000))",
  "ingestionTime": 5.3
}
```

**cURL Example**:
```bash
CONTENT=$(echo "Hello World" | base64)

curl -X POST "https://api.hartonomous.com/api/content/ingest" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"content\": \"$CONTENT\",
    \"metadata\": {
      \"source\": \"test.txt\"
    }
  }"
```

### Ingest File

```bash
# Ingest binary file
CONTENT=$(base64 < document.pdf)

curl -X POST "https://api.hartonomous.com/api/content/ingest" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"content\": \"$CONTENT\",
    \"metadata\": {
      \"source\": \"document.pdf\",
      \"contentType\": \"application/pdf\"
    }
  }"
```

### Batch Ingestion

**Endpoint**: `POST /api/content/ingest/batch`

```bash
curl -X POST "https://api.hartonomous.com/api/content/ingest/batch" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"contents\": [
      {\"content\": \"$(echo 'File 1' | base64)\", \"metadata\": {\"source\": \"file1.txt\"}},
      {\"content\": \"$(echo 'File 2' | base64)\", \"metadata\": {\"source\": \"file2.txt\"}}
    ]
  }"
```

---

## Spatial Queries

### Find Similar Content (k-NN)

**Endpoint**: `GET /api/spatial/knn`

**Parameters**:
- `targetId` (required) - UUID of target atom
- `k` (optional, default=10) - Number of neighbors
- `minSimilarity` (optional) - Minimum similarity threshold

**Example**:
```bash
curl "https://api.hartonomous.com/api/spatial/knn?targetId=atom-uuid&k=10" \
  -H "Authorization: Bearer $TOKEN"
```

**Response**:
```json
{
  "results": [
    {
      "atomId": "similar-uuid-1",
      "distance": 12.5,
      "similarity": 0.987,
      "location": {
        "x": 1000,
        "y": 500000,
        "z": 1000000,
        "m": 150
      }
    }
  ]
}
```

### Find Content Within Region

**Endpoint**: `POST /api/spatial/contains`

**Request**:
```json
{
  "boundary": "POLYGON((1000 500000, 2000 500000, 2000 600000, 1000 600000, 1000 500000))"
}
```

**cURL Example**:
```bash
curl -X POST "https://api.hartonomous.com/api/spatial/contains" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"boundary\": \"POLYGON((1000 500000, 2000 500000, 2000 600000, 1000 600000, 1000 500000))\"
  }"
```

### Search by Properties

**Endpoint**: `GET /api/spatial/search`

**Parameters**:
- `minEntropy` / `maxEntropy` - Shannon entropy range [0, 2097151]
- `minCompressibility` / `maxCompressibility` - Kolmogorov complexity range
- `minConnectivity` / `maxConnectivity` - Reference count range
- `limit` (default=100) - Maximum results

**Examples**:

```bash
# Find high entropy content (random/encrypted)
curl "https://api.hartonomous.com/api/spatial/search?minEntropy=1500000&maxEntropy=2097151" \
  -H "Authorization: Bearer $TOKEN"

# Find highly compressible content (repetitive)
curl "https://api.hartonomous.com/api/spatial/search?minCompressibility=1500000" \
  -H "Authorization: Bearer $TOKEN"

# Find hot atoms (frequently referenced)
curl "https://api.hartonomous.com/api/spatial/search?minConnectivity=1000000" \
  -H "Authorization: Bearer $TOKEN"
```

### Distance Query

**Endpoint**: `GET /api/spatial/distance`

**Parameters**:
- `sourceId` (required) - Source atom UUID
- `targetId` (required) - Target atom UUID

```bash
curl "https://api.hartonomous.com/api/spatial/distance?sourceId=uuid1&targetId=uuid2" \
  -H "Authorization: Bearer $TOKEN"
```

**Response**:
```json
{
  "sourceId": "uuid1",
  "targetId": "uuid2",
  "euclideanDistance": 12.5,
  "manhattanDistance": 25.0,
  "semanticSimilarity": 0.987
}
```

---

## BPE Vocabulary

### Get Learned Patterns

**Endpoint**: `GET /api/bpe/tokens`

**Parameters**:
- `limit` (default=100) - Maximum results
- `orderBy` (default=frequency) - Sort field: `frequency`, `length`, `created_at`
- `minFrequency` (optional) - Minimum occurrence count

```bash
# Get top 100 most frequent patterns
curl "https://api.hartonomous.com/api/bpe/tokens?limit=100&orderBy=frequency" \
  -H "Authorization: Bearer $TOKEN"
```

**Response**:
```json
{
  "tokens": [
    {
      "id": "token-uuid-1",
      "compositionGeometry": "LINESTRING(1000 500000 1000000, 1001 500100 1000100)",
      "atomCount": 2,
      "frequency": 15432,
      "atoms": [
        {"id": "atom-1", "data": "public"},
        {"id": "atom-2", "data": "static"}
      ]
    }
  ],
  "totalCount": 10000
}
```

### Get Token Details

**Endpoint**: `GET /api/bpe/tokens/{id}`

```bash
curl "https://api.hartonomous.com/api/bpe/tokens/token-uuid" \
  -H "Authorization: Bearer $TOKEN"
```

### Trigger Vocabulary Learning

**Endpoint**: `POST /api/bpe/learn` (Admin only)

**Request**:
```json
{
  "minFrequency": 100,
  "maxVocabSize": 10000,
  "hilbertGapThreshold": 1000
}
```

```bash
curl -X POST "https://api.hartonomous.com/api/bpe/learn" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"minFrequency\": 100,
    \"maxVocabSize\": 10000
  }"
```

---

## Embeddings

### Create Embedding

**Endpoint**: `POST /api/embeddings`

**Request**:
```json
{
  "contentId": "content-uuid",
  "modelName": "text-embedding-ada-002",
  "vector": [0.123, -0.456, 0.789, ...]
}
```

```bash
curl -X POST "https://api.hartonomous.com/api/embeddings" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d @embedding.json
```

### Search Embeddings (Semantic Search)

**Endpoint**: `POST /api/embeddings/search`

**Request**:
```json
{
  "vector": [0.123, -0.456, 0.789, ...],
  "k": 10,
  "modelName": "text-embedding-ada-002"
}
```

```bash
curl -X POST "https://api.hartonomous.com/api/embeddings/search" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"vector\": $(cat query_vector.json),
    \"k\": 10
  }"
```

**Response**:
```json
{
  "results": [
    {
      "embeddingId": "embedding-uuid-1",
      "contentId": "content-uuid-1",
      "cosineSimilarity": 0.987,
      "distance": 0.013
    }
  ]
}
```

---

## Error Handling

### Standard Error Response

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "Bad Request",
  "status": 400,
  "detail": "Content cannot be empty",
  "traceId": "00-abc123-def456-00",
  "errors": {
    "Content": ["The Content field is required."]
  }
}
```

### HTTP Status Codes

| Code | Meaning | Description |
|------|---------|-------------|
| 200 | OK | Request successful |
| 201 | Created | Resource created |
| 400 | Bad Request | Invalid request data |
| 401 | Unauthorized | Missing or invalid JWT |
| 403 | Forbidden | Insufficient permissions |
| 404 | Not Found | Resource not found |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Server error |
| 503 | Service Unavailable | Service temporarily unavailable |

### Common Error Scenarios

**Invalid JWT**:
```bash
curl https://api.hartonomous.com/api/content
# Response: 401 Unauthorized
{
  "type": "https://httpstatuses.com/401",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Authorization header missing or invalid"
}
```

**Rate Limit Exceeded**:
```bash
# Response: 429 Too Many Requests
{
  "type": "https://httpstatuses.com/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded",
  "retryAfter": 60
}
```

---

## Rate Limiting

### Token Bucket Algorithm

- **Token Limit**: 100 requests per minute per user
- **Replenishment**: 100 tokens per minute
- **Burst**: Up to 10 requests queued

### Rate Limit Headers

```http
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 85
X-RateLimit-Reset: 1638720000
```

### Handling Rate Limits

```bash
# Check rate limit status
curl -I "https://api.hartonomous.com/api/content" \
  -H "Authorization: Bearer $TOKEN"

# Retry after rate limit with exponential backoff
for i in {1..5}; do
  response=$(curl -w "%{http_code}" -X POST "https://api.hartonomous.com/api/content/ingest" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "$payload")
  
  if [ "$response" == "429" ]; then
    wait_time=$((2**i))
    echo "Rate limited. Waiting ${wait_time}s..."
    sleep $wait_time
  else
    break
  fi
done
```

---

## API Client Examples

### Python Client

```python
import requests
import base64

class HartonomousClient:
    def __init__(self, base_url, token):
        self.base_url = base_url
        self.headers = {
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json"
        }
    
    def ingest_content(self, content: bytes, source: str):
        encoded = base64.b64encode(content).decode()
        payload = {
            "content": encoded,
            "metadata": {"source": source}
        }
        response = requests.post(
            f"{self.base_url}/api/content/ingest",
            json=payload,
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()
    
    def find_similar(self, atom_id: str, k: int = 10):
        response = requests.get(
            f"{self.base_url}/api/spatial/knn",
            params={"targetId": atom_id, "k": k},
            headers=self.headers
        )
        response.raise_for_status()
        return response.json()

# Usage
client = HartonomousClient("https://api.hartonomous.com", token)
result = client.ingest_content(b"Hello World", "test.txt")
similar = client.find_similar(result["atomIds"][0])
```

### C# Client

```csharp
public class HartonomousClient
{
    private readonly HttpClient _httpClient;
    
    public HartonomousClient(string baseUrl, string token)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
    }
    
    public async Task<IngestionResult> IngestContentAsync(
        byte[] content, 
        string source)
    {
        var payload = new
        {
            content = Convert.ToBase64String(content),
            metadata = new { source }
        };
        
        var response = await _httpClient.PostAsJsonAsync(
            "/api/content/ingest", 
            payload);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<IngestionResult>();
    }
    
    public async Task<SpatialQueryResult> FindSimilarAsync(
        Guid atomId, 
        int k = 10)
    {
        var response = await _httpClient.GetAsync(
            $"/api/spatial/knn?targetId={atomId}&k={k}");
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<SpatialQueryResult>();
    }
}
```

---

## Interactive API Documentation

**Swagger UI**: https://api.hartonomous.com/swagger

Access interactive API documentation with:
- Live request testing
- Schema definitions
- Example requests/responses
- Authentication testing

---

**Navigation**:  
← [Technical Reference](../Technical-Reference.md) | [Home](../Home.md) | [Database Schema](Database-Schema.md) →
