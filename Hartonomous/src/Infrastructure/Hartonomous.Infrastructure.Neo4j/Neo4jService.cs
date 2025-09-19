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

    /// <summary>
    /// Find domain-relevant components using graph analysis
    /// Analyzes component relationships and metadata to find domain-specific components
    /// </summary>
    public async Task<IEnumerable<DomainRelevantComponentInfo>> FindDomainRelevantComponentsAsync(string domain, string capability, double minImportance, string userId)
    {
        try
        {
            _logger.LogDebug("Finding domain-relevant components for {Domain}/{Capability} (min importance: {MinImportance})", domain, capability, minImportance);

            const string cypher = @"
                MATCH (c:ModelComponent {userId: $userId})
                OPTIONAL MATCH (c)-[r:CONNECTS_TO]->(related:ModelComponent {userId: $userId})
                WITH c,
                     count(related) as connectionCount,
                     avg(case when related.name CONTAINS $domain OR related.name CONTAINS $capability then 1.0 else 0.0 end) as domainRelevance,
                     avg(case when r.type IN ['INFLUENCES', 'ACTIVATES', 'CONTROLS'] then 1.0 else 0.5 end) as relationshipStrength
                WHERE (c.name CONTAINS $domain OR c.name CONTAINS $capability OR c.type CONTAINS $domain)
                      AND (domainRelevance * connectionCount * relationshipStrength) >= $minImportance
                RETURN c.id as id, c.name as name, c.type as type,
                       (domainRelevance * connectionCount * relationshipStrength) as relevanceScore
                ORDER BY relevanceScore DESC
                LIMIT 50";

            using var session = _driver.AsyncSession();
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(cypher, new
                {
                    userId,
                    domain,
                    capability,
                    minImportance
                });

                var components = new List<DomainRelevantComponentInfo>();
                await foreach (var record in cursor)
                {
                    components.Add(new DomainRelevantComponentInfo
                    {
                        Id = Guid.Parse(record["id"].ToString()!),
                        Name = record["name"].ToString()!,
                        Type = record["type"].ToString()!,
                        RelevanceScore = record["relevanceScore"].As<double>()
                    });
                }

                return components;
            });

            _logger.LogDebug("Found {Count} domain-relevant components", result.Count());
            return result;
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error finding domain-relevant components: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error finding domain-relevant components");
            throw;
        }
    }

    /// <summary>
    /// Analyze component importance using graph centrality algorithms
    /// Uses PageRank and betweenness centrality for importance scoring
    /// </summary>
    public async Task<IEnumerable<ComponentImportanceInfo>> AnalyzeComponentImportanceAsync(string taskDescription, int topK, string userId)
    {
        try
        {
            _logger.LogDebug("Analyzing component importance for task '{TaskDescription}' (top {TopK})", taskDescription, topK);

            const string cypher = @"
                MATCH (c:ModelComponent {userId: $userId})
                OPTIONAL MATCH (c)-[r:CONNECTS_TO]-(connected:ModelComponent {userId: $userId})
                WITH c, count(connected) as degree,
                     avg(case when connected.name CONTAINS $taskKeyword1 OR connected.name CONTAINS $taskKeyword2 then 1.0 else 0.0 end) as taskRelevance,
                     count(case when r.type = 'INFLUENCES' then 1 end) as influences,
                     count(case when r.type = 'ACTIVATES' then 1 end) as activations

                // Calculate PageRank-like centrality score
                OPTIONAL MATCH path = (c)-[:CONNECTS_TO*2..4]-(distant:ModelComponent {userId: $userId})
                WITH c, degree, taskRelevance, influences, activations,
                     count(DISTINCT distant) as indirectConnections

                // Calculate composite importance score
                WITH c,
                     (degree * 0.3 + indirectConnections * 0.2 + influences * 0.25 + activations * 0.25) as centralityScore,
                     taskRelevance,
                     (influences + activations) as totalActivations

                WHERE (c.name CONTAINS $taskKeyword1 OR c.name CONTAINS $taskKeyword2 OR taskRelevance > 0)
                      AND centralityScore > 0

                RETURN c.id as componentId, c.name as componentName, c.type as description,
                       (centralityScore * (1 + taskRelevance)) as importanceScore,
                       (totalActivations * 1.0 / (degree + 1)) as activationLevel
                ORDER BY importanceScore DESC
                LIMIT $topK";

            // Extract keywords from task description for matching
            var taskWords = taskDescription.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var taskKeyword1 = taskWords.Length > 0 ? taskWords[0] : "";
            var taskKeyword2 = taskWords.Length > 1 ? taskWords[1] : "";

            using var session = _driver.AsyncSession();
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(cypher, new
                {
                    userId,
                    taskKeyword1,
                    taskKeyword2,
                    topK
                });

                var importanceResults = new List<ComponentImportanceInfo>();
                await foreach (var record in cursor)
                {
                    importanceResults.Add(new ComponentImportanceInfo
                    {
                        ComponentId = Guid.Parse(record["componentId"].ToString()!),
                        ComponentName = record["componentName"].ToString()!,
                        Description = record["description"].ToString()!,
                        ImportanceScore = record["importanceScore"].As<double>(),
                        ActivationLevel = record["activationLevel"].As<double>()
                    });
                }

                return importanceResults;
            });

            _logger.LogDebug("Analyzed component importance: {Count} results", result.Count());
            return result;
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error analyzing component importance: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error analyzing component importance");
            throw;
        }
    }

    /// <summary>
    /// Discover computational circuits using advanced graph traversal
    /// Finds connected subgraphs that form functional circuits
    /// </summary>
    public async Task<IEnumerable<CircuitInfo>> DiscoverCircuitsAsync(string domain, double minStrength, int maxDepth, string userId)
    {
        try
        {
            _logger.LogDebug("Discovering circuits for domain '{Domain}' (min strength: {MinStrength}, max depth: {MaxDepth})", domain, minStrength, maxDepth);

            const string cypher = @"
                MATCH path = (start:ModelComponent {userId: $userId})-[:CONNECTS_TO*2..$maxDepth]->(end:ModelComponent {userId: $userId})
                WHERE (start.name CONTAINS $domain OR start.type CONTAINS $domain)
                      AND (end.name CONTAINS $domain OR end.type CONTAINS $domain)
                      AND start <> end

                WITH path, nodes(path) as pathNodes, relationships(path) as pathRels,
                     start, end, length(path) as pathLength

                // Calculate circuit strength based on relationship types and component types
                WITH path, start, end, pathLength, pathNodes,
                     reduce(strength = 0.0, rel in pathRels |
                         strength + case rel.type
                             when 'INFLUENCES' then 1.0
                             when 'ACTIVATES' then 0.8
                             when 'CONTROLS' then 0.9
                             else 0.5
                         end
                     ) / pathLength as avgStrength

                WHERE avgStrength >= $minStrength

                // Look for circuits that form loops or have high interconnectivity
                OPTIONAL MATCH (end)-[:CONNECTS_TO*1..2]->(start)
                WITH path, start, end, pathLength, pathNodes, avgStrength,
                     case when end <> start then avgStrength * 1.5 else avgStrength end as circuitStrength

                RETURN DISTINCT
                    toString(randomUUID()) as circuitId,
                    start.id as startComponentId,
                    end.id as endComponentId,
                    [node in pathNodes | node.id] as componentIds,
                    circuitStrength,
                    pathLength,
                    $domain as domain,
                    datetime() as discoveredAt
                ORDER BY circuitStrength DESC
                LIMIT 100";

            using var session = _driver.AsyncSession();
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(cypher, new
                {
                    userId,
                    domain,
                    minStrength,
                    maxDepth
                });

                var circuits = new List<CircuitInfo>();
                await foreach (var record in cursor)
                {
                    var componentIdStrings = record["componentIds"].As<List<string>>();
                    var componentIds = componentIdStrings.Select(Guid.Parse).ToList();

                    circuits.Add(new CircuitInfo
                    {
                        CircuitId = Guid.Parse(record["circuitId"].ToString()!),
                        StartComponentId = Guid.Parse(record["startComponentId"].ToString()!),
                        EndComponentId = Guid.Parse(record["endComponentId"].ToString()!),
                        ComponentIds = componentIds,
                        CircuitStrength = record["circuitStrength"].As<double>(),
                        PathLength = record["pathLength"].As<int>(),
                        Domain = record["domain"].ToString()!,
                        DiscoveredAt = record["discoveredAt"].As<DateTime>()
                    });
                }

                return circuits;
            });

            _logger.LogDebug("Discovered {Count} circuits for domain '{Domain}'", result.Count(), domain);
            return result;
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error discovering circuits: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error discovering circuits");
            throw;
        }
    }

    /// <summary>
    /// Clear all circuit data for a specific project
    /// Removes components and relationships associated with the project
    /// </summary>
    public async Task ClearProjectCircuitsAsync(int projectId, string userId)
    {
        try
        {
            _logger.LogDebug("Clearing circuit data for project {ProjectId}", projectId);

            const string cypher = @"
                MATCH (c:ModelComponent {userId: $userId})
                WHERE c.projectId = $projectId OR c.modelId IN [
                    // Subquery to find models belonging to this project
                    MATCH (p:Project {id: $projectId})-[:CONTAINS]->(m:Model)
                    RETURN m.id
                ]
                DETACH DELETE c";

            using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(cypher, new
                {
                    projectId = projectId.ToString(),
                    userId
                });
            });

            _logger.LogDebug("Cleared circuit data for project {ProjectId}", projectId);
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error clearing project circuits {ProjectId}: {Message}", projectId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error clearing project circuits {ProjectId}", projectId);
            throw;
        }
    }

    /// <summary>
    /// Get circuit patterns for analysis and visualization
    /// Identifies recurring structural patterns in the component graph
    /// </summary>
    public async Task<IEnumerable<CircuitPatternInfo>> GetCircuitPatternsAsync(string userId, int limit = 50)
    {
        try
        {
            _logger.LogDebug("Getting circuit patterns for user {UserId} (limit: {Limit})", userId, limit);

            const string cypher = @"
                MATCH (c1:ModelComponent {userId: $userId})-[r1:CONNECTS_TO]->(c2:ModelComponent {userId: $userId})-[r2:CONNECTS_TO]->(c3:ModelComponent {userId: $userId})
                WHERE c1 <> c2 AND c2 <> c3 AND c1 <> c3

                // Group by pattern structure (component types and relationship types)
                WITH [c1.type, c2.type, c3.type] as componentTypes,
                     [r1.type, r2.type] as relationshipTypes,
                     collect([c1.id, c2.id, c3.id]) as instances,
                     avg(case r1.type when 'INFLUENCES' then 1.0 when 'ACTIVATES' then 0.8 else 0.5 end +
                         case r2.type when 'INFLUENCES' then 1.0 when 'ACTIVATES' then 0.8 else 0.5 end) as avgStrength

                // Calculate pattern statistics
                WITH componentTypes, relationshipTypes, instances, avgStrength,
                     size(instances) as frequency,
                     reduce(s = '', ct in componentTypes | s + '_' + ct) as patternType

                WHERE frequency >= 2 AND avgStrength > 0.5

                // Look for more complex patterns (4+ components)
                OPTIONAL MATCH (c1:ModelComponent {userId: $userId})-[:CONNECTS_TO*3..5]->(c4:ModelComponent {userId: $userId})
                WHERE c1.type + '_' + c4.type IN [ct[0] + '_' + ct[-1] | ct in componentTypes]

                WITH componentTypes, relationshipTypes, instances, avgStrength, frequency, patternType,
                     collect(DISTINCT c4.id) as extendedComponents

                RETURN DISTINCT
                    toString(randomUUID()) as patternId,
                    patternType,
                    instances[0] as componentIds,
                    componentTypes,
                    relationshipTypes,
                    avgStrength as patternStrength,
                    frequency,
                    'general' as domain

                ORDER BY frequency DESC, patternStrength DESC
                LIMIT $limit";

            using var session = _driver.AsyncSession();
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(cypher, new
                {
                    userId,
                    limit
                });

                var patterns = new List<CircuitPatternInfo>();
                await foreach (var record in cursor)
                {
                    var componentIdStrings = record["componentIds"].As<List<string>>();
                    var componentIds = componentIdStrings.Select(Guid.Parse).ToList();
                    var componentTypes = record["componentTypes"].As<List<string>>();
                    var relationshipTypes = record["relationshipTypes"].As<List<string>>();

                    patterns.Add(new CircuitPatternInfo
                    {
                        PatternId = Guid.Parse(record["patternId"].ToString()!),
                        PatternType = record["patternType"].ToString()!,
                        ComponentIds = componentIds,
                        ComponentTypes = componentTypes,
                        RelationshipTypes = relationshipTypes,
                        PatternStrength = record["patternStrength"].As<double>(),
                        Frequency = record["frequency"].As<int>(),
                        Domain = record["domain"].ToString()!
                    });
                }

                return patterns;
            });

            _logger.LogDebug("Found {Count} circuit patterns", result.Count());
            return result;
        }
        catch (Neo4jException ex)
        {
            _logger.LogError(ex, "Neo4j error getting circuit patterns: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting circuit patterns");
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

/// <summary>
/// Domain-relevant component information with relevance scoring
/// </summary>
public class DomainRelevantComponentInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
}

/// <summary>
/// Component importance analysis result from Neo4j
/// </summary>
public class ComponentImportanceInfo
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double ImportanceScore { get; set; }
    public double ActivationLevel { get; set; }
}

/// <summary>
/// Computational circuit information from Neo4j analysis
/// </summary>
public class CircuitInfo
{
    public Guid CircuitId { get; set; }
    public Guid StartComponentId { get; set; }
    public Guid EndComponentId { get; set; }
    public List<Guid> ComponentIds { get; set; } = new();
    public double CircuitStrength { get; set; }
    public int PathLength { get; set; }
    public string Domain { get; set; } = string.Empty;
    public DateTime DiscoveredAt { get; set; }
}

/// <summary>
/// Circuit pattern information from Neo4j analysis
/// </summary>
public class CircuitPatternInfo
{
    public Guid PatternId { get; set; }
    public string PatternType { get; set; } = string.Empty;
    public List<Guid> ComponentIds { get; set; } = new();
    public List<string> ComponentTypes { get; set; } = new();
    public List<string> RelationshipTypes { get; set; } = new();
    public double PatternStrength { get; set; }
    public int Frequency { get; set; }
    public string Domain { get; set; } = string.Empty;
}