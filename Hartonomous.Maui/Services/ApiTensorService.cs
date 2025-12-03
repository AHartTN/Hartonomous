using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using System.Net.Http.Json;

namespace Hartonomous.Maui.Services;

public class ApiTensorService : ITensorService
{
    private readonly HttpClient _httpClient;

    public ApiTensorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TensorChunkDto?> GetChunkByIdAsync(long id)
    {
        return await _httpClient.GetFromJsonAsync<TensorChunkDto>($"/api/tensors/{id}");
    }

    public async Task<List<TensorChunkDto>> GetChunksByTensorNameAsync(string tensorName, int skip = 0, int take = 100)
    {
        return await _httpClient.GetFromJsonAsync<List<TensorChunkDto>>($"/api/tensors/tensor/{tensorName}?skip={skip}&take={take}") ?? new List<TensorChunkDto>();
    }

    public async Task<TensorChunkDto> CreateChunkAsync(CreateTensorChunkRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/tensors", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TensorChunkDto>())!;
    }

    public async Task<List<TensorChunkDto>> SearchSimilarChunksAsync(TensorSearchRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/tensors/search", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<TensorChunkDto>>()) ?? new List<TensorChunkDto>();
    }
}
