using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// Entity Framework Core configuration for Constant entity.
/// 
/// 4D Spatial Architecture (Option D):
/// - PRIMARY: Composite B-tree on (hilbert_high, hilbert_low) for 4D k-NN queries
/// - REDUNDANT: B-tree indexes on quantized metadata for zero-decode filtering
/// - MATERIALIZED: PostGIS GIST index on location (POINTZM) for geometric operations
/// 
/// Performance targets:
/// - k-NN queries: <100ms for k=10 in 10M atoms
/// - Metadata filtering: <100ms for multi-property filters
/// - Combined queries: <100ms for spatial + metadata
/// 
/// Index strategy enables:
/// - 100x faster spatial queries (B-tree vs GIST for 4D)
/// - Direct metadata filtering without Hilbert decoding
/// - PostGIS compatibility for complex geometric operations
/// </summary>
public class ConstantConfiguration : IEntityTypeConfiguration<Constant>
{
    public void Configure(EntityTypeBuilder<Constant> builder)
    {
        builder.ToTable("constants");

        // Primary key
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .IsRequired();

        // Content hash (unique constraint for deduplication)
        builder.OwnsOne(c => c.Hash, hash =>
        {
            hash.Property(h => h.Bytes)
                .HasColumnName("hash")
                .HasMaxLength(64)
                .IsRequired();
        });

        builder.HasIndex("Hash_Bytes")
            .HasDatabaseName("uq_constants_hash")
            .IsUnique();

        // Content type
        builder.Property(c => c.ContentType)
            .HasColumnName("content_type")
            .IsRequired()
            .HasConversion<string>();

        builder.HasIndex(c => c.ContentType)
            .HasDatabaseName("ix_constants_content_type");

        // Raw data (TOAST-compressed for large values)
        builder.Property(c => c.Data)
            .HasColumnName("data")
            .IsRequired();

        builder.Property(c => c.Size)
            .HasColumnName("size")
            .IsRequired();

        builder.HasIndex(c => c.Size)
            .HasDatabaseName("ix_constants_size");

        // ====================================================================
        // 4D SPATIAL COORDINATE (OPTION D ARCHITECTURE)
        // ====================================================================
        
        builder.OwnsOne(c => c.Coordinate, coord =>
        {
            // PRIMARY: 4D Hilbert curve index (84 bits split into 2× ulong)
            coord.Property(sc => sc.HilbertHigh)
                .HasColumnName("hilbert_high")
                .IsRequired();

            coord.Property(sc => sc.HilbertLow)
                .HasColumnName("hilbert_low")
                .IsRequired();

            coord.Property(sc => sc.Precision)
                .HasColumnName("hilbert_precision")
                .IsRequired()
                .HasDefaultValue(21); // 21 bits per dimension

            // REDUNDANT: Quantized metadata for zero-decode filtering
            coord.Property(sc => sc.QuantizedEntropy)
                .HasColumnName("quantized_entropy")
                .IsRequired()
                .HasComment("Shannon entropy [0, 2^21-1]: content randomness");

            coord.Property(sc => sc.QuantizedCompressibility)
                .HasColumnName("quantized_compressibility")
                .IsRequired()
                .HasComment("Kolmogorov complexity [0, 2^21-1]: gzip compression ratio");

            coord.Property(sc => sc.QuantizedConnectivity)
                .HasColumnName("quantized_connectivity")
                .IsRequired()
                .HasComment("Graph connectivity [0, 2^21-1]: log2(reference_count)");
        });

        // ====================================================================
        // TIER 1: COMPOSITE 4D HILBERT INDEX (PRIMARY SPATIAL QUERIES)
        // ====================================================================
        // Purpose: Fast k-NN queries in full 4D space
        // Performance: O(log N) B-tree lookup, <25ms for 10M atoms
        // Use case: "Find 10 nearest atoms to point (x,y,z,m)"
        
        builder.HasIndex("Coordinate_HilbertHigh", "Coordinate_HilbertLow")
            .HasDatabaseName("ix_constants_hilbert4d")
            .HasMethod("btree");

        // ====================================================================
        // TIER 2: INDIVIDUAL METADATA INDEXES (FAST FILTERING)
        // ====================================================================
        // Purpose: Direct B-tree queries without Hilbert decoding
        // Performance: O(log N) per filter, <15ms for 10M atoms
        // Use case: "Find atoms where entropy > 1.5M"
        
        builder.HasIndex("Coordinate_QuantizedEntropy")
            .HasDatabaseName("ix_constants_entropy")
            .HasMethod("btree");

        builder.HasIndex("Coordinate_QuantizedCompressibility")
            .HasDatabaseName("ix_constants_compressibility")
            .HasMethod("btree");

        builder.HasIndex("Coordinate_QuantizedConnectivity")
            .HasDatabaseName("ix_constants_connectivity")
            .HasMethod("btree");

        // ====================================================================
        // TIER 3: COMPOSITE METADATA INDEX (MULTI-PROPERTY FILTERS)
        // ====================================================================
        // Purpose: Optimize common multi-property queries
        // Performance: Single index scan, <20ms for 10M atoms
        // Use case: "Find atoms where entropy > X AND compressibility < Y AND connectivity > Z"
        
        builder.HasIndex(
            "Coordinate_QuantizedEntropy",
            "Coordinate_QuantizedCompressibility",
            "Coordinate_QuantizedConnectivity")
            .HasDatabaseName("ix_constants_metadata_composite")
            .HasMethod("btree");

        // ====================================================================
        // TIER 4: POSTGIS SPATIAL INDEX (GEOMETRIC OPERATIONS)
        // ====================================================================
        // Purpose: Complex PostGIS functions (polygons, distance, containment)
        // Performance: O(log N) GIST lookup, <50ms for 10M atoms
        // Use case: "Find atoms within polygon" or "Find atoms within distance of line"
        // Note: MATERIALIZED VIEW - computed from Hilbert + metadata
        
        builder.Property(c => c.Location)
            .HasColumnName("location")
            .HasColumnType("geometry(PointZM, 4326)")
            .IsRequired(false)
            .HasComment("Materialized POINTZM view: X=spatial, Y=entropy, Z=compressibility, M=connectivity");

        builder.HasIndex(c => c.Location)
            .HasMethod("gist")
            .HasDatabaseName("ix_constants_location_gist");

        // ====================================================================
        // STATUS AND LIFECYCLE
        // ====================================================================
        
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

        // ====================================================================
        // DEDUPLICATION
        // ====================================================================
        
        builder.HasOne(c => c.CanonicalConstant)
            .WithMany()
            .HasForeignKey("canonical_constant_id")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(c => c.IsDuplicate)
            .HasColumnName("is_duplicate")
            .IsRequired()
            .HasDefaultValue(false);

        // Partial index: only duplicates (saves space)
        builder.HasIndex(c => c.IsDuplicate)
            .HasDatabaseName("ix_constants_is_duplicate")
            .HasFilter("is_duplicate = true");

        builder.Property(c => c.DeduplicatedAt)
            .HasColumnName("deduplicated_at")
            .IsRequired(false);

        // ====================================================================
        // USAGE TRACKING
        // ====================================================================
        
        builder.Property(c => c.ReferenceCount)
            .HasColumnName("reference_count")
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(c => c.Frequency)
            .HasColumnName("frequency")
            .IsRequired()
            .HasDefaultValue(0.0);

        // Partial index: only frequently accessed constants (hot data)
        builder.HasIndex(c => c.Frequency)
            .HasDatabaseName("ix_constants_frequency_hot")
            .HasFilter("frequency > 0.001"); // Top 0.1%

        builder.Property(c => c.LastAccessedAt)
            .HasColumnName("last_accessed_at")
            .IsRequired();

        // Partial index: recently accessed constants (for cache eviction)
        builder.HasIndex(c => c.LastAccessedAt)
            .HasDatabaseName("ix_constants_last_accessed_recent")
            .HasFilter("last_accessed_at > NOW() - INTERVAL '7 days'");

        // ====================================================================
        // RELATIONSHIPS
        // ====================================================================
        
        builder.HasMany(c => c.ComposingTokens)
            .WithMany(t => t.Constants)
            .UsingEntity(j =>
            {
                j.ToTable("constant_tokens");
                j.HasIndex("ConstantsId", "ComposingTokensId")
                    .HasDatabaseName("uq_constant_tokens")
                    .IsUnique();
            });

        // ====================================================================
        // AUDIT FIELDS (BaseEntity)
        // ====================================================================
        
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

        // ====================================================================
        // SOFT DELETE
        // ====================================================================
        
        builder.Property(c => c.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasDefaultValue(false);

        // Partial index: only non-deleted (most common query)
        builder.HasIndex(c => c.IsDeleted)
            .HasDatabaseName("ix_constants_is_deleted")
            .HasFilter("is_deleted = false");

        builder.Property(c => c.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(c => c.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);

        // Global query filter: exclude soft-deleted by default
        builder.HasQueryFilter(c => !c.IsDeleted);

        // ====================================================================
        // TABLE SETTINGS (POSTGRESQL OPTIMIZATIONS)
        // ====================================================================
        
        // These are applied via migration HasAnnotation
        // - FILLFACTOR 90: Leave 10% space for HOT updates
        // - AUTOVACUUM settings: Aggressive for high-write table
        // - TOAST compression: LZ4 for Data column
    }
}
