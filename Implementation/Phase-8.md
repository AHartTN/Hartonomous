---
title: "Phase 8: Production Hardening & Deployment"
author: "Hartonomous Development Team"
date: "2025-12-05"
version: "1.0"
status: "Planning"
---

# Phase 8: Production Hardening & Deployment

## Table of Contents
- [Overview](#overview)
- [Phase Details](#phase-details)
- [Objectives](#objectives)
- [Implementation Tasks](#implementation-tasks)
  - [8.1: Performance Optimization](#81-performance-optimization)
  - [8.2: Security Hardening](#82-security-hardening)
  - [8.3: Monitoring & Observability](#83-monitoring--observability)
  - [8.4: Production Deployment](#84-production-deployment)
- [Success Criteria](#success-criteria)
- [Quality Gates](#quality-gates)
- [Risks & Mitigation](#risks--mitigation)
- [Dependencies](#dependencies)
- [Next Steps](#next-steps)

---

## Overview

**Phase 8** is the final phase preparing Hartonomous for production deployment. This phase focuses on performance optimization, security hardening, comprehensive monitoring, and production infrastructure setup.

**Duration**: 5-7 days  
**Complexity**: Very High  
**Dependencies**: All previous phases  
**Prerequisites**: All features tested and documented

---

## Phase Details

### Timeline
- **Start**: After Phase 7 completion
- **Duration**: 5-7 days
- **Parallelizable**: Security, monitoring, and infrastructure tasks can overlap
- **Critical Path**: Yes - Gates production release

### Resource Requirements
- **Development**: 2-3 senior engineers
- **DevOps**: 1-2 engineers for infrastructure
- **Security**: Security review and penetration testing
- **Performance**: Load testing infrastructure

---

## Objectives

1. **Optimize critical paths** - Ingestion <50ms, k-NN <50ms at scale
2. **Security hardening** - Pass security audit, penetration testing
3. **Production monitoring** - Full observability with OpenTelemetry
4. **Zero-downtime deployment** - Blue-green deployment via Azure Arc
5. **Disaster recovery** - Backup/restore procedures validated

---

## Implementation Tasks

### 8.1: Performance Optimization

**Goal**: Achieve production-grade performance at scale (1M+ atoms, 1000+ req/s).

<details>
<summary><strong>8.1.1: Database Query Optimization</strong> (1 day)</summary>

**Index Optimization:**

```sql
-- Composite index for frequent queries
CREATE INDEX CONCURRENTLY idx_constants_hash_frequency
    ON constants (hash, frequency DESC)
    WHERE NOT is_deleted;

-- Partial index for hot atoms
CREATE INDEX CONCURRENTLY idx_constants_hot_atoms
    ON constants (location)
    WHERE frequency > 1000 AND NOT is_deleted
    USING gist;

-- Cover index for deduplication queries
CREATE INDEX CONCURRENTLY idx_constants_hash_ref_count
    ON constants (hash, reference_count, frequency)
    WHERE NOT is_deleted;
```

**Connection Pooling:**

```csharp
// Update appsettings.Production.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Database=hartonomous;Username=app;Password=***;Pooling=true;Minimum Pool Size=10;Maximum Pool Size=100;Connection Idle Lifetime=300;Connection Pruning Interval=10"
  }
}
```

**Query Batching:**

```csharp
// Batch hash lookups
public async Task<List<Constant>> BatchLookupByHash(
    List<Hash256> hashes,
    CancellationToken cancellationToken)
{
    // Use single query with IN clause instead of N queries
    return await _dbContext.Constants
        .Where(c => hashes.Contains(c.Hash))
        .AsNoTracking() // Read-only optimization
        .ToListAsync(cancellationToken);
}

// Batch counter updates
public async Task BatchIncrementCounters(
    List<Guid> constantIds,
    CancellationToken cancellationToken)
{
    await _dbContext.Database.ExecuteSqlRawAsync(@"
        UPDATE constants
        SET reference_count = reference_count + 1,
            frequency = frequency + 1,
            updated_at = NOW()
        WHERE id = ANY(@ids)
    ", new NpgsqlParameter("ids", constantIds.ToArray()));
}
```

**Caching Strategy:**

```csharp
// Redis cache for hot atoms
public sealed class CachedConstantRepository : IConstantRepository
{
    private readonly IConstantRepository _inner;
    private readonly ICacheService _cache;
    private readonly TimeSpan _hotAtomTtl = TimeSpan.FromHours(1);
    
    public async Task<Constant?> GetByHashAsync(
        Hash256 hash,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"constant:hash:{hash}";
        
        // Try cache first
        var cached = await _cache.GetAsync<Constant>(cacheKey);
        if (cached != null)
            return cached;
        
        // Miss - query database
        var constant = await _inner.GetByHashAsync(hash, cancellationToken);
        
        // Cache hot atoms (frequency > 100)
        if (constant?.Frequency > 100)
        {
            await _cache.SetAsync(
                cacheKey, 
                constant, 
                _hotAtomTtl);
        }
        
        return constant;
    }
}
```

**PostgreSQL Configuration:**

```ini
# postgresql.conf optimizations for OLTP workload
shared_buffers = 4GB                  # 25% of RAM
effective_cache_size = 12GB           # 75% of RAM
maintenance_work_mem = 1GB
work_mem = 50MB
max_connections = 200
random_page_cost = 1.1                # SSD
effective_io_concurrency = 200        # SSD

# Write-ahead log
wal_buffers = 16MB
min_wal_size = 2GB
max_wal_size = 8GB
checkpoint_completion_target = 0.9

# Planner costs
cpu_tuple_cost = 0.01
cpu_index_tuple_cost = 0.005
cpu_operator_cost = 0.0025

# Autovacuum
autovacuum_max_workers = 4
autovacuum_naptime = 10s
```

</details>

<details>
<summary><strong>8.1.2: Application Performance Tuning</strong> (1 day)</summary>

**Response Compression:**

```csharp
// Enable response compression in Program.cs
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "application/geo+json" });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
```

**Async Streaming:**

```csharp
// Stream large results instead of buffering
[HttpGet("atoms/stream")]
public async IAsyncEnumerable<AtomDto> StreamAtoms(
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var atom in _repository.StreamAllAsync(cancellationToken))
    {
        yield return atom.ToDto();
    }
}
```

**Memory Pooling:**

```csharp
// Use ArrayPool for temporary buffers
public async Task<byte[]> CompressDataAsync(byte[] data)
{
    var buffer = ArrayPool<byte>.Shared.Rent(data.Length * 2);
    try
    {
        using var memoryStream = new MemoryStream(buffer);
        using var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest);
        await gzipStream.WriteAsync(data);
        await gzipStream.FlushAsync();
        
        return memoryStream.ToArray();
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

**Parallel Processing:**

```csharp
// Process ingestion in parallel
public async Task<List<Constant>> BatchIngestAsync(
    List<byte[]> contents,
    CancellationToken cancellationToken)
{
    var tasks = contents
        .AsParallel()
        .WithDegreeOfParallelism(Environment.ProcessorCount)
        .Select(async content => await IngestAsync(content, cancellationToken));
        
    return (await Task.WhenAll(tasks)).ToList();
}
```

</details>

<details>
<summary><strong>8.1.3: Load Testing & Benchmarking</strong> (1 day)</summary>

**k6 Load Test Script:**

```javascript
// load-test.js
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

const errorRate = new Rate('errors');

export const options = {
    stages: [
        { duration: '2m', target: 100 },   // Ramp up to 100 users
        { duration: '5m', target: 100 },   // Stay at 100 users
        { duration: '2m', target: 500 },   // Ramp up to 500 users
        { duration: '5m', target: 500 },   // Stay at 500 users
        { duration: '2m', target: 1000 },  // Ramp up to 1000 users
        { duration: '5m', target: 1000 },  // Stay at 1000 users
        { duration: '2m', target: 0 },     // Ramp down
    ],
    thresholds: {
        http_req_duration: ['p(95)<100'],  // 95% requests < 100ms
        http_req_failed: ['rate<0.01'],    // Error rate < 1%
    },
};

export default function() {
    const token = getAuthToken();
    
    // Test ingestion
    const content = btoa(`Test content ${__VU}-${__ITER}`);
    const payload = JSON.stringify({
        content: content,
        metadata: { source: 'load-test' }
    });
    
    const params = {
        headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
        },
    };
    
    const response = http.post(
        'https://api.hartonomous.com/api/content/ingest',
        payload,
        params
    );
    
    check(response, {
        'status is 200': (r) => r.status === 200,
        'duration < 100ms': (r) => r.timings.duration < 100,
    }) || errorRate.add(1);
    
    sleep(1);
}
```

**Run Load Tests:**

```bash
# Install k6
choco install k6

# Run load test
k6 run load-test.js

# Output metrics to InfluxDB for visualization
k6 run --out influxdb=http://localhost:8086/k6 load-test.js
```

**Benchmark Results Documentation:**

```markdown
# Performance Benchmarks

## Ingestion Performance
- **Cold start** (first 1K documents): 50-100ms per document
- **Warm state** (after 10K documents): 5-10ms per document (95%+ deduplication)
- **Hot state** (after 1M documents): 2-5ms per document (99%+ deduplication)

## Spatial Query Performance
- **k-NN (10 nearest)**: 
  - 1K atoms: <5ms
  - 100K atoms: <20ms
  - 1M atoms: <50ms
  - 10M atoms: <100ms
- **Containment queries**: <50ms for typical boundaries
- **Hilbert range queries**: <10ms using B-tree index

## Throughput
- **Sustained load**: 1000 req/s with p95 latency <100ms
- **Peak load**: 2000 req/s with p95 latency <200ms
- **Concurrent users**: 1000+ simultaneous connections

## Resource Usage
- **Memory**: 2-4GB under normal load
- **CPU**: 30-50% utilization at 1000 req/s
- **Storage**: 99%+ deduplication reduces storage by 100:1
```

</details>

---

### 8.2: Security Hardening

**Goal**: Pass security audit and penetration testing.

<details>
<summary><strong>8.2.1: Security Configuration Audit</strong> (1 day)</summary>

**Enforce HTTPS:**

```csharp
// Program.cs
if (!builder.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    
    // Require HTTPS
    builder.Services.AddHttpsRedirection(options =>
    {
        options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
        options.HttpsPort = 443;
    });
}
```

**Content Security Policy:**

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("Content-Security-Policy", 
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'");
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
    
    await next();
});
```

**SQL Injection Prevention:**

```csharp
// ALWAYS use parameterized queries
public async Task<List<Constant>> SafeQuery(string userInput)
{
    // ✅ CORRECT - Parameterized
    return await _dbContext.Constants
        .FromSqlRaw(@"
            SELECT * FROM constants 
            WHERE data LIKE '%' || @search || '%'
        ", new NpgsqlParameter("search", userInput))
        .ToListAsync();
    
    // ❌ WRONG - Vulnerable to SQL injection
    // return await _dbContext.Constants
    //     .FromSqlRaw($"SELECT * FROM constants WHERE data LIKE '%{userInput}%'")
    //     .ToListAsync();
}
```

**Secrets Management:**

```json
// appsettings.Production.json - NO SECRETS
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=${POSTGRES_HOST};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
  },
  "AzureAd": {
    "ClientSecret": "${ENTRA_CLIENT_SECRET}"
  }
}
```

```bash
# Secrets in Azure Key Vault
az keyvault secret set \
  --vault-name hartonomous-kv \
  --name postgres-password \
  --value "$(openssl rand -base64 32)"
  
az keyvault secret set \
  --vault-name hartonomous-kv \
  --name entra-client-secret \
  --value "$CLIENT_SECRET"
```

**Rate Limiting Enhancement:**

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var username = context.User.Identity?.Name ?? "anonymous";
        
        return RateLimitPartition.GetTokenBucketLimiter(username, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 100,
            AutoReplenishment = true
        });
    });
    
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) 
                ? retryAfter.TotalSeconds 
                : 60
        }, cancellationToken);
    };
});
```

</details>

<details>
<summary><strong>8.2.2: Penetration Testing</strong> (1 day)</summary>

**Security Testing Checklist:**

- [ ] **Authentication bypass attempts** - Verify all endpoints require valid JWT
- [ ] **SQL injection testing** - Test all user inputs with malicious payloads
- [ ] **XSS attacks** - Attempt script injection in all inputs
- [ ] **CSRF protection** - Verify anti-forgery tokens on state-changing operations
- [ ] **Path traversal** - Test file operations with `../` sequences
- [ ] **Denial of service** - Test rate limiting and resource exhaustion
- [ ] **Insecure deserialization** - Test JSON payloads with malicious types
- [ ] **Insufficient logging** - Verify security events are logged

**OWASP ZAP Automated Scan:**

```bash
# Run OWASP ZAP scan
docker run -v $(pwd):/zap/wrk/:rw \
  -t owasp/zap2docker-stable \
  zap-baseline.py \
  -t https://api.hartonomous.com \
  -r security-report.html
```

</details>

---

### 8.3: Monitoring & Observability

**Goal**: Full observability with distributed tracing, metrics, and logging.

<details>
<summary><strong>8.3.1: OpenTelemetry Integration</strong> (1 day)</summary>

**Configure OpenTelemetry:**

```csharp
// Program.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
                options.SetDbStatementForStoredProcedure = true;
            })
            .AddSource("Hartonomous.*");
            
        if (builder.Environment.IsProduction())
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(builder.Configuration["Telemetry:Endpoint"]!);
            });
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Hartonomous.*");
            
        if (builder.Environment.IsProduction())
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(builder.Configuration["Telemetry:Endpoint"]!);
            });
        }
    });
```

**Custom Metrics:**

```csharp
public sealed class IngestionMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _ingestionCounter;
    private readonly Histogram<double> _ingestionDuration;
    private readonly Counter<long> _deduplicationCounter;
    
    public IngestionMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("Hartonomous.Ingestion");
        
        _ingestionCounter = _meter.CreateCounter<long>(
            "hartonomous.ingestion.count",
            description: "Total number of content ingestion requests");
            
        _ingestionDuration = _meter.CreateHistogram<double>(
            "hartonomous.ingestion.duration",
            unit: "ms",
            description: "Duration of content ingestion");
            
        _deduplicationCounter = _meter.CreateCounter<long>(
            "hartonomous.deduplication.atoms_reused",
            description: "Number of atoms deduplicated");
    }
    
    public void RecordIngestion(double durationMs, int atomsCreated, int atomsReused)
    {
        _ingestionCounter.Add(1);
        _ingestionDuration.Record(durationMs);
        _deduplicationCounter.Add(atomsReused);
    }
}
```

**Distributed Tracing:**

```csharp
public sealed class ContentIngestionHandler
{
    private static readonly ActivitySource Activity = new("Hartonomous.ContentIngestion");
    
    public async Task<IngestionResult> Handle(
        IngestContentCommand request,
        CancellationToken cancellationToken)
    {
        using var activity = Activity.StartActivity("IngestContent");
        activity?.SetTag("content.size", request.Content.Length);
        
        using var atomizeActivity = Activity.StartActivity("Atomize");
        var atoms = AtomizeContent(request.Content);
        atomizeActivity?.SetTag("atoms.count", atoms.Count);
        atomizeActivity?.Dispose();
        
        using var dedupeActivity = Activity.StartActivity("Deduplicate");
        var result = await DeduplicateAtoms(atoms, cancellationToken);
        dedupeActivity?.SetTag("deduplication.rate", result.DeduplicationRate);
        
        return result;
    }
}
```

</details>

<details>
<summary><strong>8.3.2: Structured Logging</strong> (0.5 days)</summary>

```csharp
// Use structured logging with Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(new CompactJsonFormatter())
        .WriteTo.ApplicationInsights(
            context.Configuration["ApplicationInsights:ConnectionString"],
            TelemetryConverter.Traces);
});

// Structured logging in handlers
_logger.LogInformation(
    "Content ingested successfully. ContentId: {ContentId}, Atoms: {AtomCount}, Deduplication: {DeduplicationRate:P2}",
    result.Id,
    result.AtomCount,
    result.DeduplicationRate);
```

</details>

<details>
<summary><strong>8.3.3: Application Insights Dashboards</strong> (0.5 days)</summary>

**Create custom dashboards in Azure Portal:**

1. **Performance Dashboard**
   - Request duration (p50, p95, p99)
   - Dependency duration (database, cache)
   - Failure rate
   - Request throughput

2. **Business Metrics Dashboard**
   - Ingestion rate (req/s)
   - Deduplication rate (%)
   - Atom creation rate
   - Storage efficiency

3. **Infrastructure Dashboard**
   - CPU usage
   - Memory usage
   - Database connection pool
   - Cache hit rate

**Alert Rules:**

```bash
# Create alert for high error rate
az monitor metrics alert create \
  --name "High Error Rate" \
  --resource-group hartonomous-rg \
  --scopes /subscriptions/$SUB_ID/resourceGroups/hartonomous-rg/providers/Microsoft.Insights/components/hartonomous-ai \
  --condition "avg requests/failed > 5" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action-group hartonomous-alerts

# Create alert for slow response times
az monitor metrics alert create \
  --name "Slow Response Time" \
  --resource-group hartonomous-rg \
  --scopes /subscriptions/$SUB_ID/resourceGroups/hartonomous-rg/providers/Microsoft.Insights/components/hartonomous-ai \
  --condition "avg requests/duration > 500" \
  --window-size 5m \
  --evaluation-frequency 1m \
  --action-group hartonomous-alerts
```

</details>

---

### 8.4: Production Deployment

**Goal**: Deploy to production with zero downtime and rollback capability.

<details>
<summary><strong>8.4.1: Azure Arc Deployment Script</strong> (1 day)</summary>

**Update `azure-pipelines.yml`:**

```yaml
trigger:
  branches:
    include:
      - main
      - release/*

variables:
  buildConfiguration: 'Release'
  azureSubscription: 'Hartonomous-Production'
  resourceGroupName: 'hartonomous-rg'
  arcServerName: 'hart-server'

stages:
- stage: Build
  jobs:
  - job: Build
    pool:
      vmImage: 'windows-latest'
    steps:
    - task: UseDotNet@2
      inputs:
        version: '10.x'
        
    - task: DotNetCoreCLI@2
      displayName: 'Restore packages'
      inputs:
        command: 'restore'
        
    - task: DotNetCoreCLI@2
      displayName: 'Build solution'
      inputs:
        command: 'build'
        arguments: '--configuration $(buildConfiguration) --no-restore'
        
    - task: DotNetCoreCLI@2
      displayName: 'Run tests'
      inputs:
        command: 'test'
        arguments: '--configuration $(buildConfiguration) --no-build --collect:"XPlat Code Coverage"'
        
    - task: DotNetCoreCLI@2
      displayName: 'Publish API'
      inputs:
        command: 'publish'
        publishWebProjects: false
        projects: '**/Hartonomous.API.csproj'
        arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)/api'
        
    - task: PublishBuildArtifacts@1
      inputs:
        pathToPublish: '$(Build.ArtifactStagingDirectory)'
        artifactName: 'drop'

- stage: DeployProduction
  dependsOn: Build
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - deployment: DeployToProduction
    environment: 'production'
    strategy:
      runOnce:
        deploy:
          steps:
          - task: DownloadBuildArtifacts@0
            inputs:
              artifactName: 'drop'
              
          - task: AzureCLI@2
            displayName: 'Stop API service (blue)'
            inputs:
              azureSubscription: '$(azureSubscription)'
              scriptType: 'pscore'
              scriptLocation: 'inlineScript'
              inlineScript: |
                az connectedmachine run-command create `
                  --resource-group $(resourceGroupName) `
                  --machine-name $(arcServerName) `
                  --run-command-name StopService `
                  --script "systemctl stop hartonomous-api-blue"
                  
          - task: AzureCLI@2
            displayName: 'Deploy API (blue)'
            inputs:
              azureSubscription: '$(azureSubscription)'
              scriptType: 'pscore'
              scriptLocation: 'scriptPath'
              scriptPath: '$(System.ArtifactsDirectory)/drop/deploy/Deploy-App.ps1'
              arguments: '-Environment Production -Slot blue'
              
          - task: AzureCLI@2
            displayName: 'Start API service (blue)'
            inputs:
              azureSubscription: '$(azureSubscription)'
              scriptType: 'pscore'
              scriptLocation: 'inlineScript'
              inlineScript: |
                az connectedmachine run-command create `
                  --resource-group $(resourceGroupName) `
                  --machine-name $(arcServerName) `
                  --run-command-name StartService `
                  --script "systemctl start hartonomous-api-blue"
                  
          - task: PowerShell@2
            displayName: 'Health check (blue)'
            inputs:
              targetType: 'inline'
              script: |
                $healthUrl = "https://hart-server:5001/health"
                $retries = 30
                $delay = 10
                
                for ($i = 0; $i -lt $retries; $i++) {
                    try {
                        $response = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
                        if ($response.status -eq "Healthy") {
                            Write-Host "Health check passed"
                            exit 0
                        }
                    } catch {
                        Write-Host "Health check attempt $($i+1) failed"
                    }
                    Start-Sleep -Seconds $delay
                }
                
                Write-Error "Health check failed after $retries attempts"
                exit 1
                
          - task: AzureCLI@2
            displayName: 'Switch traffic to blue'
            inputs:
              azureSubscription: '$(azureSubscription)'
              scriptType: 'pscore'
              scriptLocation: 'inlineScript'
              inlineScript: |
                # Update nginx to route to blue
                az connectedmachine run-command create `
                  --resource-group $(resourceGroupName) `
                  --machine-name $(arcServerName) `
                  --run-command-name SwitchTraffic `
                  --script "sed -i 's/hartonomous-api-green/hartonomous-api-blue/g' /etc/nginx/sites-available/hartonomous && systemctl reload nginx"
                  
          - task: AzureCLI@2
            displayName: 'Stop old version (green)'
            inputs:
              azureSubscription: '$(azureSubscription)'
              scriptType: 'pscore'
              scriptLocation: 'inlineScript'
              inlineScript: |
                Start-Sleep -Seconds 60  # Wait for in-flight requests
                az connectedmachine run-command create `
                  --resource-group $(resourceGroupName) `
                  --machine-name $(arcServerName) `
                  --run-command-name StopOldService `
                  --script "systemctl stop hartonomous-api-green"
```

</details>

<details>
<summary><strong>8.4.2: Database Migration Strategy</strong> (1 day)</summary>

**Zero-Downtime Migration Pattern:**

```csharp
// 1. Add new column (nullable)
migrationBuilder.AddColumn<int>(
    name: "new_dimension",
    table: "constants",
    nullable: true);

// 2. Backfill data (background job)
public async Task BackfillNewDimensionAsync()
{
    await _dbContext.Database.ExecuteSqlRawAsync(@"
        UPDATE constants
        SET new_dimension = compute_new_dimension(data)
        WHERE new_dimension IS NULL
        LIMIT 1000;
    ");
}

// 3. Deploy code that uses new column (with null checks)
var dimension = constant.NewDimension ?? ComputeNewDimension(constant.Data);

// 4. Make column non-nullable after backfill complete
migrationBuilder.AlterColumn<int>(
    name: "new_dimension",
    table: "constants",
    nullable: false);

// 5. Remove fallback code
var dimension = constant.NewDimension;
```

**Backup Before Deployment:**

```bash
# Create backup
pg_dump -h postgres -U app -d hartonomous \
  --format=custom \
  --file=hartonomous_backup_$(date +%Y%m%d_%H%M%S).dump

# Upload to Azure Blob Storage
az storage blob upload \
  --account-name hartonomousbackups \
  --container-name database \
  --name hartonomous_backup_$(date +%Y%m%d_%H%M%S).dump \
  --file hartonomous_backup_*.dump
```

</details>

<details>
<summary><strong>8.4.3: Rollback Procedure</strong> (0.5 days)</summary>

**Automated Rollback Script:**

```powershell
# rollback.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$FromSlot,  # "blue" or "green"
    
    [Parameter(Mandatory=$true)]
    [string]$ToSlot     # "green" or "blue"
)

Write-Host "Rolling back from $FromSlot to $ToSlot..."

# 1. Switch nginx back to old version
az connectedmachine run-command create `
    --resource-group hartonomous-rg `
    --machine-name hart-server `
    --run-command-name RollbackTraffic `
    --script "sed -i 's/hartonomous-api-$FromSlot/hartonomous-api-$ToSlot/g' /etc/nginx/sites-available/hartonomous && systemctl reload nginx"

# 2. Stop new version
az connectedmachine run-command create `
    --resource-group hartonomous-rg `
    --machine-name hart-server `
    --run-command-name StopNewService `
    --script "systemctl stop hartonomous-api-$FromSlot"

# 3. Verify old version healthy
$healthUrl = "https://hart-server:5001/health"
$response = Invoke-RestMethod -Uri $healthUrl
if ($response.status -ne "Healthy") {
    Write-Error "Rollback failed - old version unhealthy"
    exit 1
}

Write-Host "Rollback complete. Traffic restored to $ToSlot"

# 4. Alert team
# Send notification to Teams/Slack
```

</details>

---

## Success Criteria

- [ ] **Performance targets met** - p95 latency <100ms at 1000 req/s
- [ ] **Security audit passed** - No critical or high vulnerabilities
- [ ] **Zero-downtime deployment** - Blue-green deployment working
- [ ] **Monitoring operational** - All dashboards and alerts configured
- [ ] **Disaster recovery tested** - Backup/restore procedures validated
- [ ] **Production deployment successful** - Application running in production

---

## Quality Gates

### Performance Gates
- [ ] Load tests pass at 1000 req/s sustained
- [ ] p95 latency <100ms
- [ ] Error rate <0.1%
- [ ] Deduplication rate >99% after warm-up

### Security Gates
- [ ] Penetration testing passed
- [ ] All secrets in Key Vault
- [ ] HTTPS enforced
- [ ] Rate limiting configured

### Operational Gates
- [ ] All alerts configured and tested
- [ ] Runbooks complete
- [ ] Backup/restore validated
- [ ] Blue-green deployment tested

---

## Risks & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Data loss during deployment** | Critical | Low | Automated backups, rollback procedure |
| **Performance degradation** | High | Medium | Load testing, staged rollout |
| **Security vulnerability** | Critical | Low | Penetration testing, security audit |
| **Monitoring gaps** | High | Medium | Comprehensive alert coverage, testing |

---

## Dependencies

### Upstream (Required)
- **All previous phases** - Complete feature set
- **Infrastructure** - Azure Arc, PostgreSQL, Redis

### Downstream (Impacts)
- **Production operations** - Ongoing maintenance and monitoring
- **Future development** - Foundation for additional features

---

## Next Steps

After completing Phase 8:

1. **Production launch** - Go live announcement
2. **Post-deployment monitoring** - Watch for issues in first 48 hours
3. **Performance tuning** - Optimize based on real traffic patterns
4. **Incident response** - Establish on-call rotation
5. **Future phases** - Plan Phase 9+ for additional features

---

**Navigation**:  
← [Phase 7: Documentation](Phase-7.md) | [Master Plan](Master-Plan.md) | [Home](../Home.md) →
