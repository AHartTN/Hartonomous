/*
 * Hartonomous AI Agent Factory Platform
 * Constitutional AI Service - Ethics and Safety Framework for AI Agents
 *
 * Copyright (c) 2024-2025 All Rights Reserved.
 * This software is proprietary and confidential. No part of this software may be reproduced,
 * distributed, or transmitted in any form or by any means without the prior written permission
 * of the copyright holder.
 *
 * This service implements constitutional AI principles to ensure all AI agents operate within
 * defined ethical boundaries and safety constraints. It provides runtime validation, constraint
 * enforcement, and compliance monitoring for AI agent interactions.
 *
 * CRITICAL SAFETY NOTICE: This component is essential for AI safety. Any modifications to this
 * service must undergo rigorous safety review and testing before deployment.
 *
 * Author: AI Agent Factory Development Team
 * Created: 2024
 * Last Modified: 2025
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Hartonomous.Core.Data;
using Hartonomous.Core.Models;
using System.Text.Json;

namespace Hartonomous.Core.Services;

/// <summary>
/// Constitutional AI safety framework implementation that ensures agents operate within defined ethical and safety boundaries.
///
/// This service provides comprehensive safety governance for AI agents including:
/// - Rule definition and management for ethical AI behavior
/// - Runtime constraint application during agent execution
/// - Real-time interaction validation and monitoring
/// - Compliance reporting and audit trail generation
///
/// The service operates at the database level to ensure safety constraints cannot be bypassed
/// and integrates with the agent runtime to provide real-time safety enforcement.
/// </summary>
public class ConstitutionalAIService
{
    private readonly HartonomousDbContext _context;
    private readonly ILogger<ConstitutionalAIService> _logger;

    public ConstitutionalAIService(
        HartonomousDbContext context,
        ILogger<ConstitutionalAIService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Creates a constitutional rule for agent behavior governance
    /// </summary>
    public async Task<ConstitutionalRule> CreateConstitutionalRuleAsync(
        CreateConstitutionalRuleRequest request,
        string userId)
    {
        try
        {
            var rule = new ConstitutionalRule
            {
                RuleName = request.RuleName,
                Description = request.Description,
                RuleCategory = request.Category,
                Priority = request.Priority,
                IsMandatory = request.IsMandatory,
                IsActive = true,
                UserId = userId
            };

            rule.SetRuleDefinition(request.RuleDefinition);
            rule.SetApplicabilityConditions(request.ApplicabilityConditions);
            rule.SetViolationExamples(request.ViolationExamples);
            rule.SetEnforcementActions(request.EnforcementActions);

            _context.ConstitutionalRules.Add(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created constitutional rule {RuleId}: {RuleName}", rule.RuleId, rule.RuleName);
            return rule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating constitutional rule: {RuleName}", request.RuleName);
            throw;
        }
    }

    /// <summary>
    /// Applies constitutional constraints to an agent
    /// </summary>
    public async Task<AgentConstraintResult> ApplyConstraintsToAgentAsync(
        Guid agentId,
        List<Guid> ruleIds,
        string userId)
    {
        try
        {
            var agent = await _context.DistilledAgents
                .FirstOrDefaultAsync(a => a.AgentId == agentId && a.UserId == userId);

            if (agent == null)
                throw new ArgumentException("Agent not found", nameof(agentId));

            var rules = await _context.ConstitutionalRules
                .Where(r => ruleIds.Contains(r.RuleId) && r.UserId == userId && r.IsActive)
                .ToListAsync();

            var appliedConstraints = new List<SafetyConstraint>();

            foreach (var rule in rules)
            {
                var constraints = await GenerateConstraintsFromRuleAsync(rule, agentId, userId);
                appliedConstraints.AddRange(constraints);
            }

            await _context.SafetyConstraints.AddRangeAsync(appliedConstraints);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Applied {ConstraintCount} constraints to agent {AgentId}",
                appliedConstraints.Count, agentId);

            return new AgentConstraintResult
            {
                AgentId = agentId,
                AppliedConstraints = appliedConstraints,
                TotalConstraints = appliedConstraints.Count,
                AppliedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying constraints to agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Validates agent input/output against constitutional constraints
    /// </summary>
    public async Task<ConstitutionalValidationResult> ValidateAgentInteractionAsync(
        Guid agentId,
        AgentInteraction interaction,
        string userId)
    {
        try
        {
            var constraints = await _context.SafetyConstraints
                .Where(sc => sc.AgentId == agentId && sc.UserId == userId && sc.IsEnforced)
                .Include(sc => sc.ConstitutionalRule)
                .ToListAsync();

            var violations = new List<ConstraintViolation>();
            var warnings = new List<ConstraintWarning>();

            foreach (var constraint in constraints)
            {
                var validation = await ValidateAgainstConstraintAsync(constraint, interaction);

                if (validation.IsViolation)
                {
                    violations.Add(new ConstraintViolation
                    {
                        ConstraintId = constraint.ConstraintId,
                        ConstraintName = constraint.ConstraintName,
                        ViolationType = validation.ViolationType,
                        Severity = constraint.SeverityLevel,
                        Message = validation.Message,
                        Context = validation.Context
                    });

                    // Update constraint statistics
                    constraint.LastTriggeredAt = DateTime.UtcNow;
                    var stats = constraint.GetEnforcementStatistics<ConstraintStatistics>() ?? new ConstraintStatistics();
                    stats.ViolationCount++;
                    stats.LastViolation = DateTime.UtcNow;
                    constraint.SetEnforcementStatistics(stats);
                }
                else if (validation.IsWarning)
                {
                    warnings.Add(new ConstraintWarning
                    {
                        ConstraintId = constraint.ConstraintId,
                        ConstraintName = constraint.ConstraintName,
                        Message = validation.Message,
                        Confidence = validation.Confidence
                    });
                }
            }

            await _context.SaveChangesAsync();

            var isAllowed = !violations.Any(v => v.Severity >= 4); // Block on high severity violations
            var modifiedInteraction = await ApplyConstraintModificationsAsync(interaction, violations, constraints);

            return new ConstitutionalValidationResult
            {
                IsAllowed = isAllowed,
                Violations = violations,
                Warnings = warnings,
                ModifiedInteraction = modifiedInteraction,
                ValidationTimestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating agent interaction for agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Monitors agent behavior for constitutional compliance
    /// </summary>
    public async Task<ComplianceReport> GenerateComplianceReportAsync(
        Guid agentId,
        DateTime fromDate,
        DateTime toDate,
        string userId)
    {
        try
        {
            var constraints = await _context.SafetyConstraints
                .Where(sc => sc.AgentId == agentId && sc.UserId == userId)
                .Include(sc => sc.ConstitutionalRule)
                .ToListAsync();

            var violations = new List<ConstraintViolation>();
            var complianceScores = new Dictionary<string, double>();

            foreach (var constraint in constraints)
            {
                var stats = constraint.GetEnforcementStatistics<ConstraintStatistics>() ?? new ConstraintStatistics();

                // Calculate compliance score for this constraint
                var totalInteractions = stats.TotalEvaluations;
                var violationCount = stats.ViolationCount;
                var complianceScore = totalInteractions > 0 ? 1.0 - ((double)violationCount / totalInteractions) : 1.0;

                complianceScores[constraint.ConstraintName] = complianceScore;
            }

            var overallCompliance = complianceScores.Values.Any() ? complianceScores.Values.Average() : 1.0;

            return new ComplianceReport
            {
                AgentId = agentId,
                ReportPeriod = new DateRange { From = fromDate, To = toDate },
                OverallComplianceScore = overallCompliance,
                ConstraintComplianceScores = complianceScores,
                TotalViolations = violations.Count,
                HighSeverityViolations = violations.Count(v => v.Severity >= 4),
                RecommendedActions = GenerateComplianceRecommendations(complianceScores, violations),
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating compliance report for agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Updates constitutional rules based on observed violations and patterns
    /// </summary>
    public async Task<RuleUpdateResult> UpdateConstitutionalRulesAsync(
        List<Guid> ruleIds,
        RuleUpdateRequest updateRequest,
        string userId)
    {
        try
        {
            var rules = await _context.ConstitutionalRules
                .Where(r => ruleIds.Contains(r.RuleId) && r.UserId == userId)
                .ToListAsync();

            var updatedRules = new List<ConstitutionalRule>();

            foreach (var rule in rules)
            {
                var originalDefinition = rule.GetRuleDefinition<object>();

                // Apply updates based on learned patterns
                if (updateRequest.UpdateType == "strengthen")
                {
                    rule.Priority = Math.Min(10, rule.Priority + 1);
                    rule.IsMandatory = true;
                }
                else if (updateRequest.UpdateType == "refine")
                {
                    // Update rule definition with refined criteria
                    var refinedDefinition = RefineRuleDefinition(originalDefinition, updateRequest.RefinementData);
                    rule.SetRuleDefinition(refinedDefinition);
                }

                rule.LastUpdated = DateTime.UtcNow;
                updatedRules.Add(rule);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated {RuleCount} constitutional rules", updatedRules.Count);

            return new RuleUpdateResult
            {
                UpdatedRules = updatedRules,
                UpdateType = updateRequest.UpdateType,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating constitutional rules");
            throw;
        }
    }

    // Private helper methods
    private async Task<List<SafetyConstraint>> GenerateConstraintsFromRuleAsync(
        ConstitutionalRule rule,
        Guid agentId,
        string userId)
    {
        var constraints = new List<SafetyConstraint>();
        var ruleDefinition = rule.GetRuleDefinition<RuleDefinition>();

        if (ruleDefinition?.Constraints != null)
        {
            foreach (var constraintDef in ruleDefinition.Constraints)
            {
                var constraint = new SafetyConstraint
                {
                    ConstitutionalRuleId = rule.RuleId,
                    AgentId = agentId,
                    ConstraintName = constraintDef.Name,
                    ConstraintType = constraintDef.Type,
                    SeverityLevel = constraintDef.Severity,
                    IsEnforced = true,
                    UserId = userId
                };

                constraint.SetConstraintDefinition(constraintDef);
                constraint.SetTriggerConditions(constraintDef.TriggerConditions);
                constraint.SetConstraintActions(constraintDef.Actions);

                constraints.Add(constraint);
            }
        }

        return constraints;
    }

    private async Task<ConstraintValidationResult> ValidateAgainstConstraintAsync(
        SafetyConstraint constraint,
        AgentInteraction interaction)
    {
        var constraintDef = constraint.GetConstraintDefinition<ConstraintDefinition>();
        if (constraintDef == null)
        {
            return new ConstraintValidationResult { IsViolation = false };
        }

        // Apply constraint validation logic based on type
        return constraint.ConstraintType switch
        {
            "input_filter" => await ValidateInputFilterAsync(constraintDef, interaction.Input),
            "output_filter" => await ValidateOutputFilterAsync(constraintDef, interaction.Output),
            "behavioral_limit" => await ValidateBehavioralLimitAsync(constraintDef, interaction),
            "capability_restriction" => await ValidateCapabilityRestrictionAsync(constraintDef, interaction),
            _ => new ConstraintValidationResult { IsViolation = false }
        };
    }

    private async Task<ConstraintValidationResult> ValidateInputFilterAsync(
        ConstraintDefinition constraintDef,
        string input)
    {
        // Check for forbidden content patterns
        var forbiddenPatterns = constraintDef.ForbiddenPatterns ?? new List<string>();

        foreach (var pattern in forbiddenPatterns)
        {
            if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new ConstraintValidationResult
                {
                    IsViolation = true,
                    ViolationType = "forbidden_content",
                    Message = $"Input contains forbidden pattern: {pattern}",
                    Context = new { input_length = input.Length, pattern = pattern }
                };
            }
        }

        return new ConstraintValidationResult { IsViolation = false };
    }

    private async Task<ConstraintValidationResult> ValidateOutputFilterAsync(
        ConstraintDefinition constraintDef,
        string output)
    {
        // Similar validation for output content
        var forbiddenPatterns = constraintDef.ForbiddenPatterns ?? new List<string>();

        foreach (var pattern in forbiddenPatterns)
        {
            if (output.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new ConstraintValidationResult
                {
                    IsViolation = true,
                    ViolationType = "forbidden_output",
                    Message = $"Output contains forbidden pattern: {pattern}",
                    Context = new { output_length = output.Length, pattern = pattern }
                };
            }
        }

        return new ConstraintValidationResult { IsViolation = false };
    }

    private async Task<ConstraintValidationResult> ValidateBehavioralLimitAsync(
        ConstraintDefinition constraintDef,
        AgentInteraction interaction)
    {
        // Validate behavioral constraints like response length, frequency, etc.
        if (constraintDef.MaxResponseLength.HasValue &&
            interaction.Output.Length > constraintDef.MaxResponseLength.Value)
        {
            return new ConstraintValidationResult
            {
                IsViolation = true,
                ViolationType = "response_too_long",
                Message = $"Response exceeds maximum length of {constraintDef.MaxResponseLength.Value}",
                Context = new { actual_length = interaction.Output.Length, max_length = constraintDef.MaxResponseLength.Value }
            };
        }

        return new ConstraintValidationResult { IsViolation = false };
    }

    private async Task<ConstraintValidationResult> ValidateCapabilityRestrictionAsync(
        ConstraintDefinition constraintDef,
        AgentInteraction interaction)
    {
        // Validate that agent is not exceeding its allowed capabilities
        var restrictedCapabilities = constraintDef.RestrictedCapabilities ?? new List<string>();

        if (restrictedCapabilities.Contains(interaction.RequestedCapability))
        {
            return new ConstraintValidationResult
            {
                IsViolation = true,
                ViolationType = "restricted_capability",
                Message = $"Access to capability '{interaction.RequestedCapability}' is restricted",
                Context = new { capability = interaction.RequestedCapability }
            };
        }

        return new ConstraintValidationResult { IsViolation = false };
    }

    private async Task<AgentInteraction> ApplyConstraintModificationsAsync(
        AgentInteraction interaction,
        List<ConstraintViolation> violations,
        List<SafetyConstraint> constraints)
    {
        var modifiedInteraction = interaction;

        // Apply modifications based on constraint violations
        foreach (var violation in violations)
        {
            var constraint = constraints.FirstOrDefault(c => c.ConstraintId == violation.ConstraintId);
            if (constraint != null)
            {
                var actions = constraint.GetConstraintActions();

                if (actions.Contains("filter_output"))
                {
                    modifiedInteraction.Output = FilterOutput(modifiedInteraction.Output, constraint);
                }
                else if (actions.Contains("add_warning"))
                {
                    modifiedInteraction.Output += "\n\n⚠️ This response has been reviewed for safety compliance.";
                }
            }
        }

        return modifiedInteraction;
    }

    private string FilterOutput(string output, SafetyConstraint constraint)
    {
        var constraintDef = constraint.GetConstraintDefinition<ConstraintDefinition>();
        var forbiddenPatterns = constraintDef?.ForbiddenPatterns ?? new List<string>();

        var filteredOutput = output;
        foreach (var pattern in forbiddenPatterns)
        {
            filteredOutput = filteredOutput.Replace(pattern, "[FILTERED]", StringComparison.OrdinalIgnoreCase);
        }

        return filteredOutput;
    }

    private object RefineRuleDefinition(object originalDefinition, Dictionary<string, object> refinementData)
    {
        // Apply machine learning insights to refine rule definitions
        // This would incorporate feedback from violation patterns
        return originalDefinition; // Simplified for now
    }

    private List<string> GenerateComplianceRecommendations(
        Dictionary<string, double> complianceScores,
        List<ConstraintViolation> violations)
    {
        var recommendations = new List<string>();

        foreach (var (constraintName, score) in complianceScores)
        {
            if (score < 0.8)
            {
                recommendations.Add($"Strengthen enforcement of {constraintName} constraint (current compliance: {score:P})");
            }
        }

        var highSeverityCount = violations.Count(v => v.Severity >= 4);
        if (highSeverityCount > 0)
        {
            recommendations.Add($"Address {highSeverityCount} high-severity violations immediately");
        }

        return recommendations;
    }
}

// Supporting data structures
public class CreateConstitutionalRuleRequest
{
    public string RuleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public bool IsMandatory { get; set; } = true;
    public RuleDefinition RuleDefinition { get; set; } = new();
    public Dictionary<string, object> ApplicabilityConditions { get; set; } = new();
    public List<string> ViolationExamples { get; set; } = new();
    public List<string> EnforcementActions { get; set; } = new();
}

public class RuleDefinition
{
    public List<ConstraintDefinition> Constraints { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class ConstraintDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Severity { get; set; } = 1;
    public List<string> ForbiddenPatterns { get; set; } = new();
    public List<string> RestrictedCapabilities { get; set; } = new();
    public int? MaxResponseLength { get; set; }
    public Dictionary<string, object> TriggerConditions { get; set; } = new();
    public List<string> Actions { get; set; } = new();
}

public class AgentInteraction
{
    public string Input { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string RequestedCapability { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Context { get; set; } = new();
}

public class ConstraintValidationResult
{
    public bool IsViolation { get; set; }
    public bool IsWarning { get; set; }
    public string ViolationType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public object? Context { get; set; }
}

public class ConstraintViolation
{
    public Guid ConstraintId { get; set; }
    public string ConstraintName { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty;
    public int Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Context { get; set; }
}

public class ConstraintWarning
{
    public Guid ConstraintId { get; set; }
    public string ConstraintName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public class ConstraintStatistics
{
    public int ViolationCount { get; set; }
    public int TotalEvaluations { get; set; }
    public DateTime? LastViolation { get; set; }
    public Dictionary<string, int> ViolationsByType { get; set; } = new();
}

public class AgentConstraintResult
{
    public Guid AgentId { get; set; }
    public List<SafetyConstraint> AppliedConstraints { get; set; } = new();
    public int TotalConstraints { get; set; }
    public DateTime AppliedAt { get; set; }
}

public class ConstitutionalValidationResult
{
    public bool IsAllowed { get; set; }
    public List<ConstraintViolation> Violations { get; set; } = new();
    public List<ConstraintWarning> Warnings { get; set; } = new();
    public AgentInteraction? ModifiedInteraction { get; set; }
    public DateTime ValidationTimestamp { get; set; }
}

public class ComplianceReport
{
    public Guid AgentId { get; set; }
    public DateRange ReportPeriod { get; set; } = new();
    public double OverallComplianceScore { get; set; }
    public Dictionary<string, double> ConstraintComplianceScores { get; set; } = new();
    public int TotalViolations { get; set; }
    public int HighSeverityViolations { get; set; }
    public List<string> RecommendedActions { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class DateRange
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class RuleUpdateRequest
{
    public string UpdateType { get; set; } = string.Empty; // "strengthen", "refine", "deprecate"
    public Dictionary<string, object> RefinementData { get; set; } = new();
}

public class RuleUpdateResult
{
    public List<ConstitutionalRule> UpdatedRules { get; set; } = new();
    public string UpdateType { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}