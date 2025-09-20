/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ProjectRepository implementation using the canonical repository pattern.
 * Features standardized data access with retry logic and proper multi-tenant isolation.
 */

using Microsoft.Extensions.Options;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Hartonomous.Core.Models;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Repository for Project entities using canonical pattern
/// </summary>
public class ProjectEntityRepository : BaseRepository<ProjectEntity, Guid>
{
    public ProjectEntityRepository(IOptions<SqlServerOptions> sqlOptions) : base(sqlOptions)
    {
    }

    protected override string GetTableName() => "Projects";

    protected override string GetSelectColumns() =>
        "ProjectId, UserId, ProjectName, Description, Category, Status, Configuration, Metadata, IsPublic, CollaborationSettings, CreatedAt, LastUpdated, LastAccessedAt";

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters() =>
        ("ProjectId, UserId, ProjectName, Description, Category, Status, Configuration, Metadata, IsPublic, CollaborationSettings, CreatedAt",
         "@Id, @UserId, @ProjectName, @Description, @Category, @Status, @Configuration, @Metadata, @IsPublic, @CollaborationSettings, @CreatedDate");

    protected override string GetUpdateSetClause() =>
        "ProjectName = @ProjectName, Description = @Description, Category = @Category, Status = @Status, Configuration = @Configuration, Metadata = @Metadata, IsPublic = @IsPublic, CollaborationSettings = @CollaborationSettings, LastUpdated = @ModifiedDate, LastAccessedAt = GETUTCDATE()";

    protected override ProjectEntity MapToEntity(dynamic row)
    {
        return new ProjectEntity
        {
            Id = row.ProjectId,
            UserId = row.UserId,
            ProjectName = row.ProjectName ?? string.Empty,
            Description = row.Description ?? string.Empty,
            Category = row.Category ?? string.Empty,
            Status = (ProjectStatus)(row.Status ?? 0),
            Configuration = row.Configuration ?? "{}",
            Metadata = row.Metadata ?? "{}",
            IsPublic = row.IsPublic ?? false,
            CollaborationSettings = row.CollaborationSettings ?? "{}",
            CreatedDate = row.CreatedAt,
            ModifiedDate = row.LastUpdated,
            LastAccessedAt = row.LastAccessedAt
        };
    }

    protected override object GetParameters(ProjectEntity entity)
    {
        return new
        {
            Id = entity.Id,
            UserId = entity.UserId,
            ProjectName = entity.ProjectName,
            Description = entity.Description,
            Category = entity.Category,
            Status = (int)entity.Status,
            Configuration = entity.Configuration,
            Metadata = entity.Metadata,
            IsPublic = entity.IsPublic,
            CollaborationSettings = entity.CollaborationSettings,
            CreatedDate = entity.CreatedDate,
            ModifiedDate = entity.ModifiedDate
        };
    }
}