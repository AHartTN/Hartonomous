# C# Redis Consumer Implementation Guide

## Overview
This document provides the C# implementation for consuming code atomization requests from Redis queue.

## Implementation

### 1. Add Redis Package

```bash
cd src/Hartonomous.CodeAtomizer.Api
dotnet add package StackExchange.Redis
```

### 2. Create RedisAtomizationWorker.cs

```csharp
using StackExchange.Redis;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hartonomous.CodeAtomizer.Api.Workers;

/// <summary>
/// Background worker that consumes atomization requests from Redis queue.
/// </summary>
public class RedisAtomizationWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICodeAtomizationService _atomizationService;
    private readonly ILogger<RedisAtomizationWorker> _logger;
    
    private const string QueueKey = "atomization_queue";
    private const string ResultKeyPrefix = "atomization_results";
    
    public RedisAtomizationWorker(
        IConnectionMultiplexer redis,
        ICodeAtomizationService atomizationService,
        ILogger<RedisAtomizationWorker> logger)
    {
        _redis = redis;
        _atomizationService = atomizationService;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redis atomization worker starting...");
        
        var db = _redis.GetDatabase();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // BRPOP: blocking right pop (waits up to 5 seconds)
                var request = await db.ListRightPopAsync(QueueKey);
                
                if (request.IsNullOrEmpty)
                {
                    // No messages, continue loop
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }
                
                // Deserialize request
                var atomizationRequest = JsonSerializer.Deserialize<AtomizationRequest>(request!);
                
                if (atomizationRequest == null)
                {
                    _logger.LogWarning("Failed to deserialize atomization request");
                    continue;
                }
                
                _logger.LogInformation(
                    "Processing atomization request {RequestId} (language={Language})",
                    atomizationRequest.RequestId,
                    atomizationRequest.Language
                );
                
                // Process atomization
                var startTime = DateTime.UtcNow;
                AtomizationResult result;
                
                try
                {
                    var atomizationResponse = await _atomizationService.AtomizeCodeAsync(
                        atomizationRequest.SourceCode,
                        atomizationRequest.Language
                    );
                    
                    result = new AtomizationResult
                    {
                        RequestId = atomizationRequest.RequestId,
                        TrajectoryId = atomizationResponse.TrajectoryId,
                        AtomIds = atomizationResponse.AtomIds,
                        Success = true,
                        ProcessedAt = DateTime.UtcNow.ToString("O"),
                        ProcessingTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                    
                    _logger.LogInformation(
                        "Atomization completed: request={RequestId}, trajectory={TrajectoryId}, atoms={AtomCount}",
                        result.RequestId,
                        result.TrajectoryId,
                        result.AtomIds.Count
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Atomization failed for request {RequestId}", atomizationRequest.RequestId);
                    
                    result = new AtomizationResult
                    {
                        RequestId = atomizationRequest.RequestId,
                        Success = false,
                        ErrorMessage = ex.Message,
                        ProcessedAt = DateTime.UtcNow.ToString("O"),
                        ProcessingTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                }
                
                // Publish result to Redis
                var resultKey = $"{ResultKeyPrefix}:{result.RequestId}";
                var resultJson = JsonSerializer.Serialize(result);
                
                await db.StringSetAsync(
                    resultKey,
                    resultJson,
                    TimeSpan.FromHours(1) // TTL: 1 hour
                );
                
                _logger.LogInformation("Published result for request {RequestId}", result.RequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Redis atomization worker");
                await Task.Delay(5000, stoppingToken); // Back off on errors
            }
        }
        
        _logger.LogInformation("Redis atomization worker stopped");
    }
}

/// <summary>
/// Atomization request from Redis queue.
/// </summary>
public record AtomizationRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public int Priority { get; init; } = 5;
}

/// <summary>
/// Atomization result published to Redis.
/// </summary>
public record AtomizationResult
{
    public string RequestId { get; init; } = string.Empty;
    public long? TrajectoryId { get; init; }
    public List<long> AtomIds { get; init; } = new();
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ProcessedAt { get; init; }
    public int? ProcessingTimeMs { get; init; }
}
```

### 3. Register Services in Program.cs

```csharp
using StackExchange.Redis;
using Hartonomous.CodeAtomizer.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// ... existing services ...

// Redis connection
var redisUrl = builder.Configuration.GetValue<string>("REDIS_URL") ?? "redis://localhost:6379";
var redis = await ConnectionMultiplexer.ConnectAsync(redisUrl);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Register background worker
builder.Services.AddHostedService<RedisAtomizationWorker>();

var app = builder.Build();

// ... existing middleware ...

app.Run();
```

### 4. Update Dockerfile (if needed)

The Redis client package is restored via `dotnet restore`, no Dockerfile changes needed.

### 5. Environment Variables

Add to docker-compose.yml (already done):

```yaml
environment:
  - REDIS_URL=redis://redis:6379/0
```

## Testing

### Start Services

```bash
docker-compose up -d redis
docker-compose up -d code-atomizer
```

### Monitor Worker Logs

```bash
docker-compose logs -f code-atomizer
```

You should see:
```
Redis atomization worker starting...
```

### Test from Python

```python
from api.services.code_atomization.queue_client import CodeAtomizationQueue

queue = CodeAtomizationQueue()
await queue.connect()

# Enqueue request
request_id = await queue.enqueue_atomization(
    "public class Foo { }",
    "csharp"
)

print(f"Enqueued: {request_id}")

# Wait for result
result = await queue.get_result(request_id, timeout_seconds=30)

if result and result.success:
    print(f"Success! Trajectory: {result.trajectory_id}")
    print(f"Atoms: {result.atom_ids}")
else:
    print(f"Failed: {result.error_message if result else 'Timeout'}")
```

## Verification Queries

### Check Queue Depth

```bash
docker exec hartonomous-redis redis-cli LLEN atomization_queue
```

### Check Stats

```bash
docker exec hartonomous-redis redis-cli HGETALL atomization_stats
```

### View Result

```bash
docker exec hartonomous-redis redis-cli GET "atomization_results:req_abc123"
```

## Architecture Benefits

1. **Non-blocking**: API returns immediately, no waiting for C# processing
2. **Fault-tolerant**: Redis persistence survives crashes, queue preserved
3. **Scalable**: Can run multiple C# workers consuming from same queue
4. **Observable**: Queue depth and processing metrics available in real-time

## Performance Expectations

- **Queue latency**: <5ms to enqueue
- **Processing time**: ~50-200ms per file (C# atomization)
- **Throughput**: ~500-1000 files/second with 10 C# workers
