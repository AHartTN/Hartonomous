using Hartonomous.Core.DTOs;

namespace Hartonomous.Core.Interfaces;

public interface IModelRepository
{
    Task<IEnumerable<ModelMetadataDto>> GetModelsByProjectAsync(Guid projectId, string userId);
    Task<ModelMetadataDto?> GetModelByIdAsync(Guid modelId, string userId);
    Task<Guid> CreateModelAsync(Guid projectId, string modelName, string version, string license, string? metadataJson, string userId);
    Task<bool> DeleteModelAsync(Guid modelId, string userId);
}