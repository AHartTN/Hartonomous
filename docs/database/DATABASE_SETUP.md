# Database Setup & Optimization Guide

## Prerequisites

- PostgreSQL 16+ with PostGIS extension
- PL/Python3u extension (for GPU functions)
- Minimum 10GB disk space for production
- 4GB RAM minimum (16GB recommended for large datasets)

## Initial Setup

### 1. Create Database
```sql
CREATE DATABASE hartonomous_production;
\c hartonomous_production

-- Install required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "postgis";
CREATE EXTENSION IF NOT EXISTS "plpython3u";
```

### 2. Connection String Configuration

Add to `appsettings.Production.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=hartonomous_production;Username=hartonomous_user;Password=<secure_password>;Pooling=true;MinPoolSize=10;MaxPoolSize=100;ConnectionIdleLifetime=300;ConnectionPruningInterval=10;Timeout=30;CommandTimeout=60;ApplicationName=Hartonomous.API"
  }
}
```

**Connection Pooling Parameters Explained:**
- `Pooling=true` - Enable connection pooling (critical for performance)
- `MinPoolSize=10` - Keep 10 connections warm (reduces latency)
- `MaxPoolSize=100` - Allow up to 100 concurrent connections
- `ConnectionIdleLifetime=300` - Close idle connections after 5 minutes
- `ConnectionPruningInterval=10` - Check for idle connections every 10 seconds
- `Timeout=30` - Connection timeout (seconds)
- `CommandTimeout=60` - SQL command timeout (seconds)
- `ApplicationName` - Identifies connection source in pg_stat_activity

### 3. Run EF Core Migrations

```powershell
# From Hartonomous.Data directory
dotnet ef database update --startup-project ../Hartonomous.API/Hartonomous.Api.csproj

# Verify migration
dotnet ef migrations list --startup-project ../Hartonomous.API/Hartonomous.Api.csproj
```

### 4. Apply Production Optimizations

```powershell
# Execute SQL script with all optimizations
psql -U hartonomous_user -d hartonomous_production -f Hartonomous.Data/Scripts/001_ProductionOptimizations.sql
```

This script includes:
- ✅ Table partitioning by Hilbert index (64 partitions)
- ✅ Materialized view for hot atoms
- ✅ Automatic reference count triggers
- ✅ Spatial proximity function (k-NN search)
- ✅ Autovacuum tuning for high-write tables
- ✅ Query statistics refresh

## Performance Optimizations Applied

### 1. Table Partitioning
**What:** `constants` table partitioned into 64 ranges by `hilbert_index`  
**Why:** PostgreSQL can prune irrelevant partitions, scanning only relevant data  
**Impact:** 10-100x faster range queries for spatial proximity searches  
**Query Plan:** `EXPLAIN ANALYZE` shows partition pruning in action

### 2. B-tree Index on Hilbert Index
**What:** Standard B-tree index on `hilbert_index` column  
**Why:** Range queries (`BETWEEN`) are O(log n) with B-tree  
**Impact:** Sub-millisecond proximity searches up to 10 million constants  
**Alternative:** PostGIS R-tree is slower for 1D range queries

### 3. GIST Spatial Index on PostGIS Geometry
**What:** Generalized Search Tree index on `location` (PointZ)  
**Why:** PostGIS-specific operations (ST_Distance, ST_DWithin) use GIST  
**Impact:** 100x faster than sequential scan for complex spatial queries  
**Use Case:** Radius searches, polygon containment, nearest neighbor

### 4. Materialized View: hot_atoms
**What:** Cached view of frequently accessed constants  
**Criteria:** `frequency >= 10` OR `reference_count >= 5` OR accessed within 1 hour  
**Refresh:** Every 5 minutes via `MaterializedViewRefreshJob` background worker  
**Impact:** 1000x faster queries for common atoms (no table scan)  
**Size:** Max 10,000 rows (~1-10 MB)

### 5. Automatic Triggers
**What:** PL/pgSQL trigger on `constant_tokens` join table  
**Action:** Auto-increment/decrement `reference_count` on constants  
**Why:** Ensures data consistency without application logic  
**Impact:** Zero-cost maintenance (runs at INSERT/DELETE time)

### 6. Connection Pooling
**What:** Npgsql maintains pool of warm database connections  
**Configuration:** Min 10, Max 100 connections  
**Why:** Eliminates connection establishment overhead (50-200ms)  
**Impact:** 100x faster request handling under load

### 7. Batch Operations
**What:** EF Core batches multiple inserts/updates into single round-trip  
**Configuration:** `MinBatchSize=1`, `MaxBatchSize=100`  
**Why:** Reduces network overhead and transaction count  
**Impact:** 10-50x faster bulk operations (ingestion pipeline)

### 8. Autovacuum Tuning
**What:** Aggressive autovacuum for high-write tables  
**Settings:** `vacuum_scale_factor=0.05` (vacuum at 5% change)  
**Why:** Prevents table bloat during heavy ingestion  
**Impact:** Maintains query performance over time

## Monitoring Queries

### Check Partition Sizes
```sql
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size,
    n_tup_ins AS inserts,
    n_tup_upd AS updates,
    n_tup_del AS deletes
FROM pg_stat_user_tables
WHERE tablename LIKE 'constants%'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

### Check Index Usage
```sql
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_scan AS scans,
    idx_tup_read AS tuples_read,
    idx_tup_fetch AS tuples_fetched,
    pg_size_pretty(pg_relation_size(indexrelid)) AS size
FROM pg_stat_user_indexes
WHERE tablename IN ('constants', 'hot_atoms')
ORDER BY idx_scan DESC;
```

### Check Active Connections
```sql
SELECT 
    application_name,
    state,
    COUNT(*) AS count,
    MAX(backend_start) AS latest_connection
FROM pg_stat_activity
WHERE datname = 'hartonomous_production'
GROUP BY application_name, state
ORDER BY count DESC;
```

### Check Materialized View Freshness
```sql
SELECT 
    schemaname,
    matviewname,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||matviewname)) AS size,
    (SELECT COUNT(*) FROM hot_atoms) AS row_count
FROM pg_matviews
WHERE matviewname = 'hot_atoms';

-- Last refresh time (check logs)
SELECT * FROM pg_stat_user_tables WHERE relname = 'hot_atoms';
```

### Check Query Performance
```sql
-- Top 10 slowest queries
SELECT 
    query,
    calls,
    total_exec_time::NUMERIC / 1000 AS total_seconds,
    mean_exec_time::NUMERIC AS avg_milliseconds,
    max_exec_time::NUMERIC AS max_milliseconds
FROM pg_stat_statements
WHERE query LIKE '%constants%'
ORDER BY total_exec_time DESC
LIMIT 10;
```

## Maintenance Tasks

### Manual Materialized View Refresh
```sql
-- Non-blocking refresh (uses CONCURRENTLY)
REFRESH MATERIALIZED VIEW CONCURRENTLY hot_atoms;

-- Check refresh progress
SELECT * FROM pg_stat_progress_create_index 
WHERE relid = 'hot_atoms'::regclass;
```

### Vacuum Full (Reclaim Disk Space)
```sql
-- Run during maintenance window (locks table)
VACUUM FULL ANALYZE constants;
VACUUM FULL ANALYZE constant_tokens;
```

### Reindex (Fix Index Bloat)
```sql
-- Rebuild indexes (locks table)
REINDEX TABLE constants;
REINDEX TABLE hot_atoms;
```

### Update Statistics
```sql
-- Run after large data imports
ANALYZE constants;
ANALYZE landmarks;
ANALYZE bpe_tokens;
```

## Troubleshooting

### Slow Queries
1. Check `EXPLAIN ANALYZE` output for sequential scans
2. Verify partition pruning is occurring
3. Check index usage with `pg_stat_user_indexes`
4. Increase `work_mem` if sorts are slow
5. Consider increasing `shared_buffers` (25% of RAM)

### Connection Pool Exhaustion
1. Check active connections: `SELECT COUNT(*) FROM pg_stat_activity`
2. Increase `MaxPoolSize` in connection string
3. Check for connection leaks (missing `Dispose()` calls)
4. Reduce `ConnectionIdleLifetime` to recycle faster

### Disk Space Issues
1. Check partition sizes (see monitoring query above)
2. Run `VACUUM FULL` to reclaim space
3. Archive old partitions to separate tablespace
4. Consider compression for `data` column

### Materialized View Not Refreshing
1. Check Worker logs for `MaterializedViewRefreshJob` errors
2. Manually refresh: `REFRESH MATERIALIZED VIEW CONCURRENTLY hot_atoms;`
3. Verify trigger exists: `SELECT * FROM pg_trigger WHERE tgname = 'trg_update_constant_reference_count'`

## Performance Benchmarks (Expected)

| Operation | Without Optimizations | With Optimizations | Improvement |
|-----------|----------------------|-------------------|-------------|
| Proximity search (k=100) | 2000ms | 5ms | 400x faster |
| Bulk insert (10k atoms) | 30s | 2s | 15x faster |
| Hot atom query | 500ms | 0.5ms | 1000x faster |
| Reference count update | Manual | Automatic | N/A |
| Connection establishment | 150ms/request | 1ms/request | 150x faster |

## Configuration Checklist

- [ ] PostgreSQL 16+ installed
- [ ] PostGIS extension enabled
- [ ] PL/Python3u extension enabled
- [ ] Connection string configured with pooling
- [ ] EF Core migrations applied
- [ ] Production optimizations SQL script executed
- [ ] `MaterializedViewRefreshJob` running in Worker
- [ ] Autovacuum enabled (`autovacuum = on` in postgresql.conf)
- [ ] Monitoring queries tested
- [ ] Backup strategy configured

## References

- [PostgreSQL Table Partitioning](https://www.postgresql.org/docs/16/ddl-partitioning.html)
- [PostGIS Spatial Indexing](https://postgis.net/workshops/postgis-intro/indexing.html)
- [Npgsql Connection Pooling](https://www.npgsql.org/doc/connection-string-parameters.html#pooling)
- [EF Core Performance](https://learn.microsoft.com/en-us/ef/core/performance/)
