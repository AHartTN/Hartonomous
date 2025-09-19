/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ModelPerformanceMetric entity for comprehensive model analytics,
 * enabling performance tracking, optimization analysis, and comparative benchmarking.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Tracks performance metrics for models and their components
/// Enables optimization and comparative analysis
/// </summary>
public class ModelPerformanceMetric
{
    public Guid MetricId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    public Guid? ComponentId { get; set; }
    public Guid? AgentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// Category of performance metric
    /// </summary>
    [MaxLength(100)]
    public string MetricCategory { get; set; } = string.Empty; // accuracy, latency, throughput, resource_usage, quality

    /// <summary>
    /// The measured value
    /// </summary>
    public double MetricValue { get; set; }

    /// <summary>
    /// Unit of measurement
    /// </summary>
    [MaxLength(50)]
    public string Unit { get; set; } = string.Empty; // ms, tokens/sec, GB, percentage, score

    /// <summary>
    /// Benchmark or test context
    /// </summary>
    [MaxLength(500)]
    public string BenchmarkContext { get; set; } = string.Empty;

    /// <summary>
    /// Additional metric metadata
    /// </summary>
    public string MetricMetadata { get; set; } = "{}";

    /// <summary>
    /// Environment conditions during measurement
    /// </summary>
    public string EnvironmentContext { get; set; } = "{}";

    /// <summary>
    /// Statistical confidence in the measurement
    /// </summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// Number of samples used for this metric
    /// </summary>
    public int SampleSize { get; set; } = 1;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime MeasuredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ValidatedAt { get; set; }

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ModelComponent? Component { get; set; }
    public virtual DistilledAgent? Agent { get; set; }

    /// <summary>
    /// Get metric metadata as typed object
    /// </summary>
    public T? GetMetricMetadata<T>() where T : class
    {
        if (string.IsNullOrEmpty(MetricMetadata) || MetricMetadata == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(MetricMetadata);
    }

    /// <summary>
    /// Set metric metadata from typed object
    /// </summary>
    public void SetMetricMetadata<T>(T metadata) where T : class
    {
        MetricMetadata = JsonSerializer.Serialize(metadata);
    }

    /// <summary>
    /// Get environment context as typed object
    /// </summary>
    public T? GetEnvironmentContext<T>() where T : class
    {
        if (string.IsNullOrEmpty(EnvironmentContext) || EnvironmentContext == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(EnvironmentContext);
    }

    /// <summary>
    /// Set environment context from typed object
    /// </summary>
    public void SetEnvironmentContext<T>(T context) where T : class
    {
        EnvironmentContext = JsonSerializer.Serialize(context);
    }
}