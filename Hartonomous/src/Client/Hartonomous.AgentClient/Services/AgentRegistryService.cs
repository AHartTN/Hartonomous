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
    private readonly ConcurrentDictionary<AgentType, List<string>> _typeIndex = new();
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

        _agentLoader.AgentLoaded += OnAgentLoaded;
        _agentLoader.AgentUnloaded += OnAgentUnloaded;

        _metricsUpdateTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    public event EventHandler<AgentRegisteredEventArgs>? AgentRegistered;
    public event EventHandler<AgentUnregisteredEventArgs>? AgentUnregistered;
    public event EventHandler<AgentHealthChangedEventArgs>? AgentHealthChanged;

    public Task<AgentDefinition> RegisterAgentAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
    {
        if (agent == null) throw new ArgumentNullException(nameof(agent));
        _agents.AddOrUpdate(agent.Id, agent, (_, existing) => agent);
        UpdateTypeIndex(agent.Type, agent.Id);
        foreach (var tag in agent.Tags) { UpdateTagIndex(tag, agent.Id); }
        OnAgentRegistered(agent);
        return Task.FromResult(agent);
    }

    public Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));
        if (_agents.TryRemove(agentId, out var agent))
        {
            RemoveFromTypeIndex(agent.Type, agentId);
            foreach (var tag in agent.Tags) { RemoveFromTagIndex(tag, agentId); }
            OnAgentUnregistered(agentId);
        }
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<AgentDefinition>> FindAgentsForTaskAsync(string taskType, IEnumerable<string>? requiredCapabilities = null, CancellationToken cancellationToken = default)
    {
        return await FindAgentsByTaskTypeAsync(taskType, cancellationToken);
    }

    public async Task<IEnumerable<AgentDefinition>> FindAgentsByCapabilityAsync(string capabilityId, HealthStatus? healthStatus = null, CancellationToken cancellationToken = default)
    {
        var capabilityEntries = await _capabilityRegistry.DiscoverCapabilitiesAsync(healthStatus: healthStatus, available: true, cancellationToken: cancellationToken);
        var agentIds = capabilityEntries.Where(c => c.Capability.Id == capabilityId).Select(c => c.AgentId).Distinct();
        return agentIds.Select(id => _agents.TryGetValue(id, out var agent) ? agent : null).Where(a => a != null).Select(a => a!);
    }

    public Task<AgentDefinition?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _agents.TryGetValue(agentId, out var agent);
        return Task.FromResult(agent);
    }

    public Task<IEnumerable<AgentDefinition>> ListAgentsAsync(HealthStatus? healthStatus = null, string? agentType = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        var agents = _agents.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(agentType) && Enum.TryParse<AgentType>(agentType, true, out var parsedType))
        {
            agents = agents.Where(a => a.Type == parsedType);
        }
        if (tags?.Any() == true)
        {
            var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            agents = agents.Where(a => a.Tags.Any(t => tagSet.Contains(t)));
        }
        return Task.FromResult(agents);
    }

    public async Task<IEnumerable<AgentInstance>> GetAvailableInstancesAsync(string agentId, AgentInstanceStatus? status = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));
        return await _agentRuntime.ListInstancesAsync(agentId: agentId, status: status, cancellationToken: cancellationToken);
    }

    public Task UpdateAgentHealthAsync(string agentId, HealthStatus healthStatus, CancellationToken cancellationToken = default)
    {
        OnAgentHealthChanged(agentId, healthStatus, HealthStatus.Unknown);
        return Task.CompletedTask;
    }

    public Task<AgentLoadMetrics?> GetAgentLoadMetricsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _loadMetrics.TryGetValue(agentId, out var metrics);
        return Task.FromResult(metrics);
    }

    public Task<AgentPerformanceMetrics?> GetAgentPerformanceMetricsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _performanceMetrics.TryGetValue(agentId, out var metrics);
        return Task.FromResult(metrics);
    }

    public async Task<IEnumerable<AgentSearchResult>> SearchAgentsAsync(string searchTerm, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return Array.Empty<AgentSearchResult>();
        var searchTermLower = searchTerm.ToLowerInvariant();
        var results = new List<AgentSearchResult>();
        foreach (var agent in _agents.Values)
        {
            var score = 0.0;
            if (agent.Name.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase)) score += 50;
            if (agent.Description.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase)) score += 30;
            if (agent.Type.ToString().Contains(searchTermLower, StringComparison.OrdinalIgnoreCase)) score += 25;
            if (agent.Tags.Any(t => t.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))) score += 10;
            if (score > 0) results.Add(new AgentSearchResult { Agent = agent, RelevanceScore = score, Highlights = new List<string>() });
        }
        return await Task.FromResult(results.OrderByDescending(r => r.RelevanceScore).Take(limit).ToList());
    }

    private async Task<IEnumerable<AgentDefinition>> FindAgentsByTaskTypeAsync(string taskType, CancellationToken cancellationToken)
    {
        var agents = new List<AgentDefinition>();
        if (Enum.TryParse<AgentType>(taskType, true, out var parsedAgentType) && _typeIndex.TryGetValue(parsedAgentType, out var agentIds))
        {
            foreach (var agentId in agentIds)
            {
                if (_agents.TryGetValue(agentId, out var agent)) agents.Add(agent);
            }
        }
        var capabilityEntries = await _capabilityRegistry.DiscoverCapabilitiesAsync(category: taskType, available: true, cancellationToken: cancellationToken);
        foreach (var entry in capabilityEntries)
        {
            if (_agents.TryGetValue(entry.AgentId, out var agent) && !agents.Any(a => a.Id == agent.Id)) agents.Add(agent);
        }
        return agents;
    }

    private void UpdateTypeIndex(AgentType agentType, string agentId)
    {
        _typeIndex.AddOrUpdate(agentType, new List<string> { agentId }, (_, existing) => { if (!existing.Contains(agentId)) existing.Add(agentId); return existing; });
    }

    private void UpdateTagIndex(string tag, string agentId)
    {
        _tagIndex.AddOrUpdate(tag, new List<string> { agentId }, (_, existing) => { if (!existing.Contains(agentId)) existing.Add(agentId); return existing; });
    }

    private void RemoveFromTypeIndex(AgentType agentType, string agentId)
    {
        if (_typeIndex.TryGetValue(agentType, out var list)) { list.Remove(agentId); if (list.Count == 0) _typeIndex.TryRemove(agentType, out _); }
    }

    private void RemoveFromTagIndex(string tag, string agentId)
    {
        if (_tagIndex.TryGetValue(tag, out var list)) { list.Remove(agentId); if (list.Count == 0) _tagIndex.TryRemove(tag, out _); }
    }

    private async void UpdateMetrics(object? state)
    {
        try
        {
            foreach (var agentId in _agents.Keys) await UpdateAgentMetricsAsync(agentId);
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
            var availableInstanceCount = 0;
            foreach (var instance in instanceList)
            {
                var health = await _agentRuntime.CheckInstanceHealthAsync(instance.InstanceId);
                if (health == HealthStatus.Healthy) availableInstanceCount++;
            }
            var loadMetrics = new AgentLoadMetrics { AgentId = agentId, Available = availableInstanceCount > 0, LastUpdated = DateTimeOffset.UtcNow };
            _loadMetrics.AddOrUpdate(agentId, loadMetrics, (_, _) => loadMetrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update metrics for agent {AgentId}", agentId);
        }
    }

    private async void OnAgentLoaded(object? sender, AgentLoadedEventArgs e)
    {
        try { await RegisterAgentAsync(e.Agent); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to register loaded agent {AgentId}", e.Agent.Id); }
    }

    private async void OnAgentUnloaded(object? sender, AgentUnloadedEventArgs e)
    {
        try { await UnregisterAgentAsync(e.AgentId); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to unregister unloaded agent {AgentId}", e.AgentId); }
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
        AgentHealthChanged?.Invoke(this, new AgentHealthChangedEventArgs { AgentId = agentId, NewHealthStatus = newHealth, PreviousHealthStatus = previousHealth });
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