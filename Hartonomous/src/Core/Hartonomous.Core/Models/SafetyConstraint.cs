/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the SafetyConstraint entity for runtime Constitutional AI enforcement,
 * implementing specific safety measures and behavioral limits for agents and models.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents specific safety constraints applied to agents or models
/// Implements constitutional AI safety measures at runtime
/// </summary>
public class SafetyConstraint
{
    public Guid ConstraintId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ConstitutionalRuleId { get; set; }

    public Guid? AgentId { get; set; }
    public Guid? ModelId { get; set; }

    [Required]
    [MaxLength(255)]
    public string ConstraintName { get; set; } = string.Empty;

    /// <summary>
    /// Type of safety constraint
    /// </summary>
    [MaxLength(100)]
    public string ConstraintType { get; set; } = string.Empty; // input_filter, output_filter, behavioral_limit, capability_restriction

    /// <summary>
    /// The constraint implementation details
    /// </summary>
    public string ConstraintDefinition { get; set; } = "{}";

    /// <summary>
    /// Trigger conditions for this constraint
    /// </summary>
    public string TriggerConditions { get; set; } = "{}";

    /// <summary>
    /// Actions taken when constraint is triggered
    /// </summary>
    public string ConstraintActions { get; set; } = "[]";

    /// <summary>
    /// Severity level of constraint violations
    /// </summary>
    public int SeverityLevel { get; set; } = 1; // 1=low, 5=critical

    /// <summary>
    /// Whether this constraint is currently enforced
    /// </summary>
    public bool IsEnforced { get; set; } = true;

    /// <summary>
    /// Performance impact of this constraint
    /// </summary>
    public double PerformanceImpact { get; set; } = 0.0;

    /// <summary>
    /// Usage and violation statistics
    /// </summary>
    public string EnforcementStatistics { get; set; } = "{}";

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual ConstitutionalRule ConstitutionalRule { get; set; } = null!;
    public virtual DistilledAgent? Agent { get; set; }
    public virtual Model? Model { get; set; }

    /// <summary>
    /// Get constraint definition as typed object
    /// </summary>
    public T? GetConstraintDefinition<T>() where T : class
    {
        if (string.IsNullOrEmpty(ConstraintDefinition) || ConstraintDefinition == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ConstraintDefinition);
    }

    /// <summary>
    /// Set constraint definition from typed object
    /// </summary>
    public void SetConstraintDefinition<T>(T definition) where T : class
    {
        ConstraintDefinition = JsonSerializer.Serialize(definition);
    }

    /// <summary>
    /// Get trigger conditions as typed object
    /// </summary>
    public T? GetTriggerConditions<T>() where T : class
    {
        if (string.IsNullOrEmpty(TriggerConditions) || TriggerConditions == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(TriggerConditions);
    }

    /// <summary>
    /// Set trigger conditions from typed object
    /// </summary>
    public void SetTriggerConditions<T>(T conditions) where T : class
    {
        TriggerConditions = JsonSerializer.Serialize(conditions);
    }

    /// <summary>
    /// Get constraint actions as typed list
    /// </summary>
    public List<string> GetConstraintActions()
    {
        if (string.IsNullOrEmpty(ConstraintActions) || ConstraintActions == "[]")
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(ConstraintActions) ?? new List<string>();
    }

    /// <summary>
    /// Set constraint actions from list
    /// </summary>
    public void SetConstraintActions(List<string> actions)
    {
        ConstraintActions = JsonSerializer.Serialize(actions);
    }

    /// <summary>
    /// Get enforcement statistics as typed object
    /// </summary>
    public T? GetEnforcementStatistics<T>() where T : class
    {
        if (string.IsNullOrEmpty(EnforcementStatistics) || EnforcementStatistics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(EnforcementStatistics);
    }

    /// <summary>
    /// Set enforcement statistics from typed object
    /// </summary>
    public void SetEnforcementStatistics<T>(T statistics) where T : class
    {
        EnforcementStatistics = JsonSerializer.Serialize(statistics);
    }
}