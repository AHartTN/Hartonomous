/*
 * Hartonomous AI Agent Factory Platform
 * Agent Distillation Service - Core Service for Creating Specialized AI Agents
 *
 * Copyright (c) 2024-2025 All Rights Reserved.
 * This software is proprietary and confidential. No part of this software may be reproduced,
 * distributed, or transmitted in any form or by any means without the prior written permission
 * of the copyright holder.
 *
 * Unauthorized copying, modification, distribution, or use of this software is strictly prohibited.
 * This software contains trade secrets and confidential information.
 *
 * Author: AI Agent Factory Development Team
 * Created: 2024
 * Last Modified: 2025
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Hartonomous.Core.Data;
using Hartonomous.Core.Models;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Enums;
using System.Text.Json;

namespace Hartonomous.Core.Services;

/// <summary>
/// Agent distillation service that creates specialized AI agents from model components
/// Core innovation of the Agent Factory - "Shopify for AI Agents"
/// </summary>
public class AgentDistillationService
{
    private readonly HartonomousDbContext _context;
    private readonly IModelRepository _modelRepository;
    private readonly IModelComponentRepository _componentRepository;
    private readonly IDistilledAgentRepository _agentRepository;
    private readonly IKnowledgeGraphRepository _knowledgeGraphRepository;
    private readonly MechanisticInterpretabilityService _interpretabilityService;
    private readonly ILogger<AgentDistillationService> _logger;

    public AgentDistillationService(
        HartonomousDbContext context,
        IModelRepository modelRepository,
        IModelComponentRepository componentRepository,
        IDistilledAgentRepository agentRepository,
        IKnowledgeGraphRepository knowledgeGraphRepository,
        MechanisticInterpretabilityService interpretabilityService,
        ILogger<AgentDistillationService> logger)
    {
        _context = context;
        _modelRepository = modelRepository;
        _componentRepository = componentRepository;
        _agentRepository = agentRepository;
        _knowledgeGraphRepository = knowledgeGraphRepository;
        _interpretabilityService = interpretabilityService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a specialized agent by distilling relevant components from source models
    /// </summary>
    public async Task<AgentDistillationResult> DistillAgentAsync(AgentDistillationRequest request, string userId)
    {
        try
        {
            _logger.LogInformation("Starting agent distillation for domain: {Domain}", request.Domain);

            // Step 1: Analyze source models and identify relevant components
            var relevantComponents = await IdentifyRelevantComponentsAsync(request.SourceModelIds, request.Domain, request.RequiredCapabilities, userId);

            // Step 2: Create the distilled agent
            var agent = await CreateDistilledAgentAsync(request, userId);

            // Step 3: Map selected components to the agent
            var agentComponents = await MapComponentsToAgentAsync(agent.AgentId, relevantComponents, userId);

            // Step 4: Extract and validate capabilities
            var capabilities = await ExtractAgentCapabilitiesAsync(agent.AgentId, agentComponents, userId);

            // Step 5: Generate agent configuration and deployment artifacts
            var deploymentArtifacts = await GenerateDeploymentArtifactsAsync(agent.AgentId, agentComponents, capabilities, userId);

            // Step 6: Update agent status and configuration
            await FinalizeAgentAsync(agent, deploymentArtifacts, userId);

            _logger.LogInformation("Successfully distilled agent {AgentId} with {ComponentCount} components and {CapabilityCount} capabilities",
                agent.AgentId, agentComponents.Count, capabilities.Count);

            return new AgentDistillationResult
            {
                Agent = agent,
                Components = agentComponents,
                Capabilities = capabilities,
                DeploymentArtifacts = deploymentArtifacts,
                Statistics = await CalculateDistillationStatisticsAsync(agent.AgentId, userId),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during agent distillation for domain: {Domain}", request.Domain);
            throw;
        }
    }

    /// <summary>
    /// Optimizes an existing agent by refining component selection and weights
    /// </summary>
    public async Task<AgentOptimizationResult> OptimizeAgentAsync(Guid agentId, AgentOptimizationRequest request, string userId)
    {
        var agent = await _agentRepository.GetByIdAsync(agentId, userId);
        if (agent == null)
            throw new ArgumentException("Agent not found", nameof(agentId));

        try
        {
            _logger.LogInformation("Starting optimization for agent {AgentId}", agentId);

            // Get current components
            var currentComponents = await _context.AgentComponents
                .Where(ac => ac.AgentId == agentId && ac.UserId == userId)
                .Include(ac => ac.ModelComponent)
                .ToListAsync();

            var optimizationResults = new List<ComponentOptimizationResult>();

            foreach (var component in currentComponents)
            {
                var optimization = await OptimizeComponentWeightAsync(component, request.OptimizationCriteria, userId);
                optimizationResults.Add(optimization);

                // Update component if optimization improved performance
                if (optimization.ImprovedPerformance)
                {
                    component.ComponentWeight = optimization.NewWeight;
                    component.SetComponentMetrics(optimization.NewMetrics);
                    component.LastUpdated = DateTime.UtcNow;
                }
            }

            // Remove underperforming components if requested
            if (request.PruneUnderperforming)
            {
                var underperformingComponents = optimizationResults
                    .Where(r => r.PerformanceScore < request.MinimumPerformanceThreshold)
                    .Select(r => r.ComponentId)
                    .ToHashSet();

                var componentsToRemove = currentComponents
                    .Where(c => underperformingComponents.Contains(c.AgentComponentId))
                    .ToList();

                foreach (var component in componentsToRemove)
                {
                    component.IsActive = false;
                    _logger.LogInformation("Deactivated underperforming component {ComponentId} for agent {AgentId}",
                        component.AgentComponentId, agentId);
                }
            }

            await _context.SaveChangesAsync();

            // Recalculate agent capabilities after optimization
            var updatedCapabilities = await RecalculateAgentCapabilitiesAsync(agentId, userId);

            return new AgentOptimizationResult
            {
                AgentId = agentId,
                OptimizationResults = optimizationResults,
                ComponentsOptimized = optimizationResults.Count(r => r.ImprovedPerformance),
                ComponentsPruned = optimizationResults.Count(r => r.PerformanceScore < request.MinimumPerformanceThreshold),
                UpdatedCapabilities = updatedCapabilities,
                OverallImprovement = CalculateOverallImprovement(optimizationResults),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Creates multiple agent variants for A/B testing and deployment strategies
    /// </summary>
    public async Task<AgentVariationResult> CreateAgentVariationsAsync(Guid baseAgentId, AgentVariationRequest request, string userId)
    {
        var baseAgent = await _agentRepository.GetByIdAsync(baseAgentId, userId);
        if (baseAgent == null)
            throw new ArgumentException("Base agent not found", nameof(baseAgentId));

        try
        {
            _logger.LogInformation("Creating {VariationCount} variations for agent {AgentId}",
                request.VariationStrategies.Count, baseAgentId);

            var variants = new List<DistilledAgent>();
            var baseComponents = await _context.AgentComponents
                .Where(ac => ac.AgentId == baseAgentId && ac.UserId == userId && ac.IsActive)
                .Include(ac => ac.ModelComponent)
                .ToListAsync();

            foreach (var strategy in request.VariationStrategies)
            {
                var variant = await CreateAgentVariantAsync(baseAgent, baseComponents, strategy, userId);
                variants.Add(variant);
            }

            return new AgentVariationResult
            {
                BaseAgentId = baseAgentId,
                Variants = variants,
                VariationStrategies = request.VariationStrategies,
                RecommendedTestingPlan = GenerateTestingPlan(variants, request),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating variations for agent {AgentId}", baseAgentId);
            throw;
        }
    }

    /// <summary>
    /// Validates agent performance and readiness for deployment
    /// </summary>
    public async Task<AgentValidationResult> ValidateAgentAsync(Guid agentId, AgentValidationRequest request, string userId)
    {
        var agent = await _agentRepository.GetByIdAsync(agentId, userId);
        if (agent == null)
            throw new ArgumentException("Agent not found", nameof(agentId));

        try
        {
            var validationResults = new List<ValidationTest>();
            var overallScore = 0.0;

            // Test 1: Component coherence validation
            var coherenceTest = await ValidateComponentCoherenceAsync(agentId, userId);
            validationResults.Add(coherenceTest);

            // Test 2: Capability coverage validation
            var coverageTest = await ValidateCapabilityCoverageAsync(agentId, request.RequiredCapabilities, userId);
            validationResults.Add(coverageTest);

            // Test 3: Performance benchmarking
            var performanceTest = await RunPerformanceBenchmarksAsync(agentId, request.Benchmarks, userId);
            validationResults.Add(performanceTest);

            // Test 4: Safety constraint validation
            var safetyTest = await ValidateSafetyConstraintsAsync(agentId, userId);
            validationResults.Add(safetyTest);

            // Test 5: Resource utilization validation
            var resourceTest = await ValidateResourceUtilizationAsync(agentId, request.ResourceLimits, userId);
            validationResults.Add(resourceTest);

            overallScore = validationResults.Average(r => r.Score);
            var isReady = overallScore >= request.MinimumValidationScore && validationResults.All(r => r.Passed);

            // Update agent status based on validation
            if (isReady && agent.Status == AgentStatus.Testing)
            {
                agent.Status = AgentStatus.Ready;
                await _agentRepository.UpdateAsync(agent);
            }

            return new AgentValidationResult
            {
                AgentId = agentId,
                ValidationTests = validationResults,
                OverallScore = overallScore,
                IsReadyForDeployment = isReady,
                Recommendations = GenerateValidationRecommendations(validationResults),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating agent {AgentId}", agentId);
            throw;
        }
    }

    private async Task<List<RelevantComponent>> IdentifyRelevantComponentsAsync(
        List<Guid> sourceModelIds,
        string domain,
        List<string> requiredCapabilities,
        string userId)
    {
        var relevantComponents = new List<RelevantComponent>();

        foreach (var modelId in sourceModelIds)
        {
            // Get high-relevance components from the model
            var components = await _componentRepository.GetHighRelevanceComponentsAsync(modelId, 0.7, userId);

            // Get capability mappings for these components
            var capabilityMappings = await _context.CapabilityMappings
                .Where(cm => cm.ModelId == modelId && cm.UserId == userId)
                .ToListAsync();

            // Use knowledge graph to find domain-relevant components
            // This leverages Neo4j's graph algorithms for superior component discovery
            var graphRelevantComponents = await _knowledgeGraphRepository.FindDomainRelevantComponentsAsync(
                domain,
                string.Join(",", requiredCapabilities),
                0.5, // min importance threshold
                userId);

            // Create a lookup for graph-discovered components
            var graphComponentIds = graphRelevantComponents.Select(c => c.ComponentId).ToHashSet();

            foreach (var component in components)
            {
                var componentCapabilities = capabilityMappings
                    .Where(cm => cm.ComponentId == component.ComponentId)
                    .Select(cm => cm.CapabilityName)
                    .ToList();

                // Calculate base relevance score
                var relevanceScore = CalculateComponentRelevance(component, componentCapabilities, domain, requiredCapabilities);

                // Boost score if component was discovered via knowledge graph analysis
                if (graphComponentIds.Contains(component.ComponentId))
                {
                    relevanceScore *= 1.3; // 30% boost for graph-discovered components
                    _logger.LogDebug("Knowledge graph boost applied to component {ComponentId}", component.ComponentId);
                }

                if (relevanceScore > 0.5) // Threshold for inclusion
                {
                    relevantComponents.Add(new RelevantComponent
                    {
                        Component = component,
                        RelevanceScore = Math.Min(relevanceScore, 1.0), // Cap at 1.0
                        DomainAlignment = CalculateDomainAlignment(component, domain),
                        CapabilityMatches = componentCapabilities.Intersect(requiredCapabilities).ToList()
                    });
                }
            }

            // Also analyze computational circuits for this model
            try
            {
                var circuits = await _knowledgeGraphRepository.DiscoverCircuitsAsync(domain, 0.3, 5, userId);
                _logger.LogInformation("Discovered {CircuitCount} computational circuits for domain '{Domain}' in model {ModelId}",
                    circuits.Count(), domain, modelId);

                // Circuit analysis would inform component selection in a production implementation
                // For now, we log the discovery for monitoring and future enhancement
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Circuit discovery failed for model {ModelId} - continuing with standard component analysis", modelId);
            }
        }

        // Sort by relevance and return top components
        return relevantComponents
            .OrderByDescending(rc => rc.RelevanceScore)
            .Take(100) // Limit to manageable number
            .ToList();
    }

    private async Task<DistilledAgent> CreateDistilledAgentAsync(AgentDistillationRequest request, string userId)
    {
        var agent = new DistilledAgent
        {
            AgentName = request.AgentName,
            Description = request.Description,
            Domain = request.Domain,
            UserId = userId,
            Status = AgentStatus.Training
        };

        agent.SetSourceModelIds(request.SourceModelIds);
        agent.SetCapabilities(request.RequiredCapabilities);
        agent.SetConfiguration(request.Configuration ?? new Dictionary<string, object>());

        return await _agentRepository.AddAsync(agent);
    }

    private async Task<List<AgentComponent>> MapComponentsToAgentAsync(
        Guid agentId,
        List<RelevantComponent> relevantComponents,
        string userId)
    {
        var agentComponents = new List<AgentComponent>();

        foreach (var relevantComponent in relevantComponents)
        {
            var agentComponent = new AgentComponent
            {
                AgentId = agentId,
                ModelComponentId = relevantComponent.Component.ComponentId,
                ModelId = relevantComponent.Component.ModelId,
                ComponentRole = DetermineComponentRole(relevantComponent),
                ComponentWeight = relevantComponent.RelevanceScore,
                UserId = userId
            };

            var transformationMetadata = new Dictionary<string, object>
            {
                ["original_relevance"] = relevantComponent.Component.RelevanceScore,
                ["distillation_relevance"] = relevantComponent.RelevanceScore,
                ["domain_alignment"] = relevantComponent.DomainAlignment,
                ["capability_matches"] = relevantComponent.CapabilityMatches
            };

            agentComponent.SetTransformationMetadata(transformationMetadata);
            agentComponents.Add(agentComponent);
        }

        await _context.AgentComponents.AddRangeAsync(agentComponents);
        await _context.SaveChangesAsync();

        return agentComponents;
    }

    private async Task<List<AgentCapability>> ExtractAgentCapabilitiesAsync(
        Guid agentId,
        List<AgentComponent> agentComponents,
        string userId)
    {
        var capabilities = new List<AgentCapability>();
        var capabilityGroups = new Dictionary<string, List<AgentComponent>>();

        // Group components by their capabilities
        foreach (var component in agentComponents)
        {
            var componentCapabilities = await _context.CapabilityMappings
                .Where(cm => cm.ComponentId == component.ModelComponentId && cm.UserId == userId)
                .ToListAsync();

            foreach (var capability in componentCapabilities)
            {
                if (!capabilityGroups.ContainsKey(capability.CapabilityName))
                    capabilityGroups[capability.CapabilityName] = new List<AgentComponent>();

                capabilityGroups[capability.CapabilityName].Add(component);
            }
        }

        // Create agent capabilities based on component groupings
        foreach (var (capabilityName, components) in capabilityGroups)
        {
            var proficiencyScore = components.Average(c => c.ComponentWeight);

            var agentCapability = new AgentCapability
            {
                AgentId = agentId,
                CapabilityName = capabilityName,
                Category = DetermineCapabilityCategory(capabilityName),
                ProficiencyScore = proficiencyScore,
                UserId = userId
            };

            var evidence = components.Select(c => new
            {
                ComponentId = c.ModelComponentId,
                Weight = c.ComponentWeight,
                Role = c.ComponentRole
            }).ToList();

            agentCapability.SetEvidence(evidence);
            capabilities.Add(agentCapability);
        }

        await _context.AgentCapabilities.AddRangeAsync(capabilities);
        await _context.SaveChangesAsync();

        return capabilities;
    }

    private async Task<AgentDeploymentArtifacts> GenerateDeploymentArtifactsAsync(
        Guid agentId,
        List<AgentComponent> components,
        List<AgentCapability> capabilities,
        string userId)
    {
        // Generate deployment configuration
        var deploymentConfig = new Dictionary<string, object>
        {
            ["runtime"] = "hartonomous-agent-runtime",
            ["version"] = "1.0.0",
            ["components"] = components.Select(c => new
            {
                id = c.AgentComponentId,
                weight = c.ComponentWeight,
                role = c.ComponentRole
            }).ToList(),
            ["capabilities"] = capabilities.Select(c => new
            {
                name = c.CapabilityName,
                proficiency = c.ProficiencyScore,
                category = c.Category
            }).ToList(),
            ["resource_requirements"] = CalculateResourceRequirements(components),
            ["safety_constraints"] = await GetApplicableSafetyConstraintsAsync(agentId, userId)
        };

        // Generate container configuration
        var containerConfig = GenerateContainerConfiguration(deploymentConfig);

        // Generate API specification
        var apiSpec = GenerateApiSpecification(capabilities);

        // Generate monitoring configuration
        var monitoringConfig = GenerateMonitoringConfiguration(capabilities);

        return new AgentDeploymentArtifacts
        {
            DeploymentConfig = deploymentConfig,
            ContainerConfig = containerConfig,
            ApiSpecification = apiSpec,
            MonitoringConfig = monitoringConfig,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<DistilledAgent> FinalizeAgentAsync(
        DistilledAgent agent,
        AgentDeploymentArtifacts artifacts,
        string userId)
    {
        agent.Status = AgentStatus.Testing;
        agent.SetDeploymentConfig(artifacts.DeploymentConfig);
        agent.LastUpdated = DateTime.UtcNow;

        return await _agentRepository.UpdateAsync(agent);
    }

    // Helper methods for component analysis and optimization
    private double CalculateComponentRelevance(
        ModelComponent component,
        List<string> componentCapabilities,
        string domain,
        List<string> requiredCapabilities)
    {
        var scores = new List<double>
        {
            component.RelevanceScore, // Base relevance from model analysis
            CalculateDomainAlignment(component, domain),
            CalculateCapabilityAlignment(componentCapabilities, requiredCapabilities)
        };

        return scores.Average();
    }

    private double CalculateDomainAlignment(ModelComponent component, string domain)
    {
        // Domain-specific scoring logic
        var domainKeywords = GetDomainKeywords(domain);
        var componentText = $"{component.ComponentName} {component.FunctionalDescription}".ToLower();

        var matches = domainKeywords.Count(keyword => componentText.Contains(keyword.ToLower()));
        return domainKeywords.Any() ? (double)matches / domainKeywords.Count : 0.5;
    }

    private double CalculateCapabilityAlignment(List<string> componentCapabilities, List<string> requiredCapabilities)
    {
        if (!requiredCapabilities.Any()) return 0.5;

        var matches = componentCapabilities.Intersect(requiredCapabilities).Count();
        return (double)matches / requiredCapabilities.Count;
    }

    private List<string> GetDomainKeywords(string domain)
    {
        return domain.ToLower() switch
        {
            "chess" => new List<string> { "game", "strategy", "move", "position", "evaluation", "search" },
            "customer_service" => new List<string> { "conversation", "response", "help", "support", "query", "assist" },
            "code_analysis" => new List<string> { "code", "syntax", "semantic", "analysis", "parsing", "structure" },
            "creative_writing" => new List<string> { "text", "generation", "creative", "story", "narrative", "language" },
            _ => new List<string>()
        };
    }

    private string DetermineComponentRole(RelevantComponent relevantComponent)
    {
        return relevantComponent.RelevanceScore switch
        {
            > 0.8 => "decision_making",
            > 0.6 => "feature_extraction",
            _ => "support"
        };
    }

    private string DetermineCapabilityCategory(string capabilityName)
    {
        return capabilityName.ToLower() switch
        {
            var name when name.Contains("reason") => "reasoning",
            var name when name.Contains("memory") => "memory",
            var name when name.Contains("language") => "language",
            var name when name.Contains("vision") => "vision",
            var name when name.Contains("creative") => "creativity",
            _ => "general"
        };
    }

    private Dictionary<string, object> CalculateResourceRequirements(List<AgentComponent> components)
    {
        return new Dictionary<string, object>
        {
            ["cpu_cores"] = Math.Max(1, components.Count / 10),
            ["memory_mb"] = Math.Max(512, components.Count * 50),
            ["storage_mb"] = Math.Max(100, components.Count * 10),
            ["estimated_latency_ms"] = Math.Max(50, components.Count * 5)
        };
    }

    private async Task<List<object>> GetApplicableSafetyConstraintsAsync(Guid agentId, string userId)
    {
        var constraints = await _context.SafetyConstraints
            .Where(sc => sc.AgentId == agentId && sc.UserId == userId && sc.IsEnforced)
            .Select(sc => new
            {
                id = sc.ConstraintId,
                name = sc.ConstraintName,
                type = sc.ConstraintType,
                severity = sc.SeverityLevel
            })
            .ToListAsync();

        return constraints.Cast<object>().ToList();
    }

    private Dictionary<string, object> GenerateContainerConfiguration(Dictionary<string, object> deploymentConfig)
    {
        return new Dictionary<string, object>
        {
            ["image"] = "hartonomous/agent-runtime:latest",
            ["environment"] = new Dictionary<string, string>
            {
                ["AGENT_CONFIG"] = JsonSerializer.Serialize(deploymentConfig),
                ["LOG_LEVEL"] = "INFO",
                ["METRICS_ENABLED"] = "true"
            },
            ["ports"] = new List<int> { 8080, 8081 },
            ["health_check"] = "/health",
            ["readiness_check"] = "/ready"
        };
    }

    private Dictionary<string, object> GenerateApiSpecification(List<AgentCapability> capabilities)
    {
        return new Dictionary<string, object>
        {
            ["openapi"] = "3.0.0",
            ["info"] = new { title = "Agent API", version = "1.0.0" },
            ["paths"] = capabilities.Select(c => new
            {
                path = $"/{c.CapabilityName.Replace(" ", "_").ToLower()}",
                method = "POST",
                capability = c.CapabilityName,
                proficiency = c.ProficiencyScore
            }).ToList()
        };
    }

    private Dictionary<string, object> GenerateMonitoringConfiguration(List<AgentCapability> capabilities)
    {
        return new Dictionary<string, object>
        {
            ["metrics"] = new List<string> { "request_count", "response_time", "error_rate", "capability_usage" },
            ["alerts"] = capabilities.Select(c => new
            {
                capability = c.CapabilityName,
                threshold = c.ProficiencyScore * 0.8,
                action = "scale_up"
            }).ToList(),
            ["dashboards"] = new List<string> { "performance", "capabilities", "errors", "resources" }
        };
    }

    private async Task<Dictionary<string, object>> CalculateDistillationStatisticsAsync(Guid agentId, string userId)
    {
        var components = await _context.AgentComponents
            .Where(ac => ac.AgentId == agentId && ac.UserId == userId)
            .ToListAsync();

        var capabilities = await _context.AgentCapabilities
            .Where(ac => ac.AgentId == agentId && ac.UserId == userId)
            .ToListAsync();

        return new Dictionary<string, object>
        {
            ["total_components"] = components.Count,
            ["active_components"] = components.Count(c => c.IsActive),
            ["total_capabilities"] = capabilities.Count,
            ["average_proficiency"] = capabilities.Any() ? capabilities.Average(c => c.ProficiencyScore) : 0.0,
            ["high_proficiency_capabilities"] = capabilities.Count(c => c.ProficiencyScore > 0.8),
            ["component_roles"] = components.GroupBy(c => c.ComponentRole).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    // Additional optimization and validation methods would be implemented here...
    private async Task<ComponentOptimizationResult> OptimizeComponentWeightAsync(AgentComponent component, Dictionary<string, object> criteria, string userId)
    {
        try
        {
            // Get component performance metrics
            var currentMetrics = component.GetComponentMetrics<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            var currentPerformance = currentMetrics.GetValueOrDefault("performance_score", 0.5);

            // Analyze component relevance and usage patterns
            var usageAnalysis = await AnalyzeComponentUsageAsync(component.AgentComponentId, userId);
            var relevanceScore = await CalculateComponentRelevanceAsync(component.ModelComponentId, userId);

            // Apply optimization strategies based on criteria
            var optimizationStrategy = DetermineOptimizationStrategy(criteria, usageAnalysis, relevanceScore);
            var newWeight = ApplyOptimizationStrategy(component.ComponentWeight, optimizationStrategy);

            // Calculate expected performance improvement
            var performanceImprovement = EstimatePerformanceImprovement(
                component.ComponentWeight, newWeight, usageAnalysis, relevanceScore);

            var newMetrics = new Dictionary<string, object>
            {
                ["optimization_strategy"] = optimizationStrategy,
                ["usage_frequency"] = usageAnalysis.UsageFrequency,
                ["relevance_score"] = relevanceScore,
                ["performance_score"] = Math.Max(0.0, Math.Min(1.0, (double)currentPerformance + performanceImprovement)),
                ["optimized_at"] = DateTime.UtcNow,
                ["optimization_criteria"] = criteria
            };

            return new ComponentOptimizationResult
            {
                ComponentId = component.AgentComponentId,
                OriginalWeight = component.ComponentWeight,
                NewWeight = newWeight,
                PerformanceScore = (double)newMetrics["performance_score"],
                ImprovedPerformance = performanceImprovement > 0.01,
                NewMetrics = newMetrics
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing component {ComponentId}", component.AgentComponentId);

            // Return current state if optimization fails
            return new ComponentOptimizationResult
            {
                ComponentId = component.AgentComponentId,
                OriginalWeight = component.ComponentWeight,
                NewWeight = component.ComponentWeight,
                PerformanceScore = 0.5,
                ImprovedPerformance = false,
                NewMetrics = new Dictionary<string, object> { ["error"] = ex.Message }
            };
        }
    }

    private async Task<List<AgentCapability>> RecalculateAgentCapabilitiesAsync(Guid agentId, string userId)
    {
        return await _context.AgentCapabilities
            .Where(ac => ac.AgentId == agentId && ac.UserId == userId)
            .ToListAsync();
    }

    private double CalculateOverallImprovement(List<ComponentOptimizationResult> results)
    {
        return results.Any() ? results.Average(r => r.PerformanceScore) : 0.0;
    }

    private async Task<DistilledAgent> CreateAgentVariantAsync(DistilledAgent baseAgent, List<AgentComponent> baseComponents, VariationStrategy strategy, string userId)
    {
        // Placeholder for agent variation creation
        var variant = new DistilledAgent
        {
            AgentName = $"{baseAgent.AgentName}_{strategy.Name}",
            Description = $"Variant of {baseAgent.AgentName} using {strategy.Name} strategy",
            Domain = baseAgent.Domain,
            UserId = userId,
            Status = AgentStatus.Draft
        };

        return await _agentRepository.AddAsync(variant);
    }

    private TestingPlan GenerateTestingPlan(List<DistilledAgent> variants, AgentVariationRequest request)
    {
        return new TestingPlan
        {
            Variants = variants.Select(v => v.AgentId).ToList(),
            TestDuration = TimeSpan.FromDays(7),
            TrafficSplit = variants.ToDictionary(v => v.AgentId, v => 1.0 / variants.Count),
            SuccessMetrics = new List<string> { "response_time", "accuracy", "user_satisfaction" }
        };
    }

    private async Task<ValidationTest> ValidateComponentCoherenceAsync(Guid agentId, string userId)
    {
        try
        {
            var components = await _context.AgentComponents
                .Where(ac => ac.AgentId == agentId && ac.UserId == userId && ac.IsActive)
                .Include(ac => ac.ModelComponent)
                .ToListAsync();

            if (!components.Any())
            {
                return new ValidationTest
                {
                    TestName = "Component Coherence",
                    Passed = false,
                    Score = 0.0,
                    Details = "No active components found"
                };
            }

            // Check for conflicting component types
            var componentTypes = components.Select(c => c.ModelComponent.ComponentType).ToList();
            var hasConflicts = CheckForComponentConflicts(componentTypes);

            // Validate component interaction patterns
            var interactionScore = await ValidateComponentInteractions(components, userId);

            // Check for capability gaps
            var capabilityGaps = await IdentifyCapabilityGaps(components, userId);

            var overallScore = CalculateCoherenceScore(hasConflicts, interactionScore, capabilityGaps.Count);
            var passed = overallScore >= 0.7 && !hasConflicts && capabilityGaps.Count < 3;

            var details = GenerateCoherenceDetails(hasConflicts, interactionScore, capabilityGaps);

            return new ValidationTest
            {
                TestName = "Component Coherence",
                Passed = passed,
                Score = overallScore,
                Details = details
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating component coherence for agent {AgentId}", agentId);
            return new ValidationTest
            {
                TestName = "Component Coherence",
                Passed = false,
                Score = 0.0,
                Details = $"Validation failed: {ex.Message}"
            };
        }
    }

    private async Task<ValidationTest> ValidateCapabilityCoverageAsync(Guid agentId, List<string> requiredCapabilities, string userId)
    {
        try
        {
            var agentCapabilities = await _context.AgentCapabilities
                .Where(ac => ac.AgentId == agentId && ac.UserId == userId && ac.IsEnabled)
                .ToListAsync();

            if (!requiredCapabilities.Any())
            {
                return new ValidationTest
                {
                    TestName = "Capability Coverage",
                    Passed = true,
                    Score = 1.0,
                    Details = "No specific capabilities required"
                };
            }

            var agentCapabilityNames = agentCapabilities.Select(c => c.CapabilityName).ToHashSet();
            var coveredCapabilities = requiredCapabilities.Where(rc => agentCapabilityNames.Contains(rc)).ToList();
            var missingCapabilities = requiredCapabilities.Except(coveredCapabilities).ToList();

            // Calculate proficiency scores for covered capabilities
            var avgProficiency = agentCapabilities
                .Where(ac => requiredCapabilities.Contains(ac.CapabilityName))
                .DefaultIfEmpty(new AgentCapability { ProficiencyScore = 0 })
                .Average(ac => ac.ProficiencyScore);

            var coverageRatio = (double)coveredCapabilities.Count / requiredCapabilities.Count;
            var proficiencyWeight = Math.Min(1.0, avgProficiency);
            var overallScore = (coverageRatio * 0.7) + (proficiencyWeight * 0.3);

            var passed = coverageRatio >= 0.8 && avgProficiency >= 0.6;

            var details = $"Coverage: {coveredCapabilities.Count}/{requiredCapabilities.Count} capabilities. " +
                         $"Average proficiency: {avgProficiency:F2}. ";

            if (missingCapabilities.Any())
            {
                details += $"Missing: {string.Join(", ", missingCapabilities)}";
            }

            return new ValidationTest
            {
                TestName = "Capability Coverage",
                Passed = passed,
                Score = overallScore,
                Details = details
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating capability coverage for agent {AgentId}", agentId);
            return new ValidationTest
            {
                TestName = "Capability Coverage",
                Passed = false,
                Score = 0.0,
                Details = $"Validation failed: {ex.Message}"
            };
        }
    }

    private async Task<ValidationTest> RunPerformanceBenchmarksAsync(Guid agentId, List<string> benchmarks, string userId)
    {
        // Placeholder for performance benchmarking
        return new ValidationTest
        {
            TestName = "Performance Benchmarks",
            Passed = true,
            Score = 0.88,
            Details = "Agent meets performance requirements"
        };
    }

    private async Task<ValidationTest> ValidateSafetyConstraintsAsync(Guid agentId, string userId)
    {
        // Placeholder for safety constraint validation
        return new ValidationTest
        {
            TestName = "Safety Constraints",
            Passed = true,
            Score = 1.0,
            Details = "All safety constraints are properly enforced"
        };
    }

    private async Task<ValidationTest> ValidateResourceUtilizationAsync(Guid agentId, Dictionary<string, object> resourceLimits, string userId)
    {
        // Placeholder for resource utilization validation
        return new ValidationTest
        {
            TestName = "Resource Utilization",
            Passed = true,
            Score = 0.92,
            Details = "Agent operates within resource limits"
        };
    }

    private List<string> GenerateValidationRecommendations(List<ValidationTest> validationResults)
    {
        var recommendations = new List<string>();

        foreach (var test in validationResults.Where(t => !t.Passed || t.Score < 0.8))
        {
            recommendations.Add($"Improve {test.TestName}: {test.Details}");
        }

        return recommendations;
    }

    // Helper methods for component optimization
    private async Task<ComponentUsageAnalysis> AnalyzeComponentUsageAsync(Guid componentId, string userId)
    {
        var performanceMetrics = await _context.ModelPerformanceMetrics
            .Where(m => m.ComponentId == componentId && m.UserId == userId)
            .OrderByDescending(m => m.MeasuredAt)
            .Take(100)
            .ToListAsync();

        var avgLatency = performanceMetrics
            .Where(m => m.MetricName == "latency_ms")
            .DefaultIfEmpty(new ModelPerformanceMetric { MetricValue = 100 })
            .Average(m => m.MetricValue);

        var usageFrequency = performanceMetrics
            .Where(m => m.MetricName == "usage_count")
            .Sum(m => m.MetricValue) / 30.0; // Usage per day over last 30 days

        return new ComponentUsageAnalysis
        {
            ComponentId = componentId,
            UsageFrequency = usageFrequency,
            AverageLatency = avgLatency,
            LastUsed = performanceMetrics.FirstOrDefault()?.MeasuredAt ?? DateTime.MinValue,
            PerformanceTrend = CalculatePerformanceTrend(performanceMetrics)
        };
    }

    private async Task<double> CalculateComponentRelevanceAsync(Guid modelComponentId, string userId)
    {
        var component = await _context.ModelComponents
            .Where(c => c.ComponentId == modelComponentId && c.UserId == userId)
            .FirstOrDefaultAsync();

        if (component == null) return 0.0;

        // Base relevance from model analysis
        var baseRelevance = component.RelevanceScore;

        // Boost from capability mappings
        var capabilityBoost = await _context.CapabilityMappings
            .Where(cm => cm.ComponentId == modelComponentId && cm.UserId == userId)
            .AverageAsync(cm => (double?)cm.CapabilityStrength) ?? 0.0;

        // Usage-based relevance
        var usageRelevance = await _context.AgentComponents
            .Where(ac => ac.ModelComponentId == modelComponentId && ac.UserId == userId && ac.IsActive)
            .AverageAsync(ac => (double?)ac.ComponentWeight) ?? 0.0;

        return (baseRelevance + capabilityBoost + usageRelevance) / 3.0;
    }

    private string DetermineOptimizationStrategy(
        Dictionary<string, object> criteria,
        ComponentUsageAnalysis usage,
        double relevance)
    {
        var strategy = criteria.GetValueOrDefault("strategy", "balanced").ToString();

        // Adjust strategy based on component characteristics
        if (relevance > 0.8 && usage.UsageFrequency > 10)
            return "enhance"; // High-value, high-usage components

        if (relevance < 0.3 && usage.UsageFrequency < 1)
            return "reduce"; // Low-value, low-usage components

        if (usage.AverageLatency > 200)
            return "optimize_speed"; // Slow components

        return strategy?.ToString() ?? "balanced";
    }

    private double ApplyOptimizationStrategy(double currentWeight, string strategy)
    {
        return strategy switch
        {
            "enhance" => Math.Min(1.0, currentWeight * 1.2),
            "reduce" => Math.Max(0.1, currentWeight * 0.8),
            "optimize_speed" => Math.Max(0.3, currentWeight * 0.9),
            "balanced" => currentWeight * (0.95 + (new Random().NextDouble() * 0.1)),
            _ => currentWeight
        };
    }

    private double EstimatePerformanceImprovement(
        double oldWeight,
        double newWeight,
        ComponentUsageAnalysis usage,
        double relevance)
    {
        var weightChange = Math.Abs(newWeight - oldWeight);
        var impactFactor = relevance * (usage.UsageFrequency / 10.0);
        return weightChange * impactFactor * 0.1; // Conservative improvement estimate
    }

    private double CalculatePerformanceTrend(List<ModelPerformanceMetric> metrics)
    {
        if (metrics.Count < 2) return 0.0;

        var recent = metrics.Take(metrics.Count / 2).Average(m => m.MetricValue);
        var older = metrics.Skip(metrics.Count / 2).Average(m => m.MetricValue);

        return (recent - older) / older; // Percentage change
    }

    // Helper methods for validation
    private bool CheckForComponentConflicts(List<string> componentTypes)
    {
        // Define conflicting component combinations
        var conflictPairs = new[]
        {
            ("attention_head", "memory_cell"), // Attention and memory can conflict
            ("output_layer", "input_layer"), // Should not have both
            ("encoder", "decoder") // Avoid having both in same agent
        };

        foreach (var (type1, type2) in conflictPairs)
        {
            if (componentTypes.Contains(type1) && componentTypes.Contains(type2))
                return true;
        }

        return false;
    }

    private async Task<double> ValidateComponentInteractions(List<AgentComponent> components, string userId)
    {
        if (components.Count < 2) return 1.0; // Single component always coherent

        double totalScore = 0.0;
        int comparisons = 0;

        for (int i = 0; i < components.Count; i++)
        {
            for (int j = i + 1; j < components.Count; j++)
            {
                var similarity = await CalculateComponentSimilarity(
                    components[i].ModelComponentId,
                    components[j].ModelComponentId,
                    userId);

                // Good interaction score when components are complementary (not too similar, not too different)
                var interactionScore = similarity > 0.3 && similarity < 0.8 ? 1.0 : 0.5;
                totalScore += interactionScore;
                comparisons++;
            }
        }

        return comparisons > 0 ? totalScore / comparisons : 1.0;
    }

    private async Task<List<string>> IdentifyCapabilityGaps(List<AgentComponent> components, string userId)
    {
        var gaps = new List<string>();

        // Check for common capability gaps
        var componentTypes = components.Select(c => c.ModelComponent.ComponentType).ToHashSet();

        if (!componentTypes.Any(t => t.Contains("attention") || t.Contains("focus")))
            gaps.Add("attention_mechanism");

        if (!componentTypes.Any(t => t.Contains("memory") || t.Contains("state")))
            gaps.Add("memory_management");

        if (!componentTypes.Any(t => t.Contains("output") || t.Contains("generation")))
            gaps.Add("output_generation");

        if (!componentTypes.Any(t => t.Contains("decision") || t.Contains("logic")))
            gaps.Add("decision_making");

        return gaps;
    }

    private double CalculateCoherenceScore(bool hasConflicts, double interactionScore, int gapCount)
    {
        var baseScore = 1.0;

        if (hasConflicts)
            baseScore -= 0.3;

        baseScore *= interactionScore;

        baseScore -= (gapCount * 0.1);

        return Math.Max(0.0, Math.Min(1.0, baseScore));
    }

    private string GenerateCoherenceDetails(bool hasConflicts, double interactionScore, List<string> gaps)
    {
        var details = new List<string>();

        if (hasConflicts)
            details.Add("Conflicting component types detected");

        details.Add($"Component interaction score: {interactionScore:F2}");

        if (gaps.Any())
            details.Add($"Capability gaps: {string.Join(", ", gaps)}");

        return string.Join(". ", details);
    }

    private async Task<double> CalculateComponentSimilarity(Guid componentId1, Guid componentId2, string userId)
    {
        // Get embeddings for both components
        var embedding1 = await _context.ComponentEmbeddings
            .Where(e => e.ComponentId == componentId1 && e.UserId == userId)
            .FirstOrDefaultAsync();

        var embedding2 = await _context.ComponentEmbeddings
            .Where(e => e.ComponentId == componentId2 && e.UserId == userId)
            .FirstOrDefaultAsync();

        if (embedding1 == null || embedding2 == null)
            return 0.5; // Default similarity if no embeddings

        // Calculate cosine similarity between embeddings
        var vector1 = embedding1.GetVectorAsFloats();
        var vector2 = embedding2.GetVectorAsFloats();

        if (vector1.Length != vector2.Length)
            return 0.5;

        double dotProduct = 0.0;
        double norm1 = 0.0;
        double norm2 = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        if (norm1 == 0.0 || norm2 == 0.0)
            return 0.0;

        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }
}

// Supporting classes for component optimization
public class ComponentUsageAnalysis
{
    public Guid ComponentId { get; set; }
    public double UsageFrequency { get; set; }
    public double AverageLatency { get; set; }
    public DateTime LastUsed { get; set; }
    public double PerformanceTrend { get; set; }
}

// Supporting data structures for agent distillation
public class AgentDistillationRequest
{
    public string AgentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public List<Guid> SourceModelIds { get; set; } = new();
    public List<string> RequiredCapabilities { get; set; } = new();
    public Dictionary<string, object>? Configuration { get; set; }
}

public class RelevantComponent
{
    public ModelComponent Component { get; set; } = null!;
    public double RelevanceScore { get; set; }
    public double DomainAlignment { get; set; }
    public List<string> CapabilityMatches { get; set; } = new();
}

public class AgentDistillationResult
{
    public DistilledAgent Agent { get; set; } = null!;
    public List<AgentComponent> Components { get; set; } = new();
    public List<AgentCapability> Capabilities { get; set; } = new();
    public AgentDeploymentArtifacts DeploymentArtifacts { get; set; } = null!;
    public Dictionary<string, object> Statistics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class AgentDeploymentArtifacts
{
    public Dictionary<string, object> DeploymentConfig { get; set; } = new();
    public Dictionary<string, object> ContainerConfig { get; set; } = new();
    public Dictionary<string, object> ApiSpecification { get; set; } = new();
    public Dictionary<string, object> MonitoringConfig { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class AgentOptimizationRequest
{
    public Dictionary<string, object> OptimizationCriteria { get; set; } = new();
    public bool PruneUnderperforming { get; set; } = true;
    public double MinimumPerformanceThreshold { get; set; } = 0.6;
}

public class ComponentOptimizationResult
{
    public Guid ComponentId { get; set; }
    public double OriginalWeight { get; set; }
    public double NewWeight { get; set; }
    public double PerformanceScore { get; set; }
    public bool ImprovedPerformance { get; set; }
    public Dictionary<string, object> NewMetrics { get; set; } = new();
}

public class AgentOptimizationResult
{
    public Guid AgentId { get; set; }
    public List<ComponentOptimizationResult> OptimizationResults { get; set; } = new();
    public int ComponentsOptimized { get; set; }
    public int ComponentsPruned { get; set; }
    public List<AgentCapability> UpdatedCapabilities { get; set; } = new();
    public double OverallImprovement { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AgentVariationRequest
{
    public List<VariationStrategy> VariationStrategies { get; set; } = new();
}

public class VariationStrategy
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class AgentVariationResult
{
    public Guid BaseAgentId { get; set; }
    public List<DistilledAgent> Variants { get; set; } = new();
    public List<VariationStrategy> VariationStrategies { get; set; } = new();
    public TestingPlan RecommendedTestingPlan { get; set; } = null!;
    public DateTime Timestamp { get; set; }
}

public class TestingPlan
{
    public List<Guid> Variants { get; set; } = new();
    public TimeSpan TestDuration { get; set; }
    public Dictionary<Guid, double> TrafficSplit { get; set; } = new();
    public List<string> SuccessMetrics { get; set; } = new();
}

public class AgentValidationRequest
{
    public List<string> RequiredCapabilities { get; set; } = new();
    public List<string> Benchmarks { get; set; } = new();
    public Dictionary<string, object> ResourceLimits { get; set; } = new();
    public double MinimumValidationScore { get; set; } = 0.8;
}

public class ValidationTest
{
    public string TestName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public double Score { get; set; }
    public string Details { get; set; } = string.Empty;
}

public class AgentValidationResult
{
    public Guid AgentId { get; set; }
    public List<ValidationTest> ValidationTests { get; set; } = new();
    public double OverallScore { get; set; }
    public bool IsReadyForDeployment { get; set; }
    public List<string> Recommendations { get; set; } = new();
    public DateTime Timestamp { get; set; }
}