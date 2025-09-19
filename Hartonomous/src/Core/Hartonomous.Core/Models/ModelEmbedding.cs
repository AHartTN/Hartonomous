/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ModelEmbedding entity for AI-native vector storage,
 * leveraging SQL Server 2025 VECTOR data types with HNSW indexing for sub-millisecond similarity search.
 */

using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents vector embeddings for models in the semantic space
/// Enables model similarity search and clustering using SQL Server 2025 vector capabilities
/// </summary>
public class ModelEmbedding
{
    public Guid EmbeddingId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    [Required]
    [MaxLength(100)]
    public string EmbeddingType { get; set; } = string.Empty; // architectural, functional, capability, performance

    /// <summary>
    /// Native vector storage using SQL Server 2025 VECTOR data type
    /// Automatically indexed with HNSW for sub-millisecond similarity search
    /// </summary>
    public byte[] EmbeddingVector { get; set; } = Array.Empty<byte>();

    public int VectorDimension { get; set; }

    /// <summary>
    /// Source of the embedding computation
    /// </summary>
    [MaxLength(255)]
    public string EmbeddingSource { get; set; } = string.Empty; // model_analysis, user_feedback, performance_metrics

    /// <summary>
    /// Quality score of the embedding
    /// </summary>
    public double QualityScore { get; set; } = 1.0;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual Model Model { get; set; } = null!;

    /// <summary>
    /// Convert byte array back to float array for computation
    /// </summary>
    public float[] GetVectorAsFloats()
    {
        if (EmbeddingVector.Length == 0)
            return Array.Empty<float>();

        var floats = new float[EmbeddingVector.Length / sizeof(float)];
        Buffer.BlockCopy(EmbeddingVector, 0, floats, 0, EmbeddingVector.Length);
        return floats;
    }

    /// <summary>
    /// Set vector from float array
    /// </summary>
    public void SetVectorFromFloats(float[] vector)
    {
        EmbeddingVector = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, EmbeddingVector, 0, EmbeddingVector.Length);
        VectorDimension = vector.Length;
    }
}