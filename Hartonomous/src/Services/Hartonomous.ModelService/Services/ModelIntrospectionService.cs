using Dapper;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;
using Hartonomous.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SemanticSearchRequestDto = Hartonomous.ModelQuery.DTOs.SemanticSearchRequestDto;
using SemanticSearchResultDto = Hartonomous.ModelQuery.DTOs.SemanticSearchResultDto;

namespace Hartonomous.ModelQuery.Services;

public class ModelIntrospectionService : IModelIntrospectionService
{
    private readonly IModelArchitectureRepository _architectureRepository;
    private readonly IModelWeightRepository _weightRepository;
    private readonly INeuralMapRepository _neuralMapRepository;
    private readonly IModelRepository _modelRepository;
    private readonly string _connectionString;
    private readonly ILogger<ModelIntrospectionService> _logger;

    public ModelIntrospectionService(
        IModelArchitectureRepository architectureRepository,
        IModelWeightRepository weightRepository,
        INeuralMapRepository neuralMapRepository,
        IModelRepository modelRepository,
        IConfiguration configuration,
        ILogger<ModelIntrospectionService> logger)
    {
        _architectureRepository = architectureRepository;
        _weightRepository = weightRepository;
        _neuralMapRepository = neuralMapRepository;
        _modelRepository = modelRepository;
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
        _logger = logger;
    }

    public async Task<ModelIntrospectionDto?> AnalyzeModelAsync(Guid modelId, string userId)
    {
        var model = await _modelRepository.GetModelByIdAsync(modelId, userId);
        if (model == null) return null;

        var architecture = await _architectureRepository.GetModelArchitectureAsync(modelId, userId);
        var weights = await _weightRepository.GetModelWeightsAsync(modelId, userId);

        // Calculate model statistics
        var totalParameters = weights.Sum(w => w.Shape.Aggregate(1L, (acc, dim) => acc * dim));
        var trainableParameters = totalParameters; // Assuming all parameters are trainable for now
        var modelSizeBytes = weights.Sum(w => w.SizeBytes);
        var modelSizeMB = modelSizeBytes / (1024.0 * 1024.0);

        // Count layer types
        var layerTypeCount = architecture?.Layers
            .GroupBy(l => l.LayerType)
            .ToDictionary(g => g.Key, g => g.Count()) ?? new Dictionary<string, int>();

        // Generate statistics
        var statistics = await GetModelStatisticsAsync(modelId, userId);

        // Determine capabilities
        var capabilities = await GetModelCapabilitiesAsync(modelId, userId);

        return new ModelIntrospectionDto(
            modelId,
            model.ModelName,
            totalParameters,
            trainableParameters,
            modelSizeMB,
            layerTypeCount,
            statistics,
            capabilities.ToList(),
            DateTime.UtcNow
        );
    }

    public async Task<IEnumerable<SemanticSearchResultDto>> SemanticSearchAsync(SemanticSearchRequestDto request, string userId)
    {
        var results = new List<SemanticSearchResultDto>();

        // For now, implement a basic keyword-based search
        // In a real implementation, this would use embeddings and vector similarity
        var searchTerms = request.Query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Search across different model components based on search type
        switch (request.SearchType.ToLower())
        {
            case "layers":
                await SearchLayersAsync(searchTerms, request, userId, results);
                break;
            case "weights":
                await SearchWeightsAsync(searchTerms, request, userId, results);
                break;
            case "nodes":
                await SearchNodesAsync(searchTerms, request, userId, results);
                break;
            case "all":
            default:
                await SearchLayersAsync(searchTerms, request, userId, results);
                await SearchWeightsAsync(searchTerms, request, userId, results);
                await SearchNodesAsync(searchTerms, request, userId, results);
                break;
        }

        return results
            .Where(r => r.SimilarityScore >= request.SimilarityThreshold)
            .OrderByDescending(r => r.SimilarityScore)
            .Take(request.MaxResults);
    }

    public async Task<Dictionary<string, object>> GetModelStatisticsAsync(Guid modelId, string userId)
    {
        var weights = await _weightRepository.GetModelWeightsAsync(modelId, userId);
        var architecture = await _architectureRepository.GetModelArchitectureAsync(modelId, userId);
        var graph = await _neuralMapRepository.GetModelGraphAsync(modelId, userId);

        var statistics = new Dictionary<string, object>
        {
            ["total_weights"] = weights.Count(),
            ["total_layers"] = architecture?.Layers.Count ?? 0,
            ["total_nodes"] = graph?.Nodes.Count ?? 0,
            ["total_edges"] = graph?.Edges.Count ?? 0,
            ["average_layer_weights"] = architecture?.Layers.Count > 0
                ? weights.Count() / (double)architecture.Layers.Count
                : 0,
            ["framework"] = architecture?.Framework ?? "unknown",
            ["data_types"] = weights.Select(w => w.DataType).Distinct().ToList(),
            ["layer_types"] = architecture?.Layers.Select(l => l.LayerType).Distinct().ToList() ?? new List<string>()
        };

        // Calculate weight distribution statistics
        if (weights.Any())
        {
            var sizes = weights.Select(w => w.SizeBytes).ToList();
            statistics["weight_size_stats"] = new Dictionary<string, object>
            {
                ["min"] = sizes.Min(),
                ["max"] = sizes.Max(),
                ["avg"] = sizes.Average(),
                ["total"] = sizes.Sum()
            };
        }

        return statistics;
    }

    public async Task<IEnumerable<string>> GetModelCapabilitiesAsync(Guid modelId, string userId)
    {
        var capabilities = new List<string>();
        var architecture = await _architectureRepository.GetModelArchitectureAsync(modelId, userId);

        if (architecture == null) return capabilities;

        // Analyze layer types to determine capabilities
        var layerTypes = architecture.Layers.Select(l => l.LayerType.ToLower()).ToHashSet();

        if (layerTypes.Contains("conv2d") || layerTypes.Contains("convolution"))
            capabilities.Add("computer_vision");

        if (layerTypes.Contains("lstm") || layerTypes.Contains("gru") || layerTypes.Contains("rnn"))
            capabilities.Add("sequence_modeling");

        if (layerTypes.Contains("attention") || layerTypes.Contains("transformer"))
            capabilities.Add("attention_mechanism");

        if (layerTypes.Contains("dense") || layerTypes.Contains("linear"))
            capabilities.Add("fully_connected");

        if (layerTypes.Contains("dropout"))
            capabilities.Add("regularization");

        if (layerTypes.Contains("batchnorm") || layerTypes.Contains("layernorm"))
            capabilities.Add("normalization");

        if (layerTypes.Contains("embedding"))
            capabilities.Add("embedding");

        // Analyze framework
        if (architecture.Framework.ToLower().Contains("pytorch"))
            capabilities.Add("pytorch_model");
        else if (architecture.Framework.ToLower().Contains("tensorflow"))
            capabilities.Add("tensorflow_model");

        return capabilities;
    }

    public async Task<ModelComparisonDto?> CompareModelsAsync(Guid modelAId, Guid modelBId, string comparisonType, string userId)
    {
        var modelA = await _modelRepository.GetModelByIdAsync(modelAId, userId);
        var modelB = await _modelRepository.GetModelByIdAsync(modelBId, userId);

        if (modelA == null || modelB == null) return null;

        var architectureA = await _architectureRepository.GetModelArchitectureAsync(modelAId, userId);
        var architectureB = await _architectureRepository.GetModelArchitectureAsync(modelBId, userId);
        var weightsA = await _weightRepository.GetModelWeightsAsync(modelAId, userId);
        var weightsB = await _weightRepository.GetModelWeightsAsync(modelBId, userId);

        var differences = new Dictionary<string, object>();
        var similarities = new Dictionary<string, double>();
        var commonLayers = new List<string>();
        var uniqueLayers = new List<string>();

        // Compare architectures
        if (architectureA != null && architectureB != null)
        {
            var layersA = architectureA.Layers.Select(l => l.LayerName).ToHashSet();
            var layersB = architectureB.Layers.Select(l => l.LayerName).ToHashSet();

            commonLayers = layersA.Intersect(layersB).ToList();
            uniqueLayers = layersA.Except(layersB).Union(layersB.Except(layersA)).ToList();

            differences["layer_count_diff"] = architectureA.Layers.Count - architectureB.Layers.Count;
            differences["framework_diff"] = architectureA.Framework != architectureB.Framework;

            // Calculate layer similarity
            var totalLayers = layersA.Union(layersB).Count();
            similarities["layer_overlap"] = totalLayers > 0 ? (double)commonLayers.Count / totalLayers : 0.0;
        }

        // Compare weights
        var totalParamsA = weightsA.Sum(w => w.Shape.Aggregate(1L, (acc, dim) => acc * dim));
        var totalParamsB = weightsB.Sum(w => w.Shape.Aggregate(1L, (acc, dim) => acc * dim));
        differences["parameter_count_diff"] = totalParamsA - totalParamsB;

        var sizeA = weightsA.Sum(w => w.SizeBytes);
        var sizeB = weightsB.Sum(w => w.SizeBytes);
        differences["model_size_diff_mb"] = (sizeA - sizeB) / (1024.0 * 1024.0);

        // Calculate parameter similarity
        if (totalParamsA > 0 && totalParamsB > 0)
        {
            var paramRatio = (double)Math.Min(totalParamsA, totalParamsB) / Math.Max(totalParamsA, totalParamsB);
            similarities["parameter_similarity"] = paramRatio;
        }

        return new ModelComparisonDto(
            modelAId,
            modelBId,
            comparisonType,
            differences,
            similarities,
            commonLayers,
            uniqueLayers,
            DateTime.UtcNow
        );
    }

    private async Task SearchLayersAsync(string[] searchTerms, SemanticSearchRequestDto request, string userId, List<SemanticSearchResultDto> results)
    {
        const string sql = @"
            SELECT l.LayerId, l.LayerName, l.LayerType, l.Description, l.Parameters, l.ModelId, m.ModelName
            FROM dbo.ModelLayers l
            INNER JOIN dbo.Models m ON l.ModelId = m.ModelId
            WHERE m.UserId = @UserId
            AND (l.LayerName LIKE @SearchPattern OR l.Description LIKE @SearchPattern OR l.LayerType LIKE @SearchPattern)
            ORDER BY l.LayerName;";

        var searchPattern = $"%{string.Join("%", searchTerms)}%";

        using var connection = new SqlConnection(_connectionString);
        var layers = await connection.QueryAsync(sql, new { UserId = userId, SearchPattern = searchPattern });

        foreach (var layer in layers)
        {
            var similarity = CalculateTextSimilarity($"{layer.LayerName} {layer.Description} {layer.LayerType}", searchTerms);
            if (similarity > 0.1)
            {
                results.Add(new SemanticSearchResultDto(
                    layer.LayerId,
                    "layer",
                    layer.LayerName,
                    similarity,
                    new Dictionary<string, object>
                    {
                        { "layerType", layer.LayerType },
                        { "parameters", layer.Parameters },
                        { "modelId", layer.ModelId },
                        { "modelName", layer.ModelName }
                    },
                    layer.Description
                ));
            }
        }
    }

    private async Task SearchWeightsAsync(string[] searchTerms, SemanticSearchRequestDto request, string userId, List<SemanticSearchResultDto> results)
    {
        const string sql = @"
            SELECT w.WeightId, w.WeightName, w.Description, w.Shape, w.LayerId, l.LayerName, m.ModelId, m.ModelName
            FROM dbo.ModelWeights w
            INNER JOIN dbo.ModelLayers l ON w.LayerId = l.LayerId
            INNER JOIN dbo.Models m ON l.ModelId = m.ModelId
            WHERE m.UserId = @UserId
            AND (w.WeightName LIKE @SearchPattern OR w.Description LIKE @SearchPattern)
            ORDER BY w.WeightName;";

        var searchPattern = $"%{string.Join("%", searchTerms)}%";

        using var connection = new SqlConnection(_connectionString);
        var weights = await connection.QueryAsync(sql, new { UserId = userId, SearchPattern = searchPattern });

        foreach (var weight in weights)
        {
            var similarity = CalculateTextSimilarity($"{weight.WeightName} {weight.Description}", searchTerms);
            if (similarity > 0.1)
            {
                results.Add(new SemanticSearchResultDto(
                    weight.WeightId,
                    "weight",
                    weight.WeightName,
                    similarity,
                    new Dictionary<string, object>
                    {
                        { "layerId", weight.LayerId },
                        { "layerName", weight.LayerName },
                        { "shape", weight.Shape },
                        { "modelId", weight.ModelId },
                        { "modelName", weight.ModelName }
                    },
                    weight.Description
                ));
            }
        }
    }

    private async Task SearchNodesAsync(string[] searchTerms, SemanticSearchRequestDto request, string userId, List<SemanticSearchResultDto> results)
    {
        const string sql = @"
            SELECT n.NodeId, n.NodeName, n.NodeType, n.Description, n.LayerId, l.LayerName, m.ModelId, m.ModelName
            FROM dbo.ModelNodes n
            INNER JOIN dbo.ModelLayers l ON n.LayerId = l.LayerId
            INNER JOIN dbo.Models m ON l.ModelId = m.ModelId
            WHERE m.UserId = @UserId
            AND (n.NodeName LIKE @SearchPattern OR n.Description LIKE @SearchPattern OR n.NodeType LIKE @SearchPattern)
            ORDER BY n.NodeName;";

        var searchPattern = $"%{string.Join("%", searchTerms)}%";

        using var connection = new SqlConnection(_connectionString);
        var nodes = await connection.QueryAsync(sql, new { UserId = userId, SearchPattern = searchPattern });

        foreach (var node in nodes)
        {
            var similarity = CalculateTextSimilarity($"{node.NodeName} {node.Description} {node.NodeType}", searchTerms);
            if (similarity > 0.1)
            {
                results.Add(new SemanticSearchResultDto(
                    node.NodeId,
                    "node",
                    node.NodeName,
                    similarity,
                    new Dictionary<string, object>
                    {
                        { "nodeType", node.NodeType },
                        { "layerId", node.LayerId },
                        { "layerName", node.LayerName },
                        { "modelId", node.ModelId },
                        { "modelName", node.ModelName }
                    },
                    node.Description
                ));
            }
        }
    }

    private double CalculateTextSimilarity(string text, string[] searchTerms)
    {
        if (string.IsNullOrEmpty(text)) return 0.0;

        var lowerText = text.ToLower();
        var matchCount = searchTerms.Count(term => lowerText.Contains(term));
        return (double)matchCount / searchTerms.Length;
    }
}