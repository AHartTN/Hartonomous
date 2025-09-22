/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 */

using Microsoft.EntityFrameworkCore;
using Hartonomous.Core.Data;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Models;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Model component repository implementation with mechanistic interpretability support
/// </summary>
public class ModelComponentRepository : Repository<ModelComponent>, IModelComponentRepository
{
    public ModelComponentRepository(HartonomousDbContext context) : base(context) { }

    public async Task<IEnumerable<ModelComponent>> GetComponentsByModelAsync(Guid modelId, string userId)
    {
        return await _dbSet
            .Where(c => c.ModelId == modelId && c.UserId == userId)
            .Include(c => c.Layer)
            .ToListAsync();
    }

    public async Task<IEnumerable<ModelComponent>> GetComponentsByLayerAsync(Guid layerId, string userId)
    {
        return await _dbSet
            .Where(c => c.LayerId == layerId && c.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<ModelComponent>> GetComponentsByTypeAsync(string componentType, string userId)
    {
        return await _dbSet
            .Where(c => c.ComponentType == componentType && c.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<ModelComponent>> GetHighRelevanceComponentsAsync(Guid modelId, double threshold, string userId)
    {
        return await _dbSet
            .Where(c => c.ModelId == modelId && c.UserId == userId && c.RelevanceScore >= threshold)
            .OrderByDescending(c => c.RelevanceScore)
            .ToListAsync();
    }

    public async Task<IEnumerable<ModelComponent>> FindSimilarComponentsAsync(Guid componentId, string userId, double threshold = 0.8)
    {
        // Implementation would use embeddings for similarity search
        var component = await GetByIdAsync(componentId, userId);
        if (component == null) return Enumerable.Empty<ModelComponent>();

        return await _dbSet
            .Where(c => c.ComponentType == component.ComponentType &&
                       c.UserId == userId &&
                       c.ComponentId != componentId)
            .ToListAsync();
    }

    public async Task<IEnumerable<ModelComponent>> GetComponentsWithEmbeddingsAsync(Guid modelId, string userId)
    {
        return await _dbSet
            .Where(c => c.ModelId == modelId && c.UserId == userId)
            .Include(c => c.Embeddings)
            .Where(c => c.Embeddings.Any())
            .ToListAsync();
    }

    public async Task<IEnumerable<NeuronInterpretation>> GetComponentInterpretationsAsync(Guid componentId, string userId)
    {
        return await _context.NeuronInterpretations
            .Where(ni => ni.ComponentId == componentId && ni.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<ActivationPattern>> GetComponentActivationPatternsAsync(Guid componentId, string userId)
    {
        return await _context.ActivationPatterns
            .Where(ap => ap.ComponentId == componentId && ap.UserId == userId)
            .ToListAsync();
    }

    public async Task UpdateRelevanceScoreAsync(Guid componentId, double score, string userId)
    {
        var component = await GetByIdAsync(componentId, userId);
        if (component != null)
        {
            component.RelevanceScore = score;
            await UpdateAsync(component);
        }
    }

    public async Task UpdateFunctionalDescriptionAsync(Guid componentId, string description, string userId)
    {
        var component = await GetByIdAsync(componentId, userId);
        if (component != null)
        {
            component.FunctionalDescription = description;
            await UpdateAsync(component);
        }
    }

    public async Task<IEnumerable<ComponentWeight>> GetCriticalWeightsAsync(Guid componentId, string userId)
    {
        return await _context.ComponentWeights
            .Where(cw => cw.ComponentId == componentId && cw.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<ComponentWeight>> GetWeightsByImportanceAsync(Guid componentId, double threshold, string userId)
    {
        return await _context.ComponentWeights
            .Where(cw => cw.ComponentId == componentId &&
                        cw.UserId == userId)
            .ToListAsync();
    }

    public async Task<IEnumerable<AgentComponent>> GetAgentUsagesAsync(Guid componentId, string userId)
    {
        return await _context.AgentComponents
            .Where(ac => ac.ModelComponentId == componentId && ac.UserId == userId)
            .Include(ac => ac.Agent)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetComponentTypeStatsAsync(Guid modelId, string userId)
    {
        return await _dbSet
            .Where(c => c.ModelId == modelId && c.UserId == userId)
            .GroupBy(c => c.ComponentType)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<IEnumerable<ModelComponent>> GetComponentsByFunctionAsync(string functionKeyword, string userId)
    {
        return await _dbSet
            .Where(c => c.UserId == userId &&
                       (c.FunctionalDescription.Contains(functionKeyword) ||
                        c.ComponentName.Contains(functionKeyword)))
            .ToListAsync();
    }
}