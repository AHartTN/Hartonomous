using System.Data;
using System.Data.SqlTypes;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.Infrastructure.SqlServer;

/// <summary>
/// SQL Server FILESTREAM implementation for large model file storage
/// Provides efficient storage and streaming access to multi-GB model files
/// Uses SQL Server FILESTREAM for optimal disk I/O and memory-mapped access
/// </summary>
public class SqlServerFileStreamService : IModelDataService
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerFileStreamService> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileStreamErrorHandler _errorHandler;

    public SqlServerFileStreamService(
        IConfiguration configuration,
        ILogger<SqlServerFileStreamService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not configured");
        _errorHandler = new FileStreamErrorHandler(
            logger as ILogger<FileStreamErrorHandler> ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FileStreamErrorHandler>.Instance);
    }

    /// <summary>
    /// Store model data using FILESTREAM for efficient disk-based access
    /// </summary>
    public async Task<ModelStorageResult> StoreModelAsync(
        Guid modelId,
        Stream modelData,
        ModelFormat modelFormat,
        ModelMetadata metadata,
        string userId)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        return await _errorHandler.ExecuteWithRetryAsync(async () =>
        {
            _logger.LogInformation("Starting FILESTREAM storage for model {ModelId}, format: {Format}",
                modelId, modelFormat);

            using var transactionManager = new FileStreamTransactionManager(_connectionString,
                _logger as ILogger<FileStreamTransactionManager> ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FileStreamTransactionManager>.Instance);

            return await transactionManager.ExecuteInTransactionAsync(async context =>
            {
                // Calculate file hash while streaming
                var hash = await CalculateFileHashAsync(modelData);
                var originalPosition = modelData.Position;
                modelData.Position = 0; // Reset for FILESTREAM write

                // Insert model record with FILESTREAM placeholder
                var filePath = await InsertModelRecordAsync(
                    context, modelId, modelFormat, metadata, userId, hash, modelData.Length);

                // Get FILESTREAM path for writing
                var fileStreamPath = await context.GetFileStreamPathAsync("ModelFiles", "ModelData", modelId);

                // Write model data to FILESTREAM using transaction context
                var bytesWritten = await context.WriteToFileStreamAsync(fileStreamPath, modelData, _logger);

                // Validate file integrity
                var isValid = await context.ValidateFileStreamIntegrityAsync(modelId, hash, _logger);
                if (!isValid)
                {
                    throw new InvalidOperationException("FILESTREAM data integrity validation failed");
                }

                // Update model status to completed
                await context.ExecuteNonQueryAsync(
                    "UPDATE ModelFiles SET Status = 'Completed', CompletedAt = GETUTCDATE() WHERE ModelId = @ModelId",
                    new SqlParameter("@ModelId", modelId));

                stopwatch.Stop();
                _logger.LogInformation(
                    "FILESTREAM storage completed: {ModelId}, {BytesWritten} bytes, {Duration}ms",
                    modelId, bytesWritten, stopwatch.ElapsedMilliseconds);

                return new ModelStorageResult(
                    ModelId: modelId,
                    FilePath: filePath,
                    FileSizeBytes: bytesWritten,
                    FileHash: hash,
                    Format: modelFormat,
                    Success: true);
            }, IsolationLevel.ReadCommitted);
        }, "StoreModel", modelId, maxRetries: 3)
        .ContinueWith(async task =>
        {
            if (task.IsFaulted)
            {
                var ex = task.Exception?.GetBaseException();
                if (ex is FileStreamOperationException fsEx)
                {
                    await _errorHandler.HandleFailureCleanupAsync(
                        new FileStreamErrorInfo(fsEx.ErrorType, false, ex.Message, ex),
                        modelId, _connectionString);
                }

                _logger.LogError(ex, "Model storage failed after retries: {ModelId}", modelId);
                return new ModelStorageResult(
                    ModelId: modelId,
                    FilePath: string.Empty,
                    FileSizeBytes: 0,
                    FileHash: string.Empty,
                    Format: modelFormat,
                    Success: false,
                    ErrorMessage: ex?.Message ?? "Unknown error occurred");
            }

            return await task;
        }).Unwrap();
    }

    /// <summary>
    /// Get memory-mapped access to model file for streaming processing
    /// </summary>
    public async Task<ModelFileHandle> GetModelFileHandleAsync(Guid modelId, string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT FilePath, FileSizeBytes, ModelFormat, FileStreamPath
                FROM ModelFiles
                WHERE ModelId = @ModelId AND UserId = @UserId AND Status = 'Completed'";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new FileNotFoundException($"Model file not found: {modelId}");
            }

            var filePath = reader.GetString("FilePath");
            var fileSizeBytes = reader.GetInt64("FileSizeBytes");
            var modelFormat = Enum.Parse<ModelFormat>(reader.GetString("ModelFormat"));
            var fileStreamPath = reader.GetString("FileStreamPath");

            // Open file handle for memory-mapped access
            var fileHandle = await OpenFileHandleAsync(fileStreamPath);

            return new ModelFileHandle(
                ModelId: modelId,
                FilePath: fileStreamPath,
                FileSizeBytes: fileSizeBytes,
                FileHandle: fileHandle,
                Format: modelFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file handle for model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Parse model architecture using SQL CLR streaming processor
    /// </summary>
    public async Task<ModelArchitectureResult> ParseModelArchitectureAsync(Guid modelId, string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Call SQL CLR function for architecture parsing
            var query = "SELECT dbo.ParseModelArchitecture(@ModelId, @UserId)";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

            var resultJson = await command.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(resultJson))
            {
                throw new InvalidOperationException($"Failed to parse architecture for model {modelId}");
            }

            var architectureData = JsonSerializer.Deserialize<ModelArchitectureDto>(resultJson);

            return new ModelArchitectureResult(
                ModelId: modelId,
                ModelName: architectureData!.ModelName,
                Format: Enum.Parse<ModelFormat>(architectureData.Format),
                Layers: architectureData.Layers.Select(MapLayerDefinition),
                Components: architectureData.Components.Select(MapComponentDefinition),
                Configuration: MapModelConfiguration(architectureData.Configuration),
                TotalParameters: architectureData.TotalParameters,
                Success: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Architecture parsing failed for model {ModelId}", modelId);
            return new ModelArchitectureResult(
                ModelId: modelId,
                ModelName: string.Empty,
                Format: ModelFormat.Unknown,
                Layers: Enumerable.Empty<LayerDefinition>(),
                Components: Enumerable.Empty<ComponentDefinition>(),
                Configuration: new ModelConfiguration(string.Empty, new Dictionary<string, object>(), string.Empty, string.Empty),
                TotalParameters: 0,
                Success: false,
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Extract component weights using streaming access
    /// </summary>
    public async Task<IEnumerable<ComponentWeightData>> ExtractComponentWeightsAsync(
        Guid modelId,
        IEnumerable<string> componentPaths,
        string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var results = new List<ComponentWeightData>();

            foreach (var componentPath in componentPaths)
            {
                // Call SQL CLR function for component extraction
                var query = "SELECT dbo.ExtractComponentWeights(@ModelId, @ComponentPath, @UserId)";

                using var command = new SqlCommand(query, connection);
                command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
                command.Parameters.Add("@ComponentPath", SqlDbType.NVarChar, 500).Value = componentPath;
                command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

                var resultJson = await command.ExecuteScalarAsync() as string;

                if (!string.IsNullOrEmpty(resultJson))
                {
                    var componentData = JsonSerializer.Deserialize<ComponentWeightDto>(resultJson);
                    if (componentData != null)
                    {
                        results.Add(new ComponentWeightData(
                            ComponentPath: componentData.ComponentPath,
                            ComponentName: componentData.ComponentName,
                            ComponentType: componentData.ComponentType,
                            WeightData: Convert.FromBase64String(componentData.WeightDataBase64),
                            Shape: componentData.Shape,
                            DataType: componentData.DataType));
                    }
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Component extraction failed for model {ModelId}", modelId);
            return Enumerable.Empty<ComponentWeightData>();
        }
    }

    /// <summary>
    /// Perform activation tracing using SQL CLR integration
    /// </summary>
    public async Task<ActivationTraceResult> TraceActivationsAsync(
        Guid modelId,
        byte[] inputData,
        ActivationTracingOptions tracingOptions,
        string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var optionsJson = JsonSerializer.Serialize(new
            {
                TargetComponents = tracingOptions.TargetComponents.ToArray(),
                tracingOptions.ActivationThreshold,
                tracingOptions.MaxTraceDepth,
                tracingOptions.IncludeGradients
            });

            // Call SQL CLR function for activation tracing
            var query = "SELECT dbo.TraceModelActivations(@ModelId, @InputData, @TracingOptions, @UserId)";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@InputData", SqlDbType.VarBinary, -1).Value = inputData;
            command.Parameters.Add("@TracingOptions", SqlDbType.NVarChar, -1).Value = optionsJson;
            command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

            var resultJson = await command.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(resultJson))
            {
                throw new InvalidOperationException($"Activation tracing failed for model {modelId}");
            }

            var traceData = JsonSerializer.Deserialize<ActivationTraceDto>(resultJson);

            return new ActivationTraceResult(
                ModelId: modelId,
                Activations: traceData!.Activations.Select(MapComponentActivation),
                Patterns: traceData.Patterns.Select(MapActivationPattern),
                ProcessingTime: TimeSpan.FromMilliseconds(traceData.ProcessingTimeMs),
                Success: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Activation tracing failed for model {ModelId}", modelId);
            return new ActivationTraceResult(
                ModelId: modelId,
                Activations: Enumerable.Empty<ComponentActivation>(),
                Patterns: Enumerable.Empty<ActivationPattern>(),
                ProcessingTime: TimeSpan.Zero,
                Success: false,
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Delete model file and associated FILESTREAM data
    /// </summary>
    public async Task DeleteModelDataAsync(Guid modelId, string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Delete model record (CASCADE will handle FILESTREAM cleanup)
                var query = "DELETE FROM ModelFiles WHERE ModelId = @ModelId AND UserId = @UserId";

                using var command = new SqlCommand(query, connection, transaction);
                command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
                command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    throw new FileNotFoundException($"Model not found: {modelId}");
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Model deleted successfully: {ModelId}", modelId);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Get model storage statistics and file information
    /// </summary>
    public async Task<ModelStorageStats> GetModelStorageStatsAsync(Guid modelId, string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT FileSizeBytes, CreatedAt, LastAccessedAt, FileHash, ModelFormat
                FROM ModelFiles
                WHERE ModelId = @ModelId AND UserId = @UserId";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new FileNotFoundException($"Model not found: {modelId}");
            }

            return new ModelStorageStats(
                ModelId: modelId,
                FileSizeBytes: reader.GetInt64("FileSizeBytes"),
                CreatedAt: reader.GetDateTime("CreatedAt"),
                LastAccessedAt: reader.GetDateTime("LastAccessedAt"),
                FileHash: reader.GetString("FileHash"),
                Format: Enum.Parse<ModelFormat>(reader.GetString("ModelFormat")),
                CompressionRatio: "1.0"); // FILESTREAM doesn't compress
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage stats for model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Validate model file integrity and format
    /// </summary>
    public async Task<ModelValidationResult> ValidateModelAsync(Guid modelId, string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Call SQL CLR function for validation
            var query = "SELECT dbo.ValidateModelFile(@ModelId, @UserId)";

            using var command = new SqlCommand(query, connection);
            command.Parameters.Add("@ModelId", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@UserId", SqlDbType.NVarChar, 128).Value = userId;

            var resultJson = await command.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(resultJson))
            {
                return new ModelValidationResult(
                    ModelId: modelId,
                    IsValid: false,
                    DetectedFormat: ModelFormat.Unknown,
                    ValidationErrors: new[] { "Validation failed to execute" },
                    ValidationWarnings: Enumerable.Empty<string>());
            }

            var validationData = JsonSerializer.Deserialize<ModelValidationDto>(resultJson);

            return new ModelValidationResult(
                ModelId: modelId,
                IsValid: validationData!.IsValid,
                DetectedFormat: Enum.Parse<ModelFormat>(validationData.DetectedFormat),
                ValidationErrors: validationData.ValidationErrors,
                ValidationWarnings: validationData.ValidationWarnings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model validation failed for {ModelId}", modelId);
            return new ModelValidationResult(
                ModelId: modelId,
                IsValid: false,
                DetectedFormat: ModelFormat.Unknown,
                ValidationErrors: new[] { ex.Message },
                ValidationWarnings: Enumerable.Empty<string>());
        }
    }

    #region Private Methods

    private async Task<string> CalculateFileHashAsync(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var originalPosition = stream.Position;
        stream.Position = 0;
        var hash = await sha256.ComputeHashAsync(stream);
        stream.Position = originalPosition;
        return Convert.ToHexString(hash);
    }

    private async Task<string> InsertModelRecordAsync(
        FileStreamTransactionContext context,
        Guid modelId,
        ModelFormat modelFormat,
        ModelMetadata metadata,
        string userId,
        string fileHash,
        long fileSize)
    {
        var query = @"
            INSERT INTO ModelFiles (
                ModelId, ModelFormat, ModelName, Version, Description, Properties,
                UserId, FileHash, FileSizeBytes, Status, CreatedAt, LastAccessedAt,
                ModelData
            ) VALUES (
                @ModelId, @ModelFormat, @ModelName, @Version, @Description, @Properties,
                @UserId, @FileHash, @FileSizeBytes, 'Uploading', GETUTCDATE(), GETUTCDATE(),
                0x
            );

            SELECT ModelData.PathName() FROM ModelFiles WHERE ModelId = @ModelId";

        var parameters = new[]
        {
            new SqlParameter("@ModelId", SqlDbType.UniqueIdentifier) { Value = modelId },
            new SqlParameter("@ModelFormat", SqlDbType.NVarChar, 50) { Value = modelFormat.ToString() },
            new SqlParameter("@ModelName", SqlDbType.NVarChar, 255) { Value = metadata.ModelName },
            new SqlParameter("@Version", SqlDbType.NVarChar, 100) { Value = metadata.Version },
            new SqlParameter("@Description", SqlDbType.NVarChar, -1) { Value = metadata.Description },
            new SqlParameter("@Properties", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(metadata.Properties) },
            new SqlParameter("@UserId", SqlDbType.NVarChar, 128) { Value = userId },
            new SqlParameter("@FileHash", SqlDbType.NVarChar, 64) { Value = fileHash },
            new SqlParameter("@FileSizeBytes", SqlDbType.BigInt) { Value = fileSize }
        };

        var filePath = await context.ExecuteScalarAsync<string>(query, parameters);
        return filePath ?? throw new InvalidOperationException("Failed to get FILESTREAM path");
    }

    private async Task<IntPtr> OpenFileHandleAsync(string fileStreamPath)
    {
        // Implementation would open native file handle for memory-mapped access
        // This is a placeholder - actual implementation would use Win32 APIs
        return IntPtr.Zero;
    }

    #endregion

    #region Mapping Methods

    private static LayerDefinition MapLayerDefinition(LayerDefinitionDto dto) =>
        new(dto.LayerName, dto.LayerType, dto.InputShape, dto.OutputShape, dto.Parameters);

    private static ComponentDefinition MapComponentDefinition(ComponentDefinitionDto dto) =>
        new(dto.ComponentName, dto.ComponentType, dto.LayerName, dto.Shape, dto.DataType);

    private static ModelConfiguration MapModelConfiguration(ModelConfigurationDto dto) =>
        new(dto.Architecture, dto.Hyperparameters, dto.Framework, dto.Version);

    private static ComponentActivation MapComponentActivation(ComponentActivationDto dto) =>
        new(dto.ComponentName, dto.ActivationValues, dto.MaxActivation, dto.MeanActivation);

    private static ActivationPattern MapActivationPattern(ActivationPatternDto dto) =>
        new(dto.PatternName, dto.InvolvedComponents, dto.Strength, dto.Description);

    #endregion
}

#region DTO Classes for JSON Serialization

internal record ModelArchitectureDto(
    string ModelName,
    string Format,
    LayerDefinitionDto[] Layers,
    ComponentDefinitionDto[] Components,
    ModelConfigurationDto Configuration,
    long TotalParameters);

internal record LayerDefinitionDto(
    string LayerName,
    string LayerType,
    int[] InputShape,
    int[] OutputShape,
    Dictionary<string, object> Parameters);

internal record ComponentDefinitionDto(
    string ComponentName,
    string ComponentType,
    string LayerName,
    int[] Shape,
    string DataType);

internal record ModelConfigurationDto(
    string Architecture,
    Dictionary<string, object> Hyperparameters,
    string Framework,
    string Version);

internal record ComponentWeightDto(
    string ComponentPath,
    string ComponentName,
    string ComponentType,
    string WeightDataBase64,
    int[] Shape,
    string DataType);

internal record ActivationTraceDto(
    ComponentActivationDto[] Activations,
    ActivationPatternDto[] Patterns,
    long ProcessingTimeMs);

internal record ComponentActivationDto(
    string ComponentName,
    float[] ActivationValues,
    double MaxActivation,
    double MeanActivation);

internal record ActivationPatternDto(
    string PatternName,
    string[] InvolvedComponents,
    double Strength,
    string Description);

internal record ModelValidationDto(
    bool IsValid,
    string DetectedFormat,
    string[] ValidationErrors,
    string[] ValidationWarnings);

#endregion