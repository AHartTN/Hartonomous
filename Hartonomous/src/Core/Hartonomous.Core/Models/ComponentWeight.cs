/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ComponentWeight entity for granular neural network weight analysis,
 * supporting mechanistic interpretability and targeted fine-tuning algorithms.
 */

using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents individual weights within model components
/// Enables weight-level analysis and targeted fine-tuning
/// </summary>
public class ComponentWeight
{
    public Guid WeightId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ComponentId { get; set; }

    [Required]
    public Guid ModelId { get; set; }

    public int WeightIndex { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }

    public double WeightValue { get; set; }
    public double GradientMagnitude { get; set; }
    public double ImportanceScore { get; set; }

    /// <summary>
    /// Statistical significance of this weight
    /// </summary>
    public double StatisticalSignificance { get; set; }

    /// <summary>
    /// Whether this weight is considered critical for model behavior
    /// </summary>
    public bool IsCritical { get; set; } = false;

    /// <summary>
    /// Comprehensive weight analysis data including gradient history,
    /// sensitivity analysis, and optimization trajectory metadata
    /// Stored as JSON for flexible schema evolution
    /// </summary>
    public string WeightData { get; set; } = "{}";

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
    /// Get typed weight data
    /// </summary>
    public T? GetWeightData<T>() where T : class
    {
        if (string.IsNullOrEmpty(WeightData) || WeightData == "{}")
            return null;

        return System.Text.Json.JsonSerializer.Deserialize<T>(WeightData);
    }

    /// <summary>
    /// Set typed weight data
    /// </summary>
    public void SetWeightData<T>(T data) where T : class
    {
        WeightData = System.Text.Json.JsonSerializer.Serialize(data);
    }
}