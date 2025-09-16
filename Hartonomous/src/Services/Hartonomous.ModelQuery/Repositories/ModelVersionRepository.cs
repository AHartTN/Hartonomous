using Dapper;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.ModelQuery.Repositories;

public class ModelVersionRepository : IModelVersionRepository
{
    private readonly string _connectionString;

    public ModelVersionRepository(IConfiguration configuration)
    {
        _connectionString = configuration["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("DefaultConnection string not found");
    }

    public async Task<IEnumerable<ModelVersionDto>> GetModelVersionsAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT mv.VersionId, mv.ModelId, mv.Version, mv.Description, mv.Changes,
                   mv.ParentVersion, mv.CreatedAt, mv.CreatedBy
            FROM dbo.ModelVersions mv
            INNER JOIN dbo.ModelMetadata m ON mv.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE mv.ModelId = @ModelId AND p.UserId = @UserId
            ORDER BY mv.CreatedAt DESC";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ModelId = modelId, UserId = userId });

        return results.Select(r => new ModelVersionDto(
            r.VersionId,
            r.ModelId,
            r.Version,
            r.Description,
            JsonSerializer.Deserialize<Dictionary<string, object>>(r.Changes) ?? new Dictionary<string, object>(),
            r.ParentVersion,
            r.CreatedAt,
            r.CreatedBy
        ));
    }

    public async Task<ModelVersionDto?> GetVersionByIdAsync(Guid versionId, string userId)
    {
        const string sql = @"
            SELECT mv.VersionId, mv.ModelId, mv.Version, mv.Description, mv.Changes,
                   mv.ParentVersion, mv.CreatedAt, mv.CreatedBy
            FROM dbo.ModelVersions mv
            INNER JOIN dbo.ModelMetadata m ON mv.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE mv.VersionId = @VersionId AND p.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync(sql, new { VersionId = versionId, UserId = userId });

        if (result == null) return null;

        return new ModelVersionDto(
            result.VersionId,
            result.ModelId,
            result.Version,
            result.Description,
            JsonSerializer.Deserialize<Dictionary<string, object>>(result.Changes) ?? new Dictionary<string, object>(),
            result.ParentVersion,
            result.CreatedAt,
            result.CreatedBy
        );
    }

    public async Task<ModelVersionDto?> GetLatestVersionAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT TOP 1 mv.VersionId, mv.ModelId, mv.Version, mv.Description, mv.Changes,
                   mv.ParentVersion, mv.CreatedAt, mv.CreatedBy
            FROM dbo.ModelVersions mv
            INNER JOIN dbo.ModelMetadata m ON mv.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE mv.ModelId = @ModelId AND p.UserId = @UserId
            ORDER BY mv.CreatedAt DESC";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync(sql, new { ModelId = modelId, UserId = userId });

        if (result == null) return null;

        return new ModelVersionDto(
            result.VersionId,
            result.ModelId,
            result.Version,
            result.Description,
            JsonSerializer.Deserialize<Dictionary<string, object>>(result.Changes) ?? new Dictionary<string, object>(),
            result.ParentVersion,
            result.CreatedAt,
            result.CreatedBy
        );
    }

    public async Task<Guid> CreateVersionAsync(Guid modelId, string version, string description, Dictionary<string, object> changes, string? parentVersion, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.ModelVersions (VersionId, ModelId, Version, Description, Changes, ParentVersion, CreatedAt, CreatedBy)
            SELECT @VersionId, @ModelId, @Version, @Description, @Changes, @ParentVersion, @CreatedAt, @CreatedBy
            WHERE EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = @ModelId AND p.UserId = @UserId
            )";

        var versionId = Guid.NewGuid();
        using var connection = new SqlConnection(_connectionString);

        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            VersionId = versionId,
            ModelId = modelId,
            Version = version,
            Description = description,
            Changes = JsonSerializer.Serialize(changes),
            ParentVersion = parentVersion,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            UserId = userId
        });

        if (rowsAffected == 0)
        {
            throw new UnauthorizedAccessException("Model not found or access denied");
        }

        return versionId;
    }

    public async Task<bool> DeleteVersionAsync(Guid versionId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.ModelVersions
            WHERE VersionId = @VersionId
            AND EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = ModelVersions.ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { VersionId = versionId, UserId = userId });
        return rowsAffected > 0;
    }

    public async Task<ModelComparisonDto?> CompareVersionsAsync(Guid versionAId, Guid versionBId, string userId)
    {
        const string sql = @"
            SELECT va.VersionId as VersionAId, va.ModelId as ModelAId, va.Version as VersionA, va.Changes as ChangesA,
                   vb.VersionId as VersionBId, vb.ModelId as ModelBId, vb.Version as VersionB, vb.Changes as ChangesB
            FROM dbo.ModelVersions va
            CROSS JOIN dbo.ModelVersions vb
            INNER JOIN dbo.ModelMetadata ma ON va.ModelId = ma.ModelId
            INNER JOIN dbo.ModelMetadata mb ON vb.ModelId = mb.ModelId
            INNER JOIN dbo.Projects pa ON ma.ProjectId = pa.ProjectId
            INNER JOIN dbo.Projects pb ON mb.ProjectId = pb.ProjectId
            WHERE va.VersionId = @VersionAId AND vb.VersionId = @VersionBId
            AND pa.UserId = @UserId AND pb.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync(sql, new { VersionAId = versionAId, VersionBId = versionBId, UserId = userId });

        if (result == null) return null;

        var changesA = JsonSerializer.Deserialize<Dictionary<string, object>>(result.ChangesA) ?? new Dictionary<string, object>();
        var changesB = JsonSerializer.Deserialize<Dictionary<string, object>>(result.ChangesB) ?? new Dictionary<string, object>();

        // Calculate differences and similarities
        var differences = new Dictionary<string, object>();
        var similarities = new Dictionary<string, double>();

        // Find differences in changes
        foreach (var kvp in changesA)
        {
            if (!changesB.ContainsKey(kvp.Key))
            {
                differences[$"only_in_a_{kvp.Key}"] = kvp.Value;
            }
            else if (!kvp.Value.Equals(changesB[kvp.Key]))
            {
                differences[$"diff_{kvp.Key}"] = new { A = kvp.Value, B = changesB[kvp.Key] };
            }
        }

        foreach (var kvp in changesB)
        {
            if (!changesA.ContainsKey(kvp.Key))
            {
                differences[$"only_in_b_{kvp.Key}"] = kvp.Value;
            }
        }

        // Calculate basic similarity metrics
        var commonKeys = changesA.Keys.Intersect(changesB.Keys).ToList();
        var totalKeys = changesA.Keys.Union(changesB.Keys).Count();
        similarities["key_overlap"] = totalKeys > 0 ? (double)commonKeys.Count / totalKeys : 0.0;

        return new ModelComparisonDto(
            result.ModelAId,
            result.ModelBId,
            "version_comparison",
            differences,
            similarities,
            commonKeys,
            changesA.Keys.Except(changesB.Keys).Union(changesB.Keys.Except(changesA.Keys)).ToList(),
            DateTime.UtcNow
        );
    }
}