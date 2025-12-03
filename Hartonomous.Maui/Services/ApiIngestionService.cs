using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using System.Net.Http.Json;

namespace Hartonomous.Maui.Services;

public class ApiIngestionService : IIngestionService
{
    private readonly HttpClient _httpClient;

    public ApiIngestionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IngestionJobDto> CreateJobAsync(CreateIngestionJobRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/ingestion/jobs", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IngestionJobDto>())!;
    }

    public async Task<IngestionJobDto?> GetJobByIdAsync(long id)
    {
        return await _httpClient.GetFromJsonAsync<IngestionJobDto>($"/api/ingestion/jobs/{id}");
    }

    public async Task<List<IngestionJobDto>> GetActiveJobsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<IngestionJobDto>>("/api/ingestion/jobs/active") ?? new List<IngestionJobDto>();
    }

    public async Task UpdateJobStatusAsync(IngestionJobStatusUpdate update)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/ingestion/jobs/{update.JobId}/status", update);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> CancelJobAsync(long id)
    {
        var response = await _httpClient.PostAsync($"/api/ingestion/jobs/{id}/cancel", null);
        return response.IsSuccessStatusCode;
    }
}
