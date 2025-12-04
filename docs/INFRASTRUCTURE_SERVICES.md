# Infrastructure Services Implementation

**Hartonomous** infrastructure services provide comprehensive support for caching, blob storage, and message queuing with both cloud (Azure) and local development implementations.

## Architecture Overview

All infrastructure services follow the **Strategy Pattern** with interface-based abstractions:
- **ICacheService** - Distributed caching with Redis or in-memory fallback
- **IBlobStorageService** - Binary content storage with Azure Blob Storage or local file system
- **IMessageQueueService** - Async message queuing with Azure Storage Queue or in-memory queue

Services are automatically selected based on connection string configuration, enabling seamless transitions between development and production.

## Cache Service (Redis)

### Interface
```csharp
ICacheService
- GetAsync<T>(key) - Retrieve cached value
- SetAsync<T>(key, value, expiration?) - Store value with optional TTL
- RemoveAsync(key) - Delete cached value
- GetOrSetAsync<T>(key, factory, expiration?) - Cache-aside pattern
```

### Implementation
- **CacheService** - Uses IDistributedCache (Redis or in-memory)
- JSON serialization with camelCase naming
- Supports TimeSpan expiration
- Cache-aside pattern with GetOrSetAsync

### Configuration
```json
{
  "ConnectionStrings": {
    "Redis": "" // Empty = in-memory, "localhost:6379" = Redis
  },
  "Redis": {
    "InstanceName": "Hartonomous_"
  }
}
```

### Usage
```csharp
// Inject service
private readonly ICacheService _cache;

// Get or set with factory
var vocabulary = await _cache.GetOrSetAsync(
    "bpe_vocabulary",
    async () => await LoadVocabularyFromDbAsync(),
    TimeSpan.FromHours(24));

// Set with expiration
await _cache.SetAsync("recent_constants", constants, TimeSpan.FromMinutes(5));
```

## Blob Storage Service

### Interface
```csharp
IBlobStorageService
- UploadBlobAsync(container, name, stream, contentType) - Upload blob, returns URI
- DownloadBlobAsync(container, name) - Download blob as Stream
- DeleteBlobAsync(container, name) - Delete blob, returns success
- BlobExistsAsync(container, name) - Check existence
- GetBlobSasUrlAsync(container, name, expiration) - Generate temporary access URL
- ListBlobsAsync(container, prefix?) - List blobs in container
```

### Implementations

#### AzureBlobStorageService
- Uses Azure.Storage.Blobs SDK
- Automatic container creation
- SAS URL support for secure sharing
- Full blob lifecycle management

#### LocalFileStorageService
- File system-based for development
- Creates directory structure automatically
- Simulates blob URIs: `file://path/to/blob`
- No SAS URL support (returns file:// URLs)

### Configuration
```json
{
  "ConnectionStrings": {
    "BlobStorage": "file://local_storage" // Local
    // OR
    "BlobStorage": "DefaultEndpointsProtocol=https;AccountName=..." // Azure
  }
}
```

### Usage
```csharp
// Inject service
private readonly IBlobStorageService _blobStorage;

// Upload ingested content
using var stream = new MemoryStream(contentBytes);
var uri = await _blobStorage.UploadBlobAsync(
    "ingestions",
    $"{ingestionId}/content.dat",
    stream,
    "application/octet-stream");

// Download for processing
var contentStream = await _blobStorage.DownloadBlobAsync(
    "ingestions",
    $"{ingestionId}/content.dat");

// Generate temporary access URL
var sasUrl = await _blobStorage.GetBlobSasUrlAsync(
    "ingestions",
    $"{ingestionId}/content.dat",
    TimeSpan.FromHours(1));
```

## Message Queue Service

### Interface
```csharp
IMessageQueueService
- PublishAsync<T>(queue, message) - Send message to queue
- PublishWithDelayAsync<T>(queue, message, delay) - Schedule message
- ReceiveAsync<T>(queue, visibilityTimeout?) - Receive single message
- ReceiveBatchAsync<T>(queue, maxMessages, visibilityTimeout?) - Receive multiple
- CompleteAsync(queue, messageId, popReceipt) - Delete processed message
- AbandonAsync(queue, messageId, popReceipt) - Return message to queue
- GetQueueLengthAsync(queue) - Get approximate queue depth
```

### Message Model
```csharp
QueueMessage<T>
- MessageId - Unique identifier
- PopReceipt - Receipt handle for completion/abandon
- Content - Deserialized message payload
- DequeueCount - Number of times received
- InsertedOn, ExpiresOn, NextVisibleOn - Timestamps
```

### Implementations

#### AzureQueueService
- Uses Azure.Storage.Queues SDK
- Automatic queue creation
- Max 32 messages per batch
- JSON serialization with camelCase
- 7-day message retention

#### InMemoryQueueService
- ConcurrentQueue-based for development
- Simulates visibility timeout with NextVisibleOn
- Validates PopReceipt for operations
- Thread-safe with ConcurrentDictionary

### Configuration
```json
{
  "ConnectionStrings": {
    "MessageQueue": "InMemory" // In-memory
    // OR
    "MessageQueue": "DefaultEndpointsProtocol=https;AccountName=..." // Azure
  }
}
```

### Usage
```csharp
// Inject service
private readonly IMessageQueueService _queue;

// Publish ingestion job
await _queue.PublishAsync("ingestion-jobs", new IngestionJobMessage
{
    IngestionId = ingestionId,
    Priority = JobPriority.High,
    SubmittedAt = DateTime.UtcNow
});

// Receive and process
var message = await _queue.ReceiveAsync<IngestionJobMessage>(
    "ingestion-jobs",
    TimeSpan.FromMinutes(5)); // Visibility timeout

if (message != null)
{
    try
    {
        await ProcessIngestionAsync(message.Content);
        await _queue.CompleteAsync(
            "ingestion-jobs",
            message.MessageId,
            message.PopReceipt);
    }
    catch (Exception)
    {
        await _queue.AbandonAsync(
            "ingestion-jobs",
            message.MessageId,
            message.PopReceipt);
    }
}

// Batch receive for high throughput
var messages = await _queue.ReceiveBatchAsync<IngestionJobMessage>(
    "ingestion-jobs",
    maxMessages: 10,
    visibilityTimeout: TimeSpan.FromMinutes(5));

await Parallel.ForEachAsync(messages, async (msg, ct) =>
{
    // Process concurrently
});
```

## Dependency Injection Registration

All infrastructure services are registered in **InfrastructureServicesExtensions**:

```csharp
services.AddInfrastructureServices(configuration);
```

This automatically:
1. **Detects environment** via connection strings
2. **Registers appropriate implementations**:
   - Redis connection string → StackExchangeRedis, else in-memory cache
   - Azure blob connection → AzureBlobStorageService, else LocalFileStorageService
   - Azure queue connection → AzureQueueService, else InMemoryQueueService
3. **Configures clients** (BlobServiceClient, QueueServiceClient)
4. **Enables health checks** for all services

## Health Checks

Infrastructure services include comprehensive health monitoring:

### BlobStorageHealthCheck
- Tests blob existence operation
- Lightweight check (no actual upload)
- Reports healthy if service is accessible

### MessageQueueHealthCheck
- Queries test queue length
- Lightweight operation
- Reports healthy if service responds

### Registration
```csharp
builder.Services.AddComprehensiveHealthChecks(configuration);
```

Health check endpoints:
- `/health` - All checks (database, redis, blob, queue, memory, disk)
- `/health/live` - Liveness (is process running?)
- `/health/ready` - Readiness (can accept traffic?)

### Sample Response
```json
{
  "status": "Healthy",
  "checks": {
    "self": { "status": "Healthy" },
    "postgresql": { "status": "Healthy" },
    "redis": { "status": "Healthy" },
    "blob_storage": { "status": "Healthy" },
    "message_queue": { "status": "Healthy", "data": { "queueLength": 0 } },
    "memory": { "status": "Healthy", "data": { "allocatedMB": 256 } },
    "disk": { "status": "Healthy", "data": { "freeSpaceGB": 123.45 } }
  }
}
```

## Package Dependencies

### Infrastructure.csproj
```xml
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.0" />
<PackageReference Include="Azure.Storage.Blobs" Version="12.24.0" />
<PackageReference Include="Azure.Storage.Queues" Version="12.22.0" />
<PackageReference Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
```

## Development vs Production

### Local Development
```json
{
  "ConnectionStrings": {
    "Redis": "",
    "BlobStorage": "file://local_storage",
    "MessageQueue": "InMemory"
  }
}
```
- **In-memory cache** - No Redis required
- **File system storage** - Blobs in `local_storage/` directory
- **In-memory queue** - No Azure Storage required
- Zero cloud dependencies, works offline

### Production
```json
{
  "ConnectionStrings": {
    "Redis": "redis-prod.cache.windows.net:6380,password=...,ssl=True",
    "BlobStorage": "DefaultEndpointsProtocol=https;AccountName=hartonomous;...",
    "MessageQueue": "DefaultEndpointsProtocol=https;AccountName=hartonomous;..."
  }
}
```
- **Redis Cache** - Azure Cache for Redis (Standard/Premium tier)
- **Blob Storage** - Azure Storage Account (Hot tier for active data)
- **Queue Storage** - Azure Storage Account (built-in, no separate cost)

## Background Worker Integration

### ContentProcessingWorker
Uses **IMessageQueueService** to:
- Poll `ingestion-jobs` queue for new jobs
- Process with configurable parallelism (MaxParallelism: 3)
- Complete/abandon messages based on processing result
- Store processed content via **IBlobStorageService**

### BPELearningScheduler
Uses **ICacheService** to:
- Cache learned BPE vocabulary (expires after 24h)
- Avoid recomputing vocabulary on every request
- Invalidate cache when new learning occurs

### ConstantIndexingWorker
Uses **ICacheService** to:
- Coordinate index updates across multiple workers
- Cache recently indexed constant IDs
- Prevent duplicate index operations

### LandmarkDetectionWorker
Uses **IBlobStorageService** to:
- Store landmark cluster data for analysis
- Export landmark metadata for visualization
- Archive historical landmark snapshots

## Error Handling

All services implement comprehensive error handling:

### Logging
- **Information** - Successful operations (upload, publish, etc.)
- **Warning** - Non-fatal issues (deserialization failures, missing blobs)
- **Error** - Operation failures with exception details

### Exceptions
Services throw exceptions for:
- Connection failures
- Invalid configurations
- Storage/queue errors
- Authentication issues

Workers should wrap service calls in try-catch and use exponential backoff for retries.

## Performance Considerations

### Cache Service
- **Redis** provides millisecond latency for most operations
- **In-memory** is faster but not distributed (single-process only)
- Use appropriate expiration times to balance freshness vs. load

### Blob Storage
- **Azure** provides high throughput (hundreds of MB/s per blob)
- **Local** is limited by disk I/O
- Stream large files to avoid memory pressure
- Use SAS URLs for client-side downloads (no proxy through API)

### Message Queue
- **Azure** supports up to 2000 messages/second per queue
- **In-memory** is limited by memory (not persistent)
- Batch receive (10-32 messages) for higher throughput
- Visibility timeout prevents duplicate processing

## Security

### Authentication
- **Azure services** use **DefaultAzureCredential** (Managed Identity in production, Azure CLI in dev)
- **Connection strings** support for development environments
- Secrets managed via **Azure Key Vault** or **User Secrets**

### Authorization
- **Blob Storage**: Container-level access control, SAS for temporary access
- **Message Queue**: Queue-level access control
- **Redis**: Password/TLS in production, no auth in development

### Encryption
- **Azure**: All data encrypted at rest (256-bit AES)
- **Azure**: TLS 1.2+ for data in transit
- **Local**: File system permissions control access

## Migration Path

### Development → Staging
1. Create Azure Storage Account
2. Create Azure Cache for Redis
3. Update connection strings in appsettings.Staging.json
4. Test health checks return Healthy
5. Verify blob upload/download works
6. Verify message queue processing works

### Staging → Production
1. Use separate Azure resources per environment
2. Enable diagnostic logging (Azure Monitor)
3. Configure alerts for health check failures
4. Scale Redis to Standard/Premium tier
5. Enable geo-replication for disaster recovery

## Testing

### Unit Tests
Mock interfaces for unit testing:
```csharp
var mockCache = new Mock<ICacheService>();
mockCache.Setup(x => x.GetAsync<BpeVocabulary>("bpe_vocab", default))
    .ReturnsAsync(testVocabulary);
```

### Integration Tests
Use TestContainers for integration tests:
- Redis container for cache tests
- Azurite container for blob/queue tests
- Test both implementations (Azure + In-memory/Local)

## Troubleshooting

### Cache not working
- Check `ConnectionStrings:Redis` is set correctly
- Verify Redis is running (docker logs, Azure portal)
- Check health endpoint: `/health` → redis status
- Look for errors in application logs

### Blob storage failures
- Check `ConnectionStrings:BlobStorage` configuration
- For Azure: Verify storage account access keys
- For local: Check directory permissions on `local_storage/`
- Health endpoint: `/health` → blob_storage status

### Message queue not processing
- Check `ConnectionStrings:MessageQueue` configuration
- Verify queue exists (Azure Storage Explorer, or in-memory log)
- Check worker is running and polling
- Look for errors in worker logs
- Use `GetQueueLengthAsync` to verify messages are enqueued

### Health checks failing
- Check service dependencies are accessible
- Review health check logs for specific failures
- Verify network connectivity (firewall, NSG rules)
- Test connection strings with Azure Storage Explorer

## Files Created

### Storage
- `Hartonomous.Infrastructure/Storage/IBlobStorageService.cs` - Interface
- `Hartonomous.Infrastructure/Storage/AzureBlobStorageService.cs` - Azure implementation
- `Hartonomous.Infrastructure/Storage/LocalFileStorageService.cs` - Local implementation

### Messaging
- `Hartonomous.Infrastructure/Messaging/IMessageQueueService.cs` - Interface + QueueMessage<T>
- `Hartonomous.Infrastructure/Messaging/AzureQueueService.cs` - Azure implementation
- `Hartonomous.Infrastructure/Messaging/InMemoryQueueService.cs` - In-memory implementation

### Health Checks
- `Hartonomous.Infrastructure/Health/BlobStorageHealthCheck.cs`
- `Hartonomous.Infrastructure/Health/MessageQueueHealthCheck.cs`
- `Hartonomous.Infrastructure/Extensions/InfrastructureHealthChecksExtensions.cs`

### Configuration
- `Hartonomous.Infrastructure/Extensions/InfrastructureServicesExtensions.cs` - Updated DI registration
- `Hartonomous.API/appsettings.json` - Added ConnectionStrings section
- `Hartonomous.Worker/appsettings.json` - Added ConnectionStrings section

## Summary

The infrastructure services provide a **production-ready foundation** for distributed caching, blob storage, and message queuing. All services support both cloud (Azure) and local implementations, enabling seamless development and testing without cloud dependencies. Health checks ensure runtime reliability, and comprehensive error handling with logging facilitates troubleshooting.

**Key Benefits**:
- ✅ **Abstraction** - Interface-based, easy to mock and test
- ✅ **Flexibility** - Azure or local, selected via configuration
- ✅ **Monitoring** - Health checks for operational visibility
- ✅ **Performance** - Optimized for high throughput scenarios
- ✅ **Security** - Encryption, authentication, authorization built-in
- ✅ **Scalability** - Azure services scale to enterprise workloads
