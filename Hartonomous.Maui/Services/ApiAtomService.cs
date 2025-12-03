using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using System.Net.Http.Json;

namespace Hartonomous.Maui.Services;

public class ApiAtomService : IAtomService
{
    private readonly HttpClient _httpClient;

    public ApiAtomService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AtomDto?> GetAtomByIdAsync(long id)
    {
        return await _httpClient.GetFromJsonAsync<AtomDto>($"/api/atoms/{id}");
    }

    public async Task<AtomDto?> GetAtomByHashAsync(string contentHash)
    {
        return await _httpClient.GetFromJsonAsync<AtomDto>($"/api/atoms/hash/{contentHash}");
    }

    public async Task<List<AtomDto>> GetAtomsByTypeAsync(string atomType, int skip = 0, int take = 100)
    {
        return await _httpClient.GetFromJsonAsync<List<AtomDto>>($"/api/atoms/type/{atomType}?skip={skip}&take={take}") ?? new List<AtomDto>();
    }

    public async Task<AtomDto> CreateAtomAsync(CreateAtomRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/atoms", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AtomDto>())!;
    }

    public async Task<bool> DeleteAtomAsync(long id)
    {
        var response = await _httpClient.DeleteAsync($"/api/atoms/{id}");
        return response.IsSuccessStatusCode;
    }
}
