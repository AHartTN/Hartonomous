using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelVersionRepository
{
    Task<IEnumerable<ModelVersionDto>> GetModelVersionsAsync(Guid modelId, string userId);
    Task<ModelVersionDto?> GetVersionByIdAsync(Guid versionId, string userId);
    Task<ModelVersionDto?> GetLatestVersionAsync(Guid modelId, string userId);
    Task<Guid> CreateVersionAsync(Guid modelId, string version, string description, Dictionary<string, object> changes, string? parentVersion, string userId);
    Task<bool> DeleteVersionAsync(Guid versionId, string userId);
    Task<ModelComparisonDto?> CompareVersionsAsync(Guid versionAId, Guid versionBId, string userId);
}