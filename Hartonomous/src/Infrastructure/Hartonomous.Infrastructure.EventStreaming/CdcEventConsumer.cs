using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.Milvus;

namespace Hartonomous.Infrastructure.EventStreaming;

/// <summary>
/// Consumer for Debezium CDC events from SQL Server
/// Processes changes and propagates to read replicas (Neo4j, SQL Server Vector)
/// Implements the core data fabric pattern from Hartonomous blueprint
/// </summary>
public class CdcEventConsumer : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly Neo4jService _neo4jService;
    private readonly SqlServerVectorService _vectorService;
    private readonly ILogger<CdcEventConsumer> _logger;
    private readonly List<string> _topics;

    public CdcEventConsumer(IConfiguration configuration, Neo4jService neo4jService,
        SqlServerVectorService vectorService, ILogger<CdcEventConsumer> logger)
    {
        _neo4jService = neo4jService ?? throw new ArgumentNullException(nameof(neo4jService));
        _vectorService = vectorService ?? throw new ArgumentNullException(nameof(vectorService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            // Kafka consumer configuration
            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                GroupId = "hartonomous-cdc-consumer",
                AutoOffsetReset = AutoOffsetReset.Latest,
                EnableAutoCommit = false // Manual commit for reliability
            };

            _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();

            // Subscribe to Debezium CDC topics for our tables
            _topics = new List<string>
            {
                "hartonomous-server.dbo.Projects",
                "hartonomous-server.dbo.ModelMetadata",
                "hartonomous-server.dbo.ModelComponents",
                "hartonomous-server.dbo.ModelStructure",
                "hartonomous-server.dbo.ComponentEmbeddings"
            };

            _consumer.Subscribe(_topics);
            _logger.LogInformation("CDC Consumer subscribed to topics: {Topics}", string.Join(", ", _topics));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CDC Event Consumer");
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CDC Event Consumer started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(TimeSpan.FromSeconds(1));

                    if (consumeResult != null)
                    {
                        await ProcessCdcEventAsync(consumeResult, stoppingToken);
                        _consumer.Commit(consumeResult);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming CDC event");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in CDC consumer");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Back off on error
                }
            }
        }
        finally
        {
            _consumer.Close();
            _logger.LogInformation("CDC Event Consumer stopped");
        }
    }

    /// <summary>
    /// Process individual CDC event and route to appropriate read replica
    /// </summary>
    private async Task ProcessCdcEventAsync(ConsumeResult<string, string> consumeResult, CancellationToken cancellationToken)
    {
        try
        {
            var cdcEvent = JsonSerializer.Deserialize<DebeziumCdcEvent>(consumeResult.Message.Value);
            if (cdcEvent?.Payload == null)
            {
                _logger.LogWarning("Received null or invalid CDC event");
                return;
            }

            var tableName = GetTableNameFromTopic(consumeResult.Topic);
            var operation = cdcEvent.Payload.Op; // "c" = create, "u" = update, "d" = delete, "r" = read (snapshot)

            _logger.LogDebug("Processing CDC event: {Table} {Operation}", tableName, operation);

            switch (tableName.ToLower())
            {
                case "modelcomponents":
                    await ProcessModelComponentEventAsync(cdcEvent, operation, cancellationToken);
                    break;

                case "modelstructure":
                    await ProcessModelStructureEventAsync(cdcEvent, operation, cancellationToken);
                    break;

                case "componentembeddings":
                    await ProcessComponentEmbeddingEventAsync(cdcEvent, operation, cancellationToken);
                    break;

                case "projects":
                    await ProcessProjectEventAsync(cdcEvent, operation, cancellationToken);
                    break;

                case "modelmetadata":
                    await ProcessModelMetadataEventAsync(cdcEvent, operation, cancellationToken);
                    break;

                default:
                    _logger.LogDebug("Ignoring CDC event for table: {Table}", tableName);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing CDC event from topic: {Topic}", consumeResult.Topic);
            throw; // Re-throw to trigger retry logic
        }
    }

    /// <summary>
    /// Process ModelComponents table changes for Neo4j knowledge graph
    /// </summary>
    private async Task ProcessModelComponentEventAsync(DebeziumCdcEvent cdcEvent, string operation, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Processing ModelComponent event: {Operation}", operation);
            var after = cdcEvent.Payload.After;
            var before = cdcEvent.Payload.Before;

            switch (operation)
            {
                case "c": // Create
                case "r": // Read (snapshot)
                case "u": // Update
                    if (after != null)
                    {
                        var componentId = Guid.Parse(after["ComponentId"].ToString()!);
                        var modelId = Guid.Parse(after["ModelId"].ToString()!);
                        var componentName = after["ComponentName"].ToString()!;
                        var componentType = after["ComponentType"].ToString()!;

                        // Extract user ID from the model (would need to join with Projects table in real implementation)
                        // For now, using a placeholder approach
                        var userId = "system"; // This should be resolved from model->project->user relationship

                        await _neo4jService.CreateModelComponentAsync(componentId, modelId, componentName, componentType, userId);
                        _logger.LogDebug("Successfully processed ModelComponent {Operation} for {ComponentId}", operation, componentId);
                    }
                    break;

                case "d": // Delete
                    if (before != null)
                    {
                        var componentId = Guid.Parse(before["ComponentId"].ToString()!);
                        var userId = "system"; // Resolve from context

                        await _neo4jService.DeleteComponentAsync(componentId, userId);
                        _logger.LogDebug("Successfully processed ModelComponent delete for {ComponentId}", componentId);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ModelComponent event: {Operation}", operation);
            throw;
        }
    }

    /// <summary>
    /// Process ModelStructure table changes for Neo4j relationships
    /// </summary>
    private async Task ProcessModelStructureEventAsync(DebeziumCdcEvent cdcEvent, string operation, CancellationToken cancellationToken)
    {
        var after = cdcEvent.Payload.After;

        if (operation == "c" || operation == "r") // Create or snapshot
        {
            if (after != null)
            {
                var fromId = Guid.Parse(after["$from_id"].ToString()!);
                var toId = Guid.Parse(after["$to_id"].ToString()!);
                var relationshipType = after.ContainsKey("RelationshipType") ? after["RelationshipType"].ToString()! : "CONNECTS_TO";
                var userId = "system"; // Resolve from context

                await _neo4jService.CreateComponentRelationshipAsync(fromId, toId, relationshipType, userId);
            }
        }
        // Note: Edge tables in SQL Server don't support direct deletes via CDC the same way
    }

    /// <summary>
    /// Process ComponentEmbeddings table changes for Milvus vector search
    /// </summary>
    private async Task ProcessComponentEmbeddingEventAsync(DebeziumCdcEvent cdcEvent, string operation, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Processing ComponentEmbedding event: {Operation}", operation);
            var after = cdcEvent.Payload.After;
            var before = cdcEvent.Payload.Before;

            switch (operation)
            {
                case "c": // Create
                case "r": // Read (snapshot)
                case "u": // Update
                    if (after != null)
                    {
                        var componentId = Guid.Parse(after["ComponentId"].ToString()!);
                        var embeddingVector = after["EmbeddingVector"].ToString()!;

                        // Parse the embedding vector (assuming comma-separated floats)
                        var embedding = embeddingVector.Split(',').Select(float.Parse).ToArray();

                        // Get component details (would need component lookup in real implementation)
                        var modelId = Guid.NewGuid(); // Placeholder - resolve from component
                        var userId = "system";
                        var componentName = "component"; // Resolve from component
                        var componentType = "layer"; // Resolve from component

                        await _vectorService.InsertEmbeddingAsync(componentId, modelId, userId, embedding, componentType, componentName);
                        _logger.LogDebug("Successfully processed ComponentEmbedding {Operation} for {ComponentId}", operation, componentId);
                    }
                    break;

                case "d": // Delete
                    if (before != null)
                    {
                        var componentId = Guid.Parse(before["ComponentId"].ToString()!);
                        var userId = "system";

                        await _vectorService.DeleteEmbeddingsAsync(componentId, userId);
                        _logger.LogDebug("Successfully processed ComponentEmbedding delete for {ComponentId}", componentId);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ComponentEmbedding event: {Operation}", operation);
            throw;
        }
    }

    /// <summary>
    /// Process Projects table changes (could trigger cleanup operations)
    /// </summary>
    private async Task ProcessProjectEventAsync(DebeziumCdcEvent cdcEvent, string operation, CancellationToken cancellationToken)
    {
        // Projects are primarily handled by the main API
        // CDC events here could trigger cleanup or indexing operations
        _logger.LogDebug("Project CDC event processed: {Operation}", operation);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Process ModelMetadata table changes
    /// </summary>
    private async Task ProcessModelMetadataEventAsync(DebeziumCdcEvent cdcEvent, string operation, CancellationToken cancellationToken)
    {
        // Model metadata changes could trigger re-indexing
        _logger.LogDebug("ModelMetadata CDC event processed: {Operation}", operation);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Extract table name from Debezium topic name
    /// </summary>
    private static string GetTableNameFromTopic(string topic)
    {
        // Topic format: "hartonomous-server.dbo.TableName"
        var parts = topic.Split('.');
        return parts.Length >= 3 ? parts[2] : string.Empty;
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Debezium CDC event structure
/// </summary>
public class DebeziumCdcEvent
{
    public CdcEventPayload? Payload { get; set; }
}

public class CdcEventPayload
{
    public Dictionary<string, object>? Before { get; set; }
    public Dictionary<string, object>? After { get; set; }
    public string Op { get; set; } = string.Empty; // Operation: c, u, d, r
    public long TsMs { get; set; } // Timestamp
}