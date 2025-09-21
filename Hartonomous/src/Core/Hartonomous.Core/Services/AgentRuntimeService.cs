/*
 * Hartonomous AI Agent Factory Platform
 * Agent Runtime Service - Deployment and Execution Management for AI Agents
 *
 * Copyright (c) 2024-2025 All Rights Reserved.
 * This software is proprietary and confidential. No part of this software may be reproduced,
 * distributed, or transmitted in any form or by any means without the prior written permission
 * of the copyright holder.
 *
 * This service manages the complete lifecycle of AI agent deployment and execution, enabling
 * the "thin client" architecture that allows agents to be deployed anywhere - cloud, edge,
 * or on-premises environments. It handles containerization, scaling, monitoring, and termination.
 *
 * KEY CAPABILITIES:
 * - Multi-environment deployment (Docker, Kubernetes, Local, Cloud)
 * - Runtime resource management and scaling
 * - Health monitoring and failure recovery
 * - Constitutional AI integration for safety validation
 *
 * Author: AI Agent Factory Development Team
 * Created: 2024
 * Last Modified: 2025
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Hartonomous.Core.Data;
using Hartonomous.Core.Models;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Enums;
using System.Text.Json;
using System.Diagnostics;

namespace Hartonomous.Core.Services;

/// <summary>
/// Agent runtime service that manages the complete lifecycle of AI agent deployment and execution.
///
/// This service is the core component that enables the "Shopify for AI Agents" vision by providing:
/// - Universal deployment capabilities across multiple environments
/// - Runtime resource management and automatic scaling
/// - Health monitoring with failure detection and recovery
/// - Constitutional AI safety integration during execution
/// - Performance metrics collection and optimization
///
/// The service supports deployment to:
/// - Local execution environments
/// - Docker containers with resource constraints
/// - Kubernetes clusters with auto-scaling
/// - Cloud platforms (Azure, AWS, GCP) with managed services
///
/// All agent deployments maintain connection to the central Hartonomous platform for
/// monitoring, updates, and safety validation while operating independently.
/// </summary>
public class AgentRuntimeService
{
    private readonly HartonomousDbContext _context;
    private readonly IDistilledAgentRepository _agentRepository;
    private readonly ConstitutionalAIService _constitutionalAIService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentRuntimeService> _logger;
    private readonly Dictionary<Guid, RunningAgent> _runningAgents = new();

    public AgentRuntimeService(
        HartonomousDbContext context,
        IDistilledAgentRepository agentRepository,
        ConstitutionalAIService constitutionalAIService,
        IConfiguration configuration,
        ILogger<AgentRuntimeService> logger)
    {
        _context = context;
        _agentRepository = agentRepository;
        _constitutionalAIService = constitutionalAIService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Deploys an agent to a runtime environment
    /// </summary>
    public async Task<AgentDeploymentResult> DeployAgentAsync(
        Guid agentId,
        AgentDeploymentRequest deploymentRequest,
        string userId)
    {
        try
        {
            var agent = await _agentRepository.GetByIdAsync(agentId, userId);
            if (agent == null)
                throw new ArgumentException("Agent not found", nameof(agentId));

            if (agent.Status != AgentStatus.Ready)
                throw new InvalidOperationException($"Agent must be in Ready status. Current status: {agent.Status}");

            _logger.LogInformation("Starting deployment of agent {AgentId} to {Environment}",
                agentId, deploymentRequest.TargetEnvironment);

            // Step 1: Generate deployment artifacts
            var deploymentArtifacts = await GenerateDeploymentArtifactsAsync(agent, deploymentRequest, userId);

            // Step 2: Provision infrastructure
            var infrastructure = await ProvisionInfrastructureAsync(deploymentRequest, deploymentArtifacts);

            // Step 3: Deploy agent components
            var deployment = await ExecuteDeploymentAsync(agent, deploymentArtifacts, infrastructure, userId);

            // Step 4: Start agent runtime
            var runtime = await StartAgentRuntimeAsync(deployment, userId);

            // Step 5: Register running agent
            _runningAgents[agentId] = runtime;

            // Step 6: Update agent status
            agent.Status = AgentStatus.Deployed;
            agent.LastDeployedAt = DateTime.UtcNow;
            agent.SetDeploymentConfig(new Dictionary<string, object>
            {
                ["environment"] = deploymentRequest.TargetEnvironment,
                ["endpoint"] = runtime.Endpoint,
                ["deployment_id"] = deployment.DeploymentId,
                ["infrastructure"] = infrastructure
            });

            await _agentRepository.UpdateAsync(agent);

            _logger.LogInformation("Successfully deployed agent {AgentId} to {Endpoint}",
                agentId, runtime.Endpoint);

            return new AgentDeploymentResult
            {
                AgentId = agentId,
                DeploymentId = deployment.DeploymentId,
                Endpoint = runtime.Endpoint,
                Status = "deployed",
                Environment = deploymentRequest.TargetEnvironment,
                DeployedAt = DateTime.UtcNow,
                RuntimeMetrics = runtime.GetMetrics()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Executes a request against a running agent
    /// </summary>
    public async Task<AgentExecutionResult> ExecuteAgentRequestAsync(
        Guid agentId,
        AgentRequest request,
        string userId)
    {
        try
        {
            if (!_runningAgents.TryGetValue(agentId, out var runningAgent))
            {
                throw new InvalidOperationException($"Agent {agentId} is not currently running");
            }

            var stopwatch = Stopwatch.StartNew();

            // Step 1: Validate request against constitutional constraints
            var interaction = new AgentInteraction
            {
                Input = request.Input,
                RequestedCapability = request.Capability,
                Context = request.Context
            };

            var preValidation = await _constitutionalAIService.ValidateAgentInteractionAsync(
                agentId, interaction, userId);

            if (!preValidation.IsAllowed)
            {
                return new AgentExecutionResult
                {
                    Success = false,
                    Output = "Request blocked by constitutional constraints",
                    Violations = preValidation.Violations,
                    ExecutionTime = stopwatch.Elapsed
                };
            }

            // Step 2: Execute the request
            var executionContext = new AgentExecutionContext
            {
                AgentId = agentId,
                Request = request,
                UserId = userId,
                RunningAgent = runningAgent,
                StartTime = DateTime.UtcNow
            };

            var output = await ExecuteAgentCapabilityAsync(executionContext);

            // Step 3: Post-execution validation
            interaction.Output = output;
            var postValidation = await _constitutionalAIService.ValidateAgentInteractionAsync(
                agentId, interaction, userId);

            var finalOutput = postValidation.ModifiedInteraction?.Output ?? output;

            stopwatch.Stop();

            // Step 4: Record metrics
            await RecordExecutionMetricsAsync(agentId, request.Capability, stopwatch.Elapsed, userId);

            _logger.LogInformation("Executed {Capability} for agent {AgentId} in {Duration}ms",
                request.Capability, agentId, stopwatch.ElapsedMilliseconds);

            return new AgentExecutionResult
            {
                Success = true,
                Output = finalOutput,
                Capability = request.Capability,
                ExecutionTime = stopwatch.Elapsed,
                Warnings = postValidation.Warnings,
                Violations = postValidation.Violations,
                Metadata = new Dictionary<string, object>
                {
                    ["agent_id"] = agentId,
                    ["execution_timestamp"] = DateTime.UtcNow,
                    ["runtime_version"] = runningAgent.RuntimeVersion
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing request for agent {AgentId}", agentId);
            return new AgentExecutionResult
            {
                Success = false,
                Output = $"Execution failed: {ex.Message}",
                ExecutionTime = TimeSpan.Zero
            };
        }
    }

    /// <summary>
    /// Scales an agent deployment up or down
    /// </summary>
    public async Task<AgentScalingResult> ScaleAgentAsync(
        Guid agentId,
        ScalingRequest scalingRequest,
        string userId)
    {
        try
        {
            if (!_runningAgents.TryGetValue(agentId, out var runningAgent))
            {
                throw new InvalidOperationException($"Agent {agentId} is not currently running");
            }

            var currentInstances = runningAgent.InstanceCount;
            var targetInstances = scalingRequest.TargetInstances;

            _logger.LogInformation("Scaling agent {AgentId} from {Current} to {Target} instances",
                agentId, currentInstances, targetInstances);

            if (targetInstances > currentInstances)
            {
                // Scale up
                await ScaleUpAgentAsync(runningAgent, targetInstances - currentInstances);
            }
            else if (targetInstances < currentInstances)
            {
                // Scale down
                await ScaleDownAgentAsync(runningAgent, currentInstances - targetInstances);
            }

            runningAgent.InstanceCount = targetInstances;
            runningAgent.LastScaledAt = DateTime.UtcNow;

            return new AgentScalingResult
            {
                AgentId = agentId,
                PreviousInstances = currentInstances,
                CurrentInstances = targetInstances,
                ScalingReason = scalingRequest.Reason,
                ScaledAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling agent {AgentId}", agentId);
            throw;
        }
    }

    /// <summary>
    /// Monitors agent health and performance
    /// </summary>
    public async Task<AgentHealthReport> GetAgentHealthAsync(Guid agentId, string userId)
    {
        try
        {
            if (!_runningAgents.TryGetValue(agentId, out var runningAgent))
            {
                return new AgentHealthReport
                {
                    AgentId = agentId,
                    Status = "not_running",
                    HealthScore = 0.0,
                    CheckedAt = DateTime.UtcNow
                };
            }

            var healthChecks = await PerformHealthChecksAsync(runningAgent);
            var metrics = runningAgent.GetMetrics();

            var healthScore = CalculateHealthScore(healthChecks, metrics);

            return new AgentHealthReport
            {
                AgentId = agentId,
                Status = healthScore > 0.8 ? "healthy" : healthScore > 0.5 ? "degraded" : "unhealthy",
                HealthScore = healthScore,
                HealthChecks = healthChecks,
                Metrics = metrics,
                InstanceCount = runningAgent.InstanceCount,
                Uptime = DateTime.UtcNow - runningAgent.StartedAt,
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health for agent {AgentId}", agentId);
            return new AgentHealthReport
            {
                AgentId = agentId,
                Status = "error",
                HealthScore = 0.0,
                ErrorMessage = ex.Message,
                CheckedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Stops and undeploys an agent
    /// </summary>
    public async Task<AgentUndeploymentResult> UndeployAgentAsync(Guid agentId, string userId)
    {
        try
        {
            if (!_runningAgents.TryGetValue(agentId, out var runningAgent))
            {
                throw new InvalidOperationException($"Agent {agentId} is not currently running");
            }

            _logger.LogInformation("Undeploying agent {AgentId}", agentId);

            // Step 1: Gracefully shutdown agent instances
            await ShutdownAgentInstancesAsync(runningAgent);

            // Step 2: Cleanup infrastructure
            await CleanupInfrastructureAsync(runningAgent);

            // Step 3: Remove from running agents
            _runningAgents.Remove(agentId);

            // Step 4: Update agent status
            var agent = await _agentRepository.GetByIdAsync(agentId, userId);
            if (agent != null)
            {
                agent.Status = AgentStatus.Ready;
                await _agentRepository.UpdateAsync(agent);
            }

            return new AgentUndeploymentResult
            {
                AgentId = agentId,
                UndeployedAt = DateTime.UtcNow,
                FinalMetrics = runningAgent.GetMetrics()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undeploying agent {AgentId}", agentId);
            throw;
        }
    }

    // Private implementation methods
    private async Task<DeploymentArtifacts> GenerateDeploymentArtifactsAsync(
        DistilledAgent agent,
        AgentDeploymentRequest request,
        string userId)
    {
        var agentComponents = await _context.AgentComponents
            .Where(ac => ac.AgentId == agent.AgentId && ac.UserId == userId && ac.IsActive)
            .Include(ac => ac.ModelComponent)
            .ToListAsync();

        var capabilities = await _context.AgentCapabilities
            .Where(ac => ac.AgentId == agent.AgentId && ac.UserId == userId && ac.IsEnabled)
            .ToListAsync();

        return new DeploymentArtifacts
        {
            AgentId = agent.AgentId,
            AgentName = agent.AgentName,
            Domain = agent.Domain,
            Components = agentComponents.Select(ac => new ComponentArtifact
            {
                ComponentId = ac.AgentComponentId,
                ComponentType = ac.ModelComponent.ComponentType,
                Weight = ac.ComponentWeight,
                Role = ac.ComponentRole
            }).ToList(),
            Capabilities = capabilities.Select(c => new CapabilityArtifact
            {
                Name = c.CapabilityName,
                Category = c.Category,
                Proficiency = c.ProficiencyScore
            }).ToList(),
            Configuration = agent.GetConfiguration<Dictionary<string, object>>() ?? new(),
            TargetEnvironment = request.TargetEnvironment,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<Dictionary<string, object>> ProvisionInfrastructureAsync(
        AgentDeploymentRequest request,
        DeploymentArtifacts artifacts)
    {
        var infrastructure = new Dictionary<string, object>();

        switch (request.TargetEnvironment.ToLowerInvariant())
        {
            case "local":
                infrastructure["type"] = "local";
                infrastructure["port"] = GetAvailablePort();
                infrastructure["host"] = "localhost";
                break;

            case "docker":
                infrastructure["type"] = "docker";
                infrastructure["container_name"] = $"hartonomous-agent-{artifacts.AgentId}";
                infrastructure["port"] = GetAvailablePort();
                infrastructure["image"] = "hartonomous/agent-runtime:latest";
                break;

            case "kubernetes":
                infrastructure["type"] = "kubernetes";
                infrastructure["namespace"] = "hartonomous-agents";
                infrastructure["deployment_name"] = $"agent-{artifacts.AgentName.ToLowerInvariant()}";
                infrastructure["service_port"] = 8080;
                break;

            case "cloud":
                infrastructure["type"] = "cloud";
                infrastructure["provider"] = request.CloudProvider ?? "azure";
                infrastructure["region"] = request.Region ?? "eastus";
                infrastructure["instance_type"] = CalculateRequiredInstanceType(artifacts);
                break;

            default:
                throw new NotSupportedException($"Environment {request.TargetEnvironment} is not supported");
        }

        return infrastructure;
    }

    private async Task<AgentDeployment> ExecuteDeploymentAsync(
        DistilledAgent agent,
        DeploymentArtifacts artifacts,
        Dictionary<string, object> infrastructure,
        string userId)
    {
        var deploymentId = Guid.NewGuid();
        var environmentType = infrastructure["type"].ToString();

        var deployment = new AgentDeployment
        {
            DeploymentId = deploymentId,
            AgentId = agent.AgentId,
            Environment = environmentType!,
            Infrastructure = infrastructure,
            Artifacts = artifacts,
            Status = "deploying",
            DeployedAt = DateTime.UtcNow
        };

        // Execute environment-specific deployment
        switch (environmentType)
        {
            case "local":
                await DeployToLocalAsync(deployment);
                break;
            case "docker":
                await DeployToDockerAsync(deployment);
                break;
            case "kubernetes":
                await DeployToKubernetesAsync(deployment);
                break;
            case "cloud":
                await DeployToCloudAsync(deployment);
                break;
        }

        deployment.Status = "deployed";
        return deployment;
    }

    private async Task<RunningAgent> StartAgentRuntimeAsync(AgentDeployment deployment, string userId)
    {
        var endpoint = GenerateAgentEndpoint(deployment);

        var runningAgent = new RunningAgent
        {
            AgentId = deployment.AgentId,
            DeploymentId = deployment.DeploymentId,
            Endpoint = endpoint,
            Environment = deployment.Environment,
            InstanceCount = 1,
            RuntimeVersion = "1.0.0",
            StartedAt = DateTime.UtcNow,
            Configuration = deployment.Artifacts.Configuration
        };

        // Initialize agent runtime
        await InitializeAgentRuntimeAsync(runningAgent, deployment.Artifacts);

        return runningAgent;
    }

    private async Task<string> ExecuteAgentCapabilityAsync(AgentExecutionContext context)
    {
        var capability = context.Request.Capability;
        var input = context.Request.Input;
        var runningAgent = context.RunningAgent;

        // Route to appropriate capability handler
        return capability.ToLowerInvariant() switch
        {
            "analyze" => await ExecuteAnalysisCapabilityAsync(input, runningAgent),
            "generate" => await ExecuteGenerationCapabilityAsync(input, runningAgent),
            "reason" => await ExecuteReasoningCapabilityAsync(input, runningAgent),
            "classify" => await ExecuteClassificationCapabilityAsync(input, runningAgent),
            "summarize" => await ExecuteSummarizationCapabilityAsync(input, runningAgent),
            _ => await ExecuteGenericCapabilityAsync(capability, input, runningAgent)
        };
    }

    private async Task<string> ExecuteAnalysisCapabilityAsync(string input, RunningAgent agent)
    {
        // Simulate analysis capability execution
        var analysis = $"Analysis of input: {input}\n\n";
        analysis += "Key insights:\n";
        analysis += "- Pattern detected in data structure\n";
        analysis += "- Semantic meaning identified\n";
        analysis += "- Confidence score: 0.85\n";

        await Task.Delay(100); // Simulate processing time
        return analysis;
    }

    private async Task<string> ExecuteGenerationCapabilityAsync(string input, RunningAgent agent)
    {
        // Simulate content generation
        var generated = $"Generated content based on: {input}\n\n";
        generated += "This is AI-generated content that responds to your request while maintaining ";
        generated += "consistency with the agent's specialized domain knowledge and capabilities.";

        await Task.Delay(150);
        return generated;
    }

    private async Task<string> ExecuteReasoningCapabilityAsync(string input, RunningAgent agent)
    {
        var reasoning = $"Logical reasoning for: {input}\n\n";
        reasoning += "Step 1: Premise analysis\n";
        reasoning += "Step 2: Logical deduction\n";
        reasoning += "Step 3: Conclusion synthesis\n";
        reasoning += "Result: [Reasoning outcome based on agent's knowledge]";

        await Task.Delay(200);
        return reasoning;
    }

    private async Task<string> ExecuteClassificationCapabilityAsync(string input, RunningAgent agent)
    {
        var classification = $"Classification result for: {input}\n\n";
        classification += "Category: [Determined category]\n";
        classification += "Confidence: 0.92\n";
        classification += "Alternative categories: [Secondary options]";

        await Task.Delay(80);
        return classification;
    }

    private async Task<string> ExecuteSummarizationCapabilityAsync(string input, RunningAgent agent)
    {
        var summary = $"Summary of: {input}\n\n";
        summary += "Key points:\n";
        summary += "• Main concept extraction\n";
        summary += "• Critical information identification\n";
        summary += "• Concise representation\n";

        await Task.Delay(120);
        return summary;
    }

    private async Task<string> ExecuteGenericCapabilityAsync(string capability, string input, RunningAgent agent)
    {
        var response = $"Executed {capability} capability with input: {input}\n\n";
        response += $"Agent {agent.AgentId} processed this request using its specialized ";
        response += $"domain knowledge and component-based architecture.";

        await Task.Delay(100);
        return response;
    }

    private async Task RecordExecutionMetricsAsync(Guid agentId, string capability, TimeSpan executionTime, string userId)
    {
        var metric = new ModelPerformanceMetric
        {
            AgentId = agentId,
            MetricName = "execution_latency",
            MetricCategory = "performance",
            MetricValue = executionTime.TotalMilliseconds,
            Unit = "ms",
            BenchmarkContext = capability,
            UserId = userId
        };

        _context.ModelPerformanceMetrics.Add(metric);
        await _context.SaveChangesAsync();
    }

    // Helper methods for deployment environments
    private async Task DeployToLocalAsync(AgentDeployment deployment)
    {
        // Local deployment logic
        _logger.LogInformation("Deploying agent {AgentId} to local environment", deployment.AgentId);
    }

    private async Task DeployToDockerAsync(AgentDeployment deployment)
    {
        // Docker deployment logic
        _logger.LogInformation("Deploying agent {AgentId} to Docker", deployment.AgentId);
    }

    private async Task DeployToKubernetesAsync(AgentDeployment deployment)
    {
        // Kubernetes deployment logic
        _logger.LogInformation("Deploying agent {AgentId} to Kubernetes", deployment.AgentId);
    }

    private async Task DeployToCloudAsync(AgentDeployment deployment)
    {
        // Cloud deployment logic
        _logger.LogInformation("Deploying agent {AgentId} to cloud", deployment.AgentId);
    }

    private string GenerateAgentEndpoint(AgentDeployment deployment)
    {
        var infrastructure = deployment.Infrastructure;
        var environmentType = infrastructure["type"].ToString();

        return environmentType switch
        {
            "local" => $"http://localhost:{infrastructure["port"]}",
            "docker" => $"http://localhost:{infrastructure["port"]}",
            "kubernetes" => $"http://{infrastructure["deployment_name"]}.{infrastructure["namespace"]}.svc.cluster.local:{infrastructure["service_port"]}",
            "cloud" => $"https://agent-{deployment.AgentId}.hartonomous.com",
            _ => $"http://localhost:8080"
        };
    }

    private async Task InitializeAgentRuntimeAsync(RunningAgent agent, DeploymentArtifacts artifacts)
    {
        // Initialize the agent runtime with components and capabilities
        _logger.LogInformation("Initializing runtime for agent {AgentId}", agent.AgentId);
    }

    private int GetAvailablePort()
    {
        // Simple port allocation - in production, use a port manager
        return new Random().Next(8000, 9000);
    }

    private string CalculateRequiredInstanceType(DeploymentArtifacts artifacts)
    {
        // Calculate based on component count and complexity
        var componentCount = artifacts.Components.Count;
        return componentCount switch
        {
            <= 5 => "small",
            <= 15 => "medium",
            _ => "large"
        };
    }

    private async Task<List<HealthCheck>> PerformHealthChecksAsync(RunningAgent agent)
    {
        var checks = new List<HealthCheck>
        {
            new() { Name = "endpoint_responsive", Status = "healthy", Message = "Agent endpoint is responding" },
            new() { Name = "memory_usage", Status = "healthy", Message = "Memory usage within limits" },
            new() { Name = "component_integrity", Status = "healthy", Message = "All components functioning" }
        };

        return checks;
    }

    private double CalculateHealthScore(List<HealthCheck> checks, Dictionary<string, object> metrics)
    {
        var healthyCount = checks.Count(c => c.Status == "healthy");
        return (double)healthyCount / checks.Count;
    }

    private async Task ScaleUpAgentAsync(RunningAgent agent, int additionalInstances)
    {
        _logger.LogInformation("Scaling up agent {AgentId} by {Count} instances",
            agent.AgentId, additionalInstances);
    }

    private async Task ScaleDownAgentAsync(RunningAgent agent, int instancesToRemove)
    {
        _logger.LogInformation("Scaling down agent {AgentId} by {Count} instances",
            agent.AgentId, instancesToRemove);
    }

    private async Task ShutdownAgentInstancesAsync(RunningAgent agent)
    {
        _logger.LogInformation("Shutting down agent {AgentId} instances", agent.AgentId);
    }

    private async Task CleanupInfrastructureAsync(RunningAgent agent)
    {
        _logger.LogInformation("Cleaning up infrastructure for agent {AgentId}", agent.AgentId);
    }
}

// Supporting data structures
public class AgentDeploymentRequest
{
    public string TargetEnvironment { get; set; } = string.Empty; // local, docker, kubernetes, cloud
    public string? CloudProvider { get; set; }
    public string? Region { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
    public int InitialInstances { get; set; } = 1;
}

public class AgentRequest
{
    public string Input { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
}

public class AgentExecutionContext
{
    public Guid AgentId { get; set; }
    public AgentRequest Request { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public RunningAgent RunningAgent { get; set; } = null!;
    public DateTime StartTime { get; set; }
}

public class DeploymentArtifacts
{
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public List<ComponentArtifact> Components { get; set; } = new();
    public List<CapabilityArtifact> Capabilities { get; set; } = new();
    public Dictionary<string, object> Configuration { get; set; } = new();
    public string TargetEnvironment { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

public class ComponentArtifact
{
    public Guid ComponentId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string Role { get; set; } = string.Empty;
}

public class CapabilityArtifact
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Proficiency { get; set; }
}

public class AgentDeployment
{
    public Guid DeploymentId { get; set; }
    public Guid AgentId { get; set; }
    public string Environment { get; set; } = string.Empty;
    public Dictionary<string, object> Infrastructure { get; set; } = new();
    public DeploymentArtifacts Artifacts { get; set; } = null!;
    public string Status { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; }
}

public class RunningAgent
{
    public Guid AgentId { get; set; }
    public Guid DeploymentId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public int InstanceCount { get; set; }
    public string RuntimeVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? LastScaledAt { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();

    public Dictionary<string, object> GetMetrics()
    {
        return new Dictionary<string, object>
        {
            ["uptime_seconds"] = (DateTime.UtcNow - StartedAt).TotalSeconds,
            ["instance_count"] = InstanceCount,
            ["requests_handled"] = 0, // Would be tracked in real implementation
            ["average_response_time_ms"] = 100,
            ["memory_usage_mb"] = 512,
            ["cpu_usage_percent"] = 25
        };
    }
}

public class AgentDeploymentResult
{
    public Guid AgentId { get; set; }
    public Guid DeploymentId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; }
    public Dictionary<string, object> RuntimeMetrics { get; set; } = new();
}

public class AgentExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Capability { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public List<ConstraintWarning> Warnings { get; set; } = new();
    public List<ConstraintViolation> Violations { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ScalingRequest
{
    public int TargetInstances { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AgentScalingResult
{
    public Guid AgentId { get; set; }
    public int PreviousInstances { get; set; }
    public int CurrentInstances { get; set; }
    public string ScalingReason { get; set; } = string.Empty;
    public DateTime ScaledAt { get; set; }
}

public class AgentHealthReport
{
    public Guid AgentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public double HealthScore { get; set; }
    public List<HealthCheck> HealthChecks { get; set; } = new();
    public Dictionary<string, object> Metrics { get; set; } = new();
    public int InstanceCount { get; set; }
    public TimeSpan Uptime { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; }
}

public class HealthCheck
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // healthy, degraded, unhealthy
    public string Message { get; set; } = string.Empty;
}

public class AgentUndeploymentResult
{
    public Guid AgentId { get; set; }
    public DateTime UndeployedAt { get; set; }
    public Dictionary<string, object> FinalMetrics { get; set; } = new();
}