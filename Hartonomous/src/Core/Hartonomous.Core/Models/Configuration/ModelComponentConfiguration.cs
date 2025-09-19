/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Entity Framework configuration for the ModelComponent entity,
 * enabling granular neural component analysis with optimized indexing for interpretability queries.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ModelComponentConfiguration : IEntityTypeConfiguration<ModelComponent>
{
    public void Configure(EntityTypeBuilder<ModelComponent> builder)
    {
        builder.HasKey(c => c.ComponentId);

        builder.Property(c => c.ComponentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.ComponentName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.FunctionalDescription)
            .HasMaxLength(1000);

        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.BehaviorMetadata)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(c => c.ModelId);
        builder.HasIndex(c => c.LayerId);
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.ComponentType);
        builder.HasIndex(c => c.RelevanceScore);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(c => c.Model)
            .WithMany()
            .HasForeignKey(c => c.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Layer)
            .WithMany(l => l.Components)
            .HasForeignKey(c => c.LayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}