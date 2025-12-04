using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for ContentIngestion entity
/// </summary>
public class ContentIngestionConfiguration : IEntityTypeConfiguration<ContentIngestion>
{
    public void Configure(EntityTypeBuilder<ContentIngestion> builder)
    {
        builder.ToTable("content_ingestions");

        // Primary key
        builder.HasKey(i => i.Id);

        // Hash256 value object for content hash
        builder.Property(i => i.ContentHash)
            .HasColumnName("content_hash")
            .HasColumnType("bytea")
            .IsRequired()
            .HasConversion(
                hash => hash.Bytes,
                bytes => Hash256.FromBytes(bytes));

        builder.HasIndex(i => i.ContentHash)
            .HasDatabaseName("ix_content_ingestions_content_hash");

        builder.Property(i => i.SourceIdentifier)
            .HasColumnName("source_identifier")
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(i => i.SourceIdentifier)
            .HasDatabaseName("ix_content_ingestions_source_identifier");

        builder.Property(i => i.ContentType)
            .HasColumnName("content_type")
            .IsRequired()
            .HasConversion<string>();

        builder.HasIndex(i => i.ContentType)
            .HasDatabaseName("ix_content_ingestions_content_type");

        builder.Property(i => i.OriginalSize)
            .HasColumnName("original_size")
            .IsRequired();

        // Statistics
        builder.Property(i => i.ConstantCount)
            .HasColumnName("constant_count")
            .IsRequired();

        builder.Property(i => i.UniqueConstantCount)
            .HasColumnName("unique_constant_count")
            .IsRequired();

        builder.Property(i => i.DeduplicationRatio)
            .HasColumnName("deduplication_ratio")
            .IsRequired();

        builder.Property(i => i.ProcessingTimeMs)
            .HasColumnName("processing_time_ms")
            .IsRequired();

        // Timing
        builder.Property(i => i.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.HasIndex(i => i.StartedAt)
            .HasDatabaseName("ix_content_ingestions_started_at");

        builder.Property(i => i.CompletedAt)
            .HasColumnName("completed_at")
            .IsRequired(false);

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at")
            .IsRequired(false);

        // Status
        builder.Property(i => i.IsSuccessful)
            .HasColumnName("is_successful")
            .IsRequired();

        builder.HasIndex(i => i.IsSuccessful)
            .HasDatabaseName("ix_content_ingestions_is_successful");

        builder.Property(i => i.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2000)
            .IsRequired(false);

        // Audit fields from BaseEntity
        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(i => i.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(i => i.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(i => i.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(i => i.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(i => i.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.HasIndex(i => i.IsDeleted)
            .HasDatabaseName("ix_content_ingestions_is_deleted");
    }
}
