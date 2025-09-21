using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Service for agent registry providing agent discovery, routing, and load balancing
/// </summary>
public class AgentRegistryService : IAgentRegistry, IDisposable
{
    private readonly ILogger<AgentRegistryService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IAgentRuntime _agentRuntime;
    private readonly IAgentLoader _agentLoader;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly ConcurrentDictionary<string, AgentDefinition> _agents = new();
    private readonly ConcurrentDictionary<string, AgentLoadMetrics> _loadMetrics = new();
    private readonly ConcurrentDictionary<string, AgentPerformanceMetrics> _performanceMetrics = new();
    private readonly ConcurrentDictionary<string, List<string>> _typeIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _tagIndex = new();
    private readonly Timer _metricsUpdateTimer;
    private bool _disposed;

    public AgentRegistryService(
        ILogger<AgentRegistryService> logger,
        IMetricsCollector metricsCollector,
        IAgentRuntime agentRuntime,
        IAgentLoader agentLoader,
        ICapabilityRegistry capabilityRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
        _agentLoader = agentLoader ?? throw new ArgumentNullException(nameof(agentLoader));
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));

        // Subscribe to agent loader events
        _agentLoader.AgentLoaded += OnAgentLoaded;
        _agentLoader.AgentUnloaded += OnAgentUnloaded;

        // Start periodic metrics update
        _metricsUpdateTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    public event EventHandler<AgentRegisteredEventArgs>? AgentRegistered;
    public event EventHandler<AgentUnregisteredEventArgs>? AgentUnregistered;
    public event EventHandler<AgentHealthChangedEventArgs>? AgentHealthChanged;

    public async Task<AgentDefinition> RegisterAgentAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
    {
        if (agent == null) throw new ArgumentNullException(nameof(agent));

        _agents.AddOrUpdate(agent.Id, agent, (_, existing) => agent);

        // Update indexes
        UpdateTypeIndex(agent.Type, agent.Id);
        foreach (var tag in agent.Tags)
        {
            UpdateTagIndex(tag, agent.Id);
        }

        // Initialize metrics
        _loadMetrics.TryAdd(agent.Id, new AgentLoadMetrics
        {
            AgentId = agent.Id,
            Available = true
        });

        _performanceMetrics.TryAdd(agent.Id, new AgentPerformanceMetrics
        {
            AgentId = agent.Id,
            MetricsPeriod = TimeSpan.FromHours(24)
        });

        // Register agent capabilities
        try
        {
            var capabilities = await _agentLoader.GetAgentCapabilitiesAsync(agent.Id, cancellationToken);
            foreach (var capability in capabilities)
            {
                await _capabilityRegistry.RegisterCapabilityAsync(capability, agent.Id, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register capabilities for agent {AgentId}", agent.Id);
        }

        _logger.LogInformation("Registered agent {AgentId} of type {AgentType}", agent.Id, agent.Type);

        _metricsCollector.IncrementCounter("agent.registered", tags: new Dictionary<string, string>
        {
            ["agent_id"] = agent.Id,
            ["agent_type"] = agent.Type.ToString()
        });

        OnAgentRegistered(agent);

        return agent;
    }

    public async Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        if (_agents.TryRemove(agentId, out var agent))
        {
            // Remove from indexes
            RemoveFromTypeIndex(agent.Type, agentId);
            foreach (var tag in agent.Tags)
            {
                RemoveFromTagIndex(tag, agentId);
            }

            // Remove metrics
            _loadMetrics.TryRemove(agentId, out _);
            _performanceMetrics.TryRemove(agentId, out _);

            // Unregister capabilities
            try
            {
                await _capabilityRegistry.UnregisterCapabilityAsync("*", agentId, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister capabilities for agent {AgentId}", agentId);
            }

            _logger.LogInformation("Unregistered agent {AgentId}", agentId);

            _metricsCollector.IncrementCounter("agent.unregistered", tags: new Dictionary<string, string>
            {
                ["agent_id"] = agentId,
                ["agent_type"] = agent.Type.ToString()
            });

            OnAgentUnregistered(agentId);
        }

        await Task.CompletedTask;
    }

    public async Task<IEnumerable<AgentDefinition>> FindAgentsForTaskAsync(
        string taskType,
        IEnumerable<string>? requiredCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskType)) throw new ArgumentNullException(nameof(taskType));

        var suitableAgents = new List<AgentDefinition>();

        // Find agents by task type mapping
        var agentsByType = await FindAgentsByTaskTypeAsync(taskType, cancellationToken);
        suitableAgents.AddRange(agentsByType);

        // Find agents by capabilities if specified
        if (requiredCapabilities?.Any() == true)
        {
            foreach (var capabilityId in requiredCapabilities)
            {
                var agentsByCapability = await FindAgentsByCapabilityAsync(capabilityId, HealthStatus.Healthy, cancellationToken);
                suitableAgents.AddRange(agentsByCapability);
            }
        }

        // Remove duplicates and filter by health and availability
        var uniqueAgents = suitableAgents
            .GroupBy(a => a.Id)
            .Select(g => g.First())
            .ToList();

        var availableAgents = new List<AgentDefinition>();
        foreach (var agent in uniqueAgents)
        {
            var instances = await GetAvailableInstancesAsync(agent.Id, AgentStatus.Running, cancellationToken);
            if (instances.Any())
            {
                availableAgents.Add(agent);
            }
        }

        _metricsCollector.IncrementCounter("agent.discovery", tags: new Dictionary<string, string>
        {
            ["task_type"] = taskType,
            ["found_count"] = availableAgents.Count.ToString()
        });

        return availableAgents;
    }

    public async Task<IEnumerable<AgentDefinition>> FindAgentsByCapabilityAsync(
        string capabilityId,
        HealthStatus? healthStatus = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(capabilityId)) throw new ArgumentNullException(nameof(capabilityId));

        var capabilityEntries = await _capabilityRegistry.DiscoverCapabilitiesAsync(
            healthStatus: healthStatus,
            available: true,
            cancellationToken: cancellationToken);

        var agentIds = capabilityEntries
            .Where(c => c.Capability.Id == capabilityId)
            .Select(c => c.AgentId)
            .Distinct()
            .ToList();

        var agents = new List<AgentDefinition>();
        foreach (var agentId in agentIds)
        {
            if (_agents.TryGetValue(agentId, out var agent))
            {
                agents.Add(agent);
            }
        }

        return agents;
    }

    public async Task<AgentDefinition?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        _agents.TryGetValue(agentId, out var agent);
        return await Task.FromResult(agent);
    }

    public async Task<IEnumerable<AgentDefinition>> ListAgentsAsync(
        HealthStatus? healthStatus = null,
        string? agentType = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var agents = _agents.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(agentType))
        {
            if (Enum.TryParse<AgentType>(agentType, true, out var parsedAgentType))
            {
                agents = agents.Where(a => a.Type == parsedAgentType);
            }
            else
            {
                // If parsing fails, return empty result as the agentType is invalid
                agents = Enumerable.Empty<AgentDefinition>();
            }
        }

        if (tags?.Any() == true)
        {
            var tagSet = tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            agents = agents.Where(a => a.Tags.Any(t => tagSet.Contains(t)));
        }

        var result = agents.ToList();

        // Filter by health status if specified
        if (healthStatus.HasValue)
        {
            var healthyAgents = new List<AgentDefinition>();
            foreach (var agent in result)
            {
                var instances = await GetAvailableInstancesAsync(agent.Id, cancellationToken: cancellationToken);
                var hasHealthyInstance = false;

                foreach (var instance in instances)
                {
                    var health = await _agentRuntime.CheckInstanceHealthAsync(instance.InstanceId, cancellationToken);
                    if (health == healthStatus.Value)
                    {
                        hasHealthyInstance = true;
                        break;
                    }
                }

                if (hasHealthyInstance)
                {
                    healthyAgents.Add(agent);
                }
            }
            result = healthyAgents;
        }

        return result;
    }

    public async Task<IEnumerable<AgentInstance>> GetAvailableInstancesAsync(
        string agentId,
        AgentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        var instances = await _agentRuntime.ListInstancesAsync(
            agentId: agentId,
            status: status,
            cancellationToken: cancellationToken);

        return instances;
    }

    public async Task UpdateAgentHealthAsync(string agentId, HealthStatus healthStatus, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        // Update load metrics availability based on health
        if (_loadMetrics.TryGetValue(agentId, out var loadMetrics))
        {
            var available = healthStatus == HealthStatus.Healthy;
            var updatedMetrics = loadMetrics with { Available = available, LastUpdated = DateTimeOffset.UtcNow };
            _loadMetrics.TryUpdate(agentId, updatedMetrics, loadMetrics);
        }

        _logger.LogInformation("Updated health for agent {AgentId} to {HealthStatus}", agentId, healthStatus);

        OnAgentHealthChanged(agentId, healthStatus, HealthStatus.Unknown);

        await Task.CompletedTask;
    }

    public async Task<AgentLoadMetrics?> GetAgentLoadMetricsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        _loadMetrics.TryGetValue(agentId, out var metrics);
        return await Task.FromResult(metrics);
    }

    public async Task<AgentPerformanceMetrics?> GetAgentPerformanceMetricsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        _performanceMetrics.TryGetValue(agentId, out var metrics);
        return await Task.FromResult(metrics);
    }

    public async Task<IEnumerable<AgentSearchResult>> SearchAgentsAsync(
        string searchTerm,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return Array.Empty<AgentSearchResult>();

        var searchTermLower = searchTerm.ToLowerInvariant();
        var results = new List<AgentSearchResult>();

        foreach (var agent in _agents.Values)
        {
            var score = 0.0;
            var highlights = new List<string>();

            // Search in name (highest weight)
            if (agent.Name.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
                highlights.Add($"Name: {agent.Name}");
            }

            // Search in description
            if (agent.Description.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
                highlights.Add($"Description: {agent.Description}");
            }

            // Search in type
            if (agent.Type.ToString().Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
                highlights.Add($"Type: {agent.Type}");
            }

            // Search in tags
            foreach (var tag in agent.Tags)
            {
                if (tag.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                    highlights.Add($"Tag: {tag}");
                }
            }

            if (score > 0)
            {
                results.Add(new AgentSearchResult
                {
                    Agent = agent,
                    RelevanceScore = score,
                    Highlights = highlights
                });
            }
        }

        return await Task.FromResult(results
            .OrderByDescending(r => r.RelevanceScore)
            .Take(limit)
            .ToList());
    }

    private async Task<IEnumerable<AgentDefinition>> FindAgentsByTaskTypeAsync(string taskType, CancellationToken cancellationToken)
    {
        // Task type mapping logic - this could be enhanced with more sophisticated mapping
        var agents = new List<AgentDefinition>();

        // Check if any agents are explicitly registered for this task type
        if (_typeIndex.TryGetValue(taskType, out var agentIds))
        {
            foreach (var agentId in agentIds)
            {
                if (_agents.TryGetValue(agentId, out var agent))
                {
                    agents.Add(agent);
                }
            }
        }

        // Also check for agents that can handle the task type through capabilities
        var capabilityEntries = await _capabilityRegistry.DiscoverCapabilitiesAsync(
            category: taskType,
            available: true,
            cancellationToken: cancellationToken);

        foreach (var entry in capabilityEntries)
        {
            if (_agents.TryGetValue(entry.AgentId, out var agent) && !agents.Any(a => a.Id == agent.Id))
            {
                agents.Add(agent);
            }
        }

        return agents;
    }

    private void UpdateTypeIndex(AgentType agentType, string agentId)
    {
        var type = agentType.ToString();
        _typeIndex.AddOrUpdate(type,
            new List<string> { agentId },
            (_, existing) =>
            {
                if (!existing.Contains(agentId))
                    existing.Add(agentId);
                return existing;
            });
    }

    private void UpdateTagIndex(string tag, string agentId)
    {
        _tagIndex.AddOrUpdate(tag,
            new List<string> { agentId },
            (_, existing) =>
            {
                if (!existing.Contains(agentId))
                    existing.Add(agentId);
                return existing;
            });
    }

    private void RemoveFromTypeIndex(AgentType agentType, string agentId)
    {
        var type = agentType.ToString();
        if (_typeIndex.TryGetValue(type, out var list))
        {
            list.Remove(agentId);
            if (list.Count == 0)
            {
                _typeIndex.TryRemove(type, out _);
            }
        }
    }

    private void RemoveFromTagIndex(string tag, string agentId)
    {
        if (_tagIndex.TryGetValue(tag, out var list))
        {
            list.Remove(agentId);
            if (list.Count == 0)
            {
                _tagIndex.TryRemove(tag, out _);
            }
        }
    }

    private async void UpdateMetrics(object? state)
    {
        try
        {
            foreach (var agentId in _agents.Keys)
            {
                await UpdateAgentMetricsAsync(agentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent metrics");
        }
    }

    private async Task UpdateAgentMetricsAsync(string agentId)
    {
        try
        {
            var instances = await GetAvailableInstancesAsync(agentId);
            var instanceList = instances.ToList();

            // Update load metrics
            var activeTaskCount = 0;
            var totalCpu = 0.0;
            var totalMemory = 0.0;
            var availableInstanceCount = 0;

            foreach (var instance in instanceList)
            {
                try
                {
                    var health = await _agentRuntime.CheckInstanceHealthAsync(instance.InstanceId);
                    if (health == HealthStatus.Healthy)
                    {
                        availableInstanceCount++;
                        var resourceUsage = await _agentRuntime.GetInstanceResourceUsageAsync(instance.InstanceId);
                        if (resourceUsage != null)
                        {
                            totalCpu += resourceUsage.CpuUsagePercent;
                            totalMemory += resourceUsage.MemoryUsageMb;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get metrics for instance {InstanceId}", instance.InstanceId);
                }
            }

            var avgCpu = instanceList.Count > 0 ? totalCpu / instanceList.Count : 0;
            var avgMemory = instanceList.Count > 0 ? totalMemory / instanceList.Count : 0;
            var loadScore = Math.Min(100, (avgCpu + (avgMemory / 1024)) / 2); // Simple load calculation

            var loadMetrics = new AgentLoadMetrics
            {
                AgentId = agentId,
                CpuUsagePercent = avgCpu,
                MemoryUsagePercent = avgMemory,
                ActiveTasks = activeTaskCount,
                LoadScore = loadScore,
                Available = availableInstanceCount > 0,
                LastUpdated = DateTimeOffset.UtcNow
            };

            _loadMetrics.AddOrUpdate(agentId, loadMetrics, (_, _) => loadMetrics);

            // Performance metrics would be updated based on actual task execution data
            // For now, maintain basic structure
            if (_performanceMetrics.TryGetValue(agentId, out var perfMetrics))
            {
                var updatedPerfMetrics = perfMetrics with { LastUpdated = DateTimeOffset.UtcNow };
                _performanceMetrics.TryUpdate(agentId, updatedPerfMetrics, perfMetrics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update metrics for agent {AgentId}", agentId);
        }
    }

    private async void OnAgentLoaded(object? sender, AgentLoadedEventArgs e)
    {
        try
        {
            await RegisterAgentAsync(e.Agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register loaded agent {AgentId}", e.Agent.Id);
        }
    }

    private async void OnAgentUnloaded(object? sender, AgentUnloadedEventArgs e)
    {
        try
        {
            await UnregisterAgentAsync(e.AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister unloaded agent {AgentId}", e.AgentId);
        }
    }

    private void OnAgentRegistered(AgentDefinition agent)
    {
        AgentRegistered?.Invoke(this, new AgentRegisteredEventArgs { Agent = agent });
    }

    private void OnAgentUnregistered(string agentId)
    {
        AgentUnregistered?.Invoke(this, new AgentUnregisteredEventArgs { AgentId = agentId });
    }

    private void OnAgentHealthChanged(string agentId, HealthStatus newHealth, HealthStatus previousHealth)
    {
        AgentHealthChanged?.Invoke(this, new AgentHealthChangedEventArgs
        {
            AgentId = agentId,
            NewHealthStatus = newHealth,
            PreviousHealthStatus = previousHealth
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _metricsUpdateTimer?.Dispose();

        if (_agentLoader != null)
        {
            _agentLoader.AgentLoaded -= OnAgentLoaded;
            _agentLoader.AgentUnloaded -= OnAgentUnloaded;
        }

        _disposed = true;
    }
}