/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow template repository for real database template management.
 */

using Dapper;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Hartonomous.Orchestration.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.Orchestration.Repositories;

/// <summary>
/// Repository implementation for workflow template management using Dapper
/// </summary>
public class WorkflowTemplateRepository : IWorkflowTemplateRepository
{
    private readonly string _connectionString;
    private readonly ILogger<WorkflowTemplateRepository> _logger;

    public WorkflowTemplateRepository(IConfiguration configuration, ILogger<WorkflowTemplateRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
        _logger = logger;
    }

    public async Task<Guid> CreateTemplateAsync(WorkflowTemplate template)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowTemplates
            (TemplateId, UserId, Name, Description, Category, TemplateDefinitionJson, ParametersJson, TagsJson,
             CreatedAt, UpdatedAt, CreatedBy, UsageCount, IsPublic, IsActive)
            VALUES
            (@TemplateId, @UserId, @Name, @Description, @Category, @TemplateDefinitionJson, @ParametersJson, @TagsJson,
             @CreatedAt, @UpdatedAt, @CreatedBy, @UsageCount, @IsPublic, @IsActive);";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            TemplateId = template.TemplateId,
            UserId = template.UserId,
            Name = template.Name,
            Description = template.Description,
            Category = template.Category,
            TemplateDefinitionJson = template.TemplateDefinitionJson,
            ParametersJson = template.ParametersJson,
            TagsJson = template.TagsJson,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            CreatedBy = template.CreatedBy,
            UsageCount = template.UsageCount,
            IsPublic = template.IsPublic,
            IsActive = template.IsActive
        });

        _logger.LogInformation("Created workflow template {TemplateId} for user {UserId}", template.TemplateId, template.UserId);
        return template.TemplateId;
    }

    public async Task<WorkflowTemplate?> GetTemplateByIdAsync(Guid templateId, string userId)
    {
        const string sql = @"
            SELECT TemplateId, UserId, Name, Description, Category, TemplateDefinitionJson, ParametersJson, TagsJson,
                   CreatedAt, UpdatedAt, CreatedBy, UsageCount, IsPublic, IsActive
            FROM dbo.WorkflowTemplates
            WHERE TemplateId = @TemplateId AND (UserId = @UserId OR IsPublic = 1) AND IsActive = 1;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync<WorkflowTemplate>(sql, new { TemplateId = templateId, UserId = userId });

        return result;
    }

    public async Task<bool> UpdateTemplateAsync(WorkflowTemplate template)
    {
        const string sql = @"
            UPDATE dbo.WorkflowTemplates
            SET Name = @Name, Description = @Description, Category = @Category,
                TemplateDefinitionJson = @TemplateDefinitionJson, ParametersJson = @ParametersJson,
                TagsJson = @TagsJson, UpdatedAt = @UpdatedAt, IsPublic = @IsPublic, IsActive = @IsActive
            WHERE TemplateId = @TemplateId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Name = template.Name,
            Description = template.Description,
            Category = template.Category,
            TemplateDefinitionJson = template.TemplateDefinitionJson,
            ParametersJson = template.ParametersJson,
            TagsJson = template.TagsJson,
            UpdatedAt = DateTime.UtcNow,
            IsPublic = template.IsPublic,
            IsActive = template.IsActive,
            TemplateId = template.TemplateId,
            UserId = template.UserId
        });

        return rowsAffected > 0;
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowTemplates
            SET IsActive = 0, UpdatedAt = @UpdatedAt
            WHERE TemplateId = @TemplateId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            TemplateId = templateId,
            UserId = userId,
            UpdatedAt = DateTime.UtcNow
        });

        return rowsAffected > 0;
    }

    public async Task<PaginatedResult<WorkflowTemplate>> SearchTemplatesAsync(
        string? query, string? category, List<string>? tags, bool includePublic, string userId,
        int page = 1, int pageSize = 20)
    {
        var whereClause = "WHERE IsActive = 1 AND (UserId = @UserId";
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        if (includePublic)
        {
            whereClause += " OR IsPublic = 1";
        }
        whereClause += ")";

        if (!string.IsNullOrEmpty(query))
        {
            whereClause += " AND (Name LIKE @Query OR Description LIKE @Query)";
            parameters.Add("Query", $"%{query}%");
        }

        if (!string.IsNullOrEmpty(category))
        {
            whereClause += " AND Category = @Category";
            parameters.Add("Category", category);
        }

        if (tags?.Any() == true)
        {
            var tagConditions = new List<string>();
            for (int i = 0; i < tags.Count; i++)
            {
                var tagParam = $"@Tag{i}";
                tagConditions.Add($"TagsJson LIKE {tagParam}");
                parameters.Add($"Tag{i}", $"%\"{tags[i]}\"%");
            }
            whereClause += $" AND ({string.Join(" OR ", tagConditions)})";
        }

        // Count total records
        var countSql = $"SELECT COUNT(*) FROM dbo.WorkflowTemplates {whereClause}";

        using var connection = new SqlConnection(_connectionString);
        var totalCount = await connection.QuerySingleAsync<int>(countSql, parameters);

        // Calculate pagination
        var offset = (page - 1) * pageSize;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // Get paginated results
        var sql = $@"
            SELECT TemplateId, UserId, Name, Description, Category, TemplateDefinitionJson, ParametersJson, TagsJson,
                   CreatedAt, UpdatedAt, CreatedBy, UsageCount, IsPublic, IsActive
            FROM dbo.WorkflowTemplates
            {whereClause}
            ORDER BY UsageCount DESC, CreatedAt DESC
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY;";

        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var results = await connection.QueryAsync<WorkflowTemplate>(sql, parameters);

        return new PaginatedResult<WorkflowTemplate>(
            results.ToList(),
            totalCount,
            page,
            pageSize,
            totalPages
        );
    }

    public async Task<List<WorkflowTemplate>> GetTemplatesByCategoryAsync(string category, bool includePublic, string userId)
    {
        var whereClause = "WHERE Category = @Category AND IsActive = 1 AND (UserId = @UserId";
        if (includePublic)
        {
            whereClause += " OR IsPublic = 1";
        }
        whereClause += ")";

        var sql = $@"
            SELECT TemplateId, UserId, Name, Description, Category, TemplateDefinitionJson, ParametersJson, TagsJson,
                   CreatedAt, UpdatedAt, CreatedBy, UsageCount, IsPublic, IsActive
            FROM dbo.WorkflowTemplates
            {whereClause}
            ORDER BY UsageCount DESC, CreatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<WorkflowTemplate>(sql, new { Category = category, UserId = userId });

        return results.ToList();
    }

    public async Task<List<WorkflowTemplate>> GetPopularTemplatesAsync(int limit = 10, bool includePublic = true)
    {
        var whereClause = "WHERE IsActive = 1";
        if (includePublic)
        {
            whereClause += " AND IsPublic = 1";
        }

        var sql = $@"
            SELECT TOP(@Limit) TemplateId, UserId, Name, Description, Category, TemplateDefinitionJson, ParametersJson, TagsJson,
                   CreatedAt, UpdatedAt, CreatedBy, UsageCount, IsPublic, IsActive
            FROM dbo.WorkflowTemplates
            {whereClause}
            ORDER BY UsageCount DESC, CreatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<WorkflowTemplate>(sql, new { Limit = limit });

        return results.ToList();
    }

    public async Task<Dictionary<string, object>> GetTemplateUsageStatsAsync(Guid templateId, string userId)
    {
        const string sql = @"
            SELECT
                t.UsageCount,
                t.CreatedAt,
                COUNT(we.ExecutionId) as WorkflowExecutions,
                AVG(CASE WHEN we.CompletedAt IS NOT NULL THEN DATEDIFF(SECOND, we.StartedAt, we.CompletedAt) ELSE NULL END) as AverageExecutionTime,
                CAST(SUM(CASE WHEN we.Status = 3 THEN 1 ELSE 0 END) AS FLOAT) / NULLIF(COUNT(we.ExecutionId), 0) as SuccessRate,
                MAX(we.StartedAt) as LastUsed
            FROM dbo.WorkflowTemplates t
            LEFT JOIN dbo.WorkflowDefinitions wd ON wd.Name LIKE '%' + t.Name + '%' AND wd.UserId = @UserId
            LEFT JOIN dbo.WorkflowExecutions we ON we.WorkflowId = wd.WorkflowId
            WHERE t.TemplateId = @TemplateId AND (t.UserId = @UserId OR t.IsPublic = 1)
            GROUP BY t.UsageCount, t.CreatedAt;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { TemplateId = templateId, UserId = userId });

        if (result == null)
        {
            return new Dictionary<string, object>();
        }

        return new Dictionary<string, object>
        {
            ["templateId"] = templateId,
            ["totalUsage"] = result.UsageCount ?? 0,
            ["lastUsed"] = result.LastUsed,
            ["averageExecutionTime"] = result.AverageExecutionTime ?? 0,
            ["successRate"] = result.SuccessRate ?? 0,
            ["workflowExecutions"] = result.WorkflowExecutions ?? 0
        };
    }

    public async Task<bool> IncrementTemplateUsageAsync(Guid templateId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowTemplates
            SET UsageCount = UsageCount + 1, UpdatedAt = @UpdatedAt
            WHERE TemplateId = @TemplateId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            TemplateId = templateId,
            UpdatedAt = DateTime.UtcNow
        });

        return rowsAffected > 0;
    }

    public async Task<bool> TemplateExistsAsync(Guid templateId, string userId)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM dbo.WorkflowTemplates
            WHERE TemplateId = @TemplateId AND (UserId = @UserId OR IsPublic = 1) AND IsActive = 1;";

        using var connection = new SqlConnection(_connectionString);
        var count = await connection.QuerySingleAsync<int>(sql, new { TemplateId = templateId, UserId = userId });

        return count > 0;
    }

    public async Task<List<WorkflowTemplate>> GetTemplatesByUserAsync(string userId, int limit = 100)
    {
        const string sql = @"
            SELECT TOP(@Limit) TemplateId, UserId, Name, Description, Category, TemplateDefinitionJson, ParametersJson, TagsJson,
                   CreatedAt, UpdatedAt, CreatedBy, UsageCount, IsPublic, IsActive
            FROM dbo.WorkflowTemplates
            WHERE UserId = @UserId AND IsActive = 1
            ORDER BY UpdatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<WorkflowTemplate>(sql, new { UserId = userId, Limit = limit });

        return results.ToList();
    }
}