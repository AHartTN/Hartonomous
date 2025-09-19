using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Service for managing agent capability registry and discovery
/// </summary>
public class CapabilityRegistryService : ICapabilityRegistry, IDisposable
{
    private readonly ILogger<CapabilityRegistryService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IAgentRuntime _agentRuntime;
    private readonly ICurrentUserService _currentUserService;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CapabilityRegistryEntry>> _capabilities = new();
    private readonly ConcurrentDictionary<string, List<string>> _categoryIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _tagIndex = new();
    private readonly Timer _healthCheckTimer;
    private bool _disposed;

    public CapabilityRegistryService(
        ILogger<CapabilityRegistryService> logger,
        IMetricsCollector metricsCollector,
        IAgentRuntime agentRuntime,
        ICurrentUserService currentUserService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

        // Start periodic health checks
        _healthCheckTimer = new Timer(PeriodicHealthCheck, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    }

    public event EventHandler<CapabilityRegisteredEventArgs>? CapabilityRegistered;
    public event EventHandler<CapabilityUnregisteredEventArgs>? CapabilityUnregistered;
    public event EventHandler<CapabilityHealthChangedEventArgs>? CapabilityHealthChanged;
    public event EventHandler<CapabilityExecutedEventArgs>? CapabilityExecuted;

    public async Task<CapabilityRegistryEntry> RegisterCapabilityAsync(
        AgentCapability capability,
        string agentId,
        string? instanceId = null,
        string? endpoint = null,
        CancellationToken cancellationToken = default)
    {
        if (capability == null) throw new ArgumentNullException(nameof(capability));
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        var userId = await _currentUserService.GetCurrentUserIdAsync(cancellationToken);

        var entry = new CapabilityRegistryEntry
        {
            Capability = capability,
            AgentId = agentId,
            InstanceId = instanceId,
            Endpoint = endpoint,
            UserId = userId,
            HealthStatus = HealthStatus.Unknown,
            Available = true
        };

        // Add to registry
        var agentCapabilities = _capabilities.GetOrAdd(agentId, _ => new ConcurrentDictionary<string, CapabilityRegistryEntry>());
        agentCapabilities.AddOrUpdate(capability.Id, entry, (_, existing) => entry);

        // Update indexes
        UpdateCategoryIndex(capability.Category, capability.Id);
        foreach (var tag in capability.Tags)
        {
            UpdateTagIndex(tag, capability.Id);
        }

        // Perform initial health check
        try
        {
            var healthStatus = await CheckCapabilityHealthAsync(capability.Id, agentId, instanceId, cancellationToken);
            entry = entry with { HealthStatus = healthStatus, LastHealthCheck = DateTimeOffset.UtcNow };
            agentCapabilities.TryUpdate(capability.Id, entry, entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to perform initial health check for capability {CapabilityId}", capability.Id);
            entry = entry with { HealthStatus = HealthStatus.Unhealthy, LastHealthCheck = DateTimeOffset.UtcNow };
            agentCapabilities.TryUpdate(capability.Id, entry, entry);
        }

        _logger.LogInformation("Registered capability {CapabilityId} for agent {AgentId}", capability.Id, agentId);

        _metricsCollector.IncrementCounter("capability.registered", tags: new Dictionary<string, string>
        {
            ["capability_id"] = capability.Id,
            ["agent_id"] = agentId,
            ["category"] = capability.Category
        });

        OnCapabilityRegistered(entry);

        return entry;
    }

    public async Task UnregisterCapabilityAsync(
        string capabilityId,
        string agentId,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(capabilityId)) throw new ArgumentNullException(nameof(capabilityId));
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        if (_capabilities.TryGetValue(agentId, out var agentCapabilities))
        {
            if (agentCapabilities.TryRemove(capabilityId, out var entry))
            {
                // Remove from indexes
                RemoveFromCategoryIndex(entry.Capability.Category, capabilityId);
                foreach (var tag in entry.Capability.Tags)
                {
                    RemoveFromTagIndex(tag, capabilityId);
                }

                _logger.LogInformation("Unregistered capability {CapabilityId} for agent {AgentId}", capabilityId, agentId);

                _metricsCollector.IncrementCounter("capability.unregistered", tags: new Dictionary<string, string>
                {
                    ["capability_id"] = capabilityId,
                    ["agent_id"] = agentId,
                    ["category"] = entry.Capability.Category
                });

                OnCapabilityUnregistered(capabilityId, agentId, instanceId);
            }

            // Remove agent entry if no capabilities left
            if (agentCapabilities.IsEmpty)
            {
                _capabilities.TryRemove(agentId, out _);
            }
        }

        await Task.CompletedTask;
    }

    public async Task<CapabilityRegistryEntry> UpdateCapabilityAsync(
        string capabilityId,
        string agentId,
        Dictionary<string, object> updates,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetCapabilityAsync(capabilityId, agentId, instanceId, cancellationToken);
        if (entry == null)
            throw new InvalidOperationException($"Capability {capabilityId} not found for agent {agentId}");

        // Apply updates (simplified - would normally handle specific property updates)
        var updatedEntry = entry with { UpdatedAt = DateTimeOffset.UtcNow };

        var agentCapabilities = _capabilities[agentId];
        agentCapabilities.TryUpdate(capabilityId, updatedEntry, entry);

        _logger.LogInformation("Updated capability {CapabilityId} for agent {AgentId}", capabilityId, agentId);

        return updatedEntry;
    }

    public async Task<IEnumerable<CapabilityRegistryEntry>> DiscoverCapabilitiesAsync(
        string? category = null,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? requiredPermissions = null,
        HealthStatus? healthStatus = null,
        bool? available = null,
        CancellationToken cancellationToken = default)
    {
        var allCapabilities = _capabilities.Values
            .SelectMany(agentCaps => agentCaps.Values)
            .ToList();

        var filtered = allCapabilities.AsEnumerable();

        if (!string.IsNullOrEmpty(category))
        {
            filtered = filtered.Where(c => c.Capability.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (tags?.Any() == true)
        {
            var tagSet = tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(c => c.Capability.Tags.Any(t => tagSet.Contains(t)));
        }

        if (requiredPermissions?.Any() == true)
        {
            var permissionSet = requiredPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(c => permissionSet.All(p => c.Capability.RequiredPermissions.Contains(p)));
        }

        if (healthStatus.HasValue)
        {
            filtered = filtered.Where(c => c.HealthStatus == healthStatus.Value);
        }

        if (available.HasValue)
        {
            filtered = filtered.Where(c => c.Available == available.Value);
        }

        var result = filtered.ToList();

        _metricsCollector.IncrementCounter("capability.discovered", tags: new Dictionary<string, string>
        {
            ["result_count"] = result.Count.ToString(),
            ["category"] = category ?? "all",
            ["health_status"] = healthStatus?.ToString() ?? "all"
        });

        return await Task.FromResult(result);
    }

    public async Task<CapabilityRegistryEntry?> GetCapabilityAsync(
        string capabilityId,
        string? agentId = null,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(capabilityId)) throw new ArgumentNullException(nameof(capabilityId));

        if (!string.IsNullOrEmpty(agentId))
        {
            if (_capabilities.TryGetValue(agentId, out var agentCapabilities))
            {
                agentCapabilities.TryGetValue(capabilityId, out var entry);
                return await Task.FromResult(entry);
            }
        }
        else
        {
            // Search across all agents
            foreach (var agentCapabilities in _capabilities.Values)
            {
                if (agentCapabilities.TryGetValue(capabilityId, out var entry))
                {
                    if (string.IsNullOrEmpty(instanceId) || entry.InstanceId == instanceId)
                    {
                        return await Task.FromResult(entry);
                    }
                }
            }
        }

        return await Task.FromResult<CapabilityRegistryEntry?>(null);
    }

    public async Task<IEnumerable<CapabilityRegistryEntry>> ListCapabilitiesAsync(
        string? agentId = null,
        string? instanceId = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var capabilities = new List<CapabilityRegistryEntry>();

        if (!string.IsNullOrEmpty(agentId))
        {
            if (_capabilities.TryGetValue(agentId, out var agentCapabilities))
            {
                capabilities.AddRange(agentCapabilities.Values);
            }
        }
        else
        {
            capabilities.AddRange(_capabilities.Values.SelectMany(ac => ac.Values));
        }

        if (!string.IsNullOrEmpty(instanceId))
        {
            capabilities = capabilities.Where(c => c.InstanceId == instanceId).ToList();
        }

        if (!string.IsNullOrEmpty(userId))
        {
            capabilities = capabilities.Where(c => c.UserId == userId).ToList();
        }

        return await Task.FromResult(capabilities);
    }

    public async Task<CapabilityExecutionResponse> ExecuteCapabilityAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var entry = await GetCapabilityAsync(request.CapabilityId, cancellationToken: cancellationToken);
            if (entry == null)
            {
                throw new InvalidOperationException($"Capability {request.CapabilityId} not found");
            }

            if (!entry.Available || entry.HealthStatus != HealthStatus.Healthy)
            {
                throw new InvalidOperationException($"Capability {request.CapabilityId} is not available");
            }

            // Get agent instance
            var instance = await _agentRuntime.GetInstanceAsync(entry.InstanceId!, cancellationToken);
            if (instance == null)
            {
                throw new InvalidOperationException($"Agent instance {entry.InstanceId} not found");
            }

            // Execute capability through the agent instance
            // This would delegate to the actual agent implementation
            var result = await ExecuteCapabilityOnInstanceAsync(instance, request, cancellationToken);

            stopwatch.Stop();

            var response = new CapabilityExecutionResponse
            {
                RequestId = request.RequestId,
                Success = result.Success,
                Output = result.Data,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ResourceUsage = result.ResourceUsage,
                Error = result.Success ? null : new AgentError
                {
                    Code = "CAPABILITY_EXECUTION_FAILED",
                    Message = result.Message ?? "Capability execution failed",
                    Severity = ErrorSeverity.Error
                }
            };

            // Update capability usage statistics
            await UpdateCapabilityUsageInternalAsync(entry, response, cancellationToken);

            OnCapabilityExecuted(request, response);

            _metricsCollector.IncrementCounter("capability.executed", tags: new Dictionary<string, string>
            {
                ["capability_id"] = request.CapabilityId,
                ["success"] = response.Success.ToString().ToLower(),
                ["duration_ms"] = response.DurationMs.ToString()
            });

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var errorResponse = new CapabilityExecutionResponse
            {
                RequestId = request.RequestId,
                Success = false,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Error = new AgentError
                {
                    Code = "CAPABILITY_EXECUTION_ERROR",
                    Message = ex.Message,
                    Details = ex.StackTrace,
                    Severity = ErrorSeverity.Error
                }
            };

            _metricsCollector.IncrementCounter("capability.execution_error", tags: new Dictionary<string, string>
            {
                ["capability_id"] = request.CapabilityId,
                ["error_type"] = ex.GetType().Name
            });

            return errorResponse;
        }
    }

    public async Task<HealthStatus> CheckCapabilityHealthAsync(
        string capabilityId,
        string agentId,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the agent instance is healthy
            if (!string.IsNullOrEmpty(instanceId))
            {
                var healthStatus = await _agentRuntime.CheckInstanceHealthAsync(instanceId, cancellationToken);
                return healthStatus;
            }

            // Check general agent health
            var instances = await _agentRuntime.ListInstancesAsync(agentId: agentId, cancellationToken: cancellationToken);
            var runningInstances = instances.Where(i => i.Status == AgentStatus.Running).ToList();

            if (runningInstances.Count == 0)
                return HealthStatus.Unhealthy;

            // Check if any instance is healthy
            foreach (var instance in runningInstances)
            {
                var health = await _agentRuntime.CheckInstanceHealthAsync(instance.InstanceId, cancellationToken);
                if (health == HealthStatus.Healthy)
                    return HealthStatus.Healthy;
            }

            return HealthStatus.Degraded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check health for capability {CapabilityId}", capabilityId);
            return HealthStatus.Unhealthy;
        }
    }

    public async Task UpdateCapabilityHealthAsync(
        string capabilityId,
        string agentId,
        HealthStatus healthStatus,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetCapabilityAsync(capabilityId, agentId, instanceId, cancellationToken);
        if (entry == null) return;

        var previousHealth = entry.HealthStatus;
        var updatedEntry = entry with
        {
            HealthStatus = healthStatus,
            LastHealthCheck = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var agentCapabilities = _capabilities[agentId];
        agentCapabilities.TryUpdate(capabilityId, updatedEntry, entry);

        if (previousHealth != healthStatus)
        {
            OnCapabilityHealthChanged(capabilityId, agentId, instanceId, healthStatus, previousHealth);
        }
    }

    public async Task<CapabilityUsage?> GetCapabilityUsageAsync(
        string capabilityId,
        string? agentId = null,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetCapabilityAsync(capabilityId, agentId, instanceId, cancellationToken);
        return entry?.Capability.Usage;
    }

    public async Task UpdateCapabilityUsageAsync(
        string capabilityId,
        string agentId,
        CapabilityExecutionResponse executionResult,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetCapabilityAsync(capabilityId, agentId, instanceId, cancellationToken);
        if (entry == null) return;

        await UpdateCapabilityUsageInternalAsync(entry, executionResult, cancellationToken);
    }

    public async Task<IEnumerable<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_categoryIndex.Keys.ToList());
    }

    public async Task<IEnumerable<string>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_tagIndex.Keys.ToList());
    }

    public async Task<IEnumerable<CapabilitySearchResult>> SearchCapabilitiesAsync(
        string searchTerm,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return Array.Empty<CapabilitySearchResult>();

        var allCapabilities = _capabilities.Values
            .SelectMany(agentCaps => agentCaps.Values)
            .ToList();

        var searchTermLower = searchTerm.ToLowerInvariant();
        var results = new List<CapabilitySearchResult>();

        foreach (var entry in allCapabilities)
        {
            var capability = entry.Capability;
            var score = 0.0;
            var highlights = new List<string>();

            // Search in name (highest weight)
            if (capability.Name.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
                highlights.Add($"Name: {capability.Name}");
            }

            // Search in description
            if (capability.Description.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
                highlights.Add($"Description: {capability.Description}");
            }

            // Search in category
            if (capability.Category.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                highlights.Add($"Category: {capability.Category}");
            }

            // Search in tags
            foreach (var tag in capability.Tags)
            {
                if (tag.Contains(searchTermLower, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                    highlights.Add($"Tag: {tag}");
                }
            }

            if (score > 0)
            {
                results.Add(new CapabilitySearchResult
                {
                    Entry = entry,
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

    public async Task<IEnumerable<CapabilityRegistryEntry>> GetRecommendedCapabilitiesAsync(
        string userId,
        Dictionary<string, object>? context = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        // Simple recommendation based on usage patterns
        var userCapabilities = await ListCapabilitiesAsync(userId: userId, cancellationToken: cancellationToken);

        // For now, return most frequently used capabilities
        var recommended = userCapabilities
            .Where(c => c.Capability.Usage != null)
            .OrderByDescending(c => c.Capability.Usage!.TotalExecutions)
            .Take(limit)
            .ToList();

        return recommended;
    }

    private async Task<TaskResult> ExecuteCapabilityOnInstanceAsync(
        AgentInstance instance,
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        // This would be the actual capability execution logic
        // For now, simulate a simple execution
        await Task.Delay(100, cancellationToken);

        return new TaskResult
        {
            Success = true,
            Message = "Capability executed successfully",
            DurationMs = 100,
            Data = new Dictionary<string, object>
            {
                ["result"] = "Mock capability execution result",
                ["capabilityId"] = request.CapabilityId,
                ["instanceId"] = instance.InstanceId
            }
        };
    }

    private async Task UpdateCapabilityUsageInternalAsync(
        CapabilityRegistryEntry entry,
        CapabilityExecutionResponse executionResult,
        CancellationToken cancellationToken)
    {
        var usage = entry.Capability.Usage ?? new CapabilityUsage();

        var totalExecutions = usage.TotalExecutions + 1;
        var successfulExecutions = usage.SuccessfulExecutions + (executionResult.Success ? 1 : 0);
        var failedExecutions = usage.FailedExecutions + (executionResult.Success ? 0 : 1);

        var newAverageDuration = (usage.AverageDurationMs * usage.TotalExecutions + executionResult.DurationMs) / totalExecutions;
        var errorRate = totalExecutions > 0 ? (double)failedExecutions / totalExecutions * 100 : 0;

        var updatedUsage = usage with
        {
            TotalExecutions = totalExecutions,
            SuccessfulExecutions = successfulExecutions,
            FailedExecutions = failedExecutions,
            LastExecutedAt = DateTimeOffset.UtcNow,
            AverageDurationMs = newAverageDuration,
            ErrorRate = errorRate
        };

        var updatedCapability = entry.Capability with { Usage = updatedUsage };
        var updatedEntry = entry with { Capability = updatedCapability, UpdatedAt = DateTimeOffset.UtcNow };

        var agentCapabilities = _capabilities[entry.AgentId];
        agentCapabilities.TryUpdate(entry.Capability.Id, updatedEntry, entry);

        await Task.CompletedTask;
    }

    private void UpdateCategoryIndex(string category, string capabilityId)
    {
        _categoryIndex.AddOrUpdate(category,
            new List<string> { capabilityId },
            (_, existing) =>
            {
                if (!existing.Contains(capabilityId))
                    existing.Add(capabilityId);
                return existing;
            });
    }

    private void UpdateTagIndex(string tag, string capabilityId)
    {
        _tagIndex.AddOrUpdate(tag,
            new List<string> { capabilityId },
            (_, existing) =>
            {
                if (!existing.Contains(capabilityId))
                    existing.Add(capabilityId);
                return existing;
            });
    }

    private void RemoveFromCategoryIndex(string category, string capabilityId)
    {
        if (_categoryIndex.TryGetValue(category, out var list))
        {
            list.Remove(capabilityId);
            if (list.Count == 0)
            {
                _categoryIndex.TryRemove(category, out _);
            }
        }
    }

    private void RemoveFromTagIndex(string tag, string capabilityId)
    {
        if (_tagIndex.TryGetValue(tag, out var list))
        {
            list.Remove(capabilityId);
            if (list.Count == 0)
            {
                _tagIndex.TryRemove(tag, out _);
            }
        }
    }

    private async void PeriodicHealthCheck(object? state)
    {
        try
        {
            var allCapabilities = _capabilities.Values
                .SelectMany(agentCaps => agentCaps.Values)
                .ToList();

            foreach (var entry in allCapabilities)
            {
                try
                {
                    var healthStatus = await CheckCapabilityHealthAsync(
                        entry.Capability.Id,
                        entry.AgentId,
                        entry.InstanceId);

                    if (healthStatus != entry.HealthStatus)
                    {
                        await UpdateCapabilityHealthAsync(
                            entry.Capability.Id,
                            entry.AgentId,
                            healthStatus,
                            entry.InstanceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check health for capability {CapabilityId}",
                        entry.Capability.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic health check");
        }
    }

    private void OnCapabilityRegistered(CapabilityRegistryEntry entry)
    {
        CapabilityRegistered?.Invoke(this, new CapabilityRegisteredEventArgs { Entry = entry });
    }

    private void OnCapabilityUnregistered(string capabilityId, string agentId, string? instanceId)
    {
        CapabilityUnregistered?.Invoke(this, new CapabilityUnregisteredEventArgs
        {
            CapabilityId = capabilityId,
            AgentId = agentId,
            InstanceId = instanceId
        });
    }

    private void OnCapabilityHealthChanged(string capabilityId, string agentId, string? instanceId,
        HealthStatus newHealth, HealthStatus previousHealth)
    {
        CapabilityHealthChanged?.Invoke(this, new CapabilityHealthChangedEventArgs
        {
            CapabilityId = capabilityId,
            AgentId = agentId,
            InstanceId = instanceId,
            NewHealthStatus = newHealth,
            PreviousHealthStatus = previousHealth
        });
    }

    private void OnCapabilityExecuted(CapabilityExecutionRequest request, CapabilityExecutionResponse response)
    {
        CapabilityExecuted?.Invoke(this, new CapabilityExecutedEventArgs
        {
            Request = request,
            Response = response
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _healthCheckTimer?.Dispose();
        _disposed = true;
    }
}