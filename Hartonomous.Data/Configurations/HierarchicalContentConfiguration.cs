using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for HierarchicalContent entity
/// Configures GEOMETRYCOLLECTIONZM storage and hierarchical relationships
/// </summary>
public class HierarchicalContentConfiguration : IEntityTypeConfiguration<HierarchicalContent>
{
    public void Configure(EntityTypeBuilder<HierarchicalContent> builder)
    {
        builder.ToTable("hierarchical_content");
        
        // Primary key
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("id")
            .IsRequired();
        
        // Foreign key to content ingestion
        builder.Property(h => h.ContentIngestionId)
            .HasColumnName("content_ingestion_id")
            .IsRequired();
        
        builder.HasOne(h => h.ContentIngestion)
            .WithMany()
            .HasForeignKey(h => h.ContentIngestionId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasIndex(h => h.ContentIngestionId)
            .HasDatabaseName("ix_hierarchical_content_content_ingestion_id");
        
        // Complete geometry (GEOMETRYCOLLECTIONZM)
        builder.Property(h => h.CompleteGeometry)
            .HasColumnName("complete_geometry")
            .HasColumnType("geometry(GeometryCollectionZM, 4326)")
            .IsRequired();
        
        // Spatial index for geometric queries
        builder.HasIndex(h => h.CompleteGeometry)
            .HasMethod("gist")
            .HasDatabaseName("ix_hierarchical_content_geometry_gist");
        
        // Owned entity: BoundingBox4D (stored as separate columns)
        builder.OwnsOne(h => h.BoundingBox, bbox =>
        {
            bbox.Property(bb => bb.MinX).HasColumnName("min_x").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxX).HasColumnName("max_x").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MinY).HasColumnName("min_y").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxY).HasColumnName("max_y").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MinZ).HasColumnName("min_z").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxZ).HasColumnName("max_z").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MinM).HasColumnName("min_m").HasPrecision(18, 6).IsRequired();
            bbox.Property(bb => bb.MaxM).HasColumnName("max_m").HasPrecision(18, 6).IsRequired();
        });
        
        // Composite index for bounding box queries
        builder.HasIndex("BoundingBox_MinX", "BoundingBox_MaxX", "BoundingBox_MinY", "BoundingBox_MaxY")
            .HasDatabaseName("ix_hierarchical_content_bbox");
        
        // Hierarchy structure
        builder.Property(h => h.HierarchyLevel)
            .HasColumnName("hierarchy_level")
            .IsRequired();
        
        builder.HasIndex(h => h.HierarchyLevel)
            .HasDatabaseName("ix_hierarchical_content_hierarchy_level");
        
        builder.Property(h => h.ParentId)
            .HasColumnName("parent_id")
            .IsRequired(false);
        
        // Self-referencing relationship
        builder.HasOne(h => h.Parent)
            .WithMany(h => h.Children)
            .HasForeignKey(h => h.ParentId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent cascading deletes in hierarchy
        
        builder.HasIndex(h => h.ParentId)
            .HasDatabaseName("ix_hierarchical_content_parent_id");
        
        // Content metadata
        builder.Property(h => h.Label)
            .HasColumnName("label")
            .HasMaxLength(50)
            .IsRequired();
        
        builder.HasIndex(h => h.Label)
            .HasDatabaseName("ix_hierarchical_content_label");
        
        builder.Property(h => h.Title)
            .HasColumnName("title")
            .HasMaxLength(500)
            .IsRequired(false);
        
        builder.HasIndex(h => h.Title)
            .HasDatabaseName("ix_hierarchical_content_title")
            .HasFilter("title IS NOT NULL"); // Partial index
        
        builder.Property(h => h.Ordinal)
            .HasColumnName("ordinal")
            .IsRequired();
        
        builder.HasIndex(h => h.Ordinal)
            .HasDatabaseName("ix_hierarchical_content_ordinal");
        
        // Content statistics
        builder.Property(h => h.AtomCount)
            .HasColumnName("atom_count")
            .IsRequired();
        
        builder.HasIndex(h => h.AtomCount)
            .HasDatabaseName("ix_hierarchical_content_atom_count");
        
        builder.Property(h => h.ChildCount)
            .HasColumnName("child_count")
            .IsRequired()
            .HasDefaultValue(0);
        
        builder.Property(h => h.DescendantCount)
            .HasColumnName("descendant_count")
            .IsRequired()
            .HasDefaultValue(0);
        
        builder.HasIndex(h => h.DescendantCount)
            .HasDatabaseName("ix_hierarchical_content_descendant_count");
        
        // Centroid (POINTZM)
        builder.Property(h => h.Centroid)
            .HasColumnName("centroid")
            .HasColumnType("geometry(PointZM, 4326)")
            .IsRequired();
        
        // Spatial index on centroid
        builder.HasIndex(h => h.Centroid)
            .HasMethod("gist")
            .HasDatabaseName("ix_hierarchical_content_centroid_gist");
        
        // Content offsets
        builder.Property(h => h.StartOffset)
            .HasColumnName("start_offset")
            .IsRequired(false);
        
        builder.Property(h => h.EndOffset)
            .HasColumnName("end_offset")
            .IsRequired(false);
        
        builder.HasIndex("StartOffset", "EndOffset")
            .HasDatabaseName("ix_hierarchical_content_offsets")
            .HasFilter("start_offset IS NOT NULL AND end_offset IS NOT NULL");
        
        // Metadata (JSON)
        builder.Property(h => h.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb") // PostgreSQL JSONB for efficient querying
            .IsRequired(false);
        
        // GIN index on JSONB metadata
        builder.HasIndex(h => h.Metadata)
            .HasMethod("gin")
            .HasDatabaseName("ix_hierarchical_content_metadata_gin")
            .HasFilter("metadata IS NOT NULL");
        
        // Base entity audit fields
        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(h => h.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();
        
        builder.Property(h => h.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);
        
        builder.Property(h => h.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        builder.Property(h => h.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(h => h.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);
        
        builder.Property(h => h.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        // Global query filter for soft delete
        builder.HasQueryFilter(h => !h.IsDeleted);
        
        // Composite indexes for common hierarchy queries
        builder.HasIndex(h => new { h.ContentIngestionId, h.HierarchyLevel, h.Ordinal, h.IsDeleted })
            .HasDatabaseName("ix_hierarchical_content_ingestion_level_ordinal_deleted");
        
        builder.HasIndex(h => new { h.ParentId, h.Ordinal, h.IsDeleted })
            .HasDatabaseName("ix_hierarchical_content_parent_ordinal_deleted");
        
        builder.HasIndex(h => new { h.Label, h.HierarchyLevel, h.IsDeleted })
            .HasDatabaseName("ix_hierarchical_content_label_level_deleted");
        
        builder.HasIndex(h => new { h.ContentIngestionId, h.ParentId, h.HierarchyLevel })
            .HasDatabaseName("ix_hierarchical_content_tree_navigation");
    }
}
