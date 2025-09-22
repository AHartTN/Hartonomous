/*
 * Copyright (c) 2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains DTOs for MCP Vector Search operations with SQL Server 2025 VECTOR integration.
 */

using Hartonomous.Core.Enums;

namespace Hartonomous.MCP.DTOs;

/// <summary>
/// Vector search result DTO for MCP communication
/// </summary>
public class VectorSearchResultDto
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double SimilarityScore { get; set; }
    public double Distance { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Agent search result DTO for MCP communication
/// </summary>
public class AgentSearchResultDto
{
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public string? Description { get; set; }
    public AgentStatus Status { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Model component analysis result DTO
/// </summary>
public class ModelComponentAnalysisDto
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public double SimilarityScore { get; set; }
    public double VectorDistance { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Embedding storage DTO for batch operations
/// </summary>
public class EmbeddingStorageDto
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public float[] EmbeddingVector { get; set; } = Array.Empty<float>();
}