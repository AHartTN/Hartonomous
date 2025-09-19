/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the AttentionHead entity for transformer attention analysis,
 * supporting attention pattern interpretability and mechanistic understanding of language models.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents individual attention heads within transformer layers
/// Enables attention pattern analysis and interpretability
/// </summary>
public class AttentionHead
{
    public Guid AttentionHeadId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    [Required]
    public Guid LayerId { get; set; }

    public int HeadIndex { get; set; }
    public int HeadDimension { get; set; }

    /// <summary>
    /// Detected attention pattern type
    /// </summary>
    [MaxLength(100)]
    public string AttentionPatternType { get; set; } = string.Empty; // positional, syntactic, semantic, induction

    /// <summary>
    /// Human-readable description of attention function
    /// </summary>
    [MaxLength(1000)]
    public string FunctionalDescription { get; set; } = string.Empty;

    /// <summary>
    /// Statistical analysis of attention patterns
    /// </summary>
    public string AttentionStatistics { get; set; } = "{}";

    /// <summary>
    /// Example attention patterns for interpretability
    /// </summary>
    public string ExamplePatterns { get; set; } = "[]";

    /// <summary>
    /// Specificity score of this attention head
    /// </summary>
    public double SpecificityScore { get; set; } = 0.0;

    /// <summary>
    /// Importance score for model behavior
    /// </summary>
    public double ImportanceScore { get; set; } = 0.0;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ModelLayer Layer { get; set; } = null!;
    public virtual ICollection<ActivationPattern> ActivationPatterns { get; set; } = new List<ActivationPattern>();

    /// <summary>
    /// Get attention statistics as typed object
    /// </summary>
    public T? GetAttentionStatistics<T>() where T : class
    {
        if (string.IsNullOrEmpty(AttentionStatistics) || AttentionStatistics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(AttentionStatistics);
    }

    /// <summary>
    /// Set attention statistics from typed object
    /// </summary>
    public void SetAttentionStatistics<T>(T statistics) where T : class
    {
        AttentionStatistics = JsonSerializer.Serialize(statistics);
    }

    /// <summary>
    /// Get example patterns as typed list
    /// </summary>
    public List<T> GetExamplePatterns<T>() where T : class
    {
        if (string.IsNullOrEmpty(ExamplePatterns) || ExamplePatterns == "[]")
            return new List<T>();

        return JsonSerializer.Deserialize<List<T>>(ExamplePatterns) ?? new List<T>();
    }

    /// <summary>
    /// Set example patterns from typed list
    /// </summary>
    public void SetExamplePatterns<T>(List<T> patterns) where T : class
    {
        ExamplePatterns = JsonSerializer.Serialize(patterns);
    }
}