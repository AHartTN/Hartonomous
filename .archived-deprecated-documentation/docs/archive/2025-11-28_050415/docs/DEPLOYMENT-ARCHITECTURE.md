# Hartonomous Deployment Architecture

Multi-service deployment with subdomain-based routing for separation of concerns.

---

## Service Architecture

```
???????????????????????????????????????????????????????????
?  hartonomous.com (Main Landing Page)                    ?
?  - Marketing site                                       ?
?  - Documentation                                        ?
?  - Pricing                                              ?
???????????????????????????????????????????????????????????
                          ?
        ?????????????????????????????????????
        ?                 ?                 ?
        ?                 ?                 ?
?????????????????  ????????????????  ??????????????????
? api.          ?  ? code.        ?  ? ai.            ?
? hartonomous   ?  ? hartonomous  ?  ? hartonomous    ?
? .com          ?  ? .com         ?  ? .com           ?
?               ?  ?              ?  ?                ?
? Python        ?  ? C# .NET 10   ?  ? Python         ?
? FastAPI       ?  ? Roslyn/      ?  ? FastAPI        ?
?               ?  ? Tree-sitter  ?  ?                ?
? Port: 8000    ?  ? Port: 8001   ?  ? Port: 8002     ?
?????????????????  ????????????????  ??????????????????
        ?                 ?                 ?
        ?????????????????????????????????????
                          ?
          ?????????????????????????????????
          ?  PostgreSQL 15 + PostGIS      ?
          ?  Port: 5432                   ?
          ?                               ?
          ?  - atom table                 ?
          ?  - atom_composition           ?
          ?  - atom_relation              ?
          ?????????????????????????????????
                          ?
                          ?
          ?????????????????????????????????
          ?  Neo4j 5.15 (Provenance)      ?
          ?  Port: 7687                   ?
          ?????????????????????????????????
```

---

## Service Definitions

### 1. **api.hartonomous.com** (Core API)

**Purpose:** Main ingestion, query, and orchestration API

**Technology:** Python 3.14 + FastAPI

**Endpoints:**
- `/v1/ingest/text` - Text atomization
- `/v1/ingest/image` - Image atomization
- `/v1/ingest/audio` - Audio atomization
- `/v1/query/semantic` - Semantic search
- `/v1/query/spatial` - Spatial queries
- `/v1/train/*` - Model training endpoints

**Database:** Direct PostgreSQL connection

**Port:** 8000

**Docker Image:** `hartonomous/api:latest`

---

### 2. **code.hartonomous.com** (Code Atomization Service)

**Purpose:** Deep AST atomization for source code (Roslyn + Tree-sitter)

**Technology:** C# .NET 10 + Roslyn + Tree-sitter

**Endpoints:**
- `/api/v1/atomize/csharp` - C# semantic analysis
- `/api/v1/atomize/csharp/file` - C# file upload
- `/api/v1/atomize/python` - Python AST (Tree-sitter) [Coming soon]
- `/api/v1/atomize/javascript` - JS/TS AST [Coming soon]
- `/api/v1/landmarks` - Spatial landmark visualization

**Database:** Direct PostgreSQL connection (bulk insert)

**Port:** 8001

**Docker Image:** `hartonomous/code-atomizer:latest`

**SaaS Offering:**
- **Free:** 100 files/month
- **Pro:** 10,000 files/month ($49/mo)
- **Enterprise:** Unlimited + on-premise

---

### 3. **ai.hartonomous.com** (AI Model Service)

**Purpose:** AI model atomization, inference, and training

**Technology:** Python 3.14 + FastAPI + PyTorch + Transformers

**Endpoints:**
- `/v1/models/ingest` - Atomize PyTorch/GGUF/SafeTensors models
- `/v1/models/inference` - Run inference via atomized models
- `/v1/models/finetune` - Fine-tune on ingested data
- `/v1/embeddings/*` - Generate embeddings for spatial positioning

**Database:** Direct PostgreSQL connection

**Port:** 8002

**Docker Image:** `hartonomous/ai-service:latest`

**SaaS Offering:**
- **Free:** 1,000 inferences/month
- **Pro:** 100,000 inferences/month ($99/mo)
- **Enterprise:** Unlimited + custom models

---

## Deployment Targets

### Development (localhost)

```yaml
# docker-compose.dev.yml
services:
  postgres:
    image: postgis/postgis:15-3.4
    ports: ["5432:5432"]
  
  neo4j:
    image: neo4j:5.15
    ports: ["7687:7687", "7474:7474"]
  
  api:
    build: ./api
    ports: ["8000:8000"]
    depends_on: [postgres, neo4j]
  
  code-atomizer:
    build: ./src/Hartonomous.CodeAtomizer.Api
    ports: ["8001:8001"]
    depends_on: [postgres]
  
  ai-service:
    build: ./src/Hartonomous.AI.Service
    ports: ["8002:8002"]
    depends_on: [postgres]
```

**Access:**
- Main API: http://localhost:8000
- Code Service: http://localhost:8001
- AI Service: http://localhost:8002

---

### Staging (hart-server via Azure Arc)

**Deployment:** GitHub Actions ? Azure Arc ? hart-server (Ubuntu)

**DNS:**
- api-staging.hartonomous.com ? hart-server:8000
- code-staging.hartonomous.com ? hart-server:8001
- ai-staging.hartonomous.com ? hart-server:8002

**Reverse Proxy:** Nginx on hart-server
- SSL/TLS termination (Let's Encrypt)
- Rate limiting
- API key authentication

**Configuration:**
- PostgreSQL: hart-server:5432 (local)
- Neo4j: hart-server:7687 (local)
- Azure Key Vault: Secrets for production credentials

---

### Production (Azure Container Apps)

**Infrastructure:**
- **Azure Container Apps** (serverless containers)
- **Azure Database for PostgreSQL Flexible Server**
- **Neo4j Aura** (managed Neo4j)
- **Azure Front Door** (CDN + WAF)
- **Azure Key Vault** (secrets)
- **Application Insights** (monitoring)

**Subdomains:**
- api.hartonomous.com ? Container App `hartonomous-api`
- code.hartonomous.com ? Container App `hartonomous-code`
- ai.hartonomous.com ? Container App `hartonomous-ai`

**Scaling:**
- API: 2-10 replicas (CPU-based)
- Code: 1-5 replicas (memory-based)
- AI: 1-3 replicas (GPU-enabled)

**DNS Configuration:**
```
api.hartonomous.com   CNAME  hartonomous-api.azurecontainerapps.io
code.hartonomous.com  CNAME  hartonomous-code.azurecontainerapps.io
ai.hartonomous.com    CNAME  hartonomous-ai.azurecontainerapps.io
```

---

## CI/CD Pipeline

### GitHub Actions Workflow

```yaml
name: Deploy to Production

on:
  push:
    branches: [main]

jobs:
  build-api:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build Python API
        run: docker build -t ghcr.io/aharttn/hartonomous-api:${{ github.sha }} -f docker/Dockerfile.api .
      - name: Push to GHCR
        run: docker push ghcr.io/aharttn/hartonomous-api:${{ github.sha }}
  
  build-code:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build Code Atomizer
        run: docker build -t ghcr.io/aharttn/hartonomous-code:${{ github.sha }} -f docker/Dockerfile.code .
      - name: Push to GHCR
        run: docker push ghcr.io/aharttn/hartonomous-code:${{ github.sha }}
  
  build-ai:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build AI Service
        run: docker build -t ghcr.io/aharttn/hartonomous-ai:${{ github.sha }} -f docker/Dockerfile.ai .
      - name: Push to GHCR
        run: docker push ghcr.io/aharttn/hartonomous-ai:${{ github.sha }}
  
  deploy:
    needs: [build-api, build-code, build-ai]
    runs-on: ubuntu-latest
    steps:
      - name: Deploy to Azure Container Apps
        run: |
          az containerapp update \
            --name hartonomous-api \
            --image ghcr.io/aharttn/hartonomous-api:${{ github.sha }}
          
          az containerapp update \
            --name hartonomous-code \
            --image ghcr.io/aharttn/hartonomous-code:${{ github.sha }}
          
          az containerapp update \
            --name hartonomous-ai \
            --image ghcr.io/aharttn/hartonomous-ai:${{ github.sha }}
```

---

## Inter-Service Communication

### Code Atomizer ? PostgreSQL
```python
# api/services/code_atomization.py
client = CodeAtomizerClient("http://code.hartonomous.com")
result = await client.atomize_csharp(code, filename)
# Client handles bulk insert to PostgreSQL
```

### AI Service ? Code Atomizer
```python
# ai-service/routes/models.py
# When ingesting a model, extract code (if present)
code_client = CodeAtomizerClient("http://code.hartonomous.com")
await code_client.atomize_python(model_training_script)
```

### Main API ? AI Service
```python
# api/routes/ingest.py
# For ML models, delegate to AI service
ai_client = AIServiceClient("http://ai.hartonomous.com")
await ai_client.atomize_model(model_file)
```

---

## Security

### Authentication
- **API Keys:** Required for all production endpoints
- **Rate Limiting:** 100 req/min (free), 10k req/min (pro)
- **JWT Tokens:** For user authentication (Entra ID + CIAM)

### Network Security
- **Azure Private Link:** PostgreSQL accessible only from Container Apps
- **Virtual Network:** All services in same VNet
- **NSG Rules:** Restrict traffic to necessary ports

### Data Security
- **Encryption at Rest:** Azure Database Encryption
- **Encryption in Transit:** TLS 1.3 only
- **Secret Management:** Azure Key Vault (no secrets in code)

---

## Monitoring & Observability

### Metrics (Prometheus)
- Request rate, latency, error rate per service
- Database connection pool metrics
- Queue depths (background workers)

### Logs (Application Insights)
- Structured JSON logs
- Distributed tracing (OpenTelemetry)
- Exception tracking

### Alerts
- Service downtime (> 1 min)
- Error rate > 5%
- Response time P95 > 1s
- Database CPU > 80%

---

## Cost Optimization

### Free Tier
- 2 Container App replicas (shared compute)
- PostgreSQL Burstable B1ms (1 vCore, 2GB RAM)
- Neo4j Aura Free (1GB memory)
- **Total:** ~$50/month

### Production Tier
- 5-10 Container App replicas (dedicated compute)
- PostgreSQL General Purpose D4s (4 vCores, 16GB RAM)
- Neo4j Aura Professional (8GB memory)
- Azure Front Door (CDN + WAF)
- **Total:** ~$500-1000/month

---

## Roadmap

### Phase 1: Core Services ?
- [x] Main API (api.hartonomous.com)
- [x] Code Atomizer (code.hartonomous.com)
- [ ] AI Service (ai.hartonomous.com)

### Phase 2: Production Deployment ??
- [ ] Azure Container Apps setup
- [ ] DNS + SSL configuration
- [ ] CI/CD pipeline
- [ ] Monitoring + alerting

### Phase 3: SaaS Features ??
- [ ] API key management
- [ ] Usage tracking + billing (Stripe)
- [ ] Multi-tenancy (tenant_id isolation)
- [ ] Customer portal

---

**Contact:** aharttn@gmail.com  
**GitHub:** https://github.com/AHartTN/Hartonomous
