# Phase 8: Production Readiness & Hardening

**Duration**: 3-4 days  
**Dependencies**: Phases 1-5 implemented, Phase 6 testing complete  
**Critical Path**: Yes - blocks production deployment

---

## Overview

Final production hardening including performance optimization, monitoring setup, security hardening, disaster recovery planning, and deployment automation.

---

## Objectives

1. Implement hybrid indexing strategy (B-tree + GIST) with performance validation
2. Set up comprehensive monitoring and alerting (OpenTelemetry + Grafana)
3. Implement caching layer with Redis (hot atoms, vocabulary, tessellations)
4. Configure disaster recovery (backups, point-in-time recovery, failover)
5. Security hardening (connection encryption, least privilege, audit logging)
6. Load testing and capacity planning
7. Production deployment checklist

---

## Task Breakdown

### Task 8.1: Hybrid Indexing Optimization (10 hours)

#### 1. Implement Dual Index Strategy (4 hours)

**Current State**: Single GIST index on POINTZM

**Target State**: B-tree on Hilbert (X) + GIST on POINTZM

**Migration**:
```sql
-- Create B-tree index on Hilbert coordinate
CREATE INDEX CONCURRENTLY idx_constants_hilbert_btree
    ON constants USING btree ((ST_X(location)::bigint));

-- Ensure GIST index exists
CREATE INDEX CONCURRENTLY idx_constants_location_gist
    ON constants USING gist (location);

-- Analyze for query planner
ANALYZE constants;
```

**EF Core Configuration**:
```csharp
public class ConstantConfiguration : IEntityTypeConfiguration<Constant>
{
    public void Configure(EntityTypeBuilder<Constant> builder)
    {
        builder.HasIndex(c => c.Location)
            .HasMethod("gist")
            .HasDatabaseName("idx_constants_location_gist");
        
        // B-tree on Hilbert (X coordinate)
        builder.HasIndex(c => EF.Property<long>(c, "ST_X(location)"))
            .HasMethod("btree")
            .HasDatabaseName("idx_constants_hilbert_btree");
    }
}
```

#### 2. Query Pattern Optimization (3 hours)

**Pattern 1: k-NN Search (Use GIST)**
```csharp
// GOOD: Uses GIST index
public IQueryable<Constant> KNearestNeighbors(Point target, int k)
{
    return _dbContext.Constants
        .OrderBy(c => c.Location.Distance(target))
        .Take(k);
}

// Query plan: Index Scan using idx_constants_location_gist
```

**Pattern 2: Hilbert Range Query (Use B-tree)**
```csharp
// GOOD: Uses B-tree index
public IQueryable<Constant> HilbertRange(ulong start, ulong end)
{
    return _dbContext.Constants
        .Where(c => EF.Functions.AsText(c.Location)
            .StartsWith($"POINT({start}"))  // Simplified
        .OrderBy(c => c.Coordinate.HilbertIndex);
}

// Query plan: Index Scan using idx_constants_hilbert_btree
```

**Pattern 3: Hybrid Query (Both indexes)**
```csharp
// GOOD: Planner chooses optimal index
public async Task<List<Constant>> SimilarInRegion(Point target, ulong hilbertStart, ulong hilbertEnd, int limit)
{
    return await _dbContext.Constants
        .Where(c => c.Coordinate.HilbertIndex >= hilbertStart 
                 && c.Coordinate.HilbertIndex <= hilbertEnd)
        .OrderBy(c => c.Location.Distance(target))
        .Take(limit)
        .ToListAsync();
}

// Query plan: Bitmap Heap Scan (combines both indexes)
```

#### 3. Index Performance Validation (3 hours)

**Benchmark Suite**:
```csharp
[MemoryDiagnoser]
public class IndexPerformanceBenchmarks
{
    private const int DatabaseSize = 100_000;
    
    [Benchmark]
    public async Task<List<Constant>> KNN_10_GIST()
    {
        var target = CreateRandomPoint();
        return await _repository
            .KNearestNeighbors(target, k: 10)
            .ToListAsync();
    }
    
    [Benchmark]
    public async Task<List<Constant>> HilbertRange_1000_BTree()
    {
        var start = RandomULong();
        var end = start + 1000;
        return await _repository
            .HilbertRange(start, end)
            .ToListAsync();
    }
    
    [Benchmark]
    public async Task<List<Constant>> HybridQuery_BothIndexes()
    {
        var target = CreateRandomPoint();
        var start = RandomULong();
        var end = start + 10000;
        return await _repository
            .SimilarInRegion(target, start, end, limit: 50)
            .ToListAsync();
    }
}
```

**Acceptance Targets** (100K database):
| Query | Target Latency | P95 Latency | Index Used |
|-------|----------------|-------------|------------|
| k-NN (k=10) | <100ms | <150ms | GIST |
| Hilbert Range (1K) | <50ms | <75ms | B-tree |
| Hybrid (10K range, 50 results) | <200ms | <300ms | Both |

**Validation**:
```sql
-- Verify index usage
EXPLAIN ANALYZE
SELECT * FROM constants
ORDER BY location <-> 'POINTZM(100 100 100 100)'::geometry
LIMIT 10;

-- Expected: Index Scan using idx_constants_location_gist
```

---

### Task 8.2: Monitoring & Observability (8 hours)

#### 1. OpenTelemetry Integration (3 hours)

**Already configured in ServiceDefaults**, extend with custom metrics:

```csharp
public class HartonomousMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _atomizationCounter;
    private readonly Histogram<double> _queryLatencyHistogram;
    private readonly ObservableGauge<int> _constantCountGauge;
    
    public HartonomousMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("Hartonomous");
        
        _atomizationCounter = _meter.CreateCounter<long>(
            "hartonomous.atomization.count",
            description: "Total number of atomization operations");
        
        _queryLatencyHistogram = _meter.CreateHistogram<double>(
            "hartonomous.query.latency",
            unit: "ms",
            description: "Query latency distribution");
        
        _constantCountGauge = _meter.CreateObservableGauge<int>(
            "hartonomous.constants.total",
            observeValue: () => GetConstantCount(),
            description: "Total constants in database");
    }
    
    public void RecordAtomization(ContentType type, int byteCount)
    {
        _atomizationCounter.Add(1, new KeyValuePair<string, object?>("content_type", type.ToString()));
    }
    
    public void RecordQueryLatency(string queryType, double latencyMs)
    {
        _queryLatencyHistogram.Record(latencyMs, new KeyValuePair<string, object?>("query_type", queryType));
    }
    
    private int GetConstantCount()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return dbContext.Constants.Count();
    }
}
```

**Register in Program.cs**:
```csharp
builder.Services.AddSingleton<HartonomousMetrics>();
```

#### 2. Grafana Dashboard Configuration (3 hours)

**Dashboard JSON** (simplified):
```json
{
  "dashboard": {
    "title": "Hartonomous Production Monitoring",
    "panels": [
      {
        "title": "Query Latency (P95)",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, rate(hartonomous_query_latency_bucket[5m]))",
            "legendFormat": "{{query_type}}"
          }
        ],
        "alert": {
          "conditions": [
            {
              "evaluator": { "type": "gt", "params": [500] },
              "query": { "model": "A" }
            }
          ]
        }
      },
      {
        "title": "Atomization Throughput",
        "targets": [
          {
            "expr": "rate(hartonomous_atomization_count[1m])",
            "legendFormat": "{{content_type}}"
          }
        ]
      },
      {
        "title": "Database Size",
        "targets": [
          {
            "expr": "hartonomous_constants_total"
          }
        ]
      },
      {
        "title": "Index Bloat",
        "targets": [
          {
            "expr": "pg_relation_size('idx_constants_location_gist') / 1024 / 1024",
            "legendFormat": "GIST Index (MB)"
          }
        ]
      }
    ]
  }
}
```

#### 3. Alert Rules (2 hours)

**Prometheus Alert Rules** (`alerts.yml`):
```yaml
groups:
  - name: hartonomous
    interval: 30s
    rules:
      - alert: HighQueryLatency
        expr: histogram_quantile(0.95, rate(hartonomous_query_latency_bucket[5m])) > 500
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High query latency detected"
          description: "P95 latency is {{ $value }}ms (threshold: 500ms)"
      
      - alert: LowAtomizationThroughput
        expr: rate(hartonomous_atomization_count[5m]) < 1
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Low atomization throughput"
          description: "Atomization rate is {{ $value }} ops/sec"
      
      - alert: DatabaseConnectionPoolExhausted
        expr: npgsql_connection_pool_active >= npgsql_connection_pool_max * 0.9
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "Database connection pool near limit"
          description: "{{ $value }} active connections (90% of pool)"
      
      - alert: IndexBloat
        expr: pg_relation_size('idx_constants_location_gist') / pg_relation_size('constants') > 0.5
        for: 1h
        labels:
          severity: info
        annotations:
          summary: "Index bloat detected"
          description: "GIST index is {{ $value | humanizePercentage }} of table size"
```

---

### Task 8.3: Caching Layer (6 hours)

#### 1. Redis Configuration (1 hour)

**appsettings.Production.json**:
```json
{
  "Redis": {
    "Configuration": "prod-redis.hartonomous.local:6379,password=***,ssl=true,abortConnect=false",
    "InstanceName": "Hartonomous:",
    "DefaultExpiration": "00:15:00"
  }
}
```

**DI Registration**:
```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Configuration"];
    options.InstanceName = builder.Configuration["Redis:InstanceName"];
});

builder.Services.AddSingleton<ICacheService, RedisCacheService>();
```

#### 2. Cache Strategies (3 hours)

**Hot Atom Cache**:
```csharp
public class ConstantRepository : IConstantRepository
{
    private readonly ICacheService _cache;
    private static readonly TimeSpan HotAtomCacheDuration = TimeSpan.FromMinutes(15);
    
    public async Task<Constant?> GetByIdAsync(Guid id)
    {
        var cacheKey = $"constant:{id}";
        
        // Try cache first
        var cached = await _cache.GetAsync<Constant>(cacheKey);
        if (cached != null) return cached;
        
        // Fallback to database
        var constant = await _dbContext.Constants.FindAsync(id);
        
        if (constant != null)
        {
            await _cache.SetAsync(cacheKey, constant, HotAtomCacheDuration);
        }
        
        return constant;
    }
}
```

**Vocabulary Cache**:
```csharp
public class BPEService : IBPEService
{
    private readonly ICacheService _cache;
    private static readonly TimeSpan VocabularyCacheDuration = TimeSpan.FromHours(1);
    
    public async Task<List<BPEToken>> GetVocabularyAsync(ContentType type)
    {
        var cacheKey = $"vocabulary:{type}";
        
        var cached = await _cache.GetAsync<List<BPEToken>>(cacheKey);
        if (cached != null) return cached;
        
        var vocabulary = await _dbContext.BPETokens
            .Where(t => t.ContentType == type)
            .ToListAsync();
        
        await _cache.SetAsync(cacheKey, vocabulary, VocabularyCacheDuration);
        
        return vocabulary;
    }
}
```

**Tessellation Cache**:
```csharp
public class VoronoiTessellationService
{
    private readonly ICacheService _cache;
    private static readonly TimeSpan TessellationCacheDuration = TimeSpan.FromHours(4);
    
    public async Task<Geometry> GetVoronoiAsync()
    {
        var cacheKey = "voronoi:current";
        
        var cached = await _cache.GetAsync<Geometry>(cacheKey);
        if (cached != null) return cached;
        
        var tessellation = await ComputeVoronoiAsync();
        
        await _cache.SetAsync(cacheKey, tessellation, TessellationCacheDuration);
        
        return tessellation;
    }
}
```

#### 3. Cache Invalidation (2 hours)

**Invalidation Strategy**:
```csharp
public class CacheInvalidationService
{
    public async Task InvalidateConstantAsync(Guid id)
    {
        await _cache.RemoveAsync($"constant:{id}");
        await _cache.RemoveAsync($"knn:{id}");  // Invalidate k-NN results
    }
    
    public async Task InvalidateVocabularyAsync(ContentType type)
    {
        await _cache.RemoveAsync($"vocabulary:{type}");
    }
    
    public async Task InvalidateTessellationAsync()
    {
        await _cache.RemoveAsync("voronoi:current");
        await _cache.RemoveAsync("delaunay:current");
    }
}
```

**Hook into UnitOfWork**:
```csharp
public class UnitOfWork : IUnitOfWork
{
    private readonly CacheInvalidationService _cacheInvalidation;
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changes = await _dbContext.SaveChangesAsync(cancellationToken);
        
        // Invalidate affected caches
        var modifiedConstants = _dbContext.ChangeTracker.Entries<Constant>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .Select(e => e.Entity.Id);
        
        foreach (var id in modifiedConstants)
        {
            await _cacheInvalidation.InvalidateConstantAsync(id);
        }
        
        return changes;
    }
}
```

---

### Task 8.4: Disaster Recovery (8 hours)

#### 1. Backup Strategy (3 hours)

**Daily Full Backup** (cron: 2 AM):
```bash
#!/bin/bash
# /opt/hartonomous/scripts/backup-daily.sh

DATE=$(date +%Y%m%d)
BACKUP_DIR="/mnt/backups/hartonomous"
RETENTION_DAYS=30

# Full backup
pg_dump -h localhost -U postgres -d hartonomous \
    -F c -b -v -f "$BACKUP_DIR/full_$DATE.dump"

# Compress
gzip "$BACKUP_DIR/full_$DATE.dump"

# Cleanup old backups
find "$BACKUP_DIR" -name "full_*.dump.gz" -mtime +$RETENTION_DAYS -delete

# Upload to Azure Blob Storage
az storage blob upload \
    --account-name hartonomousbackups \
    --container-name daily \
    --name "full_$DATE.dump.gz" \
    --file "$BACKUP_DIR/full_$DATE.dump.gz"
```

**Hourly Incremental Backup** (WAL archiving):
```ini
# postgresql.conf
wal_level = replica
archive_mode = on
archive_command = 'test ! -f /mnt/wal_archive/%f && cp %p /mnt/wal_archive/%f'
archive_timeout = 3600  # Force WAL switch every hour
```

#### 2. Point-in-Time Recovery Setup (3 hours)

**Configuration**:
```ini
# postgresql.conf
wal_level = replica
archive_mode = on
max_wal_senders = 10
wal_keep_size = 1GB
```

**Recovery Procedure**:
```bash
#!/bin/bash
# /opt/hartonomous/scripts/pitr-restore.sh

TARGET_TIME="2025-01-15 14:30:00"
BACKUP_FILE="/mnt/backups/hartonomous/full_20250115.dump.gz"

# Stop PostgreSQL
systemctl stop postgresql

# Restore base backup
gunzip -c "$BACKUP_FILE" | pg_restore -h localhost -U postgres -d hartonomous_restored

# Create recovery.signal
touch /var/lib/postgresql/14/main/recovery.signal

# Configure recovery target
cat >> /var/lib/postgresql/14/main/postgresql.auto.conf <<EOF
restore_command = 'cp /mnt/wal_archive/%f %p'
recovery_target_time = '$TARGET_TIME'
recovery_target_action = 'promote'
EOF

# Start PostgreSQL (enters recovery mode)
systemctl start postgresql

# Wait for recovery to complete
until pg_isready; do sleep 1; done

echo "Recovery complete. Database restored to $TARGET_TIME"
```

#### 3. Failover & Replication (2 hours)

**Streaming Replication Setup**:
```ini
# Primary: postgresql.conf
wal_level = replica
max_wal_senders = 5
wal_keep_size = 1GB

# Standby: postgresql.conf
hot_standby = on
```

**Standby Configuration**:
```conf
# standby.signal
primary_conninfo = 'host=primary-db.hartonomous.local port=5432 user=replicator password=***'
```

**Failover Script**:
```bash
#!/bin/bash
# /opt/hartonomous/scripts/failover-to-standby.sh

# Promote standby to primary
pg_ctl promote -D /var/lib/postgresql/14/main

# Update application connection string
# (Update appsettings.json or environment variable)

# Restart API
systemctl restart hartonomous-api
```

---

### Task 8.5: Security Hardening (6 hours)

#### 1. Connection Encryption (2 hours)

**Force SSL/TLS**:
```ini
# postgresql.conf
ssl = on
ssl_cert_file = '/etc/ssl/certs/postgresql-server.crt'
ssl_key_file = '/etc/ssl/private/postgresql-server.key'
ssl_ca_file = '/etc/ssl/certs/ca.crt'
ssl_ciphers = 'HIGH:MEDIUM:+3DES:!aNULL'
ssl_prefer_server_ciphers = on
```

**Connection String**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=db.hartonomous.local;Database=hartonomous;Username=app;Password=***;SSL Mode=Require;Trust Server Certificate=false"
  }
}
```

#### 2. Least Privilege Access (2 hours)

**Create Application Role**:
```sql
-- Create read-write role
CREATE ROLE hartonomous_app LOGIN PASSWORD '***';

-- Grant minimal permissions
GRANT CONNECT ON DATABASE hartonomous TO hartonomous_app;
GRANT USAGE ON SCHEMA public TO hartonomous_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hartonomous_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hartonomous_app;

-- Deny dangerous operations
REVOKE CREATE ON SCHEMA public FROM hartonomous_app;
REVOKE DROP ON ALL TABLES IN SCHEMA public FROM hartonomous_app;

-- Create read-only role (for analytics)
CREATE ROLE hartonomous_readonly LOGIN PASSWORD '***';
GRANT CONNECT ON DATABASE hartonomous TO hartonomous_readonly;
GRANT USAGE ON SCHEMA public TO hartonomous_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO hartonomous_readonly;
```

#### 3. Audit Logging (2 hours)

**Enable pgAudit**:
```sql
CREATE EXTENSION pgaudit;

-- Audit all DDL and privilege changes
ALTER SYSTEM SET pgaudit.log = 'ddl, role';
ALTER SYSTEM SET pgaudit.log_level = 'notice';
ALTER SYSTEM SET pgaudit.log_catalog = 'off';

SELECT pg_reload_conf();
```

**Application-Level Audit Trail**:
```csharp
public class AuditEntry
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string UserId { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

public class AuditableDbContext : ApplicationDbContext
{
    public DbSet<AuditEntry> AuditLog { get; set; } = null!;
    
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditEntries = new List<AuditEntry>();
        
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                auditEntries.Add(new AuditEntry
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    UserId = _currentUserService.UserId ?? "system",
                    Action = entry.State.ToString(),
                    EntityType = entry.Entity.GetType().Name,
                    EntityId = (Guid)entry.Property("Id").CurrentValue!,
                    OldValue = JsonSerializer.Serialize(entry.OriginalValues.ToObject()),
                    NewValue = JsonSerializer.Serialize(entry.CurrentValues.ToObject())
                });
            }
        }
        
        AuditLog.AddRange(auditEntries);
        
        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

---

### Task 8.6: Production Deployment Checklist (2 hours)

```markdown
# Production Deployment Checklist

## Pre-Deployment

- [ ] All Phase 1-5 features implemented
- [ ] All Phase 6 tests passing (>80% coverage)
- [ ] Performance benchmarks validated
- [ ] Load testing completed
- [ ] Security audit passed
- [ ] Documentation complete

## Database

- [ ] Full backup created
- [ ] Hybrid indexes created (B-tree + GIST)
- [ ] VACUUM ANALYZE run
- [ ] Connection pooling configured
- [ ] SSL/TLS enabled
- [ ] Least privilege roles configured
- [ ] Audit logging enabled
- [ ] Replication/standby verified

## Application

- [ ] Configuration secrets externalized (Azure Key Vault)
- [ ] Redis cache configured
- [ ] OpenTelemetry enabled
- [ ] Health checks operational
- [ ] Rate limiting configured
- [ ] CORS policies set
- [ ] Authentication validated (Entra ID)

## Infrastructure

- [ ] Nginx configured (reverse proxy)
- [ ] systemd services created
- [ ] Log rotation configured
- [ ] Firewall rules applied
- [ ] Monitoring dashboard deployed (Grafana)
- [ ] Alert rules configured (Prometheus)
- [ ] Backup automation verified

## Post-Deployment

- [ ] Health checks green
- [ ] Metrics flowing to Grafana
- [ ] Test atomization operation
- [ ] Verify k-NN query performance
- [ ] Check cache hit rates
- [ ] Validate alerts trigger correctly
- [ ] Document deployed version
- [ ] Update runbook with any changes

## Rollback Plan

- [ ] Previous version binaries retained
- [ ] Database backup validated
- [ ] Rollback procedure documented
- [ ] Rollback tested in staging

---

**Sign-off**: 
- [ ] Tech Lead: ________________ Date: ______
- [ ] DevOps: __________________ Date: ______
- [ ] Security: ________________ Date: ______
```

---

## Acceptance Criteria (Phase Exit)

- ✅ Hybrid indexing (B-tree + GIST) validated with benchmarks
- ✅ Monitoring dashboard operational with >10 panels
- ✅ Alert rules configured and tested
- ✅ Redis caching operational (>50% cache hit rate)
- ✅ Disaster recovery procedures tested (backup/restore verified)
- ✅ Security hardening complete (SSL, least privilege, audit logging)
- ✅ Production deployment checklist complete
- ✅ Load testing demonstrates stability (P95 <500ms, 0% errors)

---

## Performance Targets (Production)

| Metric | Target | Monitoring |
|--------|--------|------------|
| k-NN Query (k=10, 100K db) | P95 <100ms | Grafana dashboard |
| Hilbert Range (1K results) | P95 <50ms | Grafana dashboard |
| Atomization throughput | >50 ops/sec | Custom metric |
| Cache hit rate | >50% | Redis INFO |
| Database connections | <80% pool size | OpenTelemetry |
| Index bloat | <30% of table size | Monthly check |
| Backup success rate | 100% | Alert on failure |
| WAL archive lag | <5 minutes | pg_stat_archiver |

---

**Next Phase**: Production deployment

**Status**: 📋 Ready for implementation after Phase 5

**Last Updated**: December 4, 2025
