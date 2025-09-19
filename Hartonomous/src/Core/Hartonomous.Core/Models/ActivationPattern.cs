/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ActivationPattern entity for neural activation analysis,
 * enabling information flow tracking and mechanistic interpretability across model components.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents activation patterns across model components
/// Enables understanding of how information flows through the network
/// </summary>
public class ActivationPattern
{
    public Guid PatternId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    [Required]
    public Guid ComponentId { get; set; }

    public Guid? AttentionHeadId { get; set; }

    /// <summary>
    /// Type of activation pattern
    /// </summary>
    [MaxLength(100)]
    public string PatternType { get; set; } = string.Empty; // sparse, distributed, localized, oscillatory

    /// <summary>
    /// Context or stimulus that triggered this pattern
    /// </summary>
    [MaxLength(500)]
    public string TriggerContext { get; set; } = string.Empty;

    /// <summary>
    /// Serialized activation values and coordinates
    /// </summary>
    public string ActivationData { get; set; } = "{}";

    /// <summary>
    /// Statistical measures of the pattern
    /// </summary>
    public string PatternStatistics { get; set; } = "{}";

    /// <summary>
    /// Strength of the activation pattern
    /// </summary>
    public double PatternStrength { get; set; } = 0.0;

    /// <summary>
    /// Duration or persistence of the pattern
    /// </summary>
    public double PatternDuration { get; set; } = 0.0;

    /// <summary>
    /// Frequency of occurrence
    /// </summary>
    public double Frequency { get; set; } = 0.0;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ModelComponent Component { get; set; } = null!;
    public virtual AttentionHead? AttentionHead { get; set; }

    /// <summary>
    /// Get activation data as typed object
    /// </summary>
    public T? GetActivationData<T>() where T : class
    {
        if (string.IsNullOrEmpty(ActivationData) || ActivationData == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ActivationData);
    }

    /// <summary>
    /// Set activation data from typed object
    /// </summary>
    public void SetActivationData<T>(T data) where T : class
    {
        ActivationData = JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// Get pattern statistics as typed object
    /// </summary>
    public T? GetPatternStatistics<T>() where T : class
    {
        if (string.IsNullOrEmpty(PatternStatistics) || PatternStatistics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(PatternStatistics);
    }

    /// <summary>
    /// Set pattern statistics from typed object
    /// </summary>
    public void SetPatternStatistics<T>(T statistics) where T : class
    {
        PatternStatistics = JsonSerializer.Serialize(statistics);
    }
}