/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 */

namespace Hartonomous.Core.DTOs.Models;

/// <summary>
/// Neural map node representing a component in the graph
/// </summary>
public record NeuralMapNodeDto(
    Guid NodeId,
    string NodeType,
    string Name,
    Dictionary<string, object> Properties,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Neural map edge representing relationships between components
/// </summary>
public record NeuralMapEdgeDto(
    Guid EdgeId,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string RelationType,
    double Weight,
    Dictionary<string, object> Properties,
    DateTime CreatedAt);

/// <summary>
/// Complete neural map graph with nodes and edges
/// </summary>
public record NeuralMapGraphDto(
    Guid ModelId,
    string ModelName,
    string Version,
    List<NeuralMapNodeDto> Nodes,
    List<NeuralMapEdgeDto> Edges,
    Dictionary<string, object> Metadata);