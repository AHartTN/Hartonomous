/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Entity Framework configuration for the ProjectModel relationship entity,
 * supporting flexible model sharing and role-based organization across collaborative workspaces.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ProjectModelConfiguration : IEntityTypeConfiguration<ProjectModel>
{
    public void Configure(EntityTypeBuilder<ProjectModel> builder)
    {
        builder.HasKey(pm => pm.ProjectModelId);

        builder.Property(pm => pm.ModelRole)
            .HasMaxLength(100);

        builder.Property(pm => pm.Notes)
            .HasColumnType("nvarchar(max)");

        builder.Property(pm => pm.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(pm => pm.ProjectConfiguration)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(pm => pm.UsageStatistics)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(pm => pm.ProjectId);
        builder.HasIndex(pm => pm.ModelId);
        builder.HasIndex(pm => pm.UserId);
        builder.HasIndex(pm => pm.ModelRole);
        builder.HasIndex(pm => pm.IsActive);
        builder.HasIndex(pm => new { pm.ProjectId, pm.ModelId })
            .IsUnique(); // Each model can only be in a project once

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(pm => pm.Project)
            .WithMany(p => p.ProjectModels)
            .HasForeignKey(pm => pm.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pm => pm.Model)
            .WithMany(m => m.ProjectModels)
            .HasForeignKey(pm => pm.ModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}