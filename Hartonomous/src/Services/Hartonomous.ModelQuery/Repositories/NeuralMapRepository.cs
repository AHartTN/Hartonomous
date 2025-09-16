using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;
using Neo4j.Driver;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.ModelQuery.Repositories;

public class NeuralMapRepository : INeuralMapRepository, IDisposable
{
    private readonly IDriver _driver;
    private bool _disposed = false;

    public NeuralMapRepository(IConfiguration configuration)
    {
        var uri = configuration["Neo4j:Uri"] ?? "bolt://192.168.1.2:7687";
        var username = configuration["Neo4j:Username"] ?? "neo4j";
        var password = configuration["Neo4j:Password"] ?? throw new InvalidOperationException("Neo4j password not configured");

        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
    }

    public async Task<NeuralMapGraphDto?> GetModelGraphAsync(Guid modelId, string userId)
    {
        const string cypher = @"
            MATCH (m:Model {modelId: $modelId, userId: $userId})
            OPTIONAL MATCH (m)-[:CONTAINS]->(n:Node)
            OPTIONAL MATCH (n)-[r:CONNECTS]->(n2:Node)
            WHERE n2.modelId = $modelId AND n2.userId = $userId
            RETURN m, collect(DISTINCT n) as nodes, collect(DISTINCT r) as edges";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { modelId = modelId.ToString(), userId });

        var record = await result.PeekAsync();
        if (record == null) return null;

        var model = record["m"].As<INode>();
        var nodes = record["nodes"].As<List<INode>>();
        var edges = record["edges"].As<List<IRelationship>>();

        var nodeList = nodes.Select(n => new NeuralMapNodeDto(
            Guid.Parse(n.Properties["nodeId"].As<string>()),
            n.Properties["nodeType"].As<string>(),
            n.Properties["name"].As<string>(),
            n.Properties.Where(p => !new[] { "nodeId", "nodeType", "name", "modelId", "userId", "createdAt", "updatedAt" }.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value),
            DateTime.Parse(n.Properties["createdAt"].As<string>()),
            DateTime.Parse(n.Properties["updatedAt"].As<string>())
        )).ToList();

        var edgeList = edges.Select(e => new NeuralMapEdgeDto(
            Guid.Parse(e.Properties["edgeId"].As<string>()),
            Guid.Parse(e.Properties["sourceNodeId"].As<string>()),
            Guid.Parse(e.Properties["targetNodeId"].As<string>()),
            e.Type,
            e.Properties["weight"].As<double>(),
            e.Properties.Where(p => !new[] { "edgeId", "sourceNodeId", "targetNodeId", "weight", "createdAt" }.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value),
            DateTime.Parse(e.Properties["createdAt"].As<string>())
        )).ToList();

        var metadata = model.Properties.Where(p => !new[] { "modelId", "modelName", "version", "userId" }.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);

        return new NeuralMapGraphDto(
            modelId,
            model.Properties["modelName"].As<string>(),
            model.Properties["version"].As<string>(),
            nodeList,
            edgeList,
            metadata
        );
    }

    public async Task<IEnumerable<NeuralMapNodeDto>> GetNodesAsync(Guid modelId, string userId)
    {
        const string cypher = @"
            MATCH (n:Node {modelId: $modelId, userId: $userId})
            RETURN n
            ORDER BY n.name";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { modelId = modelId.ToString(), userId });

        var nodes = new List<NeuralMapNodeDto>();
        await result.ForEachAsync(record =>
        {
            var node = record["n"].As<INode>();
            nodes.Add(new NeuralMapNodeDto(
                Guid.Parse(node.Properties["nodeId"].As<string>()),
                node.Properties["nodeType"].As<string>(),
                node.Properties["name"].As<string>(),
                node.Properties.Where(p => !new[] { "nodeId", "nodeType", "name", "modelId", "userId", "createdAt", "updatedAt" }.Contains(p.Key))
                    .ToDictionary(p => p.Key, p => p.Value),
                DateTime.Parse(node.Properties["createdAt"].As<string>()),
                DateTime.Parse(node.Properties["updatedAt"].As<string>())
            ));
        });

        return nodes;
    }

    public async Task<IEnumerable<NeuralMapEdgeDto>> GetEdgesAsync(Guid modelId, string userId)
    {
        const string cypher = @"
            MATCH (n1:Node {modelId: $modelId, userId: $userId})-[r:CONNECTS]->(n2:Node {modelId: $modelId, userId: $userId})
            RETURN r";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { modelId = modelId.ToString(), userId });

        var edges = new List<NeuralMapEdgeDto>();
        await result.ForEachAsync(record =>
        {
            var edge = record["r"].As<IRelationship>();
            edges.Add(new NeuralMapEdgeDto(
                Guid.Parse(edge.Properties["edgeId"].As<string>()),
                Guid.Parse(edge.Properties["sourceNodeId"].As<string>()),
                Guid.Parse(edge.Properties["targetNodeId"].As<string>()),
                edge.Type,
                edge.Properties["weight"].As<double>(),
                edge.Properties.Where(p => !new[] { "edgeId", "sourceNodeId", "targetNodeId", "weight", "createdAt" }.Contains(p.Key))
                    .ToDictionary(p => p.Key, p => p.Value),
                DateTime.Parse(edge.Properties["createdAt"].As<string>())
            ));
        });

        return edges;
    }

    public async Task<Guid> CreateNodeAsync(Guid modelId, string nodeType, string name, Dictionary<string, object> properties, string userId)
    {
        var nodeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        const string cypher = @"
            MATCH (m:Model {modelId: $modelId, userId: $userId})
            CREATE (n:Node $nodeProperties)
            CREATE (m)-[:CONTAINS]->(n)
            RETURN n.nodeId";

        var nodeProperties = new Dictionary<string, object>(properties)
        {
            ["nodeId"] = nodeId.ToString(),
            ["modelId"] = modelId.ToString(),
            ["userId"] = userId,
            ["nodeType"] = nodeType,
            ["name"] = name,
            ["createdAt"] = now.ToString("O"),
            ["updatedAt"] = now.ToString("O")
        };

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { modelId = modelId.ToString(), userId, nodeProperties });

        var record = await result.PeekAsync();
        if (record == null)
        {
            throw new InvalidOperationException("Failed to create node - model not found");
        }

        return nodeId;
    }

    public async Task<Guid> CreateEdgeAsync(Guid sourceNodeId, Guid targetNodeId, string relationType, double weight, Dictionary<string, object> properties, string userId)
    {
        var edgeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        const string cypher = @"
            MATCH (n1:Node {nodeId: $sourceNodeId, userId: $userId})
            MATCH (n2:Node {nodeId: $targetNodeId, userId: $userId})
            WHERE n1.modelId = n2.modelId
            CREATE (n1)-[r:CONNECTS $edgeProperties]->(n2)
            RETURN r.edgeId";

        var edgeProperties = new Dictionary<string, object>(properties)
        {
            ["edgeId"] = edgeId.ToString(),
            ["sourceNodeId"] = sourceNodeId.ToString(),
            ["targetNodeId"] = targetNodeId.ToString(),
            ["weight"] = weight,
            ["createdAt"] = now.ToString("O")
        };

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { sourceNodeId = sourceNodeId.ToString(), targetNodeId = targetNodeId.ToString(), userId, edgeProperties });

        var record = await result.PeekAsync();
        if (record == null)
        {
            throw new InvalidOperationException("Failed to create edge - nodes not found or not in same model");
        }

        return edgeId;
    }

    public async Task<bool> DeleteNodeAsync(Guid nodeId, string userId)
    {
        const string cypher = @"
            MATCH (n:Node {nodeId: $nodeId, userId: $userId})
            DETACH DELETE n
            RETURN count(n) as deleted";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { nodeId = nodeId.ToString(), userId });

        var record = await result.PeekAsync();
        return record?["deleted"].As<int>() > 0;
    }

    public async Task<bool> DeleteEdgeAsync(Guid edgeId, string userId)
    {
        const string cypher = @"
            MATCH (n1:Node {userId: $userId})-[r:CONNECTS {edgeId: $edgeId}]->(n2:Node {userId: $userId})
            DELETE r
            RETURN count(r) as deleted";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { edgeId = edgeId.ToString(), userId });

        var record = await result.PeekAsync();
        return record?["deleted"].As<int>() > 0;
    }

    public async Task<bool> UpdateNodePropertiesAsync(Guid nodeId, Dictionary<string, object> properties, string userId)
    {
        var updatedProperties = new Dictionary<string, object>(properties)
        {
            ["updatedAt"] = DateTime.UtcNow.ToString("O")
        };

        const string cypher = @"
            MATCH (n:Node {nodeId: $nodeId, userId: $userId})
            SET n += $properties
            RETURN count(n) as updated";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { nodeId = nodeId.ToString(), userId, properties = updatedProperties });

        var record = await result.PeekAsync();
        return record?["updated"].As<int>() > 0;
    }

    public async Task<bool> UpdateEdgePropertiesAsync(Guid edgeId, Dictionary<string, object> properties, string userId)
    {
        const string cypher = @"
            MATCH (n1:Node {userId: $userId})-[r:CONNECTS {edgeId: $edgeId}]->(n2:Node {userId: $userId})
            SET r += $properties
            RETURN count(r) as updated";

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(cypher, new { edgeId = edgeId.ToString(), userId, properties });

        var record = await result.PeekAsync();
        return record?["updated"].As<int>() > 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _driver?.Dispose();
            _disposed = true;
        }
    }
}