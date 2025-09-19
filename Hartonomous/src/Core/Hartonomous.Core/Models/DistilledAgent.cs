/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the DistilledAgent entity representing specialized AI agents distilled from models,
 * core to the Agent Factory enabling deployable, domain-focused AI capabilities.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents a specialized AI agent distilled from model components
/// Core entity for the Agent Factory - deployable, focused AI capabilities
/// </summary>
public class DistilledAgent
{
    public Guid AgentId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string AgentName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Domain specialization of this agent
    /// </summary>
    [MaxLength(100)]
    public string Domain { get; set; } = string.Empty; // chess, customer_service, code_analysis, creative_writing

    /// <summary>
    /// Source models used for distillation
    /// </summary>
    public string SourceModelIds { get; set; } = "[]";

    /// <summary>
    /// Capabilities and skill set
    /// </summary>
    public string Capabilities { get; set; } = "[]";

    /// <summary>
    /// Agent configuration and parameters
    /// </summary>
    public string Configuration { get; set; } = "{}";

    /// <summary>
    /// Deployment configuration
    /// </summary>
    public string DeploymentConfig { get; set; } = "{}";

    /// <summary>
    /// Current deployment status
    /// </summary>
    public AgentStatus Status { get; set; } = AgentStatus.Draft;

    /// <summary>
    /// Performance metrics and analytics
    /// </summary>
    public string PerformanceMetrics { get; set; } = "{}";

    /// <summary>
    /// Version for deployment tracking
    /// </summary>
    [MaxLength(50)]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastDeployedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }

    // Navigation properties
    public virtual ICollection<AgentComponent> AgentComponents { get; set; } = new List<AgentComponent>();
    public virtual ICollection<AgentCapability> AgentCapabilities { get; set; } = new List<AgentCapability>();

    /// <summary>
    /// Get source model IDs as typed list
    /// </summary>
    public List<Guid> GetSourceModelIds()
    {
        if (string.IsNullOrEmpty(SourceModelIds) || SourceModelIds == "[]")
            return new List<Guid>();

        return JsonSerializer.Deserialize<List<Guid>>(SourceModelIds) ?? new List<Guid>();
    }

    /// <summary>
    /// Set source model IDs from list
    /// </summary>
    public void SetSourceModelIds(List<Guid> modelIds)
    {
        SourceModelIds = JsonSerializer.Serialize(modelIds);
    }

    /// <summary>
    /// Get capabilities as typed list
    /// </summary>
    public List<string> GetCapabilities()
    {
        if (string.IsNullOrEmpty(Capabilities) || Capabilities == "[]")
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(Capabilities) ?? new List<string>();
    }

    /// <summary>
    /// Set capabilities from list
    /// </summary>
    public void SetCapabilities(List<string> capabilities)
    {
        Capabilities = JsonSerializer.Serialize(capabilities);
    }

    /// <summary>
    /// Get typed configuration
    /// </summary>
    public T? GetConfiguration<T>() where T : class
    {
        if (string.IsNullOrEmpty(Configuration) || Configuration == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(Configuration);
    }

    /// <summary>
    /// Set typed configuration
    /// </summary>
    public void SetConfiguration<T>(T config) where T : class
    {
        Configuration = JsonSerializer.Serialize(config);
    }

    /// <summary>
    /// Get typed deployment configuration
    /// </summary>
    public T? GetDeploymentConfig<T>() where T : class
    {
        if (string.IsNullOrEmpty(DeploymentConfig) || DeploymentConfig == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(DeploymentConfig);
    }

    /// <summary>
    /// Set typed deployment configuration
    /// </summary>
    public void SetDeploymentConfig<T>(T config) where T : class
    {
        DeploymentConfig = JsonSerializer.Serialize(config);
    }

    /// <summary>
    /// Get typed performance metrics
    /// </summary>
    public T? GetPerformanceMetrics<T>() where T : class
    {
        if (string.IsNullOrEmpty(PerformanceMetrics) || PerformanceMetrics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(PerformanceMetrics);
    }

    /// <summary>
    /// Set typed performance metrics
    /// </summary>
    public void SetPerformanceMetrics<T>(T metrics) where T : class
    {
        PerformanceMetrics = JsonSerializer.Serialize(metrics);
    }
}

public enum AgentStatus
{
    Draft,
    Training,
    Testing,
    Ready,
    Deployed,
    Deprecated,
    Error
}