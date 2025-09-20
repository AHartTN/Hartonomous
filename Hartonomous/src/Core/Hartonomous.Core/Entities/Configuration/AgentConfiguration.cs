/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * Entity Framework configuration for Agent entity
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Hartonomous.Core.Entities.Configuration;

/// <summary>
/// Entity Framework configuration for Agent entity
/// </summary>
public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");

        // Primary key
        builder.HasKey(a => a.Id);

        // Required properties
        builder.Property(a => a.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.AgentName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(a => a.AgentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ConnectionId)
            .HasMaxLength(255);

        builder.Property(a => a.Description)
            .HasMaxLength(1000);

        // JSON serialized properties
        builder.Property(a => a.Capabilities)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions)null) ?? Array.Empty<string>())
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.Configuration)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions)null) ?? new Dictionary<string, object>())
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.Metrics)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions)null) ?? new Dictionary<string, object>())
            .HasColumnType("nvarchar(max)");

        // Enum conversion
        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Timestamps
        builder.Property(a => a.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(a => a.ModifiedDate);

        builder.Property(a => a.LastHeartbeat)
            .IsRequired();

        // Indexes for performance
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.AgentType);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => new { a.UserId, a.Status });
    }
}