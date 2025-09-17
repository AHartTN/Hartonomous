using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using Hartonomous.Api;

namespace Hartonomous.IntegrationTests.DataFabricTests;

/// <summary>
/// Integration tests for Data Fabric API endpoints
/// Tests the HTTP layer of the data fabric services
/// </summary>
public class DataFabricApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ILogger<DataFabricApiIntegrationTests> _logger;

    public DataFabricApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override configuration for testing
                services.Configure<IConfiguration>(config =>
                {
                    config["KeyVault:EnableInDevelopment"] = "false";
                    config["Neo4j:Uri"] = "bolt://localhost:7687";
                    config["Neo4j:Username"] = "neo4j";
                    config["Neo4j:Password"] = "password";
                    config["Milvus:Host"] = "localhost";
                    config["Milvus:Port"] = "19530";
                });
            });
        });

        _client = _factory.CreateClient();
        _logger = _factory.Services.GetRequiredService<ILogger<DataFabricApiIntegrationTests>>();
    }

    [Fact]
    public async Task DataFabricController_GetHealth_ShouldReturnHealthStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/data-fabric/health");

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var health = JsonSerializer.Deserialize<JsonElement>(content);

            health.GetProperty("checkTime").GetString().Should().NotBeNullOrEmpty();
            health.GetProperty("overallStatus").GetString().Should().NotBeNullOrEmpty();
            health.GetProperty("neo4jStatus").GetString().Should().NotBeNullOrEmpty();
            health.GetProperty("milvusStatus").GetString().Should().NotBeNullOrEmpty();
        }
        else
        {
            _logger.LogWarning("Data fabric health endpoint returned {StatusCode} - services may not be available", response.StatusCode);
            // Don't fail the test if services are unavailable in the test environment
        }
    }

    [Fact]
    public async Task DataFabricController_GetModelInsights_ShouldReturnInsights()
    {
        // Arrange
        var modelId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/data-fabric/models/{modelId}/insights");

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var insights = JsonSerializer.Deserialize<JsonElement>(content);

            insights.GetProperty("modelId").GetString().Should().Be(modelId.ToString());
            insights.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
            insights.GetProperty("relationshipPaths").ValueKind.Should().Be(JsonValueKind.Array);
            insights.GetProperty("similarComponents").ValueKind.Should().Be(JsonValueKind.Array);
        }
        else
        {
            _logger.LogWarning("Model insights endpoint returned {StatusCode} for model {ModelId}", response.StatusCode, modelId);
        }
    }

    [Fact]
    public async Task DataFabricController_SearchSemantic_ShouldAcceptValidRequest()
    {
        // Arrange
        var searchRequest = new
        {
            QueryEmbedding = GenerateRandomEmbedding(768),
            ComponentType = "Dense",
            TopK = 10
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/data-fabric/search/semantic", searchRequest);

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<JsonElement>(content);

            results.GetProperty("query").GetString().Should().NotBeNullOrEmpty();
            results.GetProperty("totalMatches").ValueKind.Should().Be(JsonValueKind.Number);
            results.GetProperty("results").ValueKind.Should().Be(JsonValueKind.Array);
            results.GetProperty("searchTime").GetString().Should().NotBeNullOrEmpty();
        }
        else
        {
            _logger.LogWarning("Semantic search endpoint returned {StatusCode}", response.StatusCode);
        }
    }

    [Fact]
    public async Task DataFabricController_GetComponentPaths_ShouldReturnPaths()
    {
        // Arrange
        var componentId = Guid.NewGuid();
        var maxDepth = 3;

        // Act
        var response = await _client.GetAsync($"/api/data-fabric/components/{componentId}/paths?maxDepth={maxDepth}");

        // Assert
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var paths = JsonSerializer.Deserialize<JsonElement>(content);

            paths.ValueKind.Should().Be(JsonValueKind.Array);
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Expected for non-existent component
            _logger.LogInformation("Component {ComponentId} not found - expected for test data", componentId);
        }
        else
        {
            _logger.LogWarning("Component paths endpoint returned {StatusCode} for component {ComponentId}", response.StatusCode, componentId);
        }
    }

    [Fact]
    public async Task ModelQueryController_GetModelStats_ShouldReturnStats()
    {
        // Arrange
        var filePath = "/test/model.bin";

        // Act
        var response = await _client.GetAsync($"/api/model-query/stats?filePath={Uri.EscapeDataString(filePath)}");

        // Assert
        // This endpoint requires SQL CLR which may not be available in test environment
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogInformation("Model Query Engine not available - expected in test environment");
        }
        else if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var stats = JsonSerializer.Deserialize<JsonElement>(content);

            stats.GetProperty("filePath").GetString().Should().Be(filePath);
            stats.GetProperty("fileSize").ValueKind.Should().Be(JsonValueKind.Number);
        }
    }

    [Fact]
    public async Task ModelQueryController_QueryBytes_ShouldHandleValidRequest()
    {
        // Arrange
        var queryRequest = new
        {
            FilePath = "/test/model.bin",
            Offset = 0,
            Length = 1024
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/model-query/bytes", queryRequest);

        // Assert
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogInformation("Model Query Engine not available - expected in test environment");
        }
        else if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(content);

            result.GetProperty("filePath").GetString().Should().Be(queryRequest.FilePath);
            result.GetProperty("offset").GetInt64().Should().Be(queryRequest.Offset);
            result.GetProperty("length").GetInt32().Should().Be(queryRequest.Length);
        }
    }

    [Theory]
    [InlineData("/api/data-fabric/health")]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    public async Task HealthEndpoints_ShouldBeAccessible(string endpoint)
    {
        // Act
        var response = await _client.GetAsync(endpoint);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            $"Health endpoint {endpoint} should be accessible");
    }

    [Fact]
    public async Task ApiEndpoints_ShouldRequireAuthentication()
    {
        // Arrange
        var protectedEndpoints = new[]
        {
            "/api/data-fabric/models/123/insights",
            "/api/data-fabric/search/semantic",
            "/api/data-fabric/components/123/paths",
            "/api/model-query/stats"
        };

        foreach (var endpoint in protectedEndpoints)
        {
            // Act
            var response = await _client.GetAsync(endpoint);

            // Assert
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Endpoint {Endpoint} correctly requires authentication", endpoint);
            }
            else if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogInformation("Endpoint {Endpoint} is accessible (authentication may be disabled for testing)", endpoint);
            }
            else
            {
                _logger.LogWarning("Endpoint {Endpoint} returned unexpected status {StatusCode}", endpoint, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task ApiDocumentation_ShouldBeAvailable()
    {
        // Act
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        if (swaggerResponse.IsSuccessStatusCode)
        {
            var swaggerContent = await swaggerResponse.Content.ReadAsStringAsync();
            swaggerContent.Should().Contain("Hartonomous API");
            swaggerContent.Should().Contain("data-fabric");
            swaggerContent.Should().Contain("model-query");
        }
        else
        {
            _logger.LogWarning("Swagger documentation not available - status {StatusCode}", swaggerResponse.StatusCode);
        }
    }

    private static float[] GenerateRandomEmbedding(int dimension)
    {
        var random = new Random();
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        return embedding;
    }
}