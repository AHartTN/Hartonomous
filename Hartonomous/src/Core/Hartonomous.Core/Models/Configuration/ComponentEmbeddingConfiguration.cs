using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ComponentEmbeddingConfiguration : IEntityTypeConfiguration<ComponentEmbedding>
{
    public void Configure(EntityTypeBuilder<ComponentEmbedding> builder)
    {
        builder.HasKey(e => e.EmbeddingId);

        builder.Property(e => e.EmbeddingType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ComputationContext)
            .HasMaxLength(500);

        builder.Property(e => e.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.EmbeddingVector)
            .HasColumnType("vector(768)"); // Smaller dimension for components

        builder.HasIndex(e => e.ComponentId);
        builder.HasIndex(e => e.ModelId);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.EmbeddingType);
        builder.HasIndex(e => e.ConfidenceScore);

        builder.HasIndex(e => e.EmbeddingVector)
            .HasDatabaseName("IX_ComponentEmbedding_Vector")
            .HasMethod("hnsw");

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(e => e.Component)
            .WithMany(c => c.Embeddings)
            .HasForeignKey(e => e.ComponentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Model)
            .WithMany()
            .HasForeignKey(e => e.ModelId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}