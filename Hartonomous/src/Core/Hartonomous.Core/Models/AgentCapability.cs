/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the AgentCapability entity for distilled agent skill representation,
 * enabling capability-based discovery, composition, and marketplace functionality.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents specific capabilities of distilled agents
/// Enables capability-based discovery and composition
/// </summary>
public class AgentCapability
{
    public Guid CapabilityId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AgentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string CapabilityName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category of capability
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty; // reasoning, creativity, analysis, communication

    /// <summary>
    /// Proficiency level in this capability
    /// </summary>
    public double ProficiencyScore { get; set; } = 0.0;

    /// <summary>
    /// Evidence supporting this capability claim
    /// </summary>
    public string Evidence { get; set; } = "[]";

    /// <summary>
    /// Benchmarks and test results
    /// </summary>
    public string BenchmarkResults { get; set; } = "{}";

    /// <summary>
    /// Usage statistics for this capability
    /// </summary>
    public string UsageStatistics { get; set; } = "{}";

    /// <summary>
    /// Whether this capability is currently enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime DemonstratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastValidatedAt { get; set; }

    // Navigation properties
    public virtual DistilledAgent Agent { get; set; } = null!;

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
    /// Get benchmark results as typed object
    /// </summary>
    public T? GetBenchmarkResults<T>() where T : class
    {
        if (string.IsNullOrEmpty(BenchmarkResults) || BenchmarkResults == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(BenchmarkResults);
    }

    /// <summary>
    /// Set benchmark results from typed object
    /// </summary>
    public void SetBenchmarkResults<T>(T results) where T : class
    {
        BenchmarkResults = JsonSerializer.Serialize(results);
    }

    /// <summary>
    /// Get usage statistics as typed object
    /// </summary>
    public T? GetUsageStatistics<T>() where T : class
    {
        if (string.IsNullOrEmpty(UsageStatistics) || UsageStatistics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(UsageStatistics);
    }

    /// <summary>
    /// Set usage statistics from typed object
    /// </summary>
    public void SetUsageStatistics<T>(T statistics) where T : class
    {
        UsageStatistics = JsonSerializer.Serialize(statistics);
    }
}