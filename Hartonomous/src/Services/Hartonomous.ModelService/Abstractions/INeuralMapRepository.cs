/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the neural map repository interface for Model Query Engine (MQE).
 * Features Neo4j neural topology mapping, graph operations, and neural network relationship management.
 */

using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface INeuralMapRepository
{
    Task<NeuralMapGraphDto?> GetModelGraphAsync(Guid modelId, string userId);
    Task<IEnumerable<NeuralMapNodeDto>> GetNodesAsync(Guid modelId, string userId);
    Task<IEnumerable<NeuralMapEdgeDto>> GetEdgesAsync(Guid modelId, string userId);
    Task<Guid> CreateNodeAsync(Guid modelId, string nodeType, string name, Dictionary<string, object> properties, string userId);
    Task<Guid> CreateEdgeAsync(Guid sourceNodeId, Guid targetNodeId, string relationType, double weight, Dictionary<string, object> properties, string userId);
    Task<bool> DeleteNodeAsync(Guid nodeId, string userId);
    Task<bool> DeleteEdgeAsync(Guid edgeId, string userId);
    Task<bool> UpdateNodePropertiesAsync(Guid nodeId, Dictionary<string, object> properties, string userId);
    Task<bool> UpdateEdgePropertiesAsync(Guid edgeId, Dictionary<string, object> properties, string userId);
}