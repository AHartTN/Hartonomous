/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the core Model entity for AI-native NoSQL storage in NinaDB,
 * representing large language models with FILESTREAM support and Model Query Engine integration.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents a large language model stored in NinaDB
/// Core entity for the Model Query Engine
/// </summary>
public class Model
{
    public Guid ModelId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string ModelName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Architecture { get; set; } = string.Empty; // transformer, mamba, mixture-of-experts

    public long ParameterCount { get; set; }
    public int HiddenSize { get; set; }
    public int NumLayers { get; set; }
    public int NumAttentionHeads { get; set; }
    public int VocabSize { get; set; }

    /// <summary>
    /// FILESTREAM storage for the raw model weights
    /// Enables memory-mapped access via SQL CLR
    /// </summary>
    public byte[]? ModelWeights { get; set; }

    /// <summary>
    /// JSON metadata about the model configuration
    /// Leverages SQL Server 2025 native JSON capabilities
    /// </summary>
    public string ConfigMetadata { get; set; } = "{}";

    /// <summary>
    /// Model file path for external storage reference
    /// </summary>
    [MaxLength(500)]
    public string? ModelPath { get; set; }

    /// <summary>
    /// Current processing status of the model
    /// </summary>
    public ModelStatus Status { get; set; } = ModelStatus.Uploading;

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }

    // Navigation properties
    public virtual ICollection<ModelLayer> Layers { get; set; } = new List<ModelLayer>();
    public virtual ICollection<ModelEmbedding> Embeddings { get; set; } = new List<ModelEmbedding>();
    public virtual ICollection<ModelPerformanceMetric> PerformanceMetrics { get; set; } = new List<ModelPerformanceMetric>();
    public virtual ICollection<ProjectModel> ProjectModels { get; set; } = new List<ProjectModel>();

    /// <summary>
    /// Get typed configuration from JSON metadata
    /// </summary>
    public T? GetConfiguration<T>() where T : class
    {
        if (string.IsNullOrEmpty(ConfigMetadata) || ConfigMetadata == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ConfigMetadata);
    }

    /// <summary>
    /// Set typed configuration to JSON metadata
    /// </summary>
    public void SetConfiguration<T>(T configuration) where T : class
    {
        ConfigMetadata = JsonSerializer.Serialize(configuration);
    }
}

public enum ModelStatus
{
    Uploading,
    Processing,
    Analyzing,
    Ready,
    Error,
    Archived
}