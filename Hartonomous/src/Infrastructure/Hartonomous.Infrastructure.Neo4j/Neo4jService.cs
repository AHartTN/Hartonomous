using Neo4j.Driver;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Neo4j;

/// <summary>
/// Service for managing Neo4j knowledge graph operations
/// Implements the read-replica pattern from the Hartonomous data fabric
/// </summary>
public class Neo4jService : IDisposable
{
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jService> _logger;

    public Neo4jService(IConfiguration configuration, ILogger<Neo4jService> logger)
    {
        _logger = logger;

        try
        {
            var uri = configuration["Neo4j:Uri"] ?? throw new ArgumentException("Neo4j:Uri configuration required");
            var username = configuration["Neo4j:Username"] ?? throw new ArgumentException("Neo4j:Username configuration required");
            var password = configuration["Neo4j:Password"] ?? throw new ArgumentException("Neo4j:Password configuration required");

            _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
            _logger.LogInformation("Neo4j driver initialized for URI: {Uri}", uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Neo4j driver");
            throw;
        }
    }

    /// <summary>
    /// Create model component node in knowledge graph
    /// Called from CDC pipeline when components are added to SQL Server
    /// </summary>
    public async Task CreateModelComponentAsync(Guid componentId, Guid modelId, string componentName, string componentType, string userId)
    {
        try
        {
            _logger.LogDebug("Creating model component {ComponentId} for model {ModelId}", componentId, modelId);

            const string cypher = @"
                MERGE (c:ModelComponent {id: $componentId})
                SET c.modelId = $modelId,
                    c.name = $componentName,
                    c.type = $componentType,
                    c.userId = $userId,
                    c.updatedAt = datetime()";

            using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(cypher, new
                {
                    componentId = componentId.ToString(),
                    modelId = modelId.ToString(),
                    componentName,
                    componentType,
                userId
            });
        });

            _logger.LogDebug("Created ModelComponent node: {ComponentId}", componentId);
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error creating component {ComponentId}: {Message}", componentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating component {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Create relationship between model components
    /// Maps to the ModelStructure edge table in SQL Server
    /// </summary>
    public async Task CreateComponentRelationshipAsync(Guid fromComponentId, Guid toComponentId, string relationshipType, string userId)
    {
        try
        {
            _logger.LogDebug("Creating relationship: {From} -> {To} ({Type})", fromComponentId, toComponentId, relationshipType);

            const string cypher = @"
                MATCH (from:ModelComponent {id: $fromId, userId: $userId})
                MATCH (to:ModelComponent {id: $toId, userId: $userId})
                MERGE (from)-[r:CONNECTS_TO {type: $relType}]->(to)
                SET r.createdAt = datetime()";

            using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(cypher, new
                {
                    fromId = fromComponentId.ToString(),
                    toId = toComponentId.ToString(),
                    relType = relationshipType,
                    userId
                });
            });

            _logger.LogDebug("Created relationship: {From} -> {To} ({Type})", fromComponentId, toComponentId, relationshipType);
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error creating relationship {From} -> {To}: {Message}", fromComponentId, toComponentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating relationship {From} -> {To}", fromComponentId, toComponentId);
            throw;
        }
    }

    /// <summary>
    /// Query model structure using graph traversal
    /// Provides capabilities SQL Server graph queries cannot match
    /// </summary>
    public async Task<IEnumerable<ModelComponentPath>> GetModelPathsAsync(Guid startComponentId, int maxDepth, string userId)
    {
        try
        {
            _logger.LogDebug("Querying paths from component {ComponentId} with max depth {MaxDepth}", startComponentId, maxDepth);

            const string cypher = @"
                MATCH path = (start:ModelComponent {id: $startId, userId: $userId})-[:CONNECTS_TO*1..$maxDepth]->(end:ModelComponent)
                RETURN [node in nodes(path) | {id: node.id, name: node.name, type: node.type}] as components,
                       [rel in relationships(path) | rel.type] as relationshipTypes";

            using var session = _driver.AsyncSession();
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(cypher, new
                {
                    startId = startComponentId.ToString(),
                    maxDepth,
                    userId
                });

                var paths = new List<ModelComponentPath>();
                await foreach (var record in cursor)
                {
                    var components = record["components"].As<List<Dictionary<string, object>>>();
                    var relationshipTypes = record["relationshipTypes"].As<List<string>>();

                    var path = new ModelComponentPath
                    {
                        Components = components.Select(c => new ModelComponentInfo
                        {
                            Id = Guid.Parse(c["id"].ToString()!),
                            Name = c["name"].ToString()!,
                            Type = c["type"].ToString()!
                        }).ToList(),
                        RelationshipTypes = relationshipTypes
                    };

                    paths.Add(path);
                }

                return paths;
            });

            _logger.LogDebug("Found {Count} paths from component {ComponentId}", result.Count(), startComponentId);
            return result;
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error querying paths from {ComponentId}: {Message}", startComponentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying paths from {ComponentId}", startComponentId);
            throw;
        }
    }

    /// <summary>
    /// Find similar components by type and structure
    /// Leverages graph algorithms for model analysis
    /// </summary>
    public async Task<IEnumerable<ModelComponentInfo>> FindSimilarComponentsAsync(Guid componentId, string userId, int limit = 10)
    {
        try
        {
            _logger.LogDebug("Finding similar components to {ComponentId} (limit: {Limit})", componentId, limit);

            const string cypher = @"
                MATCH (source:ModelComponent {id: $componentId, userId: $userId})
                MATCH (similar:ModelComponent {type: source.type, userId: $userId})
                WHERE similar.id <> source.id
                OPTIONAL MATCH (source)-[:CONNECTS_TO]->(sourceConnected:ModelComponent)
                OPTIONAL MATCH (similar)-[:CONNECTS_TO]->(similarConnected:ModelComponent)
                WITH similar,
                     count(DISTINCT sourceConnected) as sourceConnections,
                     count(DISTINCT similarConnected) as similarConnections,
                     count(DISTINCT sourceConnected.type) as sourceTypes,
                     count(DISTINCT similarConnected.type) as similarTypes
                WITH similar,
                     abs(sourceConnections - similarConnections) as connectionDiff,
                     abs(sourceTypes - similarTypes) as typeDiff
                ORDER BY connectionDiff + typeDiff
                LIMIT $limit
                RETURN similar.id as id, similar.name as name, similar.type as type";

            using var session = _driver.AsyncSession();
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(cypher, new
                {
                    componentId = componentId.ToString(),
                    userId,
                    limit
                });

                var components = new List<ModelComponentInfo>();
                await foreach (var record in cursor)
                {
                    components.Add(new ModelComponentInfo
                    {
                        Id = Guid.Parse(record["id"].ToString()!),
                        Name = record["name"].ToString()!,
                        Type = record["type"].ToString()!
                    });
                }

                return components;
            });

            _logger.LogDebug("Found {Count} similar components to {ComponentId}", result.Count(), componentId);
            return result;
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error finding similar components to {ComponentId}: {Message}", componentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error finding similar components to {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Remove component and all its relationships
    /// Called from CDC pipeline when components are deleted
    /// </summary>
    public async Task DeleteComponentAsync(Guid componentId, string userId)
    {
        try
        {
            _logger.LogDebug("Deleting component {ComponentId}", componentId);

            const string cypher = @"
                MATCH (c:ModelComponent {id: $componentId, userId: $userId})
                DETACH DELETE c";

            using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(cypher, new
                {
                    componentId = componentId.ToString(),
                    userId
                });
            });

            _logger.LogDebug("Deleted component: {ComponentId}", componentId);
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error deleting component {ComponentId}: {Message}", componentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting component {ComponentId}", componentId);
            throw;
        }
    }

    public void Dispose()
    {
        _driver?.Dispose();
    }
}

/// <summary>
/// Represents a path through the model component graph
/// </summary>
public class ModelComponentPath
{
    public List<ModelComponentInfo> Components { get; set; } = new();
    public List<string> RelationshipTypes { get; set; } = new();
}

/// <summary>
/// Basic model component information
/// </summary>
public class ModelComponentInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}