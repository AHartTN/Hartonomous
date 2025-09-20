/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the unified Project repository interface supporting both canonical and legacy patterns.
 * Provides migration path from DTO-based operations to proper entity-based operations.
 */

using Hartonomous.Core.Abstractions;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Entities;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Unified Project repository interface supporting both canonical and legacy operations
/// </summary>
public interface IProjectEntityRepository : IRepository<ProjectEntity, Guid>
{
    // Legacy DTO-based operations for backward compatibility
    Task<IEnumerable<ProjectDto>> GetProjectsByUserAsync(string userId);
    Task<ProjectDto?> GetProjectByIdAsync(Guid projectId, string userId);
    Task<Guid> CreateProjectAsync(CreateProjectRequest request, string userId);
    Task<bool> DeleteProjectAsync(Guid projectId, string userId);
}