namespace Hartonomous.ModelQuery.DTOs;

public record NeuralMapNodeDto(
    Guid NodeId,
    string NodeType,
    string Name,
    Dictionary<string, object> Properties,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record NeuralMapEdgeDto(
    Guid EdgeId,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string RelationType,
    double Weight,
    Dictionary<string, object> Properties,
    DateTime CreatedAt
);

public record NeuralMapGraphDto(
    Guid ModelId,
    string ModelName,
    string Version,
    List<NeuralMapNodeDto> Nodes,
    List<NeuralMapEdgeDto> Edges,
    Dictionary<string, object> Metadata
);

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
    DateTime CreatedAt
);

public record ModelLayerDto(
    Guid LayerId,
    Guid ModelId,
    string LayerName,
    string LayerType,
    int LayerIndex,
    Dictionary<string, object> Configuration,
    List<ModelWeightDto> Weights,
    DateTime CreatedAt
);

public record ModelArchitectureDto(
    Guid ModelId,
    string ArchitectureName,
    string Framework,
    List<ModelLayerDto> Layers,
    Dictionary<string, object> Configuration,
    Dictionary<string, object> Hyperparameters,
    DateTime CreatedAt
);

public record SemanticSearchRequestDto(
    string Query,
    string SearchType,
    int MaxResults = 10,
    double SimilarityThreshold = 0.7,
    Dictionary<string, object>? Filters = null
);

public record SemanticSearchResultDto(
    Guid ItemId,
    string ItemType,
    string Name,
    double SimilarityScore,
    Dictionary<string, object> Properties,
    string? Description
);

public record ModelIntrospectionDto(
    Guid ModelId,
    string ModelName,
    long TotalParameters,
    long TrainableParameters,
    double ModelSizeMB,
    Dictionary<string, int> LayerTypeCount,
    Dictionary<string, object> Statistics,
    List<string> Capabilities,
    DateTime AnalyzedAt
);

public record ModelComparisonDto(
    Guid ModelAId,
    Guid ModelBId,
    string ComparisonType,
    Dictionary<string, object> Differences,
    Dictionary<string, double> SimilarityMetrics,
    List<string> CommonLayers,
    List<string> UniqueLayers,
    DateTime ComparedAt
);

public record ModelVersionDto(
    Guid VersionId,
    Guid ModelId,
    string Version,
    string Description,
    Dictionary<string, object> Changes,
    string? ParentVersion,
    DateTime CreatedAt,
    string CreatedBy
);