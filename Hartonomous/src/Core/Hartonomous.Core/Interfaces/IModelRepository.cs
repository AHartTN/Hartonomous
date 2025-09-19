/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model repository interface for Model Query Engine (MQE) operations.
 * Features AI model lifecycle management, weight storage, similarity analysis, and performance tracking.
 */

using Hartonomous.Core.DTOs;
using Hartonomous.Core.Models;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Specialized repository interface for Model entities
/// Extends generic repository with model-specific operations
/// </summary>
public interface IModelRepository : IRepository<Model>
{
    // Legacy DTO-based methods
    Task<IEnumerable<ModelMetadataDto>> GetModelsByProjectAsync(Guid projectId, string userId);
    Task<ModelMetadataDto?> GetModelByIdAsync(Guid modelId, string userId);
    Task<Guid> CreateModelAsync(Guid projectId, string modelName, string version, string license, string? metadataJson, string userId);
    Task<bool> DeleteModelAsync(Guid modelId, string userId);

    // Model-specific queries
    Task<IEnumerable<Model>> GetModelsByArchitectureAsync(string architecture, string userId);
    Task<IEnumerable<Model>> GetModelsByStatusAsync(ModelStatus status, string userId);
    Task<Model?> GetModelByNameAsync(string modelName, string userId);
    Task<IEnumerable<Model>> GetModelsWithEmbeddingsAsync(string userId);
    Task<IEnumerable<Model>> GetRecentModelsAsync(int count, string userId);

    // Model analysis operations
    Task<IEnumerable<Model>> FindSimilarModelsAsync(Guid modelId, string userId, double threshold = 0.8);
    Task<Dictionary<string, int>> GetArchitectureStatsAsync(string userId);
    Task<Dictionary<ModelStatus, int>> GetStatusStatsAsync(string userId);

    // Model weight operations
    Task<byte[]?> GetModelWeightsAsync(Guid modelId, string userId);
    Task UpdateModelWeightsAsync(Guid modelId, byte[] weights, string userId);

    // Performance tracking
    Task<IEnumerable<ModelPerformanceMetric>> GetModelPerformanceAsync(Guid modelId, string userId);
    Task<double> GetAveragePerformanceAsync(Guid modelId, string metricName, string userId);
}