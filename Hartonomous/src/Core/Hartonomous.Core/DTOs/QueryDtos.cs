namespace Hartonomous.Core.DTOs;

public record QueryRequest(string SemanticQuery, Guid ProjectId);
public record ComponentDto(Guid ComponentId, string ComponentName, string ComponentType);
public record QueryResponse(List<ComponentDto> Components);

/// <summary>
/// Semantic search request for model introspection
/// </summary>
public record SemanticSearchRequestDto(
    string Query,
    Guid ProjectId,
    string? ComponentType = null,
    int MaxResults = 10,
    string SearchType = "all",
    double SimilarityThreshold = 0.7
);

/// <summary>
/// Semantic search result with similarity scoring
/// </summary>
public record SemanticSearchResultDto(
    Guid ItemId,
    string ItemType,
    string Name,
    double Similarity,
    Dictionary<string, object>? Metadata = null,
    string? Description = null
)
{
    public Guid Id => ItemId;
    public string Type => ItemType;
    public double SimilarityScore => Similarity;
    public Guid ModelId => Metadata?.ContainsKey("modelId") == true ?
        Guid.Parse(Metadata["modelId"].ToString()!) : Guid.Empty;
    public string ModelName => Metadata?.ContainsKey("modelName") == true ?
        Metadata["modelName"].ToString()! : string.Empty;
};