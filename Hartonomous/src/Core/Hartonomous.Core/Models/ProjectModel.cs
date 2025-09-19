/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ProjectModel entity for many-to-many project-model relationships,
 * enabling flexible model sharing and role-based organization across collaborative workspaces.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Many-to-many relationship between Projects and Models
/// Enables models to be shared across projects with different roles
/// </summary>
public class ProjectModel
{
    public Guid ProjectModelId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ProjectId { get; set; }

    [Required]
    public Guid ModelId { get; set; }

    /// <summary>
    /// Role of this model within the project
    /// </summary>
    [MaxLength(100)]
    public string ModelRole { get; set; } = string.Empty; // primary, reference, experimental, archived

    /// <summary>
    /// Project-specific model configuration
    /// </summary>
    public string ProjectConfiguration { get; set; } = "{}";

    /// <summary>
    /// Usage statistics within this project
    /// </summary>
    public string UsageStatistics { get; set; } = "{}";

    /// <summary>
    /// Project-specific notes and metadata
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Whether this model is active in the project
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    // Navigation properties
    public virtual Project Project { get; set; } = null!;
    public virtual Model Model { get; set; } = null!;

    /// <summary>
    /// Get project configuration as typed object
    /// </summary>
    public T? GetProjectConfiguration<T>() where T : class
    {
        if (string.IsNullOrEmpty(ProjectConfiguration) || ProjectConfiguration == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ProjectConfiguration);
    }

    /// <summary>
    /// Set project configuration from typed object
    /// </summary>
    public void SetProjectConfiguration<T>(T config) where T : class
    {
        ProjectConfiguration = JsonSerializer.Serialize(config);
    }

    /// <summary>
    /// Get usage statistics as typed object
    /// </summary>
    public T? GetUsageStatistics<T>() where T : class
    {
        if (string.IsNullOrEmpty(UsageStatistics) || UsageStatistics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(UsageStatistics);
    }

    /// <summary>
    /// Set usage statistics from typed object
    /// </summary>
    public void SetUsageStatistics<T>(T statistics) where T : class
    {
        UsageStatistics = JsonSerializer.Serialize(statistics);
    }
}