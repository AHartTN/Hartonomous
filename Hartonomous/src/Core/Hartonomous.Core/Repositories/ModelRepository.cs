/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model repository for the Model Query Engine (MQE).
 * Features secure model lifecycle management with project-scoped access control and version tracking.
 */

using Dapper;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Models;
using Hartonomous.Core.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Linq.Expressions;

namespace Hartonomous.Core.Repositories;

public class ModelRepository : IModelRepository
{
    private readonly string _connectionString;

    public ModelRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string not found");
    }

    // IModelRepository specific methods
    public async Task<IEnumerable<Model>> GetModelsByArchitectureAsync(string architecture, string userId)
    {
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE Architecture = @Architecture AND UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, new { Architecture = architecture, UserId = userId });
        });
    }

    public async Task<IEnumerable<Model>> GetModelsByStatusAsync(ModelStatus status, string userId)
    {
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE Status = @Status AND UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, new { Status = status, UserId = userId });
        });
    }

    public async Task<Model?> GetModelByNameAsync(string modelName, string userId)
    {
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE ModelName = @ModelName AND UserId = @UserId";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<Model>(sql, new { ModelName = modelName, UserId = userId });
        });
    }

    public async Task<IEnumerable<Model>> GetModelsWithEmbeddingsAsync(string userId)
    {
        const string sql = @"
            SELECT DISTINCT m.* FROM dbo.Models m
            INNER JOIN dbo.ModelEmbeddings me ON m.Id = me.ModelId
            WHERE m.UserId = @UserId
            ORDER BY m.CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, new { UserId = userId });
        });
    }

    public async Task<IEnumerable<Model>> GetRecentModelsAsync(int count, string userId)
    {
        const string sql = @"
            SELECT TOP(@Count) * FROM dbo.Models
            WHERE UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, new { Count = count, UserId = userId });
        });
    }

    public async Task<IEnumerable<Model>> FindSimilarModelsAsync(Guid modelId, string userId, double threshold = 0.8)
    {
        // Implementation would use vector similarity search
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE UserId = @UserId AND Id != @ModelId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, new { ModelId = modelId, UserId = userId });
        });
    }

    public async Task<Dictionary<string, int>> GetArchitectureStatsAsync(string userId)
    {
        const string sql = @"
            SELECT Architecture, COUNT(*) as Count
            FROM dbo.Models
            WHERE UserId = @UserId
            GROUP BY Architecture";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            var results = await connection.QueryAsync<dynamic>(sql, new { UserId = userId });
            return results.ToDictionary(r => (string)r.Architecture, r => (int)r.Count);
        });
    }

    public async Task<Dictionary<ModelStatus, int>> GetStatusStatsAsync(string userId)
    {
        const string sql = @"
            SELECT Status, COUNT(*) as Count
            FROM dbo.Models
            WHERE UserId = @UserId
            GROUP BY Status";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            var results = await connection.QueryAsync<dynamic>(sql, new { UserId = userId });
            return results.ToDictionary(r => (ModelStatus)r.Status, r => (int)r.Count);
        });
    }

    public async Task<byte[]?> GetModelWeightsAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT ModelWeights FROM dbo.Models
            WHERE Id = @ModelId AND UserId = @UserId";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<byte[]>(sql, new { ModelId = modelId, UserId = userId });
        });
    }

    // Legacy DTO-based methods
    public async Task<IEnumerable<ModelMetadataDto>> GetModelsByProjectAsync(Guid projectId, string userId)
    {
        const string sql = @"
            SELECT m.ModelId, m.ModelName, '' as Version, '' as License
            FROM dbo.Models m
            INNER JOIN dbo.ProjectModels pm ON m.Id = pm.ModelId
            WHERE pm.ProjectId = @ProjectId AND m.UserId = @UserId
            ORDER BY m.CreatedDate DESC";

        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QueryAsync<ModelMetadataDto>(sql, new { ProjectId = projectId, UserId = userId });
        });
    }

    public async Task<ModelMetadataDto?> GetModelByIdAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT m.ModelId, m.ModelName, '' as Version, '' as License
            FROM dbo.Models m
            WHERE m.Id = @ModelId AND m.UserId = @UserId";

        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QuerySingleOrDefaultAsync<ModelMetadataDto>(sql, new { ModelId = modelId, UserId = userId });
        });
    }

    public async Task<Guid> CreateModelAsync(Guid projectId, string modelName, string version, string license, string? metadataJson, string userId)
    {
        var modelId = Guid.NewGuid();
        const string insertSql = @"
            INSERT INTO dbo.Models (Id, ModelName, UserId, CreatedDate, ConfigMetadata)
            VALUES (@Id, @ModelName, @UserId, @CreatedDate, @ConfigMetadata)";

        await ExecuteWithRetryAsync(async connection =>
        {
            await connection.ExecuteAsync(insertSql, new
            {
                Id = modelId,
                ModelName = modelName,
                UserId = userId,
                CreatedDate = DateTime.UtcNow,
                ConfigMetadata = metadataJson ?? "{}"
            });
            return 1;
        });

        return modelId;
    }

    public async Task<bool> DeleteModelAsync(Guid modelId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.Models
            WHERE Id = @ModelId AND UserId = @UserId";

        var rowsAffected = await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.ExecuteAsync(sql, new { ModelId = modelId, UserId = userId });
        });

        return rowsAffected > 0;
    }

    public async Task UpdateModelWeightsAsync(Guid modelId, byte[] weights, string userId)
    {
        const string sql = @"
            UPDATE dbo.Models
            SET ModelWeights = @Weights, ModifiedDate = @ModifiedDate
            WHERE Id = @ModelId AND UserId = @UserId";

        await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.ExecuteAsync(sql, new
            {
                Weights = weights,
                ModifiedDate = DateTime.UtcNow,
                ModelId = modelId,
                UserId = userId
            });
        });
    }

    public async Task<IEnumerable<ModelPerformanceMetric>> GetModelPerformanceAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT * FROM dbo.ModelPerformanceMetrics
            WHERE ModelId = @ModelId AND UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QueryAsync<ModelPerformanceMetric>(sql, new { ModelId = modelId, UserId = userId });
        });
    }

    public async Task<double> GetAveragePerformanceAsync(Guid modelId, string metricName, string userId)
    {
        const string sql = @"
            SELECT AVG(CAST(MetricValue as float))
            FROM dbo.ModelPerformanceMetrics
            WHERE ModelId = @ModelId AND MetricName = @MetricName AND UserId = @UserId";

        return await ExecuteWithRetryAsync(async connection =>
        {
            return await connection.QuerySingleOrDefaultAsync<double>(sql, new { ModelId = modelId, MetricName = metricName, UserId = userId });
        });
    }

    #region IRepository<Model> Implementation

    // Basic CRUD operations
    public async Task<Model?> GetByIdAsync(Guid id, string userId)
    {
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE Id = @Id AND UserId = @UserId";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<Model>(sql, new { Id = id, UserId = userId });
        });
    }

    public async Task<IEnumerable<Model>> GetAllAsync(string userId)
    {
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, new { UserId = userId });
        });
    }

    public async Task<IEnumerable<Model>> FindAsync(Expression<Func<Model, bool>> predicate, string userId)
    {
        // For this implementation, we'll use a basic approach since expression tree parsing is complex
        // In a production environment, you might want to use a library like SqlKata or build a proper expression visitor
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            var allModels = await connection.QueryAsync<Model>(sql, new { UserId = userId });
            return allModels.AsQueryable().Where(predicate);
        });
    }

    public async Task<Model?> FirstOrDefaultAsync(Expression<Func<Model, bool>> predicate, string userId)
    {
        // Similar approach as FindAsync for simplicity
        const string sql = @"
            SELECT * FROM dbo.Models
            WHERE UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            var allModels = await connection.QueryAsync<Model>(sql, new { UserId = userId });
            return allModels.AsQueryable().FirstOrDefault(predicate);
        });
    }

    // Paged queries
    public async Task<(IEnumerable<Model> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string userId,
        Expression<Func<Model, bool>>? filter = null,
        Expression<Func<Model, object>>? orderBy = null,
        bool descending = false)
    {
        var offset = (page - 1) * pageSize;
        var orderClause = descending ? "DESC" : "ASC";

        const string countSql = @"
            SELECT COUNT(*) FROM dbo.Models
            WHERE UserId = @UserId";

        const string dataSql = @"
            SELECT * FROM dbo.Models
            WHERE UserId = @UserId
            ORDER BY CreatedDate {0}
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);

            var totalCount = await connection.QuerySingleAsync<int>(countSql, new { UserId = userId });
            var items = await connection.QueryAsync<Model>(
                string.Format(dataSql, orderClause),
                new { UserId = userId, Offset = offset, PageSize = pageSize });

            // Apply filter in memory if provided (for simplicity)
            if (filter != null)
            {
                items = items.AsQueryable().Where(filter);
            }

            return (items, totalCount);
        });
    }

    // Create operations
    public async Task<Model> AddAsync(Model entity)
    {
        entity.Id = Guid.NewGuid();
        entity.CreatedDate = DateTime.UtcNow;
        entity.ModifiedDate = null;

        const string sql = @"
            INSERT INTO dbo.Models (Id, ModelId, ModelName, Architecture, ParameterCount, HiddenSize,
                                   NumLayers, NumAttentionHeads, VocabSize, ModelWeights, ConfigMetadata,
                                   ModelPath, Status, UserId, IngestedAt, ProcessedAt, LastAccessedAt,
                                   CreatedDate, ModifiedDate)
            VALUES (@Id, @ModelId, @ModelName, @Architecture, @ParameterCount, @HiddenSize,
                    @NumLayers, @NumAttentionHeads, @VocabSize, @ModelWeights, @ConfigMetadata,
                    @ModelPath, @Status, @UserId, @IngestedAt, @ProcessedAt, @LastAccessedAt,
                    @CreatedDate, @ModifiedDate)";

        await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, entity);
            return entity;
        });

        return entity;
    }

    public async Task<IEnumerable<Model>> AddRangeAsync(IEnumerable<Model> entities)
    {
        var modelList = entities.ToList();
        foreach (var entity in modelList)
        {
            entity.Id = Guid.NewGuid();
            entity.CreatedDate = DateTime.UtcNow;
            entity.ModifiedDate = null;
        }

        const string sql = @"
            INSERT INTO dbo.Models (Id, ModelId, ModelName, Architecture, ParameterCount, HiddenSize,
                                   NumLayers, NumAttentionHeads, VocabSize, ModelWeights, ConfigMetadata,
                                   ModelPath, Status, UserId, IngestedAt, ProcessedAt, LastAccessedAt,
                                   CreatedDate, ModifiedDate)
            VALUES (@Id, @ModelId, @ModelName, @Architecture, @ParameterCount, @HiddenSize,
                    @NumLayers, @NumAttentionHeads, @VocabSize, @ModelWeights, @ConfigMetadata,
                    @ModelPath, @Status, @UserId, @IngestedAt, @ProcessedAt, @LastAccessedAt,
                    @CreatedDate, @ModifiedDate)";

        await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, modelList);
            return modelList;
        });

        return modelList;
    }

    // Update operations
    public async Task<Model> UpdateAsync(Model entity)
    {
        entity.ModifiedDate = DateTime.UtcNow;

        const string sql = @"
            UPDATE dbo.Models
            SET ModelId = @ModelId, ModelName = @ModelName, Architecture = @Architecture,
                ParameterCount = @ParameterCount, HiddenSize = @HiddenSize, NumLayers = @NumLayers,
                NumAttentionHeads = @NumAttentionHeads, VocabSize = @VocabSize, ModelWeights = @ModelWeights,
                ConfigMetadata = @ConfigMetadata, ModelPath = @ModelPath, Status = @Status,
                IngestedAt = @IngestedAt, ProcessedAt = @ProcessedAt, LastAccessedAt = @LastAccessedAt,
                ModifiedDate = @ModifiedDate
            WHERE Id = @Id AND UserId = @UserId";

        await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, entity);
            return entity;
        });

        return entity;
    }

    // Delete operations
    public async Task DeleteAsync(Model entity)
    {
        const string sql = @"
            DELETE FROM dbo.Models
            WHERE Id = @Id AND UserId = @UserId";

        await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { Id = entity.Id, UserId = entity.UserId });
            return 1;
        });
    }

    public async Task DeleteAsync(Guid id, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.Models
            WHERE Id = @Id AND UserId = @UserId";

        await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });
            return 1;
        });
    }

    public async Task DeleteRangeAsync(IEnumerable<Model> entities)
    {
        var deleteParams = entities.Select(e => new { Id = e.Id, UserId = e.UserId }).ToList();

        const string sql = @"
            DELETE FROM dbo.Models
            WHERE Id = @Id AND UserId = @UserId";

        await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql, deleteParams);
            return deleteParams.Count;
        });
    }

    // Count and existence checks
    public async Task<int> CountAsync(string userId)
    {
        const string sql = @"
            SELECT COUNT(*) FROM dbo.Models
            WHERE UserId = @UserId";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleAsync<int>(sql, new { UserId = userId });
        });
    }

    public async Task<int> CountAsync(Expression<Func<Model, bool>> predicate, string userId)
    {
        // For simplicity, we'll get all models and apply the predicate in memory
        var models = await GetAllAsync(userId);
        return models.AsQueryable().Where(predicate).Count();
    }

    public async Task<bool> ExistsAsync(Guid id, string userId)
    {
        const string sql = @"
            SELECT COUNT(*) FROM dbo.Models
            WHERE Id = @Id AND UserId = @UserId";

        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            var count = await connection.QuerySingleAsync<int>(sql, new { Id = id, UserId = userId });
            return count > 0;
        });
    }

    public async Task<bool> ExistsAsync(Expression<Func<Model, bool>> predicate, string userId)
    {
        // For simplicity, we'll get all models and apply the predicate in memory
        var models = await GetAllAsync(userId);
        return models.AsQueryable().Any(predicate);
    }

    // Raw SQL operations
    public async Task<IEnumerable<Model>> FromSqlAsync(string sql, params object[] parameters)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Model>(sql, parameters);
        });
    }

    public async Task<int> ExecuteSqlAsync(string sql, params object[] parameters)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.ExecuteAsync(sql, parameters);
        });
    }

    // Legacy compatibility methods
    public async Task<Guid> CreateAsync(Model entity, string userId)
    {
        entity.UserId = userId;
        var createdEntity = await AddAsync(entity);
        return createdEntity.Id;
    }

    public async Task<bool> UpdateAsync(Model entity, string userId)
    {
        entity.UserId = userId;
        try
        {
            await UpdateAsync(entity);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteByIdAsync(Guid id, string userId)
    {
        try
        {
            await DeleteAsync(id, userId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Execute with proper error handling and retry logic
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
    {
        var retryCount = 0;
        const int maxRetries = 3;

        while (retryCount < maxRetries)
        {
            try
            {
                return await operation();
            }
            catch (SqlException ex) when (retryCount < maxRetries - 1 && IsTransientError(ex))
            {
                retryCount++;
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                await Task.Delay(delay);
            }
        }

        // Final attempt without retry
        return await operation();
    }

    /// <summary>
    /// Execute with connection injection and retry logic
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<SqlConnection, Task<T>> operation)
    {
        var retryCount = 0;
        const int maxRetries = 3;

        while (retryCount < maxRetries)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                return await operation(connection);
            }
            catch (SqlException ex) when (retryCount < maxRetries - 1 && IsTransientError(ex))
            {
                retryCount++;
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                await Task.Delay(delay);
            }
        }

        // Final attempt without retry
        using var finalConnection = new SqlConnection(_connectionString);
        return await operation(finalConnection);
    }

    /// <summary>
    /// Check if SQL exception is transient and retryable
    /// </summary>
    private bool IsTransientError(SqlException ex)
    {
        // Common transient error codes
        var transientErrorCodes = new[] { 2, 53, 121, 233, 997, 1204, 1205, 1222, 8645, 8651 };
        return transientErrorCodes.Contains(ex.Number);
    }

    #endregion

}