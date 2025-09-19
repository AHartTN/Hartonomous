/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the AgentComponent entity mapping model components to distilled agents,
 * enabling traceability from specialized AI agent behavior back to source neural network components.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Maps model components to distilled agents
/// Enables traceability from agent behavior back to source model components
/// </summary>
public class AgentComponent
{
    public Guid AgentComponentId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AgentId { get; set; }

    [Required]
    public Guid ModelComponentId { get; set; }

    [Required]
    public Guid ModelId { get; set; }

    /// <summary>
    /// Role this component plays in the agent
    /// </summary>
    [MaxLength(100)]
    public string ComponentRole { get; set; } = string.Empty; // decision_making, feature_extraction, output_generation

    /// <summary>
    /// Weight or importance of this component in the agent
    /// </summary>
    public double ComponentWeight { get; set; } = 1.0;

    /// <summary>
    /// Transformation applied to the original component
    /// </summary>
    public string TransformationMetadata { get; set; } = "{}";

    /// <summary>
    /// Performance metrics for this component within the agent
    /// </summary>
    public string ComponentMetrics { get; set; } = "{}";

    /// <summary>
    /// Whether this component is currently active in the agent
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime IntegratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual DistilledAgent Agent { get; set; } = null!;
    public virtual ModelComponent ModelComponent { get; set; } = null!;
    public virtual Model Model { get; set; } = null!;

    /// <summary>
    /// Get transformation metadata as typed object
    /// </summary>
    public T? GetTransformationMetadata<T>() where T : class
    {
        if (string.IsNullOrEmpty(TransformationMetadata) || TransformationMetadata == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(TransformationMetadata);
    }

    /// <summary>
    /// Set transformation metadata from typed object
    /// </summary>
    public void SetTransformationMetadata<T>(T metadata) where T : class
    {
        TransformationMetadata = JsonSerializer.Serialize(metadata);
    }

    /// <summary>
    /// Get component metrics as typed object
    /// </summary>
    public T? GetComponentMetrics<T>() where T : class
    {
        if (string.IsNullOrEmpty(ComponentMetrics) || ComponentMetrics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ComponentMetrics);
    }

    /// <summary>
    /// Set component metrics from typed object
    /// </summary>
    public void SetComponentMetrics<T>(T metrics) where T : class
    {
        ComponentMetrics = JsonSerializer.Serialize(metrics);
    }
}