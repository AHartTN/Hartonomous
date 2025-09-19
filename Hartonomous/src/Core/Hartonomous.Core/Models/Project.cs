/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Project entity for multi-tenant workspace organization,
 * enabling collaborative model development and agent creation within the platform.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents a user project that groups models and agents
/// Enables organization and collaboration within the Agent Factory
/// </summary>
public class Project
{
    public Guid ProjectId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string ProjectName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Project category or domain
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty; // research, commercial, educational, experimental

    /// <summary>
    /// Project status
    /// </summary>
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    /// <summary>
    /// Project configuration and settings
    /// </summary>
    public string Configuration { get; set; } = "{}";

    /// <summary>
    /// Project metadata and tags
    /// </summary>
    public string Metadata { get; set; } = "{}";

    /// <summary>
    /// Whether project is publicly visible
    /// </summary>
    public bool IsPublic { get; set; } = false;

    /// <summary>
    /// Collaboration settings
    /// </summary>
    public string CollaborationSettings { get; set; } = "{}";

    /// <summary>
    /// Multi-tenant isolation - owner of the project
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }
    public DateTime? LastAccessedAt { get; set; }

    // Navigation properties
    public virtual ICollection<ProjectModel> ProjectModels { get; set; } = new List<ProjectModel>();

    /// <summary>
    /// Get project configuration as typed object
    /// </summary>
    public T? GetConfiguration<T>() where T : class
    {
        if (string.IsNullOrEmpty(Configuration) || Configuration == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(Configuration);
    }

    /// <summary>
    /// Set project configuration from typed object
    /// </summary>
    public void SetConfiguration<T>(T config) where T : class
    {
        Configuration = JsonSerializer.Serialize(config);
    }

    /// <summary>
    /// Get project metadata as typed object
    /// </summary>
    public T? GetMetadata<T>() where T : class
    {
        if (string.IsNullOrEmpty(Metadata) || Metadata == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(Metadata);
    }

    /// <summary>
    /// Set project metadata from typed object
    /// </summary>
    public void SetMetadata<T>(T metadata) where T : class
    {
        Metadata = JsonSerializer.Serialize(metadata);
    }

    /// <summary>
    /// Get collaboration settings as typed object
    /// </summary>
    public T? GetCollaborationSettings<T>() where T : class
    {
        if (string.IsNullOrEmpty(CollaborationSettings) || CollaborationSettings == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(CollaborationSettings);
    }

    /// <summary>
    /// Set collaboration settings from typed object
    /// </summary>
    public void SetCollaborationSettings<T>(T settings) where T : class
    {
        CollaborationSettings = JsonSerializer.Serialize(settings);
    }
}

public enum ProjectStatus
{
    Active,
    Paused,
    Completed,
    Archived,
    Deleted
}