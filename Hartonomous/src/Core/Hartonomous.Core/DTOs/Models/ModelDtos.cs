/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 */

namespace Hartonomous.Core.DTOs.Models;

/// <summary>
/// Model weight data transfer object
/// </summary>
public record ModelWeightDto(
    Guid WeightId,
    Guid ModelId,
    string LayerName,
    string WeightName,
    string DataType,
    int[] Shape,
    long SizeBytes,
    string StoragePath,
    string ChecksumSha256,
    DateTime CreatedAt);

/// <summary>
/// Model layer data transfer object with associated weights
/// </summary>
public record ModelLayerDto(
    Guid LayerId,
    Guid ModelId,
    string LayerName,
    string LayerType,
    int LayerIndex,
    Dictionary<string, object> Configuration,
    List<ModelWeightDto> Weights,
    DateTime CreatedAt);

/// <summary>
/// Model architecture data transfer object
/// </summary>
public record ModelArchitectureDto(
    Guid ModelId,
    string ArchitectureName,
    string Framework,
    List<ModelLayerDto> Layers,
    Dictionary<string, object> Configuration,
    Dictionary<string, object> Hyperparameters,
    DateTime CreatedAt);

/// <summary>
/// Model version information
/// </summary>
public record ModelVersionDto(
    Guid VersionId,
    Guid ModelId,
    string Version,
    string Description,
    Dictionary<string, object> Changes,
    string? ParentVersion,
    DateTime CreatedAt,
    string CreatedBy);

/// <summary>
/// Model introspection analysis results
/// </summary>
public record ModelIntrospectionDto(
    Guid ModelId,
    string ModelName,
    long TotalParameters,
    long TrainableParameters,
    double ModelSizeMB,
    Dictionary<string, int> LayerTypeCount,
    Dictionary<string, object> Statistics,
    List<string> Capabilities,
    DateTime AnalyzedAt);

/// <summary>
/// Model comparison analysis results
/// </summary>
public record ModelComparisonDto(
    Guid ModelAId,
    Guid ModelBId,
    string ComparisonType,
    Dictionary<string, object> Differences,
    Dictionary<string, double> SimilarityMetrics,
    List<string> CommonLayers,
    List<string> UniqueLayers,
    DateTime ComparedAt);

/// <summary>
/// Semantic search request
/// </summary>
public record SemanticSearchRequestDto(
    string Query,
    string SearchType,
    int MaxResults = 10,
    double SimilarityThreshold = 0.7,
    Dictionary<string, object>? Filters = null);

/// <summary>
/// Semantic search result
/// </summary>
public record SemanticSearchResultDto(
    Guid ItemId,
    string ItemType,
    string Name,
    double SimilarityScore,
    Dictionary<string, object> Properties,
    string? Description);