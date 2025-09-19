/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Entity Framework configuration for the Model entity,
 * including FILESTREAM setup for AI-native NoSQL storage and multi-tenant security constraints.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ModelConfiguration : IEntityTypeConfiguration<Model>
{
    public void Configure(EntityTypeBuilder<Model> builder)
    {
        builder.HasKey(m => m.ModelId);

        builder.Property(m => m.ModelName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.Architecture)
            .HasMaxLength(100);

        builder.Property(m => m.ModelPath)
            .HasMaxLength(500);

        builder.Property(m => m.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.ConfigMetadata)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        // FILESTREAM configuration for model weights
        builder.Property(m => m.ModelWeights)
            .HasColumnType("varbinary(max) FILESTREAM");

        // Indexes
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.ModelName);
        builder.HasIndex(m => m.Status);
        builder.HasIndex(m => new { m.UserId, m.Status });

        // Shadow property for multi-tenancy
        builder.Property<string>("UserId").HasMaxLength(128);

        // Relationships
        builder.HasMany(m => m.Layers)
            .WithOne(l => l.Model)
            .HasForeignKey(l => l.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Embeddings)
            .WithOne(e => e.Model)
            .HasForeignKey(e => e.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.PerformanceMetrics)
            .WithOne(p => p.Model)
            .HasForeignKey(p => p.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.ProjectModels)
            .WithOne(pm => pm.Model)
            .HasForeignKey(pm => pm.ModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}