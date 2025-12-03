using Hartonomous.Shared.Models;

namespace Hartonomous.Shared.Interfaces;

public interface ITensorService
{
    Task<TensorChunkDto?> GetChunkByIdAsync(long id);
    Task<List<TensorChunkDto>> GetChunksByTensorNameAsync(string tensorName, int skip = 0, int take = 100);
    Task<TensorChunkDto> CreateChunkAsync(CreateTensorChunkRequest request);
    Task<List<TensorChunkDto>> SearchSimilarChunksAsync(TensorSearchRequest request);
}
