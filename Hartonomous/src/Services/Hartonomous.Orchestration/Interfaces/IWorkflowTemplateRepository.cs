/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow template repository interface for template management.
 */

using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Models;

namespace Hartonomous.Orchestration.Interfaces;

/// <summary>
/// Repository interface for workflow template management
/// </summary>
public interface IWorkflowTemplateRepository
{
    /// <summary>
    /// Create a new workflow template
    /// </summary>
    Task<Guid> CreateTemplateAsync(WorkflowTemplate template);

    /// <summary>
    /// Get a template by ID
    /// </summary>
    Task<WorkflowTemplate?> GetTemplateByIdAsync(Guid templateId, string userId);

    /// <summary>
    /// Update an existing template
    /// </summary>
    Task<bool> UpdateTemplateAsync(WorkflowTemplate template);

    /// <summary>
    /// Delete a template
    /// </summary>
    Task<bool> DeleteTemplateAsync(Guid templateId, string userId);

    /// <summary>
    /// Search templates by criteria with pagination
    /// </summary>
    Task<PaginatedResult<WorkflowTemplate>> SearchTemplatesAsync(
        string? query, string? category, List<string>? tags, bool includePublic, string userId,
        int page = 1, int pageSize = 20);

    /// <summary>
    /// Get templates by category
    /// </summary>
    Task<List<WorkflowTemplate>> GetTemplatesByCategoryAsync(string category, bool includePublic, string userId);

    /// <summary>
    /// Get popular templates ordered by usage count
    /// </summary>
    Task<List<WorkflowTemplate>> GetPopularTemplatesAsync(int limit = 10, bool includePublic = true);

    /// <summary>
    /// Get template usage statistics
    /// </summary>
    Task<Dictionary<string, object>> GetTemplateUsageStatsAsync(Guid templateId, string userId);

    /// <summary>
    /// Increment template usage count
    /// </summary>
    Task<bool> IncrementTemplateUsageAsync(Guid templateId);

    /// <summary>
    /// Check if template exists and belongs to user or is public
    /// </summary>
    Task<bool> TemplateExistsAsync(Guid templateId, string userId);

    /// <summary>
    /// Get templates created by specific user
    /// </summary>
    Task<List<WorkflowTemplate>> GetTemplatesByUserAsync(string userId, int limit = 100);
}