using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for Landmark entity
/// </summary>
public class LandmarkConfiguration : IEntityTypeConfiguration<Landmark>
{
    public void Configure(EntityTypeBuilder<Landmark> builder)
    {
        builder.ToTable("landmarks");

        // Primary key
        builder.HasKey(l => l.Id);

        // Name
        builder.Property(l => l.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        // Create unique index on name
        builder.HasIndex(l => l.Name)
            .IsUnique()
            .HasDatabaseName("ix_landmarks_name");

        builder.Property(l => l.Description)
            .HasColumnName("description")
            .HasMaxLength(1000)
            .IsRequired(false);

        // SpatialCoordinate value object for center - Hilbert-first architecture
        builder.OwnsOne(l => l.Center, coord =>
        {
            coord.Property(sc => sc.HilbertHigh)
                .HasColumnName("center_hilbert_high")
                .IsRequired();

            coord.Property(sc => sc.HilbertLow)
                .HasColumnName("center_hilbert_low")
                .IsRequired();

            coord.Property(sc => sc.Precision)
                .HasColumnName("center_precision")
                .IsRequired();

            coord.Property(sc => sc.QuantizedEntropy)
                .HasColumnName("center_quantized_entropy")
                .IsRequired();

            coord.Property(sc => sc.QuantizedCompressibility)
                .HasColumnName("center_quantized_compressibility")
                .IsRequired();

            coord.Property(sc => sc.QuantizedConnectivity)
                .HasColumnName("center_quantized_connectivity")
                .IsRequired();
        });

        // B-tree index on Hilbert index for fast landmark proximity queries
        builder.OwnsOne(l => l.Center).HasIndex("HilbertIndex")
            .HasDatabaseName("ix_landmarks_center_hilbert_index")
            .HasMethod("btree");

        // PostGIS Point for spatial queries (materialized view)
        builder.Property(l => l.Location)
            .HasColumnName("location")
            .HasColumnType("geometry(PointZ)")
            .IsRequired();

        // Spatial index on location (secondary, for PostGIS-specific operations)
        builder.HasIndex(l => l.Location)
            .HasMethod("gist")
            .HasDatabaseName("ix_landmarks_location_spatial");

        builder.Property(l => l.Radius)
            .HasColumnName("radius")
            .IsRequired();

        builder.HasIndex(l => l.Radius)
            .HasDatabaseName("ix_landmarks_radius");

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

        // Audit fields from BaseEntity
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
