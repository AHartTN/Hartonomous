namespace Hartonomous.DataFabric.Abstractions;

/// <summary>
/// Vector operations abstraction for SQL Server 2025 native VECTOR data type
/// Provides high-performance similarity search and embedding operations
/// </summary>
public interface IVectorService
{
    /// <summary>
    /// Initialize vector storage tables and indexes
    /// Creates ComponentEmbeddings table with VECTOR column and optimized indexes
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Insert component embedding vector using SQL Server 2025 VECTOR type
    /// </summary>
    /// <param name="componentId">Unique component identifier</param>
    /// <param name="modelId">Parent model identifier</param>
    /// <param name="embeddingVector">1536-dimensional embedding vector</param>
    /// <param name="componentType">Type of neural component</param>
    /// <param name="description">Component description for semantic search</param>
    /// <param name="userId">User identifier for multi-tenant isolation</param>
    Task InsertEmbeddingAsync(Guid componentId, Guid modelId, float[] embeddingVector,
        string componentType, string description, string userId);

    /// <summary>
    /// Find similar components using SQL Server native COSINE_DISTANCE
    /// Returns components within similarity threshold ordered by distance
    /// </summary>
    /// <param name="queryVector">Query embedding vector</param>
    /// <param name="threshold">Similarity threshold (0.0 to 1.0)</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="userId">User identifier for scoped search</param>
    /// <returns>Similar components with distance scores</returns>
    Task<IEnumerable<ComponentSimilarityDto>> FindSimilarComponentsAsync(float[] queryVector,
        double threshold, int maxResults, string userId);

    /// <summary>
    /// Batch insert multiple component embeddings for efficient processing
    /// Optimized for large model ingestion with transaction support
    /// </summary>
    /// <param name="embeddings">Collection of component embeddings to insert</param>
    /// <param name="userId">User identifier for multi-tenant isolation</param>
    Task BatchInsertEmbeddingsAsync(IEnumerable<ComponentEmbeddingDto> embeddings, string userId);

    /// <summary>
    /// Delete all embeddings for a specific model
    /// Used during model removal or re-processing
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped deletion</param>
    Task DeleteModelEmbeddingsAsync(Guid modelId, string userId);

    /// <summary>
    /// Get embedding statistics for a model
    /// Returns component counts by type and dimension validation
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped query</param>
    /// <returns>Embedding statistics and metadata</returns>
    Task<ModelEmbeddingStatsDto> GetModelEmbeddingStatsAsync(Guid modelId, string userId);
}

/// <summary>
/// Component similarity result with distance score
/// </summary>
public record ComponentSimilarityDto(
    Guid ComponentId,
    Guid ModelId,
    string ComponentName,
    string ComponentType,
    string Description,
    double Distance);

/// <summary>
/// Component embedding for batch operations
/// </summary>
public record ComponentEmbeddingDto(
    Guid ComponentId,
    Guid ModelId,
    float[] EmbeddingVector,
    string ComponentType,
    string ComponentName,
    string Description);

/// <summary>
/// Model embedding statistics
/// </summary>
public record ModelEmbeddingStatsDto(
    Guid ModelId,
    int TotalComponents,
    Dictionary<string, int> ComponentTypeCounts,
    int VectorDimensions,
    DateTime LastUpdated);