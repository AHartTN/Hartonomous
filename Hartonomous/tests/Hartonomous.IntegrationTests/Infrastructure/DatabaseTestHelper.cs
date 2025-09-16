using Microsoft.Data.SqlClient;
using Hartonomous.Core.DTOs;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.Infrastructure;

/// <summary>
/// Helper class for database operations in integration tests
/// </summary>
public class DatabaseTestHelper
{
    private readonly string _connectionString;

    public DatabaseTestHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> ProjectExistsAsync(Guid projectId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Projects WHERE ProjectId = @ProjectId";
        command.Parameters.AddWithValue("@ProjectId", projectId);

        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    public async Task<ProjectDto?> GetProjectAsync(Guid projectId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ProjectId, ProjectName, CreatedAt
            FROM dbo.Projects
            WHERE ProjectId = @ProjectId";
        command.Parameters.AddWithValue("@ProjectId", projectId);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ProjectDto(
                reader.GetGuid("ProjectId"),
                reader.GetString("ProjectName"),
                reader.GetDateTime("CreatedAt"));
        }

        return null;
    }

    public async Task<List<ProjectDto>> GetProjectsByUserAsync(string userId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ProjectId, ProjectName, CreatedAt
            FROM dbo.Projects
            WHERE UserId = @UserId
            ORDER BY CreatedAt DESC";
        command.Parameters.AddWithValue("@UserId", userId);

        var projects = new List<ProjectDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projects.Add(new ProjectDto(
                reader.GetGuid("ProjectId"),
                reader.GetString("ProjectName"),
                reader.GetDateTime("CreatedAt")));
        }

        return projects;
    }

    public async Task<bool> ModelExistsAsync(Guid modelId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.ModelMetadata WHERE ModelId = @ModelId";
        command.Parameters.AddWithValue("@ModelId", modelId);

        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    public async Task<ModelMetadataDto?> GetModelAsync(Guid modelId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT m.ModelId, m.ProjectId, m.ModelName, m.Version, m.License, m.MetadataJson, m.CreatedAt,
                   p.ProjectName
            FROM dbo.ModelMetadata m
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE m.ModelId = @ModelId";
        command.Parameters.AddWithValue("@ModelId", modelId);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ModelMetadataDto(
                reader.GetGuid("ModelId"),
                reader.GetGuid("ProjectId"),
                reader.GetString("ProjectName"),
                reader.GetString("ModelName"),
                reader.GetString("Version"),
                reader.GetString("License"),
                reader.IsDBNull("MetadataJson") ? null : reader.GetString("MetadataJson"),
                reader.GetDateTime("CreatedAt"));
        }

        return null;
    }

    public async Task<List<ModelMetadataDto>> GetModelsByProjectAsync(Guid projectId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT m.ModelId, m.ProjectId, m.ModelName, m.Version, m.License, m.MetadataJson, m.CreatedAt,
                   p.ProjectName
            FROM dbo.ModelMetadata m
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE m.ProjectId = @ProjectId
            ORDER BY m.CreatedAt DESC";
        command.Parameters.AddWithValue("@ProjectId", projectId);

        var models = new List<ModelMetadataDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            models.Add(new ModelMetadataDto(
                reader.GetGuid("ModelId"),
                reader.GetGuid("ProjectId"),
                reader.GetString("ProjectName"),
                reader.GetString("ModelName"),
                reader.GetString("Version"),
                reader.GetString("License"),
                reader.IsDBNull("MetadataJson") ? null : reader.GetString("MetadataJson"),
                reader.GetDateTime("CreatedAt")));
        }

        return models;
    }

    public async Task<int> GetOutboxEventCountAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.OutboxEvents";

        return (int)await command.ExecuteScalarAsync();
    }

    public async Task<List<OutboxEvent>> GetOutboxEventsAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT EventId, EventType, Payload, CreatedAt, ProcessedAt
            FROM dbo.OutboxEvents
            ORDER BY CreatedAt DESC";

        var events = new List<OutboxEvent>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new OutboxEvent(
                reader.GetGuid("EventId"),
                reader.GetString("EventType"),
                reader.GetString("Payload"),
                reader.GetDateTime("CreatedAt"),
                reader.IsDBNull("ProcessedAt") ? null : reader.GetDateTime("ProcessedAt")));
        }

        return events;
    }

    public async Task<Guid> InsertTestProjectAsync(string userId, string projectName)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var projectId = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName, CreatedAt)
            VALUES (@ProjectId, @UserId, @ProjectName, @CreatedAt)";

        command.Parameters.AddWithValue("@ProjectId", projectId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@ProjectName", projectName);
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
        return projectId;
    }

    public async Task<Guid> InsertTestModelAsync(Guid projectId, string modelName, string version, string license, string? metadataJson = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        var modelId = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO dbo.ModelMetadata (ModelId, ProjectId, ModelName, Version, License, MetadataJson, CreatedAt)
            VALUES (@ModelId, @ProjectId, @ModelName, @Version, @License, @MetadataJson, @CreatedAt)";

        command.Parameters.AddWithValue("@ModelId", modelId);
        command.Parameters.AddWithValue("@ProjectId", projectId);
        command.Parameters.AddWithValue("@ModelName", modelName);
        command.Parameters.AddWithValue("@Version", version);
        command.Parameters.AddWithValue("@License", license);
        command.Parameters.AddWithValue("@MetadataJson", (object?)metadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
        return modelId;
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

        // Delete models first (foreign key constraint)
        var deleteModelsCommand = connection.CreateCommand();
        deleteModelsCommand.CommandText = "DELETE FROM dbo.ModelMetadata WHERE ProjectId = @ProjectId";
        deleteModelsCommand.Parameters.AddWithValue("@ProjectId", projectId);
        await deleteModelsCommand.ExecuteNonQueryAsync();

        // Delete project
        var deleteProjectCommand = connection.CreateCommand();
        deleteProjectCommand.CommandText = "DELETE FROM dbo.Projects WHERE ProjectId = @ProjectId";
        deleteProjectCommand.Parameters.AddWithValue("@ProjectId", projectId);
        await deleteProjectCommand.ExecuteNonQueryAsync();
    }
}

public record OutboxEvent(
    Guid EventId,
    string EventType,
    string Payload,
    DateTime CreatedAt,
    DateTime? ProcessedAt);

public record ModelMetadataDto(
    Guid ModelId,
    Guid ProjectId,
    string ProjectName,
    string ModelName,
    string Version,
    string License,
    string? MetadataJson,
    DateTime CreatedAt);