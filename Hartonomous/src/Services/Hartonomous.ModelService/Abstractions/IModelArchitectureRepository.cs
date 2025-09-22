/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model architecture repository interface for Model Query Engine (MQE).
 * Features architectural analysis, layer management, and configuration tracking for AI model structures.
 */

using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelArchitectureRepository
{
    Task<ModelArchitectureDto?> GetModelArchitectureAsync(Guid modelId, string userId);
    Task<IEnumerable<ModelLayerDto>> GetModelLayersAsync(Guid modelId, string userId);
    Task<ModelLayerDto?> GetLayerByIdAsync(Guid layerId, string userId);
    Task<Guid> CreateLayerAsync(Guid modelId, string layerName, string layerType, int layerIndex, Dictionary<string, object> configuration, string userId);
    Task<bool> UpdateLayerConfigurationAsync(Guid layerId, Dictionary<string, object> configuration, string userId);
    Task<bool> DeleteLayerAsync(Guid layerId, string userId);
    Task<Guid> CreateArchitectureAsync(Guid modelId, string architectureName, string framework, Dictionary<string, object> configuration, Dictionary<string, object> hyperparameters, string userId);
    Task<bool> UpdateArchitectureAsync(Guid modelId, string architectureName, string framework, Dictionary<string, object> configuration, Dictionary<string, object> hyperparameters, string userId);
}