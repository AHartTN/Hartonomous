using Hartonomous.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Data.Configurations;

/// <summary>
/// EF Core configuration for Embedding entity
/// Configures MULTIPOINTZM geometry storage and spatial indexes
/// </summary>
public class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.ToTable("embeddings");
        
        // Primary key
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();
        
        // Foreign key to constant
        builder.Property(e => e.ConstantId)
            .HasColumnName("constant_id")
            .IsRequired();
        
        builder.HasOne(e => e.Constant)
            .WithMany()
            .HasForeignKey(e => e.ConstantId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasIndex(e => e.ConstantId)
            .HasDatabaseName("ix_embeddings_constant_id");
        
        // Vector geometry (MULTIPOINTZM)
        builder.Property(e => e.VectorGeometry)
            .HasColumnName("vector_geometry")
            .HasColumnType("geometry(MultiPointZM, 4326)")
            .IsRequired();
        
        // Spatial index for k-NN similarity search
        builder.HasIndex(e => e.VectorGeometry)
            .HasMethod("gist")
            .HasDatabaseName("ix_embeddings_vector_gist");
        
        // Dimensions
        builder.Property(e => e.Dimensions)
            .HasColumnName("dimensions")
            .IsRequired();
        
        builder.HasIndex(e => e.Dimensions)
            .HasDatabaseName("ix_embeddings_dimensions");
        
        // Model information
        builder.Property(e => e.ModelName)
            .HasColumnName("model_name")
            .HasMaxLength(100)
            .IsRequired();
        
        builder.Property(e => e.ModelVersion)
            .HasColumnName("model_version")
            .HasMaxLength(50)
            .IsRequired(false);
        
        builder.HasIndex(e => new { e.ModelName, e.ModelVersion })
            .HasDatabaseName("ix_embeddings_model");
        
        // Timestamps
        builder.Property(e => e.GeneratedAt)
            .HasColumnName("generated_at")
            .IsRequired();
        
        builder.HasIndex(e => e.GeneratedAt)
            .HasDatabaseName("ix_embeddings_generated_at");
        
        // Vector properties
        builder.Property(e => e.Magnitude)
            .HasColumnName("magnitude")
            .HasPrecision(18, 10)
            .IsRequired();
        
        builder.Property(e => e.IsNormalized)
            .HasColumnName("is_normalized")
            .IsRequired();
        
        builder.HasIndex(e => e.IsNormalized)
            .HasDatabaseName("ix_embeddings_is_normalized");
        
        // Base entity audit fields
        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        
        builder.Property(e => e.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();
        
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);
        
        builder.Property(e => e.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(e => e.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);
        
        builder.Property(e => e.DeletedBy)
            .HasColumnName("deleted_by")
            .HasMaxLength(256)
            .IsRequired(false);
        
        // Global query filter for soft delete
        builder.HasQueryFilter(e => !e.IsDeleted);
        
        // Composite index for common queries
        builder.HasIndex(e => new { e.ConstantId, e.ModelName, e.IsDeleted })
            .HasDatabaseName("ix_embeddings_constant_model_deleted");
    }
}
