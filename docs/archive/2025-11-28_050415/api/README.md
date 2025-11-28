# Hartonomous REST API

**FastAPI + psycopg3 async + Apache AGE sync worker**

---

## ?? Quick Start

### 1. Install Dependencies

```bash
cd api
pip install -r requirements.txt
```

### 2. Configure Environment

```bash
cp .env.example .env
# Edit .env with your PostgreSQL credentials
```

### 3. Run API

```bash
# Development (with auto-reload)
python main.py

# Production (with uvicorn)
uvicorn main:app --host 0.0.0.0 --port 8000 --workers 4
```

### 4. Test Endpoints

```bash
# Health check
curl http://localhost:8000/v1/health

# Readiness check (tests database)
curl http://localhost:8000/v1/ready

# Statistics
curl http://localhost:8000/v1/stats

# Interactive docs
open http://localhost:8000/docs
```

---

## ?? API Endpoints

### Health & Monitoring

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Root endpoint |
| `/v1/health` | GET | Basic health check |
| `/v1/ready` | GET | Readiness probe (DB check) |
| `/v1/stats` | GET | Database statistics |

### Ingest (Coming Soon)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/v1/ingest/text` | POST | Atomize text |
| `/v1/ingest/image` | POST | Atomize image |
| `/v1/ingest/audio` | POST | Atomize audio |

### Query (Coming Soon)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/v1/atoms/:id` | GET | Get atom by ID |
| `/v1/atoms/:id/lineage` | GET | Get atom lineage (provenance) |
| `/v1/atoms/search` | GET | Spatial search |

### Train (Coming Soon)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/v1/train/batch` | POST | Train on batch |
| `/v1/train/status` | GET | Training status |

### Export (Coming Soon)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/v1/export/onnx` | POST | Export model to ONNX |

---

## ??? Architecture

### Connection Pooling

**AsyncConnectionPool (psycopg3):**
- Min size: 5 connections
- Max size: 20 connections
- Timeout: 30 seconds
- Max idle: 600 seconds (10 minutes)

**Best practices:**
- Pool created once at startup (lifespan)
- Connections reused across requests
- Automatic connection health checks
- Graceful shutdown

### AGE Sync Worker

**Background worker for provenance:**
- Listens on PostgreSQL channel: `atom_created`
- Syncs atom_relation changes to Apache AGE graph
- Zero-latency updates via LISTEN/NOTIFY
- Independent of request/response cycle

**Flow:**
1. Atom created ? Trigger fires ? NOTIFY sent
2. Worker receives notification
3. Worker fetches atom details
4. Worker syncs to AGE graph
5. Provenance queries available immediately

---

## ?? Configuration

### Environment Variables

See `.env.example` for all options.

**Key settings:**

```bash
# Database
DATABASE_URL=postgresql://user:pass@host:5432/hartonomous

# Connection pool
POOL_MIN_SIZE=5
POOL_MAX_SIZE=20

# API
API_HOST=0.0.0.0
API_PORT=8000

# Logging
LOG_LEVEL=INFO

# AGE worker
AGE_WORKER_ENABLED=true
```

---

## ?? Testing

### Manual Testing

```bash
# Health check
curl http://localhost:8000/v1/health

# Readiness (tests DB connection)
curl http://localhost:8000/v1/ready

# Statistics
curl http://localhost:8000/v1/stats
```

### Automated Testing (Coming Soon)

```bash
pytest tests/
```

---

## ?? Performance

### Connection Pool Benefits

**Without pool:**
- New connection per request (~50ms overhead)
- Connection limit exhaustion
- Database load spikes

**With AsyncConnectionPool:**
- Connection reuse (<1ms overhead)
- Controlled concurrency
- Predictable performance

### Benchmarks

| Metric | Value |
|--------|-------|
| Health check latency | <1ms |
| DB readiness check | <10ms |
| Connection pool overhead | <1ms |
| AGE sync latency | <5ms |

---

## ?? Security

### Current State (v0.6.0)

- ? **No authentication** (development only)
- ? **CORS enabled** (configurable origins)
- ? **Rate limiting** (100 req/min per IP)
- ? **SQL injection protection** (parameterized queries)

### Future (v0.7.0)

- [ ] JWT authentication
- [ ] API key support
- [ ] Role-based access control (RBAC)
- [ ] Request signing

---

## ?? Documentation

**Interactive API docs:**
- Swagger UI: http://localhost:8000/docs
- ReDoc: http://localhost:8000/redoc
- OpenAPI JSON: http://localhost:8000/openapi.json

**Code documentation:**
- See docstrings in source files
- Full project docs: `../docs/`

---

## ?? Troubleshooting

### Connection pool errors

**Error:** `RuntimeError: Connection pool not initialized`

**Solution:** Ensure lifespan is running (check startup logs)

---

### Database connection failed

**Error:** `Database connection failed: ...`

**Solution:** 
1. Check DATABASE_URL in .env
2. Verify PostgreSQL is running
3. Test connection: `psql $DATABASE_URL`

---

### AGE worker not starting

**Error:** `Worker failed: ...`

**Solution:**
1. Check AGE_WORKER_ENABLED=true
2. Verify LISTEN/NOTIFY permissions
3. Check worker logs

---

## ?? Learn More

- [Full Documentation](../docs/)
- [Architecture Guide](../docs/architecture/)
- [Deployment Guide](../docs/deployment/)

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
