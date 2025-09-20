/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * Entity Framework configuration for Workflow entity
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Hartonomous.Core.Entities.Configuration;

/// <summary>
/// Entity Framework configuration for Workflow entity
/// </summary>
public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("Workflows");

        // Primary key
        builder.HasKey(w => w.Id);

        // Required properties
        builder.Property(w => w.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(w => w.Description)
            .HasMaxLength(1000);

        builder.Property(w => w.Definition)
            .IsRequired()
            .HasColumnType("nvarchar(max)"); // Can be large workflow definition

        // JSON serialized properties
        builder.Property(w => w.Parameters)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions)null) ?? new Dictionary<string, object>())
            .HasColumnType("nvarchar(max)");

        // Enum conversion
        builder.Property(w => w.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Timestamps
        builder.Property(w => w.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(w => w.ModifiedDate);

        // Indexes for performance
        builder.HasIndex(w => w.UserId);
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.Name);
        builder.HasIndex(w => new { w.UserId, w.Status });
    }
}