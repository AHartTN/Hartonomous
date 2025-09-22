using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Real model ingestion service for Ollama GGUF models
/// Handles actual file parsing, component extraction, and database storage
/// </summary>
public class OllamaModelIngestionService
{
    private readonly string _connectionString;
    private readonly IVectorService _vectorService;
    private readonly IGraphService _graphService;
    private readonly ILogger<OllamaModelIngestionService> _logger;

    public OllamaModelIngestionService(
        IConfiguration configuration,
        IVectorService vectorService,
        IGraphService graphService,
        ILogger<OllamaModelIngestionService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentException("DefaultConnection is required");
        _vectorService = vectorService;
        _graphService = graphService;
        _logger = logger;
    }

    /// <summary>
    /// Ingest a model from Ollama model directory
    /// </summary>
    public async Task<ModelIngestionResult> IngestOllamaModelAsync(string modelPath, string modelName, string userId)
    {
        try
        {
            _logger.LogInformation("Starting ingestion of Ollama model: {ModelName} from {ModelPath}", modelName, modelPath);

            // Validate model file exists
            if (!File.Exists(modelPath))
            {
                return new ModelIngestionResult
                {
                    Success = false,
                    ErrorMessage = $"Model file not found: {modelPath}"
                };
            }

            var modelId = Guid.NewGuid();

            // Step 1: Parse GGUF file structure
            var modelStructure = await ParseGGUFFileAsync(modelPath);
            if (modelStructure == null)
            {
                return new ModelIngestionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse GGUF model structure"
                };
            }

            // Step 2: Store model in database
            await StoreModelInDatabaseAsync(modelId, modelName, modelPath, modelStructure, userId);

            // Step 3: Extract and store components
            var components = await ExtractModelComponentsAsync(modelId, modelStructure, userId);

            // Step 4: Generate embeddings for components
            await GenerateComponentEmbeddingsAsync(components, userId);

            // Step 5: Store graph relationships in Neo4j
            await StoreGraphRelationshipsAsync(modelId, components, userId);

            _logger.LogInformation("Successfully ingested model {ModelName} with {ComponentCount} components",
                modelName, components.Count);

            return new ModelIngestionResult
            {
                Success = true,
                ModelId = modelId,
                ComponentsExtracted = components.Count,
                Message = $"Model {modelName} ingested successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting model {ModelName}", modelName);
            return new ModelIngestionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Parse GGUF file to extract model structure
    /// </summary>
    private async Task<GGUFModelStructure?> ParseGGUFFileAsync(string filePath)
    {
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fileStream);

            // Read GGUF magic number
            var magic = reader.ReadBytes(4);
            if (System.Text.Encoding.ASCII.GetString(magic) != "GGUF")
            {
                _logger.LogError("Invalid GGUF file format - missing magic number");
                return null;
            }

            // Read version
            var version = reader.ReadUInt32();
            _logger.LogDebug("GGUF version: {Version}", version);

            // Read tensor count and metadata count
            var tensorCount = reader.ReadUInt64();
            var metadataCount = reader.ReadUInt64();

            _logger.LogInformation("GGUF file contains {TensorCount} tensors and {MetadataCount} metadata entries",
                tensorCount, metadataCount);

            var structure = new GGUFModelStructure
            {
                Version = version,
                TensorCount = tensorCount,
                MetadataCount = metadataCount,
                Metadata = new Dictionary<string, object>(),
                Tensors = new List<GGUFTensor>()
            };

            // Read metadata
            for (ulong i = 0; i < metadataCount; i++)
            {
                var key = ReadGGUFString(reader);
                var valueType = (GGUFValueType)reader.ReadUInt32();
                var value = ReadGGUFValue(reader, valueType);
                structure.Metadata[key] = value;
            }

            // Read tensor info
            for (ulong i = 0; i < tensorCount; i++)
            {
                var tensor = ReadGGUFTensor(reader);
                structure.Tensors.Add(tensor);
            }

            return structure;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing GGUF file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Store model information in SQL Server database
    /// </summary>
    private async Task StoreModelInDatabaseAsync(Guid modelId, string modelName, string modelPath,
        GGUFModelStructure structure, string userId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand("sp_IngestModel", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@ModelName", modelName);
        command.Parameters.AddWithValue("@ModelPath", modelPath);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.Add("@ModelId", System.Data.SqlDbType.UniqueIdentifier)
            .Direction = System.Data.ParameterDirection.Output;

        await command.ExecuteNonQueryAsync();

        var returnedModelId = (Guid)command.Parameters["@ModelId"].Value;
        _logger.LogDebug("Model stored in database with ID: {ModelId}", returnedModelId);
    }

    /// <summary>
    /// Extract components from model structure
    /// </summary>
    private async Task<List<ModelComponent>> ExtractModelComponentsAsync(Guid modelId,
        GGUFModelStructure structure, string userId)
    {
        var components = new List<ModelComponent>();

        // Extract components from tensors
        foreach (var tensor in structure.Tensors)
        {
            var componentType = DetermineComponentType(tensor.Name);
            var layerInfo = ExtractLayerInfo(tensor.Name);

            var component = new ModelComponent
            {
                ComponentId = Guid.NewGuid(),
                ModelId = modelId,
                ComponentName = tensor.Name,
                ComponentType = componentType,
                LayerIndex = layerInfo.LayerIndex,
                LayerName = layerInfo.LayerName,
                Shape = tensor.Dimensions,
                DataType = tensor.Type.ToString(),
                ParameterCount = CalculateParameterCount(tensor.Dimensions),
                RelevanceScore = CalculateInitialRelevanceScore(componentType, tensor.Name),
                FunctionalDescription = GenerateComponentDescription(componentType, tensor.Name),
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            components.Add(component);
        }

        // Store components in database
        await StoreComponentsInDatabaseAsync(components);

        _logger.LogInformation("Extracted {ComponentCount} components from model", components.Count);
        return components;
    }

    /// <summary>
    /// Generate embeddings for model components
    /// </summary>
    private async Task GenerateComponentEmbeddingsAsync(List<ModelComponent> components, string userId)
    {
        foreach (var component in components)
        {
            // Generate embedding from component description and type
            var embeddingText = $"{component.ComponentType} {component.ComponentName} {component.FunctionalDescription}";

            // For now, generate a simple hash-based embedding
            // In production, this would call a real embedding service
            var embedding = GenerateSimpleEmbedding(embeddingText);

            await _vectorService.InsertEmbeddingAsync(
                component.ComponentId,
                component.ModelId,
                embedding,
                component.ComponentType,
                component.FunctionalDescription,
                userId);
        }

        _logger.LogInformation("Generated embeddings for {ComponentCount} components", components.Count);
    }

    /// <summary>
    /// Store component relationships in Neo4j
    /// </summary>
    private async Task StoreGraphRelationshipsAsync(Guid modelId, List<ModelComponent> components, string userId)
    {
        // Create nodes for each component
        foreach (var component in components)
        {
            var properties = new Dictionary<string, object>
            {
                ["parameterCount"] = component.ParameterCount,
                ["dataType"] = component.DataType,
                ["relevanceScore"] = component.RelevanceScore
            };

            await _graphService.CreateModelComponentAsync(
                component.ComponentId,
                modelId,
                component.ComponentName,
                component.ComponentType,
                properties,
                userId);
        }

        // Create relationships between components in the same layer
        var layerGroups = components.GroupBy(c => c.LayerName);
        foreach (var layerGroup in layerGroups)
        {
            var layerComponents = layerGroup.ToList();
            for (int i = 0; i < layerComponents.Count - 1; i++)
            {
                await _graphService.CreateComponentRelationshipAsync(
                    layerComponents[i].ComponentId,
                    layerComponents[i + 1].ComponentId,
                    "FEEDS_INTO",
                    1.0,
                    new Dictionary<string, object> { ["layer"] = layerGroup.Key },
                    userId);
            }
        }

        _logger.LogInformation("Stored graph relationships for model components");
    }

    #region Helper Methods

    private string ReadGGUFString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();
        var bytes = reader.ReadBytes((int)length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private object ReadGGUFValue(BinaryReader reader, GGUFValueType type)
    {
        return type switch
        {
            GGUFValueType.UINT8 => reader.ReadByte(),
            GGUFValueType.INT8 => reader.ReadSByte(),
            GGUFValueType.UINT16 => reader.ReadUInt16(),
            GGUFValueType.INT16 => reader.ReadInt16(),
            GGUFValueType.UINT32 => reader.ReadUInt32(),
            GGUFValueType.INT32 => reader.ReadInt32(),
            GGUFValueType.UINT64 => reader.ReadUInt64(),
            GGUFValueType.INT64 => reader.ReadInt64(),
            GGUFValueType.FLOAT32 => reader.ReadSingle(),
            GGUFValueType.FLOAT64 => reader.ReadDouble(),
            GGUFValueType.BOOL => reader.ReadBoolean(),
            GGUFValueType.STRING => ReadGGUFString(reader),
            _ => throw new NotSupportedException($"GGUF value type {type} not supported")
        };
    }

    private GGUFTensor ReadGGUFTensor(BinaryReader reader)
    {
        var name = ReadGGUFString(reader);
        var dimensionCount = reader.ReadUInt32();
        var dimensions = new long[dimensionCount];

        for (uint i = 0; i < dimensionCount; i++)
        {
            dimensions[i] = reader.ReadInt64();
        }

        var type = (GGUFDataType)reader.ReadUInt32();
        var offset = reader.ReadUInt64();

        return new GGUFTensor
        {
            Name = name,
            Dimensions = dimensions,
            Type = type,
            Offset = offset
        };
    }

    private string DetermineComponentType(string tensorName)
    {
        var nameLower = tensorName.ToLowerInvariant();

        if (nameLower.Contains("embed") || nameLower.Contains("wte"))
            return "embedding";
        if (nameLower.Contains("attn") || nameLower.Contains("attention"))
            return "attention";
        if (nameLower.Contains("mlp") || nameLower.Contains("ffn"))
            return "feedforward";
        if (nameLower.Contains("norm") || nameLower.Contains("ln"))
            return "normalization";
        if (nameLower.Contains("head") || nameLower.Contains("lm_head"))
            return "output_head";

        return "unknown";
    }

    private (int LayerIndex, string LayerName) ExtractLayerInfo(string tensorName)
    {
        // Extract layer number from tensor name (e.g., "model.layers.0.attention.wq")
        var parts = tensorName.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "layers" && i + 1 < parts.Length && int.TryParse(parts[i + 1], out int layerIndex))
            {
                return (layerIndex, $"layer_{layerIndex}");
            }
        }

        return (0, "layer_0");
    }

    private long CalculateParameterCount(long[] dimensions)
    {
        return dimensions.Aggregate(1L, (acc, dim) => acc * dim);
    }

    private double CalculateInitialRelevanceScore(string componentType, string name)
    {
        // Simple relevance scoring based on component type
        return componentType switch
        {
            "attention" => 0.9,
            "feedforward" => 0.8,
            "embedding" => 0.7,
            "output_head" => 0.85,
            "normalization" => 0.6,
            _ => 0.5
        };
    }

    private string GenerateComponentDescription(string componentType, string name)
    {
        return componentType switch
        {
            "attention" => $"Attention mechanism component: {name}",
            "feedforward" => $"Feed-forward network component: {name}",
            "embedding" => $"Token embedding component: {name}",
            "output_head" => $"Output projection head: {name}",
            "normalization" => $"Normalization layer: {name}",
            _ => $"Neural network component: {name}"
        };
    }

    private float[] GenerateSimpleEmbedding(string text)
    {
        // Simple hash-based embedding for demonstration
        // In production, use a real embedding model
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[1536];

        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        // Normalize to unit vector
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= (float)magnitude;
        }

        return embedding;
    }

    private async Task StoreComponentsInDatabaseAsync(List<ModelComponent> components)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var component in components)
        {
            var sql = @"
                INSERT INTO ModelComponents (
                    ComponentId, ModelId, ComponentName, ComponentType, LayerIndex, LayerName,
                    Shape, DataType, ParameterCount, RelevanceScore, FunctionalDescription,
                    UserId, CreatedAt
                )
                VALUES (
                    @ComponentId, @ModelId, @ComponentName, @ComponentType, @LayerIndex, @LayerName,
                    @Shape, @DataType, @ParameterCount, @RelevanceScore, @FunctionalDescription,
                    @UserId, @CreatedAt
                )";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ComponentId", component.ComponentId);
            command.Parameters.AddWithValue("@ModelId", component.ModelId);
            command.Parameters.AddWithValue("@ComponentName", component.ComponentName);
            command.Parameters.AddWithValue("@ComponentType", component.ComponentType);
            command.Parameters.AddWithValue("@LayerIndex", component.LayerIndex);
            command.Parameters.AddWithValue("@LayerName", component.LayerName);
            command.Parameters.AddWithValue("@Shape", JsonSerializer.Serialize(component.Shape));
            command.Parameters.AddWithValue("@DataType", component.DataType);
            command.Parameters.AddWithValue("@ParameterCount", component.ParameterCount);
            command.Parameters.AddWithValue("@RelevanceScore", component.RelevanceScore);
            command.Parameters.AddWithValue("@FunctionalDescription", component.FunctionalDescription);
            command.Parameters.AddWithValue("@UserId", component.UserId);
            command.Parameters.AddWithValue("@CreatedAt", component.CreatedAt);

            await command.ExecuteNonQueryAsync();
        }
    }

    #endregion
}

#region Data Models

public class GGUFModelStructure
{
    public uint Version { get; set; }
    public ulong TensorCount { get; set; }
    public ulong MetadataCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<GGUFTensor> Tensors { get; set; } = new();
}

public class GGUFTensor
{
    public string Name { get; set; } = string.Empty;
    public long[] Dimensions { get; set; } = Array.Empty<long>();
    public GGUFDataType Type { get; set; }
    public ulong Offset { get; set; }
}

public enum GGUFValueType : uint
{
    UINT8 = 0,
    INT8 = 1,
    UINT16 = 2,
    INT16 = 3,
    UINT32 = 4,
    INT32 = 5,
    UINT64 = 6,
    INT64 = 7,
    FLOAT32 = 8,
    FLOAT64 = 9,
    BOOL = 10,
    STRING = 11,
    ARRAY = 12
}

public enum GGUFDataType : uint
{
    F32 = 0,
    F16 = 1,
    Q4_0 = 2,
    Q4_1 = 3,
    Q5_0 = 6,
    Q5_1 = 7,
    Q8_0 = 8,
    Q8_1 = 9,
    Q2_K = 10,
    Q3_K = 11,
    Q4_K = 12,
    Q5_K = 13,
    Q6_K = 14,
    Q8_K = 15
}

public class ModelComponent
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public int LayerIndex { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public long[] Shape { get; set; } = Array.Empty<long>();
    public string DataType { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public double RelevanceScore { get; set; }
    public string FunctionalDescription { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ModelIngestionResult
{
    public bool Success { get; set; }
    public Guid ModelId { get; set; }
    public int ComponentsExtracted { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

#endregion