# Python App Layer - Research Summary & Best Practices

**Author**: Anthony Hart  
**Date**: January 2025  
**Status**: Ready for Implementation

---

## ? Phase 1: Git Commits COMPLETE

**15 commits pushed to GitHub:**
1. Core schema (tables, extensions, types)
2. 18 atomized indexes
3. 3 triggers (temporal, ref counting, LISTEN/NOTIFY)
4. 6 helper functions
5. 14 atomization functions
6. 35+ spatial algorithms
7. Composition & relations (Hebbian learning)
8. Apache AGE integration (CQRS)
9. 10 in-database AI functions
10. 5 OODA functions
11. 15+ domain views
12. Performance tuning config
13. Cross-platform init scripts
14. Complete architecture docs
15. README, audit, roadmap

**Repository**: https://github.com/AHartTN/Hartonomous  
**Commit count**: 233 objects, 169KB pushed

---

## ?? Phase 2: Python Stack Research Complete

### Key Findings from MS Docs

#### 1. **PostgreSQL Connection Pooling (CRITICAL)**

**Use psycopg3 with AsyncConnectionPool:**

```python
from psycopg_pool import AsyncConnectionPool

# Create async connection pool
pool = AsyncConnectionPool(
    conninfo="postgresql://user:pass@localhost/hartonomous",
    min_size=5,
    max_size=20,
    timeout=30,
    max_idle=600  # 10 minutes
)

# Use with context manager
async with pool.connection() as conn:
    async with conn.cursor() as cur:
        await cur.execute("SELECT * FROM atom LIMIT 10")
        results = await cur.fetchall()
```

**Why AsyncConnectionPool:**
- Application-side pooling prevents connection limit exhaustion
- Drastically improves latency and throughput
- PostgreSQL must fork for each new connection (expensive)
- Reusing connections avoids fork overhead

**Key Insight**: MS Docs STRONGLY recommends connection pooling for all production apps.

---

#### 2. **FastAPI Lifespan Context Manager**

**Best Practice: Use lifespan for startup/shutdown:**

```python
from contextlib import asynccontextmanager
from fastapi import FastAPI
from psycopg_pool import AsyncConnectionPool

@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup: create connection pool
    app.state.pool = AsyncConnectionPool(
        conninfo=os.getenv("DATABASE_URL"),
        min_size=5,
        max_size=20
    )
    await app.state.pool.open()
    
    yield  # Application runs
    
    # Shutdown: close pool gracefully
    await app.state.pool.close()

app = FastAPI(lifespan=lifespan)

# Dependency injection for routes
async def get_db_conn():
    async with app.state.pool.connection() as conn:
        yield conn
```

**Why lifespan:**
- Replaces deprecated `@app.on_event("startup")` / `@app.on_event("shutdown")`
- Guaranteed cleanup even if errors occur
- Context manager ensures proper resource management

---

#### 3. **Background Workers (AGE Sync)**

**Use BackgroundService pattern:**

```python
from fastapi import BackgroundTasks
import asyncio

class AGESyncWorker:
    """Background worker for LISTEN/NOTIFY sync to AGE"""
    
    def __init__(self, pool: AsyncConnectionPool):
        self.pool = pool
        self.running = False
    
    async def start(self):
        """Start listening for atom_created notifications"""
        self.running = True
        
        async with self.pool.connection() as conn:
            await conn.execute("LISTEN atom_created;")
            
            while self.running:
                # Poll for notifications with 5s timeout
                async for notify in conn.notifies():
                    payload = json.loads(notify.payload)
                    await self.sync_to_age(payload)
    
    async def sync_to_age(self, payload):
        """Sync atom to AGE provenance graph"""
        async with self.pool.connection() as conn:
            cypher = """
            SELECT * FROM cypher('provenance', $$
                MERGE (a:Atom {atom_id: $atom_id})
                SET a.content_hash = $content_hash
                RETURN a
            $$, ...) AS (a agtype);
            """
            await conn.execute(cypher, (payload['atom_id'], ...))
    
    async def stop(self):
        """Graceful shutdown"""
        self.running = False
```

**Integration with FastAPI:**

```python
@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    app.state.pool = AsyncConnectionPool(...)
    app.state.age_worker = AGESyncWorker(app.state.pool)
    
    # Start background worker in separate task
    app.state.worker_task = asyncio.create_task(app.state.age_worker.start())
    
    yield
    
    # Shutdown
    await app.state.age_worker.stop()
    await app.state.worker_task  # Wait for graceful stop
    await app.state.pool.close()
```

---

#### 4. **Dependency Injection Pattern**

**Best Practice: Scoped dependencies per request:**

```python
from fastapi import Depends

async def get_db_conn():
    """Dependency: database connection"""
    async with app.state.pool.connection() as conn:
        yield conn

async def get_current_user(token: str = Depends(oauth2_scheme)):
    """Dependency: authentication"""
    # Validate token
    return user

@app.post("/ingest/text")
async def ingest_text(
    text: str,
    conn = Depends(get_db_conn),
    user = Depends(get_current_user)
):
    """Dependencies injected automatically"""
    async with conn.cursor() as cur:
        await cur.execute("SELECT atomize_text(%s)", (text,))
        return await cur.fetchone()
```

**Why Dependency Injection:**
- Separates concerns (auth, DB, config)
- Easy to test (mock dependencies)
- Follows FastAPI best practices

---

#### 5. **Error Handling & Retry Logic**

**Best Practice: Exponential backoff for transient errors:**

```python
import tenacity

@tenacity.retry(
    stop=tenacity.stop_after_attempt(3),
    wait=tenacity.wait_exponential(multiplier=1, min=1, max=10),
    retry=tenacity.retry_if_exception_type(psycopg.OperationalError)
)
async def execute_with_retry(conn, query, params):
    """Retry transient database errors"""
    async with conn.cursor() as cur:
        await cur.execute(query, params)
        return await cur.fetchall()
```

**Why Retry Logic:**
- Network failures are transient (resolve in seconds)
- Improves user experience (wait vs error)
- MS Docs recommends for production apps

---

#### 6. **Async/Await Best Practices**

**Key Rules:**

1. **Use async libraries:**
   - ? `psycopg3` (async PostgreSQL)
   - ? `httpx` (async HTTP client)
   - ? `aiofiles` (async file I/O)
   - ? `psycopg2` (blocking, don't use with FastAPI)

2. **Always await I/O operations:**
   ```python
   # ? GOOD: Awaits database call
   result = await conn.execute("SELECT ...")
   
   # ? BAD: Blocks event loop
   result = conn.execute("SELECT ...")  # Missing await
   ```

3. **Use async context managers:**
   ```python
   # ? GOOD
   async with pool.connection() as conn:
       ...
   
   # ? BAD: Manual cleanup
   conn = await pool.getconn()
   try:
       ...
   finally:
       await pool.putconn(conn)
   ```

---

## ??? Recommended Python Stack

### Core Dependencies

```txt
# requirements.txt

# FastAPI & ASGI server
fastapi>=0.109.0
uvicorn[standard]>=0.27.0

# PostgreSQL async driver (psycopg3)
psycopg[binary,pool]>=3.1.0

# Async utilities
aiofiles>=23.0.0
httpx>=0.26.0

# NumPy/SciPy (matches in-database versions)
numpy>=1.26.0
scipy>=1.12.0
scikit-learn>=1.4.0

# ML/AI (optional)
torch>=2.1.0
onnx>=1.15.0

# GPU acceleration (optional)
cupy-cuda12x>=12.0.0

# Monitoring & logging
prometheus-client>=0.19.0
python-json-logger>=2.0.0

# Retry logic
tenacity>=8.2.0

# Environment variables
python-dotenv>=1.0.0

# Pydantic for validation
pydantic>=2.5.0
pydantic-settings>=2.1.0
```

---

## ?? Proposed Directory Structure

```
Hartonomous/
??? schema/                    # SQL (already done) ?
??? api/                       # FastAPI application
?   ??? main.py               # App entry point with lifespan
?   ??? config.py             # Settings (Pydantic)
?   ??? dependencies.py       # DI functions (get_db_conn, etc.)
?   ??? routes/
?   ?   ??? __init__.py
?   ?   ??? ingest.py        # POST /ingest/text, /ingest/image
?   ?   ??? query.py         # GET /atoms/:id, /atoms/:id/lineage
?   ?   ??? train.py         # POST /train/batch
?   ?   ??? export.py        # POST /export/onnx
?   ??? models/              # Pydantic models
?   ?   ??? atom.py
?   ?   ??? ingest.py
?   ?   ??? training.py
?   ??? services/            # Business logic
?   ?   ??? atomization.py
?   ?   ??? inference.py
?   ?   ??? provenance.py
?   ??? workers/             # Background tasks
?       ??? age_sync.py      # AGE LISTEN/NOTIFY worker
??? docker/
?   ??? docker-compose.yml   # Multi-container setup
?   ??? api/
?   ?   ??? Dockerfile
?   ?   ??? requirements.txt
?   ??? postgres/
?       ??? Dockerfile       # PostgreSQL + AGE + PL/Python
??? tests/
?   ??? conftest.py          # pytest fixtures
?   ??? test_atomization.py
?   ??? test_inference.py
?   ??? test_provenance.py
??? scripts/
?   ??? setup/               # Init scripts (done) ?
?   ??? benchmark/
?       ??? run_benchmarks.py
??? notebooks/               # Jupyter exploration
    ??? explore_atoms.ipynb
    ??? visualize.ipynb
```

---

## ?? Implementation Checklist

### Phase 1: API Foundation
- [ ] Create `api/config.py` with Pydantic settings
- [ ] Create `api/main.py` with lifespan context manager
- [ ] Implement AsyncConnectionPool
- [ ] Create dependency injection functions
- [ ] Add health check endpoint (`/health`)

### Phase 2: Core Routes
- [ ] Implement `/ingest/text` endpoint
- [ ] Implement `/ingest/image` endpoint
- [ ] Implement `/atoms/:id` GET endpoint
- [ ] Implement `/atoms/:id/lineage` endpoint
- [ ] Add request validation (Pydantic models)

### Phase 3: AGE Sync Worker
- [ ] Create `AGESyncWorker` class
- [ ] Implement LISTEN/NOTIFY loop
- [ ] Implement `sync_to_age()` with Cypher queries
- [ ] Add graceful shutdown
- [ ] Test with actual atom creation

### Phase 4: Training & Export
- [ ] Implement `/train/batch` endpoint
- [ ] Implement `/export/onnx` endpoint
- [ ] Add batch processing utilities
- [ ] Test with real training data

### Phase 5: Docker & Deployment
- [ ] Create Dockerfile for API
- [ ] Create docker-compose.yml
- [ ] Add environment variable configuration
- [ ] Test full stack deployment
- [ ] Add health checks to compose

### Phase 6: Testing
- [ ] Write pytest fixtures for database
- [ ] Test all API endpoints
- [ ] Test AGE worker
- [ ] Integration tests
- [ ] Load testing

---

## ?? Critical Decisions

### 1. **Use psycopg3, NOT psycopg2**
- psycopg2 is blocking (bad for async)
- psycopg3 has native async support
- MS Docs confirms psycopg3 for FastAPI

### 2. **AsyncConnectionPool is Mandatory**
- NOT optional for production
- MS Docs: "strongly recommended"
- Prevents connection exhaustion

### 3. **Lifespan over on_event**
- `@app.on_event()` is deprecated
- Use `@asynccontextmanager` lifespan
- Guaranteed cleanup

### 4. **AGE Worker in Separate Process?**
**Decision needed**: Run AGE worker as:
- Option A: Same process as API (asyncio task)
- Option B: Separate container (docker service)

**Recommendation**: Separate container for:
- Independent scaling
- Isolated failures
- Easier monitoring

---

## ?? Expected Performance

### API Latency (estimated)

| Endpoint | Operation | Expected Latency |
|----------|-----------|-----------------|
| `/ingest/text` | Atomize 100 chars | 5-10ms |
| `/ingest/image` | Atomize 1000x1000 pixels | 50-100ms |
| `/atoms/:id` | Get single atom | 1-2ms |
| `/atoms/:id/lineage` | 50-hop provenance | 10-15ms |
| `/train/batch` | Train 1000 samples | 50-100ms |

**Connection Pool Impact**: 10-100x faster vs creating connections per request

---

## ?? Next Steps

**Ready to implement:**

1. ? Git commits complete
2. ? Research complete
3. ? Architecture defined
4. ? Best practices documented

**Start with:**
```bash
cd D:\Repositories\Hartonomous
mkdir api
cd api
python -m venv venv
venv\Scripts\activate  # Windows
pip install fastapi uvicorn "psycopg[binary,pool]"
```

**Create `api/main.py` with lifespan pattern.**

---

## ?? References

- [FastAPI Lifespan Events](https://fastapi.tiangolo.com/advanced/events/)
- [psycopg3 Async Documentation](https://www.psycopg.org/psycopg3/docs/advanced/async.html)
- [Azure PostgreSQL Python Quickstart](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/connect-python)
- [BackgroundService Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)

---

**Status**: Ready for implementation. All research complete. Best practices documented.

**Author**: Anthony Hart  
**Copyright**: © 2025 Anthony Hart. All Rights Reserved.
