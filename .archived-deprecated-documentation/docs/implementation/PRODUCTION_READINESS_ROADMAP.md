# Production Readiness Roadmap
## From "Sleeping Giant" to Enterprise System

**Status**: Implementation Plan  
**Created**: December 2, 2025  
**Priority**: Critical Path to Production

---

## Executive Summary

The Hartonomous system has a **brilliant SQL/PostGIS core** (the "brain"), a **robust C# atomizer** (the "senses"), but a **disconnected Python API** (the "nervous system"). This document outlines the specific changes needed to activate the system's intelligence.

### Current State
- ✅ Database functions exist and are well-designed
- ✅ C# TreeSitter atomizer is production-grade
- ❌ Intelligence functions never called automatically
- ❌ Memory issues in video/audio processing
- ❌ Synchronous C# coupling blocks Python
- ❌ Recursive schema incomplete (atom_relation missing content_hash)
- ❌ No multi-tenant isolation (RLS missing)

---

## Phase 1: Activate the Brain (OODA Loop) 🧠
**Goal**: Make the database self-organizing without human intervention

### Task 1.1: Implement Database Heartbeat
**File**: `schema/core/functions/scheduled/heartbeat_ooda.sql`

```sql
-- Install pg_cron extension
CREATE EXTENSION IF NOT EXISTS pg_cron;

-- Schedule OODA cycle every 1 minute
SELECT cron.schedule(
    'hartonomous-ooda-heartbeat',
    '* * * * *',  -- Every minute
    'SELECT run_ooda_cycle();'
);

-- Monitor execution
CREATE TABLE IF NOT EXISTS ooda_heartbeat_log (
    execution_id BIGSERIAL PRIMARY KEY,
    executed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    cycle_count INT,
    errors TEXT
);
```

**Verification**:
```sql
-- Check if heartbeat is running
SELECT * FROM cron.job WHERE jobname = 'hartonomous-ooda-heartbeat';

-- View recent executions
SELECT * FROM ooda_heartbeat_log ORDER BY executed_at DESC LIMIT 10;
```

---

### Task 1.2: Event-Driven Triggers
**File**: `schema/core/triggers/atom_insert_trigger.sql`

```sql
-- Trigger automatic pattern recognition after batch inserts
CREATE OR REPLACE FUNCTION trigger_pattern_analysis()
RETURNS TRIGGER AS $$
DECLARE
    pending_count BIGINT;
BEGIN
    -- Count unstable atoms (not yet crystallized)
    SELECT COUNT(*) INTO pending_count
    FROM atom
    WHERE is_stable = FALSE;
    
    -- If 1000+ unstable atoms, trigger compression
    IF pending_count >= 1000 THEN
        -- Async notification to worker
        PERFORM pg_notify(
            'pattern_analysis_needed',
            json_build_object(
                'pending_count', pending_count,
                'timestamp', now()
            )::text
        );
    END IF;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Attach to atom table
CREATE TRIGGER atom_batch_analysis
    AFTER INSERT ON atom
    FOR EACH STATEMENT
    EXECUTE FUNCTION trigger_pattern_analysis();
```

**Python Worker** (`api/workers/pattern_analyzer.py`):
```python
import asyncio
import asyncpg
from api.core.logging import get_logger

logger = get_logger(__name__)

async def pattern_analysis_worker(db_url: str):
    """Background worker listening for pattern analysis notifications"""
    conn = await asyncpg.connect(db_url)
    
    async def notification_handler(connection, pid, channel, payload):
        logger.info(f"Pattern analysis triggered: {payload}")
        # Call SQL function
        await connection.execute("SELECT compress_uniform_hilbert_region()")
    
    await conn.add_listener('pattern_analysis_needed', notification_handler)
    logger.info("Pattern analysis worker started")
    
    # Keep alive
    while True:
        await asyncio.sleep(1)
```

---

### Task 1.3: Wire Up Borsuk-Ulam Continuity Checks
**File**: `schema/core/functions/ooda/ooda_orient.sql`

**Modify OODA_ORIENT phase** to detect knowledge gaps:

```sql
-- Inside ooda_orient function, add:

-- Check for topological holes using Borsuk-Ulam
WITH continuity_check AS (
    SELECT * FROM borsuk_ulam_analysis(p_focal_atom_id, p_radius)
    WHERE discontinuity_detected = TRUE
)
INSERT INTO atom (content_hash, canonical_text, metadata, is_stable)
SELECT
    digest(concat('SystemAlert:', region_id)::text, 'sha256'),
    format('Knowledge gap detected in region %s', region_id),
    jsonb_build_object(
        'alert_type', 'continuity_violation',
        'severity', 'high',
        'region_id', region_id,
        'detected_at', now()
    ),
    TRUE
FROM continuity_check;
```

---

## Phase 2: Fortify Ingestion (Memory & Performance) 💉
**Goal**: Prevent crashes under load

### Task 2.1: Streaming Video Atomization
**File**: `api/services/video_atomization/video_atomizer.py`

**Problem**: Current code loads all frames into memory.

**Solution**: Use generator pattern with async streaming.

```python
async def atomize_video_streaming(
    self,
    conn: AsyncConnection,
    video_path: Path,
    metadata: Optional[Dict[str, Any]] = None,
    frame_skip: int = 1,
    batch_size: int = 10  # Process in batches
) -> AsyncGenerator[Dict[str, Any], None]:
    """
    Stream video atomization - process frames in batches without loading all into memory.
    
    Yields:
        Progress updates with frame_trajectory_id for each processed frame
    """
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        raise ValueError(f"Failed to open video: {video_path}")
    
    # Extract metadata
    fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    
    frame_batch = []
    frame_idx = 0
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        if frame_idx % frame_skip != 0:
            frame_idx += 1
            continue
        
        # Encode frame
        ret, frame_bytes = cv2.imencode(".png", frame)
        if not ret:
            frame_idx += 1
            continue
        
        # Add to batch
        frame_batch.append((frame_idx, frame_bytes.tobytes()))
        
        # Process batch when full
        if len(frame_batch) >= batch_size:
            # Atomize batch
            for idx, frame_data in frame_batch:
                frame_metadata = {
                    "frame_idx": idx,
                    "timestamp": idx / fps if fps > 0 else 0,
                    "video_source": str(video_path),
                    **metadata,
                }
                
                result = await self.image_atomizer.atomize_image(
                    conn=conn,
                    image_data=frame_data,
                    metadata=frame_metadata,
                    learn_patterns=False
                )
                
                # Yield progress (don't accumulate in memory)
                yield {
                    "frame_idx": idx,
                    "trajectory_id": result["trajectory_atom_id"],
                    "progress": idx / total_frames
                }
            
            # Clear batch (free memory)
            frame_batch = []
        
        frame_idx += 1
    
    cap.release()
    
    # Process remaining frames
    for idx, frame_data in frame_batch:
        frame_metadata = {
            "frame_idx": idx,
            "timestamp": idx / fps if fps > 0 else 0,
            "video_source": str(video_path),
            **metadata,
        }
        
        result = await self.image_atomizer.atomize_image(
            conn=conn,
            image_data=frame_data,
            metadata=frame_metadata,
            learn_patterns=False
        )
        
        yield {
            "frame_idx": idx,
            "trajectory_id": result["trajectory_atom_id"],
            "progress": idx / total_frames
        }
```

---

### Task 2.2: Message Queue for C# Interop
**Goal**: Decouple Python ↔ C# communication

**Architecture**:
```
Python API → Redis Queue → C# Atomizer Worker → PostgreSQL
            ↑                              ↓
            └──────────── Callback ────────┘
```

**Implementation**:

1. **Add Redis to docker-compose.yml**:
```yaml
services:
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

volumes:
  redis_data:
```

2. **Python Producer** (`api/services/code_atomization/queue_client.py`):
```python
import redis.asyncio as redis
import json
from typing import Dict, Any

class CodeAtomizationQueue:
    def __init__(self, redis_url: str = "redis://localhost:6379"):
        self.redis = redis.from_url(redis_url)
    
    async def enqueue_atomization(
        self,
        code: str,
        language: str,
        metadata: Dict[str, Any]
    ) -> str:
        """
        Enqueue code for atomization.
        Returns: job_id for tracking
        """
        job_id = str(uuid.uuid4())
        job_data = {
            "job_id": job_id,
            "code": code,
            "language": language,
            "metadata": metadata,
            "enqueued_at": datetime.utcnow().isoformat()
        }
        
        await self.redis.lpush("atomization_queue", json.dumps(job_data))
        await self.redis.setex(f"job:{job_id}:status", 3600, "pending")
        
        return job_id
    
    async def get_result(self, job_id: str) -> Optional[Dict[str, Any]]:
        """Poll for atomization result"""
        result = await self.redis.get(f"job:{job_id}:result")
        if result:
            return json.loads(result)
        return None
```

3. **C# Consumer** (pseudo-code for `src/Hartonomous.CodeAtomizer/Worker/RedisConsumer.cs`):
```csharp
public class RedisAtomizationWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICodeAtomizer _atomizer;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            // Pop job from queue
            var job = await db.ListRightPopAsync("atomization_queue");
            
            if (job.IsNull)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }
            
            var jobData = JsonSerializer.Deserialize<AtomizationJob>(job);
            
            try
            {
                // Atomize code
                var result = await _atomizer.AtomizeAsync(jobData.Code, jobData.Language);
                
                // Store result
                await db.StringSetAsync(
                    $"job:{jobData.JobId}:result",
                    JsonSerializer.Serialize(result),
                    TimeSpan.FromHours(1)
                );
                
                await db.StringSetAsync($"job:{jobData.JobId}:status", "completed");
            }
            catch (Exception ex)
            {
                await db.StringSetAsync($"job:{jobData.JobId}:status", $"failed:{ex.Message}");
            }
        }
    }
}
```

---

## Phase 3: Recursive Schema Fix (Mendeleev Requirement) 🔄
**Goal**: Enable "Patterns of Patterns"

### Task 3.1: Add content_hash to atom_relation
**File**: `schema/migrations/001_atom_relation_content_hash.sql`

```sql
-- Add content_hash column to atom_relation
ALTER TABLE atom_relation
ADD COLUMN content_hash BYTEA;

-- Populate existing relations
UPDATE atom_relation
SET content_hash = digest(
    concat(source_atom_id, ':', target_atom_id, ':', relation_type_id)::text,
    'sha256'
);

-- Make it required and unique
ALTER TABLE atom_relation
ALTER COLUMN content_hash SET NOT NULL,
ADD CONSTRAINT atom_relation_content_hash_unique UNIQUE (content_hash);

-- Create index
CREATE INDEX idx_atom_relation_content_hash ON atom_relation(content_hash);

-- Add relation_atom_id to link back to atom table
ALTER TABLE atom_relation
ADD COLUMN relation_atom_id BIGINT REFERENCES atom(atom_id);

-- Create atoms for existing relations
INSERT INTO atom (content_hash, canonical_text, metadata, is_stable)
SELECT
    ar.content_hash,
    format('%s -[%s]-> %s',
        source.canonical_text,
        rel_type.canonical_text,
        target.canonical_text
    ),
    jsonb_build_object(
        'type', 'relation',
        'source_id', ar.source_atom_id,
        'target_id', ar.target_atom_id,
        'relation_type_id', ar.relation_type_id
    ),
    TRUE
FROM atom_relation ar
JOIN atom source ON source.atom_id = ar.source_atom_id
JOIN atom target ON target.atom_id = ar.target_atom_id
JOIN atom rel_type ON rel_type.atom_id = ar.relation_type_id
ON CONFLICT (content_hash) DO NOTHING;

-- Link relations to their atom representations
UPDATE atom_relation ar
SET relation_atom_id = a.atom_id
FROM atom a
WHERE a.content_hash = ar.content_hash;

-- Make relation_atom_id required
ALTER TABLE atom_relation
ALTER COLUMN relation_atom_id SET NOT NULL;

COMMENT ON COLUMN atom_relation.content_hash IS 
'Hash of (source_id, target_id, relation_type_id) for deduplication';

COMMENT ON COLUMN atom_relation.relation_atom_id IS 
'This relation IS an atom - enables meta-relations (relations about relations)';
```

### Task 3.2: Create meta-relation support functions
**File**: `schema/core/functions/relations/create_meta_relation.sql`

```sql
CREATE OR REPLACE FUNCTION create_meta_relation(
    p_source_relation_id BIGINT,
    p_target_relation_id BIGINT,
    p_meta_relation_type_id BIGINT,
    p_weight REAL DEFAULT 0.5
)
RETURNS BIGINT
LANGUAGE plpgsql
AS $$
DECLARE
    v_source_atom_id BIGINT;
    v_target_atom_id BIGINT;
    v_new_relation_id BIGINT;
BEGIN
    -- Get atom IDs for the relations
    SELECT relation_atom_id INTO v_source_atom_id
    FROM atom_relation WHERE relation_id = p_source_relation_id;
    
    SELECT relation_atom_id INTO v_target_atom_id
    FROM atom_relation WHERE relation_id = p_target_relation_id;
    
    -- Create meta-relation (relation between relations)
    INSERT INTO atom_relation (
        source_atom_id,
        target_atom_id,
        relation_type_id,
        weight,
        content_hash
    )
    VALUES (
        v_source_atom_id,
        v_target_atom_id,
        p_meta_relation_type_id,
        p_weight,
        digest(
            concat(v_source_atom_id, ':', v_target_atom_id, ':', p_meta_relation_type_id)::text,
            'sha256'
        )
    )
    ON CONFLICT (content_hash) DO NOTHING
    RETURNING relation_id INTO v_new_relation_id;
    
    RETURN v_new_relation_id;
END;
$$;

COMMENT ON FUNCTION create_meta_relation IS
'Create a relation between two relations (meta-cognition: "I agree with this relationship")';
```

---

## Phase 4: Enterprise Hardening 🔒
**Goal**: Security and Observability

### Task 4.1: Implement Row Level Security (RLS)
**File**: `schema/security/rls_policies.sql`

```sql
-- Enable RLS on atom table
ALTER TABLE atom ENABLE ROW LEVEL SECURITY;

-- Add tenant_id column
ALTER TABLE atom
ADD COLUMN tenant_id TEXT;

-- Create policy: users can only see their own atoms
CREATE POLICY atom_tenant_isolation ON atom
    FOR ALL
    USING (tenant_id = current_setting('app.current_tenant', TRUE))
    WITH CHECK (tenant_id = current_setting('app.current_tenant', TRUE));

-- System admin can see all
CREATE POLICY atom_admin_access ON atom
    FOR ALL
    USING (current_setting('app.user_role', TRUE) = 'admin');

-- Apply same to atom_relation
ALTER TABLE atom_relation ENABLE ROW LEVEL SECURITY;
ALTER TABLE atom_relation ADD COLUMN tenant_id TEXT;

CREATE POLICY relation_tenant_isolation ON atom_relation
    FOR ALL
    USING (tenant_id = current_setting('app.current_tenant', TRUE));

CREATE POLICY relation_admin_access ON atom_relation
    FOR ALL
    USING (current_setting('app.user_role', TRUE) = 'admin');
```

**Python middleware** (`api/middleware/tenant_context.py`):
```python
from fastapi import Request
from starlette.middleware.base import BaseHTTPMiddleware

class TenantContextMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next):
        # Extract tenant from JWT or header
        tenant_id = request.headers.get("X-Tenant-ID", "default")
        
        # Set PostgreSQL session variable
        if hasattr(request.state, "db_conn"):
            await request.state.db_conn.execute(
                f"SET app.current_tenant = '{tenant_id}'"
            )
        
        response = await call_next(request)
        return response
```

---

### Task 4.2: Vectorized Audit Dashboard
**File**: `api/routes/analytics/mendeleev_audit.py`

```python
from fastapi import APIRouter, Depends
from psycopg import AsyncConnection
from api.dependencies import get_db_connection

router = APIRouter(prefix="/analytics", tags=["analytics"])

@router.get("/mendeleev-audit")
async def mendeleev_audit(
    conn: AsyncConnection = Depends(get_db_connection)
):
    """
    Run Mendeleev audit across entire dataset.
    Returns density/sparsity heatmap proving "Finite Universe" theory.
    """
    result = await conn.execute("""
        SELECT
            hilbert_index >> 10 AS region,  -- Group by 1024-cell regions
            COUNT(*) AS atom_count,
            AVG(reference_count) AS avg_references,
            PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY reference_count) AS median_references,
            SUM(CASE WHEN is_stable THEN 1 ELSE 0 END) AS stable_atoms,
            SUM(CASE WHEN is_stable THEN 0 ELSE 1 END) AS transient_atoms
        FROM atom
        WHERE spatial_key IS NOT NULL
        GROUP BY region
        ORDER BY atom_count DESC
    """)
    
    regions = await result.fetchall()
    
    return {
        "total_regions": len(regions),
        "densest_region": regions[0] if regions else None,
        "regions": [
            {
                "region_id": r[0],
                "atom_count": r[1],
                "avg_references": float(r[2]),
                "median_references": float(r[3]),
                "stable_atoms": r[4],
                "transient_atoms": r[5]
            }
            for r in regions[:100]  # Top 100 densest regions
        ]
    }
```

---

## Implementation Priority

### Week 1: Critical Path (Activate Brain)
1. ✅ Audit complete
2. Task 1.1: Database heartbeat (pg_cron)
3. Task 1.2: Event-driven triggers
4. Task 2.1: Streaming video atomization

### Week 2: Stability
5. Task 3.1: atom_relation content_hash migration
6. Task 2.2: Redis message queue
7. Task 1.3: Borsuk-Ulam integration

### Week 3: Enterprise Features
8. Task 4.1: RLS implementation
9. Task 3.2: Meta-relation functions
10. Task 4.2: Audit dashboard

---

## Success Metrics

### System Intelligence (OODA Loop Active)
- [ ] `run_ooda_cycle()` executes every minute automatically
- [ ] Pattern compression triggered after 1000 atom inserts
- [ ] Borsuk-Ulam alerts generated for knowledge gaps

### Performance (Memory Fixed)
- [ ] Video atomization: constant RAM usage (streaming)
- [ ] C# atomization: async queue (no blocking)
- [ ] 500MB video ingestion completes without OOM

### Recursive Schema (Mendeleev)
- [ ] atom_relation has content_hash
- [ ] Meta-relations work (relation about relation)
- [ ] Can query: "Show me all disagreements with relation X"

### Enterprise Ready
- [ ] RLS enabled (tenant isolation)
- [ ] Audit dashboard shows density heatmap
- [ ] Multi-tenant tested with 2+ tenants

---

## Conclusion

You have built a **revolutionary cognitive database**. The pieces are all there:

- ✅ The Biology (SQL functions)
- ✅ The Senses (C# atomizer)
- ❌ **The Nervous System is severed**

**Fix**: Connect the wires. This roadmap does exactly that.

**Timeline**: 3 weeks to full production readiness.

**Next Step**: Begin with Task 1.1 (Database Heartbeat) - this is the single most important change.
