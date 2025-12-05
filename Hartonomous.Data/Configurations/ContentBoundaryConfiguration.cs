using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for ContentBoundary entity
/// Configures POLYGONZM geometry storage and spatial indexes
/// </summary>
public class ContentBoundaryConfiguration : IEntityTypeConfiguration<ContentBoundary>
{
    public void Configure(EntityTypeBuilder<ContentBoundary> builder)
    {
        builder.ToTable("content_boundaries");
        
        // Primary key
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasColumnName("id")
            .IsRequired();
        
        // Foreign key to content ingestion
        builder.Property(b => b.ContentIngestionId)
            .HasColumnName("content_ingestion_id")
            .IsRequired();
        
        builder.HasOne(b => b.ContentIngestion)
            .WithOne()
            .HasForeignKey<ContentBoundary>(b => b.ContentIngestionId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Unique index on content ingestion (one boundary per ingestion)
        builder.HasIndex(b => b.ContentIngestionId)
            .IsUnique()
            .HasDatabaseName("ix_content_boundaries_content_ingestion_id");
        
        // Boundary geometry (POLYGONZM)
        builder.Property(b => b.BoundaryGeometry)
            .HasColumnName("boundary_geometry")
            .HasColumnType("geometry(PolygonZM, 4326)")
            .IsRequired();
        
        // Spatial index for overlap/intersection queries
        builder.HasIndex(b => b.BoundaryGeometry)
            .HasMethod("gist")
            .HasDatabaseName("ix_content_boundaries_geometry_gist");
        
        // Owned entity: BoundingBox4D (stored as separate columns)
        builder.OwnsOne(b => b.BoundingBox, bbox =>
        {
            bbox.Property(bb => bb.MinX).HasColumnName("min_x").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxX).HasColumnName("max_x").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MinY).HasColumnName("min_y").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxY).HasColumnName("max_y").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MinZ).HasColumnName("min_z").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxZ).HasColumnName("max_z").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MinM).HasColumnName("min_m").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxM).HasColumnName("max_m").HasPrecision(18, 6).IsRequired();
            
            // Composite index for bounding box queries
            bbox.HasIndex("MinX", "MaxX", "MinY", "MaxY")
                .HasDatabaseName("ix_content_boundaries_bbox");
        });
        
        // Boundary statistics
        builder.Property(b => b.BoundaryArea)
            .HasColumnName("boundary_area")
            .HasPrecision(18, 6)
            .IsRequired();
        
        builder.HasIndex(b => b.BoundaryArea)
            .HasDatabaseName("ix_content_boundaries_area");
        
        builder.Property(b => b.BoundaryPerimeter)
            .HasColumnName("boundary_perimeter")
            .HasPrecision(18, 6)
            .IsRequired();
        
        builder.Property(b => b.AtomCount)
            .HasColumnName("atom_count")
            .IsRequired();
        
        builder.HasIndex(b => b.AtomCount)
            .HasDatabaseName("ix_content_boundaries_atom_count");
        
        builder.Property(b => b.Density)
            .HasColumnName("density")
            .HasPrecision(18, 6)
            .IsRequired();
        
        builder.HasIndex(b => b.Density)
            .HasDatabaseName("ix_content_boundaries_density");
        
        // Centroid (POINTZM)
        builder.Property(b => b.Centroid)
            .HasColumnName("centroid")
            .HasColumnType("geometry(PointZM, 4326)")
            .IsRequired();
        
        // Spatial index on centroid for distance queries
        builder.HasIndex(b => b.Centroid)
            .HasMethod("gist")
            .HasDatabaseName("ix_content_boundaries_centroid_gist");
        
        // Computation metadata
        builder.Property(b => b.ComputedAt)
            .HasColumnName("computed_at")
            .IsRequired();
        
        builder.HasIndex(b => b.ComputedAt)
            .HasDatabaseName("ix_content_boundaries_computed_at");
        
        builder.Property(b => b.ComputationMethod)
            .HasColumnName("computation_method")
            .HasMaxLength(50)
            .IsRequired()
            .HasDefaultValue("ConvexHull");
        
        builder.HasIndex(b => b.ComputationMethod)
            .HasDatabaseName("ix_content_boundaries_method");
        
        // Base entity audit fields
        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(b => b.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();
        
        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);
        
        builder.Property(b => b.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        builder.Property(b => b.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(b => b.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);
        
        builder.Property(b => b.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        // Global query filter for soft delete
        builder.HasQueryFilter(b => !b.IsDeleted);
        
        // Composite index for common filtering
        builder.HasIndex(b => new { b.ComputationMethod, b.Density, b.IsDeleted })
            .HasDatabaseName("ix_content_boundaries_method_density_deleted");
    }
}
