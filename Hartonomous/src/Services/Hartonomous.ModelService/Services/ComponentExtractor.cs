using System.Text.Json;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Extracts neural network components from model structure
/// Handles component identification and classification
/// </summary>
public class ComponentExtractor
{
    public List<ExtractedComponent> ExtractComponents(ModelStructure structure, Guid modelId, string userId)
    {
        var components = new List<ExtractedComponent>();

        foreach (var tensor in structure.Tensors)
        {
            var component = new ExtractedComponent
            {
                ComponentId = Guid.NewGuid(),
                ModelId = modelId,
                Name = tensor.Name,
                Type = ClassifyComponent(tensor.Name),
                LayerInfo = ExtractLayerInfo(tensor.Name),
                Shape = tensor.Dimensions,
                DataType = tensor.Type.ToString(),
                ParameterCount = CalculateParameterCount(tensor.Dimensions),
                RelevanceScore = CalculateRelevance(tensor.Name, tensor.Dimensions),
                Description = GenerateDescription(tensor.Name),
                UserId = userId
            };

            components.Add(component);
        }

        return components;
    }

    private string ClassifyComponent(string tensorName)
    {
        var name = tensorName.ToLowerInvariant();

        if (name.Contains("embed") || name.Contains("wte"))
            return "embedding";
        if (name.Contains("attn") || name.Contains("attention"))
            return "attention";
        if (name.Contains("mlp") || name.Contains("ffn"))
            return "feedforward";
        if (name.Contains("norm") || name.Contains("ln"))
            return "normalization";
        if (name.Contains("output") || name.Contains("lm_head"))
            return "output_projection";
        if (name.Contains("pos"))
            return "positional";

        return "linear";
    }

    private LayerInfo ExtractLayerInfo(string tensorName)
    {
        var parts = tensorName.Split('.');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "layers" && int.TryParse(parts[i + 1], out int layerIndex))
            {
                return new LayerInfo
                {
                    Name = $"layer_{layerIndex}",
                    Type = "transformer_layer",
                    Index = layerIndex
                };
            }
        }

        if (tensorName.StartsWith("token_embd") || tensorName.StartsWith("embed"))
            return new LayerInfo { Name = "embedding", Type = "embedding_layer", Index = -1 };

        if (tensorName.Contains("output") || tensorName.Contains("lm_head"))
            return new LayerInfo { Name = "output", Type = "output_layer", Index = 1000 };

        return new LayerInfo { Name = "unknown", Type = "unknown_layer", Index = 0 };
    }

    private long CalculateParameterCount(long[] dimensions)
    {
        return dimensions.Aggregate(1L, (acc, dim) => acc * dim);
    }

    private double CalculateRelevance(string name, long[] dimensions)
    {
        var componentType = ClassifyComponent(name);
        var baseScore = componentType switch
        {
            "attention" => 0.95,
            "feedforward" => 0.85,
            "embedding" => 0.90,
            "output_projection" => 0.88,
            "normalization" => 0.75,
            _ => 0.60
        };

        var paramCount = CalculateParameterCount(dimensions);
        var sizeMultiplier = Math.Log10(paramCount) / 10.0;

        return Math.Min(1.0, baseScore + sizeMultiplier * 0.05);
    }

    private string GenerateDescription(string name)
    {
        var componentType = ClassifyComponent(name);
        return componentType switch
        {
            "attention" => $"Multi-head attention mechanism: {name}",
            "feedforward" => $"Feed-forward neural network: {name}",
            "embedding" => $"Token embedding matrix: {name}",
            "output_projection" => $"Output projection layer: {name}",
            "normalization" => $"Layer normalization: {name}",
            _ => $"Neural network component: {name}"
        };
    }
}

public class ExtractedComponent
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public LayerInfo LayerInfo { get; set; } = new();
    public long[] Shape { get; set; } = Array.Empty<long>();
    public string DataType { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public double RelevanceScore { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

public class LayerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Index { get; set; }
}