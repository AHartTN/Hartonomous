using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Stores model component relationships in Neo4j graph database
/// </summary>
public class GraphStorageService
{
    private readonly IGraphService _graphService;

    public GraphStorageService(IGraphService graphService)
    {
        _graphService = graphService;
    }

    public async Task StoreModelGraphAsync(Guid modelId, List<ExtractedComponent> components, string userId)
    {
        // Group components by layer
        var layerGroups = components
            .GroupBy(c => c.LayerInfo.Index)
            .OrderBy(g => g.Key)
            .ToList();

        // Create component nodes
        foreach (var component in components)
        {
            var properties = new Dictionary<string, object>
            {
                ["componentType"] = component.Type,
                ["parameterCount"] = component.ParameterCount,
                ["relevanceScore"] = component.RelevanceScore,
                ["dataType"] = component.DataType,
                ["shape"] = string.Join("x", component.Shape),
                ["layerIndex"] = component.LayerInfo.Index
            };

            await _graphService.CreateModelComponentAsync(
                component.ComponentId,
                modelId,
                component.Name,
                component.Type,
                properties,
                userId);
        }

        // Create intra-layer relationships
        foreach (var layerGroup in layerGroups)
        {
            var layerComponents = layerGroup.ToList();

            // Connect components within same layer
            for (int i = 0; i < layerComponents.Count - 1; i++)
            {
                await _graphService.CreateComponentRelationshipAsync(
                    layerComponents[i].ComponentId,
                    layerComponents[i + 1].ComponentId,
                    "SAME_LAYER",
                    1.0,
                    new Dictionary<string, object> { ["layer"] = layerGroup.Key },
                    userId);
            }
        }

        // Create inter-layer relationships (feed forward connections)
        for (int i = 0; i < layerGroups.Count - 1; i++)
        {
            var currentLayer = layerGroups[i].ToList();
            var nextLayer = layerGroups[i + 1].ToList();

            // Connect layers (simplified - in reality would be more complex)
            foreach (var currentComponent in currentLayer)
            {
                foreach (var nextComponent in nextLayer)
                {
                    if (ShouldConnect(currentComponent, nextComponent))
                    {
                        await _graphService.CreateComponentRelationshipAsync(
                            currentComponent.ComponentId,
                            nextComponent.ComponentId,
                            "FEEDS_INTO",
                            CalculateConnectionWeight(currentComponent, nextComponent),
                            new Dictionary<string, object>
                            {
                                ["fromLayer"] = currentComponent.LayerInfo.Index,
                                ["toLayer"] = nextComponent.LayerInfo.Index
                            },
                            userId);
                    }
                }
            }
        }
    }

    private bool ShouldConnect(ExtractedComponent from, ExtractedComponent to)
    {
        // Simple heuristic for component connections
        if (from.Type == "attention" && to.Type == "feedforward") return true;
        if (from.Type == "feedforward" && to.Type == "normalization") return true;
        if (from.Type == "normalization" && to.Type == "attention") return true;
        if (from.Type == "embedding" && to.Type == "attention") return true;

        return false;
    }

    private double CalculateConnectionWeight(ExtractedComponent from, ExtractedComponent to)
    {
        // Weight based on component types and parameter counts
        var baseWeight = (from.Type, to.Type) switch
        {
            ("attention", "feedforward") => 0.9,
            ("feedforward", "normalization") => 0.8,
            ("normalization", "attention") => 0.85,
            ("embedding", "attention") => 0.95,
            _ => 0.5
        };

        // Adjust based on relative parameter counts
        var paramRatio = Math.Min(from.ParameterCount, to.ParameterCount) /
                        (double)Math.Max(from.ParameterCount, to.ParameterCount);

        return baseWeight * (0.5 + 0.5 * paramRatio);
    }
}