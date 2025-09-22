/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 */

using Microsoft.EntityFrameworkCore;
using Hartonomous.Core.Data;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Models;
using Hartonomous.Core.Enums;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Distilled agent repository implementation with deployment and capability tracking
/// </summary>
public class DistilledAgentRepository : Repository<DistilledAgent>, IDistilledAgentRepository
{
    public DistilledAgentRepository(HartonomousDbContext context) : base(context) { }

    public async Task<IEnumerable<DistilledAgent>> GetAgentsByDomainAsync(string domain, string userId)
    {
        return await _dbSet
            .Where(a => a.Domain == domain && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<DistilledAgent>> GetAgentsByStatusAsync(AgentStatus status, string userId)
    {
        return await _dbSet
            .Where(a => a.Status == status && a.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<DistilledAgent>> SearchAgentsByCapabilityAsync(string capability, string userId)
    {
        return await _dbSet
            .Where(a => a.UserId == userId)
            .Include(a => a.AgentCapabilities)
            .Where(a => a.AgentCapabilities.Any(ac => ac.CapabilityName.Contains(capability)))
            .ToListAsync();
    }

    public async Task<DistilledAgent?> GetAgentByNameAsync(string agentName, string userId)
    {
        return await _dbSet
            .Where(a => a.AgentName == agentName && a.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<DistilledAgent>> GetDeployedAgentsAsync(string userId)
    {
        return await _dbSet
            .Where(a => a.Status == AgentStatus.Deployed && a.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<DistilledAgent>> GetAgentsReadyForDeploymentAsync(string userId)
    {
        return await _dbSet
            .Where(a => a.Status == AgentStatus.Ready && a.UserId == userId)
            .ToListAsync();
    }

    public async Task UpdateAgentStatusAsync(Guid agentId, AgentStatus status, string userId)
    {
        var agent = await GetByIdAsync(agentId, userId);
        if (agent != null)
        {
            agent.Status = status;
            agent.LastUpdated = DateTime.UtcNow;
            await UpdateAsync(agent);
        }
    }

    public async Task RecordAgentAccessAsync(Guid agentId, string userId)
    {
        var agent = await GetByIdAsync(agentId, userId);
        if (agent != null)
        {
            agent.LastAccessedAt = DateTime.UtcNow;
            await UpdateAsync(agent);
        }
    }

    public async Task<Dictionary<string, int>> GetDomainStatsAsync(string userId)
    {
        return await _dbSet
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.Domain)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<Dictionary<AgentStatus, int>> GetStatusStatsAsync(string userId)
    {
        return await _dbSet
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.Status)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<IEnumerable<DistilledAgent>> GetTopPerformingAgentsAsync(int count, string userId)
    {
        return await _dbSet
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.LastAccessedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<IEnumerable<ModelComponent>> GetAgentComponentsAsync(Guid agentId, string userId)
    {
        return await _context.AgentComponents
            .Where(ac => ac.AgentId == agentId && ac.UserId == userId)
            .Include(ac => ac.ModelComponent)
            .Select(ac => ac.ModelComponent)
            .ToListAsync();
    }

    public async Task<IEnumerable<AgentCapability>> GetAgentCapabilitiesAsync(Guid agentId, string userId)
    {
        return await _context.AgentCapabilities
            .Where(ac => ac.AgentId == agentId && ac.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<DistilledAgent>> GetAgentsBySourceModelAsync(Guid modelId, string userId)
    {
        return await _dbSet
            .Where(a => a.UserId == userId)
            .Include(a => a.AgentComponents)
            .Where(a => a.AgentComponents.Any(ac => ac.ModelId == modelId))
            .ToListAsync();
    }

    public async Task UpdateDeploymentConfigAsync(Guid agentId, string deploymentConfig, string userId)
    {
        var agent = await GetByIdAsync(agentId, userId);
        if (agent != null)
        {
            agent.SetDeploymentConfig(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(deploymentConfig) ?? new Dictionary<string, object>());
            await UpdateAsync(agent);
        }
    }

    public async Task RecordPerformanceMetricAsync(Guid agentId, string metricName, double value, string userId)
    {
        // Implementation would record performance metrics
        // For now, just update the last accessed time
        await RecordAgentAccessAsync(agentId, userId);
    }
}