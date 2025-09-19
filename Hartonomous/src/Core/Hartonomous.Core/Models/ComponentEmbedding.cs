/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ComponentEmbedding entity for fine-grained vector analysis,
 * enabling component-level similarity search and clustering with native SQL Server 2025 vector storage.
 */

using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents vector embeddings for individual model components
/// Enables fine-grained similarity search and component clustering
/// </summary>
public class ComponentEmbedding
{
    public Guid EmbeddingId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ComponentId { get; set; }

    [Required]
    public Guid ModelId { get; set; }

    [Required]
    [MaxLength(100)]
    public string EmbeddingType { get; set; } = string.Empty; // functional, behavioral, structural, semantic

    /// <summary>
    /// Native vector storage using SQL Server 2025 VECTOR data type
    /// </summary>
    public byte[] EmbeddingVector { get; set; } = Array.Empty<byte>();

    public int VectorDimension { get; set; }

    /// <summary>
    /// Context in which this embedding was computed
    /// </summary>
    [MaxLength(500)]
    public string ComputationContext { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score of the embedding
    /// </summary>
    public double ConfidenceScore { get; set; } = 1.0;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual ModelComponent Component { get; set; } = null!;
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