using Dapper;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Hartonomous.Core.Repositories;

public class ModelRepository : IModelRepository
{
    private readonly string _connectionString;

    public ModelRepository(IConfiguration configuration)
    {
        _connectionString = configuration["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("DefaultConnection string not found");
    }

    public async Task<IEnumerable<ModelMetadataDto>> GetModelsByProjectAsync(Guid projectId, string userId)
    {
        const string sql = @"
            SELECT m.ModelId, m.ModelName, m.Version, m.License
            FROM dbo.ModelMetadata m
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE m.ProjectId = @ProjectId AND p.UserId = @UserId
            ORDER BY m.CreatedAt DESC";

        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ModelMetadataDto>(sql, new { ProjectId = projectId, UserId = userId });
    }

    public async Task<ModelMetadataDto?> GetModelByIdAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT m.ModelId, m.ModelName, m.Version, m.License
            FROM dbo.ModelMetadata m
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE m.ModelId = @ModelId AND p.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<ModelMetadataDto>(sql, new { ModelId = modelId, UserId = userId });
    }

    public async Task<Guid> CreateModelAsync(Guid projectId, string modelName, string version, string license, string? metadataJson, string userId)
    {
        // First verify user owns the project
        const string verifyProjectSql = @"
            SELECT COUNT(1) FROM dbo.Projects
            WHERE ProjectId = @ProjectId AND UserId = @UserId";

        var modelId = Guid.NewGuid();

        const string insertSql = @"
            INSERT INTO dbo.ModelMetadata (ModelId, ProjectId, ModelName, Version, License, MetadataJson, CreatedAt)
            SELECT @ModelId, @ProjectId, @ModelName, @Version, @License, @MetadataJson, @CreatedAt
            WHERE EXISTS (
                SELECT 1 FROM dbo.Projects
                WHERE ProjectId = @ProjectId AND UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);

        var projectExists = await connection.QuerySingleAsync<int>(verifyProjectSql, new { ProjectId = projectId, UserId = userId });
        if (projectExists == 0)
        {
            throw new UnauthorizedAccessException("Project not found or access denied");
        }

        var rowsAffected = await connection.ExecuteAsync(insertSql, new
        {
            ModelId = modelId,
            ProjectId = projectId,
            ModelName = modelName,
            Version = version,
            License = license,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.UtcNow,
            UserId = userId
        });

        if (rowsAffected == 0)
        {
            throw new InvalidOperationException("Failed to create model - project verification failed");
        }

        return modelId;
    }

    public async Task<bool> DeleteModelAsync(Guid modelId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.ModelMetadata
            WHERE ModelId = @ModelId
            AND EXISTS (
                SELECT 1 FROM dbo.Projects p
                WHERE p.ProjectId = ModelMetadata.ProjectId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { ModelId = modelId, UserId = userId });
        return rowsAffected > 0;
    }
}