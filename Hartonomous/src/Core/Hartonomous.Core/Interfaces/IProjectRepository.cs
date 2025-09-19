/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the project repository interface for multi-tenant project management.
 * Features user-scoped project operations with secure data isolation and project lifecycle management.
 */

using Hartonomous.Core.DTOs;

namespace Hartonomous.Core.Interfaces;

public interface IProjectRepository
{
    Task<IEnumerable<ProjectDto>> GetProjectsByUserAsync(string userId);
    Task<ProjectDto?> GetProjectByIdAsync(Guid projectId, string userId);
    Task<Guid> CreateProjectAsync(CreateProjectRequest request, string userId);
    Task<bool> DeleteProjectAsync(Guid projectId, string userId);
}