/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Project entity wrapper for repository pattern compliance.
 * Provides IEntityBase implementation for the existing Project model.
 */

using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Models;

namespace Hartonomous.Core.Entities;

/// <summary>
/// Project entity wrapper that implements IEntityBase for repository pattern
/// Maps to the existing Project model structure
/// </summary>
public class ProjectEntity : IEntityBase<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // Project-specific properties
    public string ProjectName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public string Configuration { get; set; } = "{}";
    public string Metadata { get; set; } = "{}";
    public bool IsPublic { get; set; } = false;
    public string CollaborationSettings { get; set; } = "{}";
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// Convert from Project model to ProjectEntity
    /// </summary>
    public static ProjectEntity FromModel(Project project)
    {
        return new ProjectEntity
        {
            Id = project.ProjectId,
            UserId = project.UserId,
            CreatedDate = project.CreatedAt,
            ModifiedDate = project.LastUpdated,
            ProjectName = project.ProjectName,
            Description = project.Description,
            Category = project.Category,
            Status = project.Status,
            Configuration = project.Configuration,
            Metadata = project.Metadata,
            IsPublic = project.IsPublic,
            CollaborationSettings = project.CollaborationSettings,
            LastAccessedAt = project.LastAccessedAt
        };
    }

    /// <summary>
    /// Convert from ProjectEntity to Project model
    /// </summary>
    public Project ToModel()
    {
        return new Project
        {
            ProjectId = Id,
            UserId = UserId,
            CreatedAt = CreatedDate,
            LastUpdated = ModifiedDate,
            ProjectName = ProjectName,
            Description = Description,
            Category = Category,
            Status = Status,
            Configuration = Configuration,
            Metadata = Metadata,
            IsPublic = IsPublic,
            CollaborationSettings = CollaborationSettings,
            LastAccessedAt = LastAccessedAt
        };
    }
}