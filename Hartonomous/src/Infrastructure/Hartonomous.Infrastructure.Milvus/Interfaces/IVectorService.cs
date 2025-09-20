using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hartonomous.Infrastructure.Milvus.Interfaces;

/// <summary>
/// Interface for vector database operations
/// Provides abstraction over vector storage implementations (SQL Server Vector, Milvus, etc.)
/// </summary>
public interface IVectorService
{
    /// <summary>
    /// Initialize the vector storage collection
    /// </summary>
    Task InitializeCollectionAsync();

    /// <summary>
    /// Ensures vector storage tables/collections exist
    /// </summary>
    Task EnsureVectorTablesExistAsync();

    /// <summary>
    /// Insert component embedding vector
    /// </summary>
    /// <param name="componentId">Unique component identifier</param>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for data isolation</param>
    /// <param name="embedding">Embedding vector</param>
    /// <param name="componentType">Type of component</param>
    /// <param name="description">Component description</param>
    Task InsertEmbeddingAsync(Guid componentId, Guid modelId, string userId,
        float[] embedding, string componentType, string description);

    /// <summary>
    /// Search for similar component embeddings
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector</param>
    /// <param name="userId">User identifier for data isolation</param>
    /// <param name="topK">Number of top results to return</param>
    /// <param name="componentType">Optional component type filter</param>
    /// <returns>Similar components with similarity scores</returns>
    Task<IEnumerable<SimilarComponent>> SearchSimilarAsync(float[] queryEmbedding, string userId,
        int topK = 10, string? componentType = null);

    /// <summary>
    /// Delete embeddings for a specific component
    /// </summary>
    /// <param name="componentId">Component identifier</param>
    /// <param name="userId">User identifier for data isolation</param>
    Task DeleteEmbeddingsAsync(Guid componentId, string userId);

    /// <summary>
    /// Get collection statistics for monitoring
    /// </summary>
    Task<MilvusCollectionStats> GetCollectionStatsAsync();

    /// <summary>
    /// Batch insert embeddings for better performance
    /// </summary>
    /// <param name="embeddings">Collection of embeddings to insert</param>
    /// <param name="userId">User identifier for data isolation</param>
    Task BatchInsertEmbeddingsAsync(IEnumerable<ComponentEmbedding> embeddings, string userId);
}