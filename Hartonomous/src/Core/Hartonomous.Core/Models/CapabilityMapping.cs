/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the CapabilityMapping entity linking AI capabilities to neural components,
 * enabling systematic understanding of how model architecture creates specific AI capabilities.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Maps capabilities to model components and neural patterns
/// Enables understanding of how model architecture creates specific capabilities
/// </summary>
public class CapabilityMapping
{
    public Guid MappingId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    public Guid? ComponentId { get; set; }
    public Guid? LayerId { get; set; }

    [Required]
    [MaxLength(255)]
    public string CapabilityName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category of capability
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty; // reasoning, memory, language, vision, creativity

    /// <summary>
    /// Evidence supporting this capability mapping
    /// </summary>
    public string Evidence { get; set; } = "[]";

    /// <summary>
    /// Strength of the capability
    /// </summary>
    public double CapabilityStrength { get; set; } = 0.0;

    /// <summary>
    /// Confidence in this mapping
    /// </summary>
    public double MappingConfidence { get; set; } = 0.0;

    /// <summary>
    /// Method used to establish this mapping
    /// </summary>
    [MaxLength(100)]
    public string MappingMethod { get; set; } = string.Empty; // ablation_study, activation_analysis, gradient_analysis

    /// <summary>
    /// Detailed analysis results
    /// </summary>
    public string AnalysisResults { get; set; } = "{}";

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime MappedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastValidatedAt { get; set; }

    // Knowledge graph synchronization properties
    public bool IsSyncedToGraph { get; set; } = false;
    public DateTime? GraphSyncTimestamp { get; set; }

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ModelComponent? Component { get; set; }
    public virtual ModelLayer? Layer { get; set; }

    /// <summary>
    /// Get evidence as typed list
    /// </summary>
    public List<T> GetEvidence<T>() where T : class
    {
        if (string.IsNullOrEmpty(Evidence) || Evidence == "[]")
            return new List<T>();

        return JsonSerializer.Deserialize<List<T>>(Evidence) ?? new List<T>();
    }

    /// <summary>
    /// Set evidence from typed list
    /// </summary>
    public void SetEvidence<T>(List<T> evidence) where T : class
    {
        Evidence = JsonSerializer.Serialize(evidence);
    }

    /// <summary>
    /// Get analysis results as typed object
    /// </summary>
    public T? GetAnalysisResults<T>() where T : class
    {
        if (string.IsNullOrEmpty(AnalysisResults) || AnalysisResults == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(AnalysisResults);
    }

    /// <summary>
    /// Set analysis results from typed object
    /// </summary>
    public void SetAnalysisResults<T>(T results) where T : class
    {
        AnalysisResults = JsonSerializer.Serialize(results);
    }
}