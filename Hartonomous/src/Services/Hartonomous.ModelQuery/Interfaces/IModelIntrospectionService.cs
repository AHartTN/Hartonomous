using Hartonomous.ModelQuery.DTOs;
using Hartonomous.Core.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelIntrospectionService
{
    Task<ModelIntrospectionDto?> AnalyzeModelAsync(Guid modelId, string userId);
    Task<IEnumerable<SemanticSearchResultDto>> SemanticSearchAsync(SemanticSearchRequestDto request, string userId);
    Task<Dictionary<string, object>> GetModelStatisticsAsync(Guid modelId, string userId);
    Task<IEnumerable<string>> GetModelCapabilitiesAsync(Guid modelId, string userId);
    Task<ModelComparisonDto?> CompareModelsAsync(Guid modelAId, Guid modelBId, string comparisonType, string userId);
}