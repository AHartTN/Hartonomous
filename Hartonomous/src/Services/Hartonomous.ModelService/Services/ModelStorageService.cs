using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Handles SQL Server storage operations for models and components
/// Integrates with FILESTREAM for large model file storage
/// </summary>
public class ModelStorageService
{
    private readonly string _connectionString;
    private readonly IModelDataService _fileStreamService;
    private readonly ILogger<ModelStorageService> _logger;

    public ModelStorageService(
        IConfiguration configuration,
        IModelDataService fileStreamService,
        ILogger<ModelStorageService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentException("DefaultConnection is required");
        _fileStreamService = fileStreamService;
        _logger = logger;
    }

    public async Task StoreFoundationModelAsync(Guid modelId, string modelName, string modelPath, ModelStructure structure, string userId)
    {
        _logger.LogInformation("Storing foundation model {ModelId} using FILESTREAM", modelId);

        try
        {
            var parameterCount = structure.Tensors.Sum(t => t.Dimensions.Aggregate(1L, (a, b) => a * b));
            var fileInfo = new FileInfo(modelPath);

            // Create model metadata for FILESTREAM service
            var metadata = new ModelMetadata(
                ModelName: modelName,
                Version: "1.0",
                Description: $"GGUF model with {parameterCount:N0} parameters",
                Properties: new Dictionary<string, object>
                {
                    ["architecture"] = structure.Metadata.TryGetValue("general.architecture", out var arch) ? arch.ToString() : "unknown",
                    ["context_length"] = structure.Metadata.TryGetValue("llama.context_length", out var ctx) ? Convert.ToInt32(ctx) : 0,
                    ["parameter_count"] = parameterCount,
                    ["tensor_count"] = structure.Tensors.Count,
                    ["metadata"] = structure.Metadata
                });

            // Store model file using FILESTREAM
            using var fileStream = new FileStream(modelPath, FileMode.Open, FileAccess.Read);
            var storageResult = await _fileStreamService.StoreModelAsync(
                modelId, fileStream, ModelFormat.GGUF, metadata, userId);

            if (!storageResult.Success)
            {
                throw new InvalidOperationException($"FILESTREAM storage failed: {storageResult.ErrorMessage}");
            }

            // Store model metadata in FoundationModels table
            await StoreModelMetadataAsync(modelId, modelName, storageResult, structure, userId, parameterCount);

            _logger.LogInformation("Foundation model stored successfully: {ModelId}, Size: {Size} bytes",
                modelId, storageResult.FileSizeBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store foundation model {ModelId}", modelId);
            throw;
        }
    }

    private async Task StoreModelMetadataAsync(
        Guid modelId,
        string modelName,
        ModelStorageResult storageResult,
        ModelStructure structure,
        string userId,
        long parameterCount)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO FoundationModels (
                ModelId, ModelName, ModelFormat, FilePath, ModelSizeBytes,
                ParameterCount, Metadata, Architecture, ContextLength,
                UserId, CreatedAt, Status
            ) VALUES (
                @ModelId, @ModelName, @ModelFormat, @FilePath, @ModelSizeBytes,
                @ParameterCount, @Metadata, @Architecture, @ContextLength,
                @UserId, GETUTCDATE(), 'Processing'
            )";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ModelId", modelId);
        command.Parameters.AddWithValue("@ModelName", modelName);
        command.Parameters.AddWithValue("@ModelFormat", storageResult.Format.ToString());
        command.Parameters.AddWithValue("@FilePath", storageResult.FilePath);
        command.Parameters.AddWithValue("@ModelSizeBytes", storageResult.FileSizeBytes);
        command.Parameters.AddWithValue("@ParameterCount", parameterCount);
        command.Parameters.AddWithValue("@Metadata", JsonSerializer.Serialize(structure.Metadata));
        command.Parameters.AddWithValue("@Architecture",
            structure.Metadata.TryGetValue("general.architecture", out var arch) ? arch.ToString() : "unknown");
        command.Parameters.AddWithValue("@ContextLength",
            structure.Metadata.TryGetValue("llama.context_length", out var ctx) ? Convert.ToInt32(ctx) : 0);
        command.Parameters.AddWithValue("@UserId", userId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task StoreComponentsAsync(List<ExtractedComponent> components)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var component in components)
        {
            var sql = @"
                INSERT INTO ModelComponents (
                    ComponentId, ModelId, ComponentName, ComponentType,
                    LayerName, LayerIndex, Shape, DataType, ParameterCount,
                    RelevanceScore, FunctionalDescription, UserId, CreatedAt
                ) VALUES (
                    @ComponentId, @ModelId, @ComponentName, @ComponentType,
                    @LayerName, @LayerIndex, @Shape, @DataType, @ParameterCount,
                    @RelevanceScore, @FunctionalDescription, @UserId, GETUTCDATE()
                )";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ComponentId", component.ComponentId);
            command.Parameters.AddWithValue("@ModelId", component.ModelId);
            command.Parameters.AddWithValue("@ComponentName", component.Name);
            command.Parameters.AddWithValue("@ComponentType", component.Type);
            command.Parameters.AddWithValue("@LayerName", component.LayerInfo.Name);
            command.Parameters.AddWithValue("@LayerIndex", component.LayerInfo.Index);
            command.Parameters.AddWithValue("@Shape", JsonSerializer.Serialize(component.Shape));
            command.Parameters.AddWithValue("@DataType", component.DataType);
            command.Parameters.AddWithValue("@ParameterCount", component.ParameterCount);
            command.Parameters.AddWithValue("@RelevanceScore", component.RelevanceScore);
            command.Parameters.AddWithValue("@FunctionalDescription", component.Description);
            command.Parameters.AddWithValue("@UserId", component.UserId);

            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task MarkCompleteAsync(Guid modelId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            UPDATE FoundationModels
            SET Status = 'Complete', ProcessedAt = GETUTCDATE()
            WHERE ModelId = @ModelId";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ModelId", modelId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkFailedAsync(Guid modelId, string errorMessage)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            UPDATE FoundationModels
            SET Status = 'Failed', ErrorMessage = @ErrorMessage, ProcessedAt = GETUTCDATE()
            WHERE ModelId = @ModelId";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ModelId", modelId);
        command.Parameters.AddWithValue("@ErrorMessage", errorMessage);
        await command.ExecuteNonQueryAsync();
    }
}