/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the project repository implementation for multi-tenant project management.
 * Features user-scoped data access patterns ensuring tenant isolation and secure project operations.
 */

using Dapper;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Hartonomous.Core.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly string _connectionString;

    public ProjectRepository(IConfiguration configuration)
    {
        _connectionString = configuration["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("DefaultConnection string not found");
    }

    public async Task<IEnumerable<ProjectDto>> GetProjectsByUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        const string sql = @"
            SELECT ProjectId, ProjectName, CreatedAt
            FROM dbo.Projects
            WHERE UserId = @UserId
            ORDER BY CreatedAt DESC";

        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ProjectDto>(sql, new { UserId = userId });
    }

    public async Task<ProjectDto?> GetProjectByIdAsync(Guid projectId, string userId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project ID cannot be empty", nameof(projectId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        const string sql = @"
            SELECT ProjectId, ProjectName, CreatedAt
            FROM dbo.Projects
            WHERE ProjectId = @ProjectId AND UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<ProjectDto>(sql, new { ProjectId = projectId, UserId = userId });
    }

    public async Task<Guid> CreateProjectAsync(CreateProjectRequest request, string userId)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(request.ProjectName))
            throw new ArgumentException("Project name cannot be null or empty", nameof(request));

        var projectId = Guid.NewGuid();

        const string sql = @"
            INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName, CreatedAt)
            VALUES (@ProjectId, @UserId, @ProjectName, @CreatedAt)";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            ProjectId = projectId,
            UserId = userId,
            ProjectName = request.ProjectName,
            CreatedAt = DateTime.UtcNow
        });

        return projectId;
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, string userId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project ID cannot be empty", nameof(projectId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        const string sql = @"
            DELETE FROM dbo.Projects
            WHERE ProjectId = @ProjectId AND UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { ProjectId = projectId, UserId = userId });
        return rowsAffected > 0;
    }
}