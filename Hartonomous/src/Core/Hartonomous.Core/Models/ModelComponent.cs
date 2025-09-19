/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ModelComponent entity for granular neural network analysis,
 * core to mechanistic interpretability and intelligent agent distillation processes.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents granular components within model layers (neurons, attention heads, weight matrices)
/// Core entity for mechanistic interpretability and agent distillation
/// </summary>
public class ModelComponent
{
    public Guid ComponentId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    [Required]
    public Guid LayerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ComponentType { get; set; } = string.Empty; // neuron, attention_head, weight_matrix, bias_vector

    [Required]
    [MaxLength(255)]
    public string ComponentName { get; set; } = string.Empty;

    public int ComponentIndex { get; set; }
    public int DimensionStart { get; set; }
    public int DimensionEnd { get; set; }

    /// <summary>
    /// Functional interpretation of this component
    /// </summary>
    [MaxLength(1000)]
    public string? FunctionalDescription { get; set; }

    /// <summary>
    /// Activation patterns and behavioral metadata
    /// </summary>
    public string BehaviorMetadata { get; set; } = "{}";

    /// <summary>
    /// Relevance score for distillation and pruning
    /// </summary>
    public double RelevanceScore { get; set; } = 0.0;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAccessedAt { get; set; }

    // Knowledge graph synchronization properties
    public bool IsSyncedToGraph { get; set; } = false;
    public DateTime? GraphSyncTimestamp { get; set; }
    public int? GraphSyncFailures { get; set; }
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ModelLayer Layer { get; set; } = null!;
    public virtual ICollection<ComponentWeight> Weights { get; set; } = new List<ComponentWeight>();
    public virtual ICollection<ComponentEmbedding> Embeddings { get; set; } = new List<ComponentEmbedding>();
    public virtual ICollection<AgentComponent> AgentComponents { get; set; } = new List<AgentComponent>();
    public virtual ICollection<ActivationPattern> ActivationPatterns { get; set; } = new List<ActivationPattern>();

    /// <summary>
    /// Get typed behavior metadata
    /// </summary>
    public T? GetBehaviorMetadata<T>() where T : class
    {
        if (string.IsNullOrEmpty(BehaviorMetadata) || BehaviorMetadata == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(BehaviorMetadata);
    }

    /// <summary>
    /// Set typed behavior metadata
    /// </summary>
    public void SetBehaviorMetadata<T>(T metadata) where T : class
    {
        BehaviorMetadata = JsonSerializer.Serialize(metadata);
    }
}