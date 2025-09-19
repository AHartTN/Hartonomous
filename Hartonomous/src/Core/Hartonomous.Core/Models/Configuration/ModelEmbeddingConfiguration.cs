/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Entity Framework configuration for the ModelEmbedding entity,
 * leveraging SQL Server 2025 native vector storage with HNSW indexing for sub-millisecond similarity search.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hartonomous.Core.Models.Configuration;

public class ModelEmbeddingConfiguration : IEntityTypeConfiguration<ModelEmbedding>
{
    public void Configure(EntityTypeBuilder<ModelEmbedding> builder)
    {
        builder.HasKey(e => e.EmbeddingId);

        builder.Property(e => e.EmbeddingType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.EmbeddingSource)
            .HasMaxLength(255);

        builder.Property(e => e.UserId)
            .IsRequired()
            .HasMaxLength(128);

        // SQL Server 2025 native vector storage
        builder.Property(e => e.EmbeddingVector)
            .HasColumnType("vector(1536)"); // Common dimension for embeddings

        builder.HasIndex(e => e.ModelId);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.EmbeddingType);
        builder.HasIndex(e => e.QualityScore);

        // HNSW vector index for similarity search
        builder.HasIndex(e => e.EmbeddingVector)
            .HasDatabaseName("IX_ModelEmbedding_Vector")
            .HasMethod("hnsw");

        builder.Property<string>("UserId").HasMaxLength(128);

        builder.HasOne(e => e.Model)
            .WithMany(m => m.Embeddings)
            .HasForeignKey(e => e.ModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}