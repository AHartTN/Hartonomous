/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * Entity Framework configuration for Message entity
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Hartonomous.Core.Entities.Configuration;

/// <summary>
/// Entity Framework configuration for Message entity
/// </summary>
public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");

        // Primary key
        builder.HasKey(m => m.Id);

        // Required properties
        builder.Property(m => m.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.ConversationId)
            .IsRequired();

        builder.Property(m => m.AgentId);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(4000); // Large enough for most messages

        // JSON serialized properties
        builder.Property(m => m.Metadata)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions)null) ?? new Dictionary<string, object>())
            .HasColumnType("nvarchar(max)");

        // Enum conversion
        builder.Property(m => m.MessageType)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Timestamps
        builder.Property(m => m.CreatedDate)
            .IsRequired()
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(m => m.ModifiedDate);

        // Indexes for performance
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.ConversationId);
        builder.HasIndex(m => m.AgentId);
        builder.HasIndex(m => m.MessageType);
        builder.HasIndex(m => new { m.ConversationId, m.CreatedDate });
        builder.HasIndex(m => new { m.UserId, m.ConversationId });
    }
}