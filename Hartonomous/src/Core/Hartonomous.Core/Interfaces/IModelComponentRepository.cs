/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model component repository interface for mechanistic interpretability operations.
 * Features component analysis, similarity detection, neural interpretation, and weight analysis algorithms.
 */

using Hartonomous.Core.Models;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Specialized repository interface for ModelComponent entities
/// Supports mechanistic interpretability and component analysis
/// </summary>
public interface IModelComponentRepository : IRepository<ModelComponent>
{
    // Component discovery and analysis
    Task<IEnumerable<ModelComponent>> GetComponentsByModelAsync(Guid modelId, string userId);
    Task<IEnumerable<ModelComponent>> GetComponentsByLayerAsync(Guid layerId, string userId);
    Task<IEnumerable<ModelComponent>> GetComponentsByTypeAsync(string componentType, string userId);
    Task<IEnumerable<ModelComponent>> GetHighRelevanceComponentsAsync(Guid modelId, double threshold, string userId);

    // Similarity and clustering
    Task<IEnumerable<ModelComponent>> FindSimilarComponentsAsync(Guid componentId, string userId, double threshold = 0.8);
    Task<IEnumerable<ModelComponent>> GetComponentsWithEmbeddingsAsync(Guid modelId, string userId);

    // Interpretability operations
    Task<IEnumerable<NeuronInterpretation>> GetComponentInterpretationsAsync(Guid componentId, string userId);
    Task<IEnumerable<ActivationPattern>> GetComponentActivationPatternsAsync(Guid componentId, string userId);
    Task UpdateRelevanceScoreAsync(Guid componentId, double score, string userId);
    Task UpdateFunctionalDescriptionAsync(Guid componentId, string description, string userId);

    // Weight analysis
    Task<IEnumerable<ComponentWeight>> GetCriticalWeightsAsync(Guid componentId, string userId);
    Task<IEnumerable<ComponentWeight>> GetWeightsByImportanceAsync(Guid componentId, double threshold, string userId);

    // Component relationships
    Task<IEnumerable<AgentComponent>> GetAgentUsagesAsync(Guid componentId, string userId);
    Task<Dictionary<string, int>> GetComponentTypeStatsAsync(Guid modelId, string userId);
    Task<IEnumerable<ModelComponent>> GetComponentsByFunctionAsync(string functionKeyword, string userId);
}