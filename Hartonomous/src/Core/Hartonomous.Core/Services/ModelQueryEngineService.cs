/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model Query Engine (MQE) core implementation - the revolutionary
 * "ESRI for AI Models" system that enables T-SQL queries against large language models.
 * The neural mapping algorithms and T-SQL integration patterns contained herein represent
 * proprietary intellectual property and trade secrets.
 *
 * Key Innovations Protected:
 * - Neural architecture to SQL table mapping algorithms
 * - Memory-mapped model weight access via T-SQL
 * - Real-time mechanistic interpretability integration
 * - llama.cpp service T-SQL REST bridge architecture
 *
 * Any attempt to reverse engineer, extract, or replicate these algorithms is prohibited
 * by law and subject to legal action. This includes but is not limited to analysis of
 * neural mapping functions, weight access patterns, or SQL integration methodologies.
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Hartonomous.Core.Data;
using Hartonomous.Core.Models;

namespace Hartonomous.Core.Services;

/// <summary>
/// Core Model Query Engine implementation
/// Provides T-SQL REST integration with llama.cpp and neural mapping capabilities
/// </summary>
public class ModelQueryEngineService : IModelQueryEngineService
{
    private readonly HartonomousDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModelQueryEngineService> _logger;
    private readonly string _llamaCppServiceUrl;

    public ModelQueryEngineService(
        HartonomousDbContext context,
        IConfiguration configuration,
        ILogger<ModelQueryEngineService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _llamaCppServiceUrl = configuration["LlamaCpp:ServiceUrl"] ?? "http://localhost:8080";
    }

    /// <summary>
    /// Ingest and analyze a model using llama.cpp integration
    /// </summary>
    public async Task<ModelIngestionResult> IngestModelAsync(string modelPath, string modelName, string userId)
    {
        _logger.LogInformation("Starting model ingestion for {ModelPath}", modelPath);

        try
        {
            // Step 1: Create model record
            var model = new Model
            {
                ModelName = modelName,
                ModelPath = modelPath,
                Status = ModelStatus.Processing,
                UserId = userId
            };

            _context.Models.Add(model);
            await _context.SaveChangesAsync();

            // Step 2: Call llama.cpp service for model analysis via T-SQL REST
            var analysisPayload = JsonSerializer.Serialize(new
            {
                model_path = modelPath,
                model_id = model.ModelId.ToString(),
                extract_layers = true,
                extract_weights = true,
                analyze_attention = true,
                extract_embeddings = true,
                perform_interpretability = true
            });

            var analysisResponse = await _context.InvokeExternalRestEndpointAsync(
                $"{_llamaCppServiceUrl}/api/analyze",
                "POST",
                analysisPayload
            );

            if (string.IsNullOrEmpty(analysisResponse))
            {
                throw new InvalidOperationException("No response from llama.cpp service");
            }

            var analysisData = JsonSerializer.Deserialize<LlamaCppAnalysisResponse>(analysisResponse);
            if (analysisData == null)
            {
                throw new InvalidOperationException("Invalid response format from llama.cpp service");
            }

            // Step 3: Store model architecture and layers
            await StoreModelArchitectureAsync(model.ModelId, analysisData, userId);

            // Step 4: Store model components and weights
            await StoreModelComponentsAsync(model.ModelId, analysisData, userId);

            // Step 5: Generate and store embeddings
            await GenerateModelEmbeddingsAsync(model.ModelId, analysisData, userId);

            // Step 6: Perform mechanistic interpretability analysis
            await PerformMechanisticAnalysisAsync(model.ModelId, analysisData, userId);

            // Step 7: Create neural map
            await CreateNeuralMapAsync(model.ModelId, analysisData, userId);

            // Step 8: Update model status
            model.Status = ModelStatus.Ready;
            model.ProcessedAt = DateTime.UtcNow;
            model.ParameterCount = analysisData.TotalParameters;
            model.Architecture = analysisData.Architecture;
            model.HiddenSize = analysisData.HiddenSize;
            model.NumLayers = analysisData.NumLayers;
            model.NumAttentionHeads = analysisData.NumAttentionHeads;
            model.SetConfiguration(analysisData.Config);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Model ingestion completed successfully for {ModelId}", model.ModelId);

            return new ModelIngestionResult
            {
                ModelId = model.ModelId,
                Success = true,
                TotalParameters = analysisData.TotalParameters,
                LayersExtracted = analysisData.Layers?.Count ?? 0,
                ComponentsExtracted = analysisData.Components?.Count ?? 0,
                ProcessingTimeMs = (int)(DateTime.UtcNow - model.IngestedAt).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during model ingestion for {ModelPath}", modelPath);

            // Update model status to error
            var model = await _context.Models.FirstOrDefaultAsync(m => m.ModelPath == modelPath && m.UserId == userId);
            if (model != null)
            {
                model.Status = ModelStatus.Error;
                await _context.SaveChangesAsync();
            }

            return new ModelIngestionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Query model components using T-SQL vector operations
    /// </summary>
    public async Task<IEnumerable<ModelComponentQueryResult>> QueryModelComponentsAsync(
        Guid modelId,
        string query,
        string userId,
        double similarityThreshold = 0.8,
        int limit = 10)
    {
        _logger.LogDebug("Querying model components for model {ModelId} with query: {Query}", modelId, query);

        // Step 1: Get query embedding
        var queryEmbedding = await GetTextEmbeddingAsync(query);

        // Step 2: Perform vector similarity search using SQL Server 2025 native capabilities
        var sql = @"
            SELECT TOP (@limit)
                mc.ComponentId,
                mc.ComponentName,
                mc.ComponentType,
                mc.SemanticPurpose,
                ml.LayerIndex,
                VECTOR_DISTANCE('cosine', ce.Embedding, @queryVector) AS Similarity,
                mc.InterpretabilityData
            FROM ModelComponents mc
            INNER JOIN ComponentEmbeddings ce ON mc.ComponentId = ce.ComponentId
            INNER JOIN ModelLayers ml ON mc.LayerId = ml.LayerId
            WHERE ml.ModelId = @modelId
              AND mc.UserId = @userId
              AND VECTOR_DISTANCE('cosine', ce.Embedding, @queryVector) > @threshold
            ORDER BY Similarity DESC";

        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@modelId", modelId);
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@queryVector", $"[{string.Join(",", queryEmbedding)}]");
        command.Parameters.AddWithValue("@threshold", similarityThreshold);

        var results = new List<ModelComponentQueryResult>();
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new ModelComponentQueryResult
            {
                ComponentId = reader.GetGuid(reader.GetOrdinal("ComponentId")),
                ComponentName = reader.GetString(reader.GetOrdinal("ComponentName")),
                ComponentType = reader.GetString(reader.GetOrdinal("ComponentType")),
                SemanticPurpose = reader.IsDBNull(reader.GetOrdinal("SemanticPurpose")) ? null : reader.GetString(reader.GetOrdinal("SemanticPurpose")),
                LayerIndex = reader.GetInt32(reader.GetOrdinal("LayerIndex")),
                Similarity = reader.GetDouble(reader.GetOrdinal("Similarity")),
                InterpretabilityData = reader.IsDBNull(reader.GetOrdinal("InterpretabilityData")) ? null : reader.GetString(reader.GetOrdinal("InterpretabilityData"))
            });
        }

        return results;
    }

    /// <summary>
    /// Extract specific neural patterns from model weights using memory-mapped access
    /// </summary>
    public async Task<NeuralPatternExtractionResult> ExtractNeuralPatternsAsync(
        Guid modelId,
        string patternType,
        string userId,
        Dictionary<string, object>? parameters = null)
    {
        _logger.LogDebug("Extracting neural patterns of type {PatternType} from model {ModelId}", patternType, modelId);

        try
        {
            // Call SQL CLR function for memory-mapped pattern extraction
            var sql = @"
                SELECT dbo.ExtractNeuralPatterns(@modelId, @patternType, @parameters, @userId) AS PatternData";

            using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await connection.OpenAsync();

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@modelId", modelId);
            command.Parameters.AddWithValue("@patternType", patternType);
            command.Parameters.AddWithValue("@parameters", JsonSerializer.Serialize(parameters ?? new Dictionary<string, object>()));
            command.Parameters.AddWithValue("@userId", userId);

            var patternData = await command.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(patternData))
            {
                return new NeuralPatternExtractionResult
                {
                    Success = false,
                    ErrorMessage = "No pattern data extracted"
                };
            }

            var patterns = JsonSerializer.Deserialize<List<NeuralPattern>>(patternData);

            return new NeuralPatternExtractionResult
            {
                Success = true,
                PatternType = patternType,
                Patterns = patterns ?? new List<NeuralPattern>(),
                ExtractionTimeMs = 0 // TODO: Track timing
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting neural patterns of type {PatternType} from model {ModelId}", patternType, modelId);

            return new NeuralPatternExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Create a neural map for the model - this is the core MQE innovation
    /// </summary>
    private async Task CreateNeuralMapAsync(Guid modelId, LlamaCppAnalysisResponse analysisData, string userId)
    {
        _logger.LogInformation("Creating neural map for model {ModelId}", modelId);

        // Neural map creation involves:
        // 1. Mapping neuron activations to semantic concepts
        // 2. Creating attention pattern mappings
        // 3. Building component relationship graphs
        // 4. Storing interpretability metadata

        foreach (var layer in analysisData.Layers ?? new List<LayerAnalysis>())
        {
            foreach (var component in layer.Components ?? new List<ComponentAnalysis>())
            {
                // Store neural activation patterns
                if (component.NeuronActivations != null)
                {
                    foreach (var neuron in component.NeuronActivations)
                    {
                        var interpretation = new NeuronInterpretation
                        {
                            ComponentId = Guid.Parse(component.ComponentId),
                            NeuronIndex = neuron.Index,
                            LayerIndex = layer.Index,
                            LearnedConcept = neuron.LearnedConcept ?? "Unknown",
                            ConceptCategory = neuron.ConceptCategory ?? "Unclassified",
                            ConceptEmbedding = neuron.ConceptEmbedding != null ? FloatArrayToBytes(neuron.ConceptEmbedding) : Array.Empty<byte>(),
                            ActivationThreshold = neuron.ActivationThreshold,
                            ConceptStrength = neuron.ConceptStrength,
                            ConceptExamples = JsonSerializer.Serialize(neuron.ConceptExamples ?? new List<string>()),
                            AutomatedDescription = JsonSerializer.Serialize(neuron.DetailedAnalysis ?? new object()),
                            UserId = userId
                        };

                        _context.NeuronInterpretations.Add(interpretation);
                    }
                }

                // Store attention head patterns
                if (component.AttentionHeads != null)
                {
                    foreach (var head in component.AttentionHeads)
                    {
                        var attentionHead = new AttentionHead
                        {
                            ComponentId = Guid.Parse(component.ComponentId),
                            LayerIndex = layer.Index,
                            HeadIndex = head.Index,
                            AttentionPattern = head.PatternType ?? "Unknown",
                            PatternStrength = head.PatternStrength,
                            SemanticDescription = head.Description ?? "",
                            ExampleInputs = JsonSerializer.Serialize(head.ExampleInputs ?? new List<object>()),
                            UserId = userId
                        };

                        _context.AttentionHeads.Add(attentionHead);
                    }
                }
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Neural map creation completed for model {ModelId}", modelId);
    }

    /// <summary>
    /// Get text embedding using external service
    /// </summary>
    private async Task<float[]> GetTextEmbeddingAsync(string text)
    {
        var embeddingPayload = JsonSerializer.Serialize(new { text });

        var response = await _context.InvokeExternalRestEndpointAsync(
            $"{_llamaCppServiceUrl}/api/embed",
            "POST",
            embeddingPayload
        );

        var embeddingData = JsonSerializer.Deserialize<EmbeddingResponse>(response);
        return embeddingData?.Embedding ?? new float[1536];
    }

    // Additional private methods for storing architecture, components, etc.
    private async Task StoreModelArchitectureAsync(Guid modelId, LlamaCppAnalysisResponse analysisData, string userId)
    {
        foreach (var layerData in analysisData.Layers ?? new List<LayerAnalysis>())
        {
            var layer = new ModelLayer
            {
                ModelId = modelId,
                LayerIndex = layerData.Index,
                LayerType = layerData.Type,
                LayerConfig = JsonSerializer.Serialize(layerData.Config ?? new object()),
                InterpretabilityScore = layerData.InterpretabilityScore,
                UserId = userId
            };

            _context.ModelLayers.Add(layer);
        }

        await _context.SaveChangesAsync();
    }

    private async Task StoreModelComponentsAsync(Guid modelId, LlamaCppAnalysisResponse analysisData, string userId)
    {
        var layers = await _context.ModelLayers.Where(l => l.ModelId == modelId).ToListAsync();

        foreach (var layerData in analysisData.Layers ?? new List<LayerAnalysis>())
        {
            var layer = layers.First(l => l.LayerIndex == layerData.Index);

            foreach (var componentData in layerData.Components ?? new List<ComponentAnalysis>())
            {
                var component = new ModelComponent
                {
                    LayerId = layer.LayerId,
                    ComponentType = componentData.Type,
                    ComponentIndex = componentData.Index,
                    LearnedFunction = componentData.LearnedFunction ?? "",
                    SemanticPurpose = componentData.SemanticPurpose ?? "",
                    InterpretabilityData = JsonSerializer.Serialize(componentData.DetailedAnalysis ?? new object()),
                    UserId = userId
                };

                _context.ModelComponents.Add(component);

                // Store component weights if available
                if (componentData.WeightData != null)
                {
                    var weight = new ComponentWeight
                    {
                        ComponentId = component.ComponentId,
                        WeightData = JsonSerializer.Serialize(new { RawWeights = componentData.WeightData }),
                        UserId = userId
                    };

                    _context.ComponentWeights.Add(weight);
                }
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task GenerateModelEmbeddingsAsync(Guid modelId, LlamaCppAnalysisResponse analysisData, string userId)
    {
        // Generate embeddings for each component based on its semantic purpose
        var components = await _context.ModelComponents
            .Include(c => c.Layer)
            .Where(c => c.Layer.ModelId == modelId)
            .ToListAsync();

        foreach (var component in components)
        {
            if (!string.IsNullOrEmpty(component.SemanticPurpose))
            {
                var embedding = await GetTextEmbeddingAsync(component.SemanticPurpose);

                var componentEmbedding = new ComponentEmbedding
                {
                    ComponentId = component.ComponentId,
                    Embedding = FloatArrayToBytes(embedding),
                    EmbeddingType = "semantic",
                    UserId = userId
                };

                _context.ComponentEmbeddings.Add(componentEmbedding);
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task PerformMechanisticAnalysisAsync(Guid modelId, LlamaCppAnalysisResponse analysisData, string userId)
    {
        // Call specialized mechanistic interpretability service
        var mechanisticPayload = JsonSerializer.Serialize(new
        {
            model_id = modelId.ToString(),
            analysis_type = "comprehensive",
            extract_causal_patterns = true,
            analyze_feature_interactions = true
        });

        var mechanisticResponse = await _context.InvokeExternalRestEndpointAsync(
            $"{_llamaCppServiceUrl}/api/mechanistic-analysis",
            "POST",
            mechanisticPayload
        );

        // Store mechanistic analysis results
        if (!string.IsNullOrEmpty(mechanisticResponse))
        {
            var mechanisticData = JsonSerializer.Deserialize<MechanisticAnalysisResponse>(mechanisticResponse);
            // TODO: Store mechanistic analysis results in appropriate tables
        }
    }

    /// <summary>
    /// Converts float array to byte array for SQL Server 2025 VECTOR storage
    /// </summary>
    private static byte[] FloatArrayToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

// Supporting types
public class ModelIngestionResult
{
    public Guid? ModelId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long TotalParameters { get; set; }
    public int LayersExtracted { get; set; }
    public int ComponentsExtracted { get; set; }
    public int ProcessingTimeMs { get; set; }
}

public class ModelComponentQueryResult
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string? SemanticPurpose { get; set; }
    public int LayerIndex { get; set; }
    public double Similarity { get; set; }
    public string? InterpretabilityData { get; set; }
}

public class NeuralPatternExtractionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string PatternType { get; set; } = string.Empty;
    public List<NeuralPattern> Patterns { get; set; } = new();
    public int ExtractionTimeMs { get; set; }
}

// NeuralPattern class moved to MechanisticInterpretabilityService.cs to avoid duplication

// llama.cpp service response types
public class LlamaCppAnalysisResponse
{
    public string Architecture { get; set; } = string.Empty;
    public long TotalParameters { get; set; }
    public int HiddenSize { get; set; }
    public int NumLayers { get; set; }
    public int NumAttentionHeads { get; set; }
    public object? Config { get; set; }
    public List<LayerAnalysis>? Layers { get; set; }
    public List<ComponentAnalysis>? Components { get; set; }
}

public class LayerAnalysis
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public object? Config { get; set; }
    public double InterpretabilityScore { get; set; }
    public List<ComponentAnalysis>? Components { get; set; }
}

public class ComponentAnalysis
{
    public string ComponentId { get; set; } = string.Empty;
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? LearnedFunction { get; set; }
    public string? SemanticPurpose { get; set; }
    public byte[]? WeightData { get; set; }
    public object? DetailedAnalysis { get; set; }
    public List<NeuronActivation>? NeuronActivations { get; set; }
    public List<AttentionHeadAnalysis>? AttentionHeads { get; set; }
}

public class NeuronActivation
{
    public int Index { get; set; }
    public string? LearnedConcept { get; set; }
    public string? ConceptCategory { get; set; }
    public float[]? ConceptEmbedding { get; set; }
    public double ActivationThreshold { get; set; }
    public double ConceptStrength { get; set; }
    public List<string>? ConceptExamples { get; set; }
    public object? DetailedAnalysis { get; set; }
}

// AttentionHeadAnalysis class moved to MechanisticInterpretabilityService.cs to avoid duplication

public class EmbeddingResponse
{
    public float[]? Embedding { get; set; }
}

public class MechanisticAnalysisResponse
{
    public object? CausalPatterns { get; set; }
    public object? FeatureInteractions { get; set; }
}

public interface IModelQueryEngineService
{
    Task<ModelIngestionResult> IngestModelAsync(string modelPath, string modelName, string userId);
    Task<IEnumerable<ModelComponentQueryResult>> QueryModelComponentsAsync(Guid modelId, string query, string userId, double similarityThreshold = 0.8, int limit = 10);
    Task<NeuralPatternExtractionResult> ExtractNeuralPatternsAsync(Guid modelId, string patternType, string userId, Dictionary<string, object>? parameters = null);
}