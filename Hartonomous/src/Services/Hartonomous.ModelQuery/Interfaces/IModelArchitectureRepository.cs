using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelArchitectureRepository
{
    Task<ModelArchitectureDto?> GetModelArchitectureAsync(Guid modelId, string userId);
    Task<IEnumerable<ModelLayerDto>> GetModelLayersAsync(Guid modelId, string userId);
    Task<ModelLayerDto?> GetLayerByIdAsync(Guid layerId, string userId);
    Task<Guid> CreateLayerAsync(Guid modelId, string layerName, string layerType, int layerIndex, Dictionary<string, object> configuration, string userId);
    Task<bool> UpdateLayerConfigurationAsync(Guid layerId, Dictionary<string, object> configuration, string userId);
    Task<bool> DeleteLayerAsync(Guid layerId, string userId);
    Task<Guid> CreateArchitectureAsync(Guid modelId, string architectureName, string framework, Dictionary<string, object> configuration, Dictionary<string, object> hyperparameters, string userId);
    Task<bool> UpdateArchitectureAsync(Guid modelId, string architectureName, string framework, Dictionary<string, object> configuration, Dictionary<string, object> hyperparameters, string userId);
}