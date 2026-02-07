using System.Net.Http.Json;
using Hartonomous.Shared.Models;

namespace Hartonomous.Web.Services;

public class HartonomousApiClient
{
    private readonly HttpClient _httpClient;

    public HartonomousApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatCompletionResponse>();
    }

    public async Task<IngestStats?> IngestTextAsync(string text)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/ingestion/text", new IngestTextRequest { Text = text });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IngestStats>();
    }

    public async Task<AnalyzeResponse?> AnalyzeAsync(string problem)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/godel/analyze", new AnalyzeRequest { Problem = problem });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AnalyzeResponse>();
    }

    public async Task<List<ExplorerNode>> GetDomainsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<ExplorerNode>>("/api/explorer/domains") ?? [];
    }

    public async Task<List<ExplorerNode>> SearchAsync(string query)
    {
        return await _httpClient.GetFromJsonAsync<List<ExplorerNode>>($"/api/explorer/search?q={Uri.EscapeDataString(query)}") ?? [];
    }

    public async Task<ExplorerDetails?> GetDetailsAsync(string text)
    {
        var response = await _httpClient.GetAsync($"/api/explorer/details/{Uri.EscapeDataString(text)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExplorerDetails>();
    }
}

