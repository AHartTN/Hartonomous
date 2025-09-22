using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService.Abstractions;

/// <summary>
/// Unified model processing pipeline interface
/// Orchestrates the complete flow from model ingestion to agent synthesis
/// </summary>
public interface IModelProcessingPipeline
{
    /// <summary>
    /// Execute the complete model processing pipeline
    /// Includes ingestion, parsing, circuit discovery, and agent synthesis preparation
    /// </summary>
    /// <param name="input">Pipeline input configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Pipeline execution result with processed model information</returns>
    Task<ModelProcessingResult> ExecuteAsync(ModelProcessingInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute specific pipeline stage
    /// Allows for granular control over individual processing steps
    /// </summary>
    /// <param name="stage">Pipeline stage to execute</param>
    /// <param name="context">Pipeline execution context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stage execution result</returns>
    Task<PipelineStageResult> ExecuteStageAsync(PipelineStage stage, PipelineContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pipeline execution status and progress
    /// </summary>
    /// <param name="executionId">Pipeline execution identifier</param>
    /// <returns>Current pipeline status and progress information</returns>
    Task<PipelineStatus> GetStatusAsync(Guid executionId);
}

/// <summary>
/// Model processing pipeline input
/// </summary>
public record ModelProcessingInput(
    Guid ModelId,
    Stream ModelData,
    ModelFormat Format,
    ModelMetadata Metadata,
    string UserId,
    ProcessingOptions Options);

/// <summary>
/// Model processing pipeline result
/// </summary>
public record ModelProcessingResult(
    Guid ModelId,
    Guid ExecutionId,
    bool Success,
    TimeSpan ProcessingTime,
    ModelArchitectureResult Architecture,
    IEnumerable<ComputationalCircuitDto> DiscoveredCircuits,
    ModelEmbeddingStatsDto EmbeddingStats,
    string? ErrorMessage = null);

/// <summary>
/// Pipeline stage enumeration
/// </summary>
public enum PipelineStage
{
    ModelIngestion,
    ArchitectureParsing,
    ComponentExtraction,
    EmbeddingGeneration,
    CircuitDiscovery,
    ActivationTracing,
    AgentSynthesisPrep
}

/// <summary>
/// Pipeline execution context
/// </summary>
public record PipelineContext(
    Guid ExecutionId,
    ModelProcessingInput Input,
    Dictionary<string, object> StageResults,
    DateTime StartTime,
    PipelineStage CurrentStage);

/// <summary>
/// Pipeline stage execution result
/// </summary>
public record PipelineStageResult(
    PipelineStage Stage,
    bool Success,
    TimeSpan Duration,
    object? Result = null,
    string? ErrorMessage = null);

/// <summary>
/// Pipeline execution status
/// </summary>
public record PipelineStatus(
    Guid ExecutionId,
    PipelineStage CurrentStage,
    double ProgressPercentage,
    TimeSpan ElapsedTime,
    bool IsCompleted,
    bool IsSuccessful,
    string? ErrorMessage = null);

/// <summary>
/// Model processing options
/// </summary>
public record ProcessingOptions(
    bool EnableCircuitDiscovery = true,
    bool EnableActivationTracing = true,
    bool EnableEmbeddingGeneration = true,
    double SimilarityThreshold = 0.8,
    int MaxCircuitDepth = 5,
    IEnumerable<string>? TargetComponentTypes = null);