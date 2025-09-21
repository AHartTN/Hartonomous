/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ModelLayer entity for transformer layer representation,
 * enabling mechanistic interpretability and layer-by-layer neural network analysis.
 */

using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents a transformer layer within a neural network model
/// Enables layer-by-layer analysis for mechanistic interpretability
/// </summary>
public class ModelLayer
{
    public Guid LayerId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    public int LayerIndex { get; set; }

    [MaxLength(50)]
    public string LayerType { get; set; } = string.Empty; // attention, feedforward, embedding, normalization

    public int InputDimension { get; set; }
    public int OutputDimension { get; set; }
    public int NumHeads { get; set; } // for attention layers
    public int FeedforwardDimension { get; set; } // for FFN layers

    /// <summary>
    /// FILESTREAM storage for layer weights
    /// </summary>
    public byte[]? LayerWeights { get; set; }

    /// <summary>
    /// Layer-specific configuration metadata
    /// </summary>
    public string ConfigMetadata { get; set; } = "{}";

    /// <summary>
    /// Detailed layer configuration for mechanistic interpretability
    /// Stores layer-specific parameters, activation functions, and architectural details
    /// </summary>
    public string LayerConfig { get; set; } = "{}";

    /// <summary>
    /// Interpretability score indicating how well this layer's function is understood
    /// Range: 0.0 (completely opaque) to 1.0 (fully interpretable)
    /// </summary>
    public double InterpretabilityScore { get; set; } = 0.0;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ICollection<ModelComponent> Components { get; set; } = new List<ModelComponent>();
    public virtual ICollection<AttentionHead> AttentionHeads { get; set; } = new List<AttentionHead>();
    public virtual ICollection<NeuronInterpretation> NeuronInterpretations { get; set; } = new List<NeuronInterpretation>();
}