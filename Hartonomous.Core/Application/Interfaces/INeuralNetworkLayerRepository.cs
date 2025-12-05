using Hartonomous.Core.Domain.Entities;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for NeuralNetworkLayer entity
/// </summary>
public interface INeuralNetworkLayerRepository : IRepository<NeuralNetworkLayer>
{
    /// <summary>
    /// Get all layers for a specific model ordered by layer index
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetByModelIdAsync(
        Guid modelId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get specific layer by model ID and layer index
    /// </summary>
    Task<NeuralNetworkLayer?> GetByModelAndIndexAsync(
        Guid modelId,
        int layerIndex,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get specific layer by model ID and layer name
    /// </summary>
    Task<NeuralNetworkLayer?> GetByModelAndNameAsync(
        Guid modelId,
        string layerName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get layers by type across all models
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetByLayerTypeAsync(
        string layerType,
        int? maxResults = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get layers by epoch (for temporal analysis)
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetByEpochAsync(
        Guid modelId,
        int epoch,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get frozen (non-trainable) layers
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetFrozenLayersAsync(
        Guid? modelId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get layers by parameter count range
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetByParameterCountRangeAsync(
        long minParams,
        long maxParams,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get layers by weight norm range (for regularization analysis)
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetByWeightNormRangeAsync(
        double minNorm,
        double maxNorm,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get layer evolution across epochs
    /// </summary>
    Task<List<NeuralNetworkLayer>> GetLayerHistoryAsync(
        Guid modelId,
        int layerIndex,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get model statistics (layer counts, total parameters, etc.)
    /// </summary>
    Task<ModelStatistics> GetModelStatisticsAsync(
        Guid modelId,
        CancellationToken cancellationToken = default);
}
