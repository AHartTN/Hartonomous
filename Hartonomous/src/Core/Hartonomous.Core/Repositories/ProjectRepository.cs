/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * CLEANUP: Refactored to use EnhancedBaseRepository - eliminated 85+ lines of duplicate code
 * including connection management, user validation, retry logic, and common patterns.
 */

using Hartonomous.Core.Abstractions;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Project repository with consolidated data access patterns
/// Inherits connection management, retry logic, and user scoping from EnhancedBaseRepository
/// </summary>
public class ProjectRepository : EnhancedBaseRepository<ProjectDto, Guid>, IProjectRepository
{
    public ProjectRepository(IConfiguration configuration)
        : base(configuration, "dbo.Projects", "ProjectId")
    {
    }

    public async Task<IEnumerable<ProjectDto>> GetProjectsByUserAsync(string userId)
    {
        ValidateRequiredString(userId, nameof(userId));

        return await GetByUserAsync(userId,
            customSelectColumns: "ProjectId, ProjectName, CreatedAt",
            orderBy: "CreatedAt DESC");
    }

    public async Task<ProjectDto?> GetProjectByIdAsync(Guid projectId, string userId)
    {
        ValidateId(projectId, nameof(projectId));
        ValidateRequiredString(userId, nameof(userId));

        return await GetByIdWithUserScopeAsync(projectId, userId,
            customSelectColumns: "ProjectId, ProjectName, CreatedAt");
    }

    public async Task<Guid> CreateProjectAsync(CreateProjectRequest request, string userId)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        ValidateRequiredString(userId, nameof(userId));
        ValidateRequiredString(request.ProjectName, nameof(request.ProjectName));

        var projectId = Guid.NewGuid();

        return await CreateWithUserScopeAsync(
            entity: new { ProjectId = projectId, ProjectName = request.ProjectName },
            userId: userId,
            insertColumns: "ProjectId, ProjectName",
            insertParameters: "@ProjectId, @ProjectName");
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, string userId)
    {
        ValidateId(projectId, nameof(projectId));
        ValidateRequiredString(userId, nameof(userId));

        return await DeleteWithUserScopeAsync(projectId, userId);
    }
}