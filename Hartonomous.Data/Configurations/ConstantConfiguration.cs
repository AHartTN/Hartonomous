using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for Constant entity
/// </summary>
public class ConstantConfiguration : IEntityTypeConfiguration<Constant>
{
    public void Configure(EntityTypeBuilder<Constant> builder)
    {
        builder.ToTable("constants");

        // Primary key
        builder.HasKey(c => c.Id);

        // Hash256 value object - store as byte array
        builder.Property(c => c.Hash)
            .HasColumnName("hash")
            .HasColumnType("bytea")
            .IsRequired()
            .HasConversion(
                hash => hash.Bytes,
                bytes => Hash256.FromBytes(bytes));

        // Create unique index on hash
        builder.HasIndex(c => c.Hash)
            .IsUnique()
            .HasDatabaseName("ix_constants_hash");

        // Data storage
        builder.Property(c => c.Data)
            .HasColumnName("data")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(c => c.Size)
            .HasColumnName("size")
            .IsRequired();

        builder.Property(c => c.ContentType)
            .HasColumnName("content_type")
            .IsRequired()
            .HasConversion<string>();

        // SpatialCoordinate value object - Hilbert-first architecture
        // Hilbert index is the primary representation (source of truth)
        // Cartesian coordinates (X,Y,Z) are decoded on-demand for visualization
        builder.OwnsOne(c => c.Coordinate, coord =>
        {
            // PRIMARY: Hilbert index (source of truth, B-tree indexed)
            coord.Property(sc => sc.HilbertIndex)
                .HasColumnName("hilbert_index")
                .HasColumnType("bigint")
                .IsRequired();

            coord.Property(sc => sc.Precision)
                .HasColumnName("hilbert_precision")
                .IsRequired()
                .HasDefaultValue(21);

            // DERIVED: Cartesian coordinates (decoded from Hilbert for PostGIS)
            // These are computed/persisted for PostGIS spatial functions
            // but Hilbert index is the canonical spatial representation
            coord.Property(sc => sc.X)
                .HasColumnName("coordinate_x")
                .HasField("_cachedX")
                .IsRequired();

            coord.Property(sc => sc.Y)
                .HasColumnName("coordinate_y")
                .HasField("_cachedY")
                .IsRequired();

            coord.Property(sc => sc.Z)
                .HasColumnName("coordinate_z")
                .HasField("_cachedZ")
                .IsRequired();
        });

        // B-tree index on Hilbert index for fast range queries (100x faster than R-tree)
        builder.OwnsOne(c => c.Coordinate).HasIndex("HilbertIndex")
            .HasDatabaseName("ix_constants_hilbert_index")
            .HasMethod("btree");

        // PostGIS Point for spatial queries (materialized view of decoded Hilbert coordinates)
        // Keep this for complex PostGIS spatial functions, but primary queries use Hilbert index
        builder.Property(c => c.Location)
            .HasColumnName("location")
            .HasColumnType("geometry(PointZ)")
            .IsRequired(false);

        // Spatial index on location (secondary, for PostGIS-specific operations)
        builder.HasIndex(c => c.Location)
            .HasMethod("gist")
            .HasDatabaseName("ix_constants_location_spatial");

        // Status and lifecycle
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasConversion<string>();

        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_constants_status");

        builder.Property(c => c.ProjectedAt)
            .HasColumnName("projected_at")
            .IsRequired(false);

        builder.Property(c => c.ActivatedAt)
            .HasColumnName("activated_at")
            .IsRequired(false);

        // Deduplication
        builder.HasOne(c => c.CanonicalConstant)
            .WithMany()
            .HasForeignKey("canonical_constant_id")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(c => c.IsDuplicate)
            .HasColumnName("is_duplicate")
            .IsRequired();

        builder.Property(c => c.DeduplicatedAt)
            .HasColumnName("deduplicated_at")
            .IsRequired(false);

        // Usage tracking
        builder.Property(c => c.ReferenceCount)
            .HasColumnName("reference_count")
            .IsRequired();

        builder.Property(c => c.Frequency)
            .HasColumnName("frequency")
            .IsRequired();

        builder.Property(c => c.LastAccessedAt)
            .HasColumnName("last_accessed_at")
            .IsRequired();

        builder.HasIndex(c => c.Frequency)
            .HasDatabaseName("ix_constants_frequency");

        builder.HasIndex(c => c.LastAccessedAt)
            .HasDatabaseName("ix_constants_last_accessed");

        // Relationships
        builder.HasMany(c => c.ComposingTokens)
            .WithMany(t => t.Constants)
            .UsingEntity(j => j.ToTable("constant_tokens"));

        // Audit fields from BaseEntity
        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(c => c.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        builder.Property(c => c.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(c => c.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(c => c.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(c => c.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);

        builder.HasIndex(c => c.IsDeleted)
            .HasDatabaseName("ix_constants_is_deleted");
    }
}
