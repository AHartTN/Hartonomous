/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Entity Framework configuration for the ModelLayer entity,
 * supporting transformer layer storage with FILESTREAM and mechanistic interpretability indexing.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ModelLayerConfiguration : IEntityTypeConfiguration<ModelLayer>
{
    public void Configure(EntityTypeBuilder<ModelLayer> builder)
    {
        builder.HasKey(l => l.LayerId);

        builder.Property(l => l.LayerType)
            .HasMaxLength(50);

        builder.Property(l => l.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(l => l.ConfigMetadata)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(l => l.LayerWeights)
            .HasColumnType("varbinary(max) FILESTREAM");

        builder.HasIndex(l => l.ModelId);
        builder.HasIndex(l => l.UserId);
        builder.HasIndex(l => new { l.ModelId, l.LayerIndex });

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(l => l.Model)
            .WithMany(m => m.Layers)
            .HasForeignKey(l => l.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Components)
            .WithOne(c => c.Layer)
            .HasForeignKey(c => c.LayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}