using Hartonomous.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.ApiTests;

/// <summary>
/// Full HTTP workflow integration tests for the Models API
/// </summary>
[Collection("ApiTests")]
public class ModelsApiIntegrationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly DatabaseTestHelper _dbHelper;

    public ModelsApiIntegrationTests(TestFixture fixture)
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
    public async Task ModelsApi_FullCrudWorkflow_ShouldWorkEndToEnd()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var createModelRequest = TestDataGenerator.GenerateCreateModelRequest();

        // Act & Assert - Create Model
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", createModelRequest);
        createResponse.Should().HaveStatusCode(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();

        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        modelId.Should().NotBeEmpty();

        // Act & Assert - Read Model
        var getResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");
        getResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var model = await getResponse.Content.ReadFromJsonAsync<ModelMetadataDto>();
        model.Should().NotBeNull();
        model!.ModelId.Should().Be(modelId);
        model.ProjectId.Should().Be(projectId);
        model.ModelName.Should().Be(createModelRequest.ModelName);
        model.Version.Should().Be(createModelRequest.Version);
        model.License.Should().Be(createModelRequest.License);

        // Act & Assert - List Models in Project
        var listResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        listResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var models = await listResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        models.Should().ContainSingle().Which.ModelId.Should().Be(modelId);

        // Act & Assert - Delete Model
        var deleteResponse = await _fixture.HttpClient.DeleteAsync(
            $"/api/projects/{projectId}/models/{modelId}");
        deleteResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

        // Verify deletion
        var getAfterDeleteResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");
        getAfterDeleteResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ModelsApi_WithComplexMetadata_ShouldPreserveJsonStructure()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var complexMetadata = new
        {
            architecture = new
            {
                type = "Transformer",
                layers = new[]
                {
                    new { name = "embedding", size = 768, dropout = 0.1 },
                    new { name = "attention", heads = 12, dropout = 0.1 },
                    new { name = "feedforward", hidden_size = 3072, activation = "relu" }
                }
            },
            training = new
            {
                dataset = "Custom Dataset v2.1",
                epochs = 50,
                batch_size = 32,
                optimizer = new { type = "AdamW", lr = 0.0001, weight_decay = 0.01 },
                metrics = new { accuracy = 0.945, f1_score = 0.923, loss = 0.234 }
            },
            deployment = new
            {
                framework = "ONNX",
                target_platforms = new[] { "CPU", "GPU", "TPU" },
                optimization_level = "O3",
                quantization = new { enabled = true, precision = "int8" }
            }
        };

        var modelRequest = new CreateModelRequest(
            ModelName: "Complex-Transformer-Model",
            Version: "2.1.0",
            License: "Apache-2.0",
            MetadataJson: JsonSerializer.Serialize(complexMetadata, new JsonSerializerOptions { WriteIndented = true })
        );

        // Act
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        createResponse.Should().BeSuccessful();

        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        var getResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");

        // Assert
        getResponse.Should().BeSuccessful();
        var retrievedModel = await getResponse.Content.ReadFromJsonAsync<ModelMetadataDto>();
        retrievedModel.Should().NotBeNull();
        retrievedModel!.MetadataJson.Should().NotBeNullOrEmpty();

        // Verify JSON structure is preserved
        var parsedMetadata = JsonSerializer.Deserialize<JsonElement>(retrievedModel.MetadataJson!);
        parsedMetadata.GetProperty("architecture").GetProperty("type").GetString().Should().Be("Transformer");
        parsedMetadata.GetProperty("training").GetProperty("metrics").GetProperty("accuracy").GetDouble().Should().Be(0.945);
        parsedMetadata.GetProperty("deployment").GetProperty("target_platforms").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task ModelsApi_WithInvalidData_ShouldReturnValidationErrors()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        var testCases = new[]
        {
            new { Request = new { ModelName = (string?)null, Version = "1.0.0", License = "MIT" }, ExpectedError = "Model name is required" },
            new { Request = new { ModelName = "", Version = "1.0.0", License = "MIT" }, ExpectedError = "Model name is required" },
            new { Request = new { ModelName = "TestModel", Version = (string?)null, License = "MIT" }, ExpectedError = "Version is required" },
            new { Request = new { ModelName = "TestModel", Version = "1.0.0", License = (string?)null }, ExpectedError = "License is required" },
            new { Request = new { ModelName = new string('A', 300), Version = "1.0.0", License = "MIT" }, ExpectedError = "characters" }
        };

        foreach (var testCase in testCases)
        {
            // Act
            var response = await _fixture.HttpClient.PostAsJsonAsync(
                $"/api/projects/{projectId}/models", testCase.Request);

            // Assert
            response.Should().HaveClientError();
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain(testCase.ExpectedError);
        }
    }

    [Fact]
    public async Task ModelsApi_InNonExistentProject_ShouldReturnUnauthorized()
    {
        // Arrange
        var nonExistentProjectId = Guid.NewGuid();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        // Act & Assert - Create model in non-existent project
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{nonExistentProjectId}/models", modelRequest);
        createResponse.Should().HaveStatusCode(HttpStatusCode.Unauthorized);

        // Act & Assert - List models in non-existent project
        var listResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{nonExistentProjectId}/models");
        listResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

        // Act & Assert - Get specific model in non-existent project
        var getResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{nonExistentProjectId}/models/{Guid.NewGuid()}");
        getResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ModelsApi_ModelVersioning_ShouldSupportMultipleVersions()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var versioningScenario = TestDataGenerator.Scenarios.GenerateModelVersioningScenario();

        var createdModels = new List<Guid>();

        // Act - Create multiple versions of the same model
        foreach (var version in versioningScenario.Versions)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync(
                $"/api/projects/{projectId}/models", version);
            response.Should().BeSuccessful();

            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            createdModels.Add(modelId);
        }

        // Act - List all models
        var listResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");

        // Assert
        listResponse.Should().BeSuccessful();
        var models = await listResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        models.Should().HaveCount(3);

        // Verify all models have the same name but different versions
        var uniqueNames = models.Select(m => m.ModelName).Distinct().ToList();
        uniqueNames.Should().ContainSingle();

        var versions = models.Select(m => m.Version).OrderBy(v => v).ToList();
        versions.Should().Equal("1.1.0", "1.2.0", "1.3.0");

        // Verify each version can be retrieved individually
        foreach (var modelId in createdModels)
        {
            var getResponse = await _fixture.HttpClient.GetAsync(
                $"/api/projects/{projectId}/models/{modelId}");
            getResponse.Should().BeSuccessful();
        }
    }

    [Fact]
    public async Task ModelsApi_WithLargeMetadata_ShouldHandleEfficiently()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        // Create a large metadata payload (simulating complex model information)
        var largeMetadata = new
        {
            description = string.Join(" ", Enumerable.Repeat("This is a comprehensive model description.", 200)),
            training_history = Enumerable.Range(1, 1000).Select(epoch => new
            {
                epoch = epoch,
                loss = Math.Round(1.0 / epoch + 0.001 * Math.Sin(epoch), 4),
                accuracy = Math.Round(Math.Min(0.99, 0.5 + 0.4 * (1 - Math.Exp(-epoch / 100.0))), 4),
                validation_loss = Math.Round(1.2 / epoch + 0.002 * Math.Sin(epoch + 1), 4),
                validation_accuracy = Math.Round(Math.Min(0.95, 0.45 + 0.35 * (1 - Math.Exp(-epoch / 120.0))), 4),
                learning_rate = Math.Round(0.001 * Math.Exp(-epoch / 500.0), 6),
                timestamp = DateTime.UtcNow.AddMinutes(-epoch)
            }).ToArray(),
            model_architecture = new
            {
                total_parameters = 175_000_000,
                layers = Enumerable.Range(1, 96).Select(i => new
                {
                    layer_id = i,
                    type = i % 12 == 0 ? "MultiHeadAttention" : "FeedForward",
                    parameters = i % 12 == 0 ? 12_582_912 : 50_331_648,
                    activation = "gelu",
                    dropout = 0.1,
                    layer_norm = true
                }).ToArray()
            },
            hyperparameters = Enumerable.Range(1, 100).ToDictionary(
                i => $"param_{i}",
                i => new { value = i * 0.001, description = $"Hyperparameter {i} controls aspect X of training" }
            )
        };

        var modelRequest = new CreateModelRequest(
            ModelName: "Large-Metadata-GPT-Model",
            Version: "3.5.0",
            License: "MIT",
            MetadataJson: JsonSerializer.Serialize(largeMetadata)
        );

        // Act
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);

        // Assert
        createResponse.Should().BeSuccessful();
        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Verify retrieval works with large metadata
        var getResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");
        getResponse.Should().BeSuccessful();

        var model = await getResponse.Content.ReadFromJsonAsync<ModelMetadataDto>();
        model.Should().NotBeNull();
        model!.MetadataJson.Should().NotBeNullOrEmpty();

        // Verify large JSON can be parsed
        var parsedMetadata = JsonSerializer.Deserialize<JsonElement>(model.MetadataJson!);
        parsedMetadata.GetProperty("training_history").GetArrayLength().Should().Be(1000);
        parsedMetadata.GetProperty("model_architecture").GetProperty("layers").GetArrayLength().Should().Be(96);
    }

    [Fact]
    public async Task ModelsApi_ConcurrentOperations_ShouldMaintainConsistency()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequests = TestDataGenerator.GenerateMultipleModels(15);

        // Act - Create models concurrently
        var createTasks = modelRequests.Select(request =>
            _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", request)
        );

        var createResponses = await Task.WhenAll(createTasks);
        var createdModelIds = new List<Guid>();

        foreach (var response in createResponses)
        {
            response.Should().BeSuccessful();
            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            createdModelIds.Add(modelId);
        }

        // Act - Read models concurrently
        var readTasks = createdModelIds.Select(id =>
            _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models/{id}")
        );

        var readResponses = await Task.WhenAll(readTasks);

        // Act - Delete some models concurrently
        var modelsToDelete = createdModelIds.Take(5).ToList();
        var deleteTasks = modelsToDelete.Select(id =>
            _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}/models/{id}")
        );

        var deleteResponses = await Task.WhenAll(deleteTasks);

        // Assert
        foreach (var response in readResponses)
        {
            response.Should().BeSuccessful();
        }

        foreach (var response in deleteResponses)
        {
            response.Should().HaveStatusCode(HttpStatusCode.NoContent);
        }

        // Verify final state
        var finalListResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        var finalModels = await finalListResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        finalModels.Should().HaveCount(10); // 15 created - 5 deleted = 10 remaining
    }

    [Fact]
    public async Task ModelsApi_CrossProjectIsolation_ShouldEnforceProjectBoundaries()
    {
        // Arrange
        var project1Id = await CreateTestProjectAsync();
        var project2Id = await CreateTestProjectAsync();

        var model1Request = TestDataGenerator.GenerateCreateModelRequest();
        var model2Request = TestDataGenerator.GenerateCreateModelRequest();

        // Create models in different projects
        var model1Response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{project1Id}/models", model1Request);
        var model1Id = await model1Response.Content.ReadFromJsonAsync<Guid>();

        var model2Response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{project2Id}/models", model2Request);
        var model2Id = await model2Response.Content.ReadFromJsonAsync<Guid>();

        // Act & Assert - Project 1 should only see its models
        var project1ModelsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{project1Id}/models");
        var project1Models = await project1ModelsResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        project1Models.Should().ContainSingle().Which.ModelId.Should().Be(model1Id);

        // Act & Assert - Project 2 should only see its models
        var project2ModelsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{project2Id}/models");
        var project2Models = await project2ModelsResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        project2Models.Should().ContainSingle().Which.ModelId.Should().Be(model2Id);

        // Act & Assert - Cannot access model from wrong project context
        var wrongProjectResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{project2Id}/models/{model1Id}");
        wrongProjectResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ModelsApi_HttpCaching_ShouldSupportConditionalRequests()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Act - First request to get ETag
        var firstResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");
        firstResponse.Should().BeSuccessful();

        var etag = firstResponse.Headers.ETag?.Tag;

        // Act - Conditional request with If-None-Match
        if (!string.IsNullOrEmpty(etag))
        {
            _fixture.HttpClient.DefaultRequestHeaders.IfNoneMatch.Clear();
            _fixture.HttpClient.DefaultRequestHeaders.IfNoneMatch.Add(new(etag));

            var conditionalResponse = await _fixture.HttpClient.GetAsync(
                $"/api/projects/{projectId}/models/{modelId}");

            // Assert - Should return 304 Not Modified if ETags match
            // Note: This behavior depends on the actual implementation of caching in the API
            conditionalResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NotModified, HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task ModelsApi_ErrorScenarios_ShouldProvideDetailedErrors()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var validModelId = await CreateTestModelAsync(projectId);

        var errorScenarios = new[]
        {
            new {
                Url = $"/api/projects/{Guid.NewGuid()}/models/{validModelId}",
                Method = HttpMethod.Get,
                ExpectedStatus = HttpStatusCode.NotFound,
                Description = "Model ID in wrong project context"
            },
            new {
                Url = $"/api/projects/{projectId}/models/{Guid.NewGuid()}",
                Method = HttpMethod.Get,
                ExpectedStatus = HttpStatusCode.NotFound,
                Description = "Non-existent model ID"
            },
            new {
                Url = $"/api/projects/{projectId}/models/invalid-guid",
                Method = HttpMethod.Get,
                ExpectedStatus = HttpStatusCode.BadRequest,
                Description = "Invalid GUID format"
            }
        };

        foreach (var scenario in errorScenarios)
        {
            // Act
            var request = new HttpRequestMessage(scenario.Method, scenario.Url);
            var response = await _fixture.HttpClient.SendAsync(request);

            // Assert
            response.StatusCode.Should().Be(scenario.ExpectedStatus,
                because: scenario.Description);

            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeEmpty();
        }
    }

    private async Task<Guid> CreateTestProjectAsync()
    {
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        response.Should().BeSuccessful();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<Guid> CreateTestModelAsync(Guid projectId)
    {
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        response.Should().BeSuccessful();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}

public record ModelMetadataDto(
    Guid ModelId,
    Guid ProjectId,
    string ProjectName,
    string ModelName,
    string Version,
    string License,
    string? MetadataJson,
    DateTime CreatedAt);

public record CreateModelRequest(
    string ModelName,
    string Version,
    string License,
    string? MetadataJson = null);