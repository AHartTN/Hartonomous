using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for Deterministic Hilbert Landmark entity.
/// </summary>
public class LandmarkConfiguration : IEntityTypeConfiguration<Landmark>
{
    public void Configure(EntityTypeBuilder<Landmark> builder)
    {
        builder.ToTable("landmarks");

        // Primary key
        builder.HasKey(l => l.Id);

        // Name (now derived from HilbertPrefix and Level)
        builder.Property(l => l.Name)
            .HasColumnName("name")
            .HasMaxLength(256) // Increased length for derived name
            .IsRequired();

        // Create unique index on name
        builder.HasIndex(l => l.Name)
            .IsUnique()
            .HasDatabaseName("uq_landmarks_name"); // Changed name for clarity

        builder.Property(l => l.Description)
            .HasColumnName("description")
            .HasMaxLength(1000)
            .IsRequired(false);

        // Hilbert Tile Definition
        builder.Property(l => l.HilbertPrefixHigh)
            .HasColumnName("hilbert_prefix_high")
            .IsRequired();

        builder.Property(l => l.HilbertPrefixLow)
            .HasColumnName("hilbert_prefix_low")
            .IsRequired();

        builder.Property(l => l.Level)
            .HasColumnName("level")
            .IsRequired();

        // Composite unique index for the Hilbert tile itself
        builder.HasIndex(l => new { l.HilbertPrefixHigh, l.HilbertPrefixLow, l.Level })
            .IsUnique()
            .HasDatabaseName("uq_landmarks_hilbert_tile");

        // Statistics
        builder.Property(l => l.ConstantCount)
            .HasColumnName("constant_count")
            .IsRequired();

        builder.HasIndex(l => l.ConstantCount)
            .HasDatabaseName("ix_landmarks_constant_count");

        builder.Property(l => l.Density)
            .HasColumnName("density")
            .IsRequired();

        builder.HasIndex(l => l.Density)
            .HasDatabaseName("ix_landmarks_density");

        builder.Property(l => l.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(l => l.IsActive)
            .HasDatabaseName("ix_landmarks_is_active");
        
        builder.Property(l => l.LastStatisticsUpdate)
            .HasColumnName("last_statistics_update")
            .IsRequired();

        // Audit fields from BaseEntity (assuming they are still needed, removed AverageDistance from original spec)
        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(l => l.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(l => l.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(l => l.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(l => l.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(l => l.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.HasIndex(l => l.IsDeleted)
            .HasDatabaseName("ix_landmarks_is_deleted");
    }
}
