namespace Hartonomous.DataFabric.Abstractions;

/// <summary>
/// Model data service abstraction for FILESTREAM-based operations
/// Provides memory-mapped access to large model files without full memory load
/// Optimized for consumer hardware with streaming processing capabilities
/// </summary>
public interface IModelDataService
{
    /// <summary>
    /// Store model data using FILESTREAM for efficient disk-based access
    /// Enables memory-mapped processing of large models (70B+ parameters)
    /// </summary>
    /// <param name="modelId">Unique model identifier</param>
    /// <param name="modelData">Model file data (GGUF, SafeTensors, etc.)</param>
    /// <param name="modelFormat">Format of the model file</param>
    /// <param name="metadata">Model metadata and configuration</param>
    /// <param name="userId">User identifier for multi-tenant isolation</param>
    /// <returns>Storage result with file path and access information</returns>
    Task<ModelStorageResult> StoreModelAsync(Guid modelId, Stream modelData, ModelFormat modelFormat,
        ModelMetadata metadata, string userId);

    /// <summary>
    /// Get memory-mapped access to model file for streaming processing
    /// Returns handle for SQL CLR assembly to process model without loading into memory
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped access</param>
    /// <returns>Memory-mapped file access handle</returns>
    Task<ModelFileHandle> GetModelFileHandleAsync(Guid modelId, string userId);

    /// <summary>
    /// Parse model architecture using SQL CLR streaming processor
    /// Extracts layer definitions, component structures, and metadata without full load
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped operation</param>
    /// <returns>Parsed model architecture and component definitions</returns>
    Task<ModelArchitectureResult> ParseModelArchitectureAsync(Guid modelId, string userId);

    /// <summary>
    /// Extract component weights using streaming access
    /// Processes specific model components without loading entire model
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="componentPaths">Specific component paths to extract</param>
    /// <param name="userId">User identifier for scoped access</param>
    /// <returns>Component weight data and metadata</returns>
    Task<IEnumerable<ComponentWeightData>> ExtractComponentWeightsAsync(Guid modelId,
        IEnumerable<string> componentPaths, string userId);

    /// <summary>
    /// Perform activation tracing using SQL CLR integration
    /// Traces neural activations through model components with memory-mapped access
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="inputData">Input data for activation tracing</param>
    /// <param name="tracingOptions">Configuration for activation analysis</param>
    /// <param name="userId">User identifier for scoped operation</param>
    /// <returns>Activation patterns and component responses</returns>
    Task<ActivationTraceResult> TraceActivationsAsync(Guid modelId, byte[] inputData,
        ActivationTracingOptions tracingOptions, string userId);

    /// <summary>
    /// Delete model file and associated FILESTREAM data
    /// Cleanup operation for model removal
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped deletion</param>
    Task DeleteModelDataAsync(Guid modelId, string userId);

    /// <summary>
    /// Get model storage statistics and file information
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped query</param>
    /// <returns>Storage statistics and file metadata</returns>
    Task<ModelStorageStats> GetModelStorageStatsAsync(Guid modelId, string userId);

    /// <summary>
    /// Validate model file integrity and format
    /// Performs format validation without full parsing
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped validation</param>
    /// <returns>Validation result with format information</returns>
    Task<ModelValidationResult> ValidateModelAsync(Guid modelId, string userId);
}

/// <summary>
/// Model storage result with access information
/// </summary>
public record ModelStorageResult(
    Guid ModelId,
    string FilePath,
    long FileSizeBytes,
    string FileHash,
    ModelFormat Format,
    bool Success,
    string? ErrorMessage = null);

/// <summary>
/// Memory-mapped file handle for streaming access
/// </summary>
public record ModelFileHandle(
    Guid ModelId,
    string FilePath,
    long FileSizeBytes,
    IntPtr FileHandle,
    ModelFormat Format);

/// <summary>
/// Model architecture parsing result
/// </summary>
public record ModelArchitectureResult(
    Guid ModelId,
    string ModelName,
    ModelFormat Format,
    IEnumerable<LayerDefinition> Layers,
    IEnumerable<ComponentDefinition> Components,
    ModelConfiguration Configuration,
    long TotalParameters,
    bool Success,
    string? ErrorMessage = null);

/// <summary>
/// Component weight data from streaming extraction
/// </summary>
public record ComponentWeightData(
    string ComponentPath,
    string ComponentName,
    string ComponentType,
    byte[] WeightData,
    int[] Shape,
    string DataType);

/// <summary>
/// Activation tracing result
/// </summary>
public record ActivationTraceResult(
    Guid ModelId,
    IEnumerable<ComponentActivation> Activations,
    IEnumerable<ActivationPattern> Patterns,
    TimeSpan ProcessingTime,
    bool Success,
    string? ErrorMessage = null);

/// <summary>
/// Activation tracing configuration options
/// </summary>
public record ActivationTracingOptions(
    IEnumerable<string> TargetComponents,
    double ActivationThreshold,
    int MaxTraceDepth,
    bool IncludeGradients);

/// <summary>
/// Model storage statistics
/// </summary>
public record ModelStorageStats(
    Guid ModelId,
    long FileSizeBytes,
    DateTime CreatedAt,
    DateTime LastAccessedAt,
    string FileHash,
    ModelFormat Format,
    string CompressionRatio);

/// <summary>
/// Model validation result
/// </summary>
public record ModelValidationResult(
    Guid ModelId,
    bool IsValid,
    ModelFormat DetectedFormat,
    IEnumerable<string> ValidationErrors,
    IEnumerable<string> ValidationWarnings);

/// <summary>
/// Model format enumeration
/// </summary>
public enum ModelFormat
{
    GGUF,
    SafeTensors,
    PyTorch,
    ONNX,
    Unknown
}

/// <summary>
/// Model metadata for storage
/// </summary>
public record ModelMetadata(
    string ModelName,
    string Version,
    string Description,
    Dictionary<string, object> Properties);

/// <summary>
/// Layer definition from model architecture
/// </summary>
public record LayerDefinition(
    string LayerName,
    string LayerType,
    int[] InputShape,
    int[] OutputShape,
    Dictionary<string, object> Parameters);

/// <summary>
/// Component definition from model architecture
/// </summary>
public record ComponentDefinition(
    string ComponentName,
    string ComponentType,
    string LayerName,
    int[] Shape,
    string DataType);

/// <summary>
/// Model configuration from parsed architecture
/// </summary>
public record ModelConfiguration(
    string Architecture,
    Dictionary<string, object> Hyperparameters,
    string Framework,
    string Version);

/// <summary>
/// Component activation during tracing
/// </summary>
public record ComponentActivation(
    string ComponentName,
    float[] ActivationValues,
    double MaxActivation,
    double MeanActivation);

/// <summary>
/// Activation pattern identified during tracing
/// </summary>
public record ActivationPattern(
    string PatternName,
    IEnumerable<string> InvolvedComponents,
    double Strength,
    string Description);