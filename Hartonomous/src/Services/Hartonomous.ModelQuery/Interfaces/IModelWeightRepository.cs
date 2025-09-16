using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelWeightRepository
{
    Task<IEnumerable<ModelWeightDto>> GetModelWeightsAsync(Guid modelId, string userId);
    Task<ModelWeightDto?> GetWeightByIdAsync(Guid weightId, string userId);
    Task<IEnumerable<ModelWeightDto>> GetWeightsByLayerAsync(Guid modelId, string layerName, string userId);
    Task<Guid> CreateWeightAsync(Guid modelId, string layerName, string weightName, string dataType, int[] shape, long sizeBytes, string storagePath, string checksumSha256, string userId);
    Task<bool> DeleteWeightAsync(Guid weightId, string userId);
    Task<bool> UpdateWeightStoragePathAsync(Guid weightId, string newStoragePath, string userId);
    Task<Stream?> GetWeightDataStreamAsync(Guid weightId, string userId);
    Task<bool> StoreWeightDataAsync(Guid weightId, Stream dataStream, string userId);
}