using Microsoft.Extensions.Logging;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Orchestrates model ingestion pipeline
/// Coordinates parsing, extraction, storage, and indexing
/// </summary>
public class ModelIngestionService
{
    private readonly GGUFParser _parser;
    private readonly ComponentExtractor _extractor;
    private readonly ModelStorageService _storage;
    private readonly EmbeddingService _embedding;
    private readonly GraphStorageService _graph;
    private readonly ILogger<ModelIngestionService> _logger;

    public ModelIngestionService(
        GGUFParser parser,
        ComponentExtractor extractor,
        ModelStorageService storage,
        EmbeddingService embedding,
        GraphStorageService graph,
        ILogger<ModelIngestionService> logger)
    {
        _parser = parser;
        _extractor = extractor;
        _storage = storage;
        _embedding = embedding;
        _graph = graph;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestAsync(string modelPath, string modelName, string userId)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var modelId = Guid.NewGuid();

        try
        {
            _logger.LogInformation("Starting ingestion: {ModelName} from {ModelPath}", modelName, modelPath);

            // Parse GGUF file
            var structure = await _parser.ParseAsync(modelPath);
            _logger.LogDebug("Parsed GGUF: {TensorCount} tensors, {MetadataCount} metadata entries",
                structure.TensorCount, structure.MetadataCount);

            // Store foundation model
            await _storage.StoreFoundationModelAsync(modelId, modelName, modelPath, structure, userId);

            // Extract components
            var components = _extractor.ExtractComponents(structure, modelId, userId);
            _logger.LogDebug("Extracted {ComponentCount} components", components.Count);

            // Store components
            await _storage.StoreComponentsAsync(components);

            // Generate embeddings
            await _embedding.GenerateEmbeddingsAsync(components);

            // Store graph relationships
            await _graph.StoreModelGraphAsync(modelId, components, userId);

            // Mark complete
            await _storage.MarkCompleteAsync(modelId);

            stopwatch.Stop();
            _logger.LogInformation("Ingestion complete: {Duration}ms, {ComponentCount} components",
                stopwatch.ElapsedMilliseconds, components.Count);

            return new IngestionResult
            {
                Success = true,
                ModelId = modelId,
                ComponentCount = components.Count,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed: {ModelName}", modelName);
            await _storage.MarkFailedAsync(modelId, ex.Message);

            return new IngestionResult
            {
                Success = false,
                ModelId = modelId,
                ErrorMessage = ex.Message,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }
}

public class IngestionResult
{
    public bool Success { get; set; }
    public Guid ModelId { get; set; }
    public int ComponentCount { get; set; }
    public long ProcessingTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
}