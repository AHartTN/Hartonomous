/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model entity wrapper for repository pattern compliance.
 * Provides IEntityBase implementation for the existing Model model.
 */

using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Models;

namespace Hartonomous.Core.Entities;

/// <summary>
/// Model entity wrapper that implements IEntityBase for repository pattern
/// Maps to the existing Model model structure
/// </summary>
public class ModelEntity : IEntityBase<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // Model-specific properties
    public string ModelName { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public int HiddenSize { get; set; }
    public int NumLayers { get; set; }
    public int NumAttentionHeads { get; set; }
    public int VocabSize { get; set; }
    public byte[]? ModelWeights { get; set; }
    public string ConfigMetadata { get; set; } = "{}";
    public string? ModelPath { get; set; }
    public ModelStatus Status { get; set; } = ModelStatus.Uploading;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// Convert from Model model to ModelEntity
    /// </summary>
    public static ModelEntity FromModel(Model model)
    {
        return new ModelEntity
        {
            Id = model.ModelId,
            UserId = model.UserId,
            CreatedDate = model.IngestedAt,
            ModifiedDate = model.ProcessedAt,
            ModelName = model.ModelName,
            Architecture = model.Architecture,
            ParameterCount = model.ParameterCount,
            HiddenSize = model.HiddenSize,
            NumLayers = model.NumLayers,
            NumAttentionHeads = model.NumAttentionHeads,
            VocabSize = model.VocabSize,
            ModelWeights = model.ModelWeights,
            ConfigMetadata = model.ConfigMetadata,
            ModelPath = model.ModelPath,
            Status = model.Status,
            ProcessedAt = model.ProcessedAt,
            LastAccessedAt = model.LastAccessedAt
        };
    }

    /// <summary>
    /// Convert from ModelEntity to Model model
    /// </summary>
    public Model ToModel()
    {
        return new Model
        {
            ModelId = Id,
            UserId = UserId,
            IngestedAt = CreatedDate,
            ProcessedAt = ModifiedDate,
            ModelName = ModelName,
            Architecture = Architecture,
            ParameterCount = ParameterCount,
            HiddenSize = HiddenSize,
            NumLayers = NumLayers,
            NumAttentionHeads = NumAttentionHeads,
            VocabSize = VocabSize,
            ModelWeights = ModelWeights,
            ConfigMetadata = ConfigMetadata,
            ModelPath = ModelPath,
            Status = Status,
            LastAccessedAt = LastAccessedAt
        };
    }
}