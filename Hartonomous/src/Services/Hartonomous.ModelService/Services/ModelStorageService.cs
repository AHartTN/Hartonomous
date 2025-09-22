using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Handles SQL Server storage operations for models and components
/// </summary>
public class ModelStorageService
{
    private readonly string _connectionString;

    public ModelStorageService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentException("DefaultConnection is required");
    }

    public async Task StoreFoundationModelAsync(Guid modelId, string modelName, string modelPath, ModelStructure structure, string userId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var parameterCount = structure.Tensors.Sum(t => t.Dimensions.Aggregate(1L, (a, b) => a * b));
        var fileInfo = new FileInfo(modelPath);

        var sql = @"
            INSERT INTO FoundationModels (
                ModelId, ModelName, ModelFormat, FilePath, ModelSizeBytes,
                ParameterCount, Metadata, Architecture, ContextLength,
                UserId, CreatedAt, Status
            ) VALUES (
                @ModelId, @ModelName, 'GGUF', @FilePath, @ModelSizeBytes,
                @ParameterCount, @Metadata, @Architecture, @ContextLength,
                @UserId, GETUTCDATE(), 'Processing'
            )";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ModelId", modelId);
        command.Parameters.AddWithValue("@ModelName", modelName);
        command.Parameters.AddWithValue("@FilePath", modelPath);
        command.Parameters.AddWithValue("@ModelSizeBytes", fileInfo.Length);
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