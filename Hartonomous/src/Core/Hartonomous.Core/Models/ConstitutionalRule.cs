/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the ConstitutionalRule entity for Constitutional AI governance,
 * ensuring agent behavior alignment with ethical, safety, and operational boundaries.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents constitutional AI rules for agent behavior governance
/// Ensures agents operate within defined ethical and safety boundaries
/// </summary>
public class ConstitutionalRule
{
    public Guid RuleId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string RuleName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category of constitutional rule
    /// </summary>
    [MaxLength(100)]
    public string RuleCategory { get; set; } = string.Empty; // safety, ethics, privacy, accuracy, helpfulness

    /// <summary>
    /// The actual rule definition in a structured format
    /// </summary>
    public string RuleDefinition { get; set; } = "{}";

    /// <summary>
    /// Priority level of this rule (higher numbers = higher priority)
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Whether this rule is mandatory or advisory
    /// </summary>
    public bool IsMandatory { get; set; } = true;

    /// <summary>
    /// Conditions under which this rule applies
    /// </summary>
    public string ApplicabilityConditions { get; set; } = "{}";

    /// <summary>
    /// Examples of rule violations
    /// </summary>
    public string ViolationExamples { get; set; } = "[]";

    /// <summary>
    /// Actions to take when rule is violated
    /// </summary>
    public string EnforcementActions { get; set; } = "[]";

    /// <summary>
    /// Whether this rule is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual ICollection<SafetyConstraint> SafetyConstraints { get; set; } = new List<SafetyConstraint>();

    /// <summary>
    /// Get rule definition as typed object
    /// </summary>
    public T? GetRuleDefinition<T>() where T : class
    {
        if (string.IsNullOrEmpty(RuleDefinition) || RuleDefinition == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(RuleDefinition);
    }

    /// <summary>
    /// Set rule definition from typed object
    /// </summary>
    public void SetRuleDefinition<T>(T definition) where T : class
    {
        RuleDefinition = JsonSerializer.Serialize(definition);
    }

    /// <summary>
    /// Get applicability conditions as typed object
    /// </summary>
    public T? GetApplicabilityConditions<T>() where T : class
    {
        if (string.IsNullOrEmpty(ApplicabilityConditions) || ApplicabilityConditions == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ApplicabilityConditions);
    }

    /// <summary>
    /// Set applicability conditions from typed object
    /// </summary>
    public void SetApplicabilityConditions<T>(T conditions) where T : class
    {
        ApplicabilityConditions = JsonSerializer.Serialize(conditions);
    }

    /// <summary>
    /// Get violation examples as typed list
    /// </summary>
    public List<T> GetViolationExamples<T>() where T : class
    {
        if (string.IsNullOrEmpty(ViolationExamples) || ViolationExamples == "[]")
            return new List<T>();

        return JsonSerializer.Deserialize<List<T>>(ViolationExamples) ?? new List<T>();
    }

    /// <summary>
    /// Set violation examples from typed list
    /// </summary>
    public void SetViolationExamples<T>(List<T> examples) where T : class
    {
        ViolationExamples = JsonSerializer.Serialize(examples);
    }

    /// <summary>
    /// Get enforcement actions as typed list
    /// </summary>
    public List<string> GetEnforcementActions()
    {
        if (string.IsNullOrEmpty(EnforcementActions) || EnforcementActions == "[]")
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(EnforcementActions) ?? new List<string>();
    }

    /// <summary>
    /// Set enforcement actions from list
    /// </summary>
    public void SetEnforcementActions(List<string> actions)
    {
        EnforcementActions = JsonSerializer.Serialize(actions);
    }
}