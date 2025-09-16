using Hartonomous.IntegrationTests.Infrastructure;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.DatabaseTests;

/// <summary>
/// Integration tests for model repository operations with real SQL Server database
/// </summary>
[Collection("DatabaseTests")]
public class ModelRepositoryIntegrationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly DatabaseTestHelper _dbHelper;

    public ModelRepositoryIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
        _dbHelper = new DatabaseTestHelper(_fixture.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateModel_WithValidData_ShouldPersistToDatabase()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);

        // Assert
        response.Should().BeSuccessful();
        var modelId = await response.Content.ReadFromJsonAsync<Guid>();
        modelId.Should().NotBeEmpty();

        // Verify database state
        var modelExists = await _dbHelper.ModelExistsAsync(modelId);
        modelExists.Should().BeTrue();

        var storedModel = await _dbHelper.GetModelAsync(modelId);
        storedModel.Should().NotBeNull();
        storedModel!.ModelName.Should().Be(modelRequest.ModelName);
        storedModel.Version.Should().Be(modelRequest.Version);
        storedModel.License.Should().Be(modelRequest.License);
        storedModel.MetadataJson.Should().Be(modelRequest.MetadataJson);
    }

    [Fact]
    public async Task CreateModel_WithComplexMetadata_ShouldStoreJsonCorrectly()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var complexMetadata = new
        {
            architecture = "Transformer",
            layers = new[]
            {
                new { type = "Embedding", parameters = 768 },
                new { type = "MultiHeadAttention", heads = 12 },
                new { type = "FeedForward", hidden_size = 3072 }
            },
            training = new
            {
                optimizer = "AdamW",
                learning_rate = 0.0001,
                batch_size = 32,
                epochs = 100
            },
            metrics = new
            {
                accuracy = 0.94,
                f1_score = 0.92,
                precision = 0.95,
                recall = 0.91
            }
        };

        var modelRequest = new CreateModelRequest(
            ModelName: "Complex-BERT-Model",
            Version: "2.1.0",
            License: "Apache-2.0",
            MetadataJson: JsonSerializer.Serialize(complexMetadata, new JsonSerializerOptions { WriteIndented = true })
        );

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);

        // Assert
        response.Should().BeSuccessful();
        var modelId = await response.Content.ReadFromJsonAsync<Guid>();

        var storedModel = await _dbHelper.GetModelAsync(modelId);
        storedModel.Should().NotBeNull();

        // Verify JSON was stored and can be parsed back
        var parsedMetadata = JsonSerializer.Deserialize<JsonElement>(storedModel!.MetadataJson!);
        parsedMetadata.GetProperty("architecture").GetString().Should().Be("Transformer");
        parsedMetadata.GetProperty("metrics").GetProperty("accuracy").GetDouble().Should().Be(0.94);
    }

    [Fact]
    public async Task GetModels_WithMultipleModelsInProject_ShouldReturnAll()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequests = TestDataGenerator.GenerateMultipleModels(4);
        var createdModelIds = new List<Guid>();

        // Create multiple models
        foreach (var modelRequest in modelRequests)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync(
                $"/api/projects/{projectId}/models", modelRequest);
            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            createdModelIds.Add(modelId);
        }

        // Act
        var getResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");

        // Assert
        getResponse.Should().BeSuccessful();
        var models = await getResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        models.Should().HaveCount(4);

        foreach (var modelId in createdModelIds)
        {
            models.Should().Contain(m => m.ModelId == modelId);
        }
    }

    [Fact]
    public async Task GetModel_WithValidId_ShouldReturnModelWithMetadata()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Act
        var getResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");

        // Assert
        getResponse.Should().BeSuccessful();
        var model = await getResponse.Content.ReadFromJsonAsync<ModelMetadataDto>();
        model.Should().NotBeNull();
        model!.ModelId.Should().Be(modelId);
        model.ProjectId.Should().Be(projectId);
        model.ModelName.Should().Be(modelRequest.ModelName);
        model.Version.Should().Be(modelRequest.Version);
        model.License.Should().Be(modelRequest.License);
    }

    [Fact]
    public async Task DeleteModel_WithValidId_ShouldRemoveFromDatabase()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Verify model exists
        var modelExists = await _dbHelper.ModelExistsAsync(modelId);
        modelExists.Should().BeTrue();

        // Act
        var deleteResponse = await _fixture.HttpClient.DeleteAsync(
            $"/api/projects/{projectId}/models/{modelId}");

        // Assert
        deleteResponse.Should().BeSuccessful();
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Verify model is deleted from database
        var modelStillExists = await _dbHelper.ModelExistsAsync(modelId);
        modelStillExists.Should().BeFalse();
    }

    [Fact]
    public async Task ModelVersioning_WithSameNameDifferentVersions_ShouldStoreAll()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var versioningScenario = TestDataGenerator.Scenarios.GenerateModelVersioningScenario();

        var createdVersions = new List<Guid>();

        // Act - Create multiple versions of the same model
        foreach (var version in versioningScenario.Versions)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync(
                $"/api/projects/{projectId}/models", version);
            response.Should().BeSuccessful();
            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            createdVersions.Add(modelId);
        }

        // Assert
        var models = await _dbHelper.GetModelsByProjectAsync(projectId);
        models.Should().HaveCount(3);

        // Verify all versions have the same name but different version numbers
        var modelsByName = models.GroupBy(m => m.ModelName);
        modelsByName.Should().HaveCount(1);

        var versions = models.Select(m => m.Version).OrderBy(v => v).ToList();
        versions.Should().Equal("1.1.0", "1.2.0", "1.3.0");
    }

    [Fact]
    public async Task CreateModel_InNonExistentProject_ShouldReturnUnauthorized()
    {
        // Arrange
        var nonExistentProjectId = Guid.NewGuid();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{nonExistentProjectId}/models", modelRequest);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetModels_InEmptyProject_ShouldReturnEmptyList()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        // Act
        var response = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");

        // Assert
        response.Should().BeSuccessful();
        var models = await response.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        models.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentModelCreation_InSameProject_ShouldHandleCorrectly()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequests = TestDataGenerator.GenerateMultipleModels(5);

        // Act - Create models concurrently
        var tasks = modelRequests.Select(model =>
            _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", model)
        );

        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
        }

        // Verify all models were created
        var models = await _dbHelper.GetModelsByProjectAsync(projectId);
        models.Should().HaveCount(5);
    }

    [Fact]
    public async Task ModelMetadata_WithLargeJsonPayload_ShouldStoreCorrectly()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        // Create a large metadata payload
        var largeMetadata = new
        {
            description = string.Join(" ", Enumerable.Repeat("This is a very long description.", 100)),
            training_data = Enumerable.Range(1, 1000).Select(i => new {
                sample_id = i,
                input = $"input_{i}",
                output = $"output_{i}"
            }).ToArray(),
            hyperparameters = Enumerable.Range(1, 100).ToDictionary(
                i => $"param_{i}",
                i => i * 0.001)
        };

        var modelRequest = new CreateModelRequest(
            ModelName: "Large-Metadata-Model",
            Version: "1.0.0",
            License: "MIT",
            MetadataJson: JsonSerializer.Serialize(largeMetadata)
        );

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);

        // Assert
        response.Should().BeSuccessful();
        var modelId = await response.Content.ReadFromJsonAsync<Guid>();

        var storedModel = await _dbHelper.GetModelAsync(modelId);
        storedModel.Should().NotBeNull();
        storedModel!.MetadataJson.Should().NotBeNullOrEmpty();

        // Verify we can deserialize the stored JSON
        var deserializedMetadata = JsonSerializer.Deserialize<JsonElement>(storedModel.MetadataJson!);
        deserializedMetadata.GetProperty("training_data").GetArrayLength().Should().Be(1000);
    }

    [Fact]
    public async Task DeleteProject_WithModels_ShouldCascadeDelete()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequests = TestDataGenerator.GenerateMultipleModels(3);
        var createdModelIds = new List<Guid>();

        // Create models in the project
        foreach (var modelRequest in modelRequests)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync(
                $"/api/projects/{projectId}/models", modelRequest);
            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            createdModelIds.Add(modelId);
        }

        // Verify models exist
        foreach (var modelId in createdModelIds)
        {
            (await _dbHelper.ModelExistsAsync(modelId)).Should().BeTrue();
        }

        // Act - Delete the project
        var deleteResponse = await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");

        // Assert
        deleteResponse.Should().BeSuccessful();

        // Verify models are also deleted (cascade)
        foreach (var modelId in createdModelIds)
        {
            (await _dbHelper.ModelExistsAsync(modelId)).Should().BeFalse();
        }
    }

    private async Task<Guid> CreateTestProjectAsync()
    {
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        response.Should().BeSuccessful();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}

public record CreateModelRequest(
    string ModelName,
    string Version,
    string License,
    string? MetadataJson = null);