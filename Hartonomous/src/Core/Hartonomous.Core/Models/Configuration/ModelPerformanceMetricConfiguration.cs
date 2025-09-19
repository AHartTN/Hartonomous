/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Entity Framework configuration for the ModelPerformanceMetric entity,
 * enabling comprehensive model analytics with optimized indexing for performance analysis queries.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ModelPerformanceMetricConfiguration : IEntityTypeConfiguration<ModelPerformanceMetric>
{
    public void Configure(EntityTypeBuilder<ModelPerformanceMetric> builder)
    {
        builder.HasKey(mpm => mpm.MetricId);

        builder.Property(mpm => mpm.MetricName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(mpm => mpm.MetricCategory)
            .HasMaxLength(100);

        builder.Property(mpm => mpm.Unit)
            .HasMaxLength(50);

        builder.Property(mpm => mpm.BenchmarkContext)
            .HasMaxLength(500);

        builder.Property(mpm => mpm.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(mpm => mpm.MetricMetadata)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.Property(mpm => mpm.EnvironmentContext)
            .HasDefaultValue("{}")
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(mpm => mpm.ModelId);
        builder.HasIndex(mpm => mpm.ComponentId);
        builder.HasIndex(mpm => mpm.AgentId);
        builder.HasIndex(mpm => mpm.UserId);
        builder.HasIndex(mpm => mpm.MetricName);
        builder.HasIndex(mpm => mpm.MetricCategory);
        builder.HasIndex(mpm => mpm.MetricValue);
        builder.HasIndex(mpm => mpm.MeasuredAt);

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(mpm => mpm.Model)
            .WithMany(m => m.PerformanceMetrics)
            .HasForeignKey(mpm => mpm.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mpm => mpm.Component)
            .WithMany()
            .HasForeignKey(mpm => mpm.ComponentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(mpm => mpm.Agent)
            .WithMany()
            .HasForeignKey(mpm => mpm.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}