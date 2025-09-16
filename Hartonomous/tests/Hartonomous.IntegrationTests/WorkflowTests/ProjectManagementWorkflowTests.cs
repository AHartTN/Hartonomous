using Hartonomous.IntegrationTests.Infrastructure;
using Hartonomous.Core.DTOs;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Hartonomous.IntegrationTests.WorkflowTests;

/// <summary>
/// Comprehensive project management workflow integration tests that validate end-to-end scenarios
/// </summary>
public class ProjectManagementWorkflowTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly ILogger<ProjectManagementWorkflowTests> _logger;
    private readonly List<Guid> _createdProjectIds = new();

    public ProjectManagementWorkflowTests(TestFixture fixture)
    {
        _fixture = fixture;
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
        _logger = loggerFactory.CreateLogger<ProjectManagementWorkflowTests>();
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing project management workflow tests");
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _logger.LogInformation("Cleaning up project management workflow tests");
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task CompleteProjectLifecycle_ShouldWorkEndToEnd()
    {
        _logger.LogInformation("Starting complete project lifecycle test");
        var stopwatch = Stopwatch.StartNew();

        // SCENARIO: A data scientist creates a project, adds models, manages versions, and collaborates

        // 1. Project Creation Phase
        var createProjectRequest = new CreateProjectRequest("ML Sentiment Analysis Pipeline");

        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", createProjectRequest);
        createResponse.Should().HaveStatusCode(HttpStatusCode.Created);

        var projectId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        projectId.Should().NotBeEmpty();
        _createdProjectIds.Add(projectId);

        _logger.LogInformation("Project created with ID: {ProjectId}", projectId);

        // 2. Project Metadata Validation
        var getProjectResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
        getProjectResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var project = await getProjectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        project.Should().NotBeNull();
        project!.ProjectName.Should().Be("ML Sentiment Analysis Pipeline");
        project.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));

        // 3. Model Addition Phase
        var modelRequests = new[]
        {
            new CreateModelRequest("BERT-Base-Sentiment", "1.0.0", "MIT",
                JsonSerializer.Serialize(new { architecture = "transformer", parameters = 110000000 })),
            new CreateModelRequest("LSTM-Sentiment", "1.0.0", "Apache-2.0",
                JsonSerializer.Serialize(new { architecture = "lstm", parameters = 2500000 })),
            new CreateModelRequest("SVM-Baseline", "1.0.0", "MIT",
                JsonSerializer.Serialize(new { architecture = "svm", parameters = 1000 }))
        };

        var modelIds = new List<Guid>();
        foreach (var modelRequest in modelRequests)
        {
            var modelResponse = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", modelRequest);
            modelResponse.Should().HaveStatusCode(HttpStatusCode.Created);

            var modelId = await modelResponse.Content.ReadFromJsonAsync<Guid>();
            modelId.Should().NotBeEmpty();
            modelIds.Add(modelId);
        }

        _logger.LogInformation("Added {ModelCount} models to project {ProjectId}", modelIds.Count, projectId);

        // 4. Model Retrieval and Validation
        var getModelsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        getModelsResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var models = await getModelsResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        models.Should().HaveCount(3);
        models.Should().Contain(m => m.ModelName == "BERT-Base-Sentiment");
        models.Should().Contain(m => m.ModelName == "LSTM-Sentiment");
        models.Should().Contain(m => m.ModelName == "SVM-Baseline");

        // 5. Model Versioning Workflow
        var bertModelId = models.First(m => m.ModelName == "BERT-Base-Sentiment").ModelId;
        var bertV2Request = new CreateModelRequest("BERT-Base-Sentiment", "2.0.0", "MIT",
            JsonSerializer.Serialize(new { architecture = "transformer", parameters = 110000000, finetuned = true }));

        var bertV2Response = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", bertV2Request);
        bertV2Response.Should().HaveStatusCode(HttpStatusCode.Created);

        // Verify version management
        var modelsAfterVersioning = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        var updatedModels = await modelsAfterVersioning.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        updatedModels.Should().HaveCount(4); // Original 3 + new version
        updatedModels.Count(m => m.ModelName == "BERT-Base-Sentiment").Should().Be(2);

        // 6. Model Query and Search
        var searchResponse = await _fixture.HttpClient.GetAsync($"/api/models/search?query=BERT");
        searchResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var searchResults = await searchResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        searchResults.Should().HaveCountGreaterOrEqualTo(2);
        searchResults.Should().OnlyContain(m => m.ModelName.Contains("BERT"));

        // 7. Project Statistics and Analytics
        var statsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/statistics");
        if (statsResponse.StatusCode == HttpStatusCode.OK)
        {
            var stats = await statsResponse.Content.ReadFromJsonAsync<ProjectStatistics>();
            stats.Should().NotBeNull();
            stats!.ModelCount.Should().Be(4);
            stats.CreatedAt.Should().Be(project.CreatedAt);
        }

        // 8. Collaboration Features (User Scoping)
        var userProjectsResponse = await _fixture.HttpClient.GetAsync("/api/projects");
        userProjectsResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var userProjects = await userProjectsResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        userProjects.Should().Contain(p => p.ProjectId == projectId);

        // 9. Model Deletion (Cleanup specific model)
        var modelToDelete = models.First(m => m.ModelName == "SVM-Baseline");
        var deleteModelResponse = await _fixture.HttpClient.DeleteAsync($"/api/models/{modelToDelete.ModelId}");
        deleteModelResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

        // Verify model deletion
        var modelsAfterDeletion = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        var finalModels = await modelsAfterDeletion.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        finalModels.Should().HaveCount(3);
        finalModels.Should().NotContain(m => m.ModelName == "SVM-Baseline");

        stopwatch.Stop();
        _logger.LogInformation("Complete project lifecycle test completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        // Performance validation
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "Complete workflow should finish within 30 seconds");
    }

    [Fact]
    public async Task MultiProjectDataIsolation_ShouldMaintainUserBoundaries()
    {
        _logger.LogInformation("Testing multi-project data isolation");

        // Create multiple projects
        var projects = new[]
        {
            "Project Alpha - Computer Vision",
            "Project Beta - Natural Language Processing",
            "Project Gamma - Recommendation System"
        };

        var projectIds = new List<Guid>();
        foreach (var projectName in projects)
        {
            var createRequest = new CreateProjectRequest(projectName);
            var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", createRequest);
            response.Should().HaveStatusCode(HttpStatusCode.Created);

            var projectId = await response.Content.ReadFromJsonAsync<Guid>();
            projectIds.Add(projectId);
            _createdProjectIds.Add(projectId);
        }

        // Add models to each project
        for (int i = 0; i < projectIds.Count; i++)
        {
            var projectId = projectIds[i];
            var modelRequest = new CreateModelRequest($"Model-P{i+1}", "1.0.0", "MIT", "{}");

            var modelResponse = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", modelRequest);
            modelResponse.Should().HaveStatusCode(HttpStatusCode.Created);
        }

        // Verify each project only shows its own models
        for (int i = 0; i < projectIds.Count; i++)
        {
            var projectId = projectIds[i];
            var modelsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
            modelsResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            var models = await modelsResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
            models.Should().HaveCount(1);
            models.Single().ModelName.Should().Be($"Model-P{i+1}");
        }

        // Verify project list contains all user projects
        var allProjectsResponse = await _fixture.HttpClient.GetAsync("/api/projects");
        allProjectsResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var allProjects = await allProjectsResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        allProjects.Should().HaveCountGreaterOrEqualTo(3);

        foreach (var projectId in projectIds)
        {
            allProjects.Should().Contain(p => p.ProjectId == projectId);
        }

        _logger.LogInformation("Multi-project data isolation test passed for {ProjectCount} projects", projectIds.Count);
    }

    [Fact]
    public async Task ConcurrentProjectOperations_ShouldMaintainConsistency()
    {
        _logger.LogInformation("Testing concurrent project operations");

        const int concurrentProjects = 10;
        const int modelsPerProject = 3;

        // Phase 1: Concurrent project creation
        var createTasks = Enumerable.Range(1, concurrentProjects)
            .Select(i => CreateProjectAsync($"Concurrent Project {i}"))
            .ToArray();

        var projectIds = await Task.WhenAll(createTasks);
        projectIds.Should().HaveCount(concurrentProjects);
        projectIds.Should().OnlyHaveUniqueItems();

        _createdProjectIds.AddRange(projectIds);

        // Phase 2: Concurrent model additions
        var modelTasks = projectIds.SelectMany((projectId, projectIndex) =>
            Enumerable.Range(1, modelsPerProject)
                .Select(modelIndex => CreateModelAsync(projectId, $"Model-{projectIndex}-{modelIndex}"))
        ).ToArray();

        var modelIds = await Task.WhenAll(modelTasks);
        modelIds.Should().HaveCount(concurrentProjects * modelsPerProject);
        modelIds.Should().OnlyHaveUniqueItems();

        // Phase 3: Concurrent reads and validations
        var readTasks = projectIds.Select(async projectId =>
        {
            var projectResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
            projectResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            var modelsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
            modelsResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            var models = await modelsResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
            return models?.Count ?? 0;
        }).ToArray();

        var modelCounts = await Task.WhenAll(readTasks);
        modelCounts.Should().AllSatisfy(count => count.Should().Be(modelsPerProject));

        // Phase 4: Concurrent deletions
        var projectsToDelete = projectIds.Take(5).ToArray();
        var deleteTasks = projectsToDelete.Select(projectId =>
            _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}")
        ).ToArray();

        var deleteResponses = await Task.WhenAll(deleteTasks);
        deleteResponses.Should().AllSatisfy(response =>
            response.StatusCode.Should().Be(HttpStatusCode.NoContent));

        // Verify final state
        var finalProjectsResponse = await _fixture.HttpClient.GetAsync("/api/projects");
        var finalProjects = await finalProjectsResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();

        var remainingTestProjects = finalProjects.Where(p =>
            projectIds.Skip(5).Contains(p.ProjectId)).ToList();
        remainingTestProjects.Should().HaveCount(concurrentProjects - 5);

        _logger.LogInformation("Concurrent operations test completed: {Created} projects, {ModelsPerProject} models each, {Deleted} deleted",
            concurrentProjects, modelsPerProject, projectsToDelete.Length);
    }

    [Fact]
    public async Task ProjectWorkflowWithLargeModels_ShouldHandleEfficiently()
    {
        _logger.LogInformation("Testing project workflow with large models");

        // Create project for large models
        var projectId = await CreateProjectAsync("Large Models Project");
        _createdProjectIds.Add(projectId);

        // Create models with large metadata
        var largeModelRequests = GenerateLargeModelRequests(5);
        var modelIds = new List<Guid>();

        var stopwatch = Stopwatch.StartNew();

        foreach (var modelRequest in largeModelRequests)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", modelRequest);
            response.Should().HaveStatusCode(HttpStatusCode.Created);

            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            modelIds.Add(modelId);
        }

        stopwatch.Stop();
        var creationTime = stopwatch.ElapsedMilliseconds;

        // Test retrieval performance
        stopwatch.Restart();

        var getModelsResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        getModelsResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var models = await getModelsResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        models.Should().HaveCount(5);

        stopwatch.Stop();
        var retrievalTime = stopwatch.ElapsedMilliseconds;

        // Performance assertions
        creationTime.Should().BeLessThan(15000, "Large model creation should be efficient");
        retrievalTime.Should().BeLessThan(5000, "Large model retrieval should be fast");

        // Test individual model retrieval
        foreach (var modelId in modelIds.Take(2))
        {
            var modelResponse = await _fixture.HttpClient.GetAsync($"/api/models/{modelId}");
            modelResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            var model = await modelResponse.Content.ReadFromJsonAsync<ModelMetadataDto>();
            model.Should().NotBeNull();
        }

        _logger.LogInformation("Large models test completed: {ModelCount} models created in {CreationTime}ms, retrieved in {RetrievalTime}ms",
            largeModelRequests.Count, creationTime, retrievalTime);
    }

    [Fact]
    public async Task ProjectDeletionCascade_ShouldCleanupAllRelatedData()
    {
        _logger.LogInformation("Testing project deletion cascade");

        // Create project with models
        var projectId = await CreateProjectAsync("Project to Delete");

        // Add multiple models
        var modelIds = new List<Guid>();
        for (int i = 1; i <= 3; i++)
        {
            var modelId = await CreateModelAsync(projectId, $"Model {i}");
            modelIds.Add(modelId);
        }

        // Verify models exist
        var modelsBeforeResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
        var modelsBefore = await modelsBeforeResponse.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
        modelsBefore.Should().HaveCount(3);

        // Delete project
        var deleteResponse = await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");
        deleteResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

        // Verify project is deleted
        var getProjectResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
        getProjectResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

        // Verify models are also deleted (cascade)
        foreach (var modelId in modelIds)
        {
            var getModelResponse = await _fixture.HttpClient.GetAsync($"/api/models/{modelId}");
            getModelResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);
        }

        // Verify project not in user's project list
        var userProjectsResponse = await _fixture.HttpClient.GetAsync("/api/projects");
        var userProjects = await userProjectsResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        userProjects.Should().NotContain(p => p.ProjectId == projectId);

        _logger.LogInformation("Project deletion cascade test completed: project and {ModelCount} models deleted", modelIds.Count);
    }

    [Fact]
    public async Task ProjectSearchAndFiltering_ShouldWorkAccurately()
    {
        _logger.LogInformation("Testing project search and filtering");

        // Create diverse projects
        var projectNames = new[]
        {
            "Computer Vision CNN Project",
            "Natural Language BERT Model",
            "Computer Vision YOLO Detection",
            "Reinforcement Learning Game AI",
            "Computer Vision Image Segmentation"
        };

        var projectIds = new List<Guid>();
        foreach (var name in projectNames)
        {
            var projectId = await CreateProjectAsync(name);
            projectIds.Add(projectId);
            _createdProjectIds.Add(projectId);
        }

        // Test search functionality
        var searchTests = new[]
        {
            new { Query = "Computer Vision", ExpectedCount = 3 },
            new { Query = "BERT", ExpectedCount = 1 },
            new { Query = "CNN", ExpectedCount = 1 },
            new { Query = "Model", ExpectedCount = 1 },
            new { Query = "NonExistent", ExpectedCount = 0 }
        };

        foreach (var test in searchTests)
        {
            var searchResponse = await _fixture.HttpClient.GetAsync($"/api/projects/search?query={test.Query}");
            if (searchResponse.StatusCode == HttpStatusCode.OK)
            {
                var searchResults = await searchResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
                var matchingResults = searchResults.Where(p => projectIds.Contains(p.ProjectId)).ToList();
                matchingResults.Should().HaveCount(test.ExpectedCount,
                    $"Search for '{test.Query}' should return {test.ExpectedCount} results");
            }
        }

        _logger.LogInformation("Project search and filtering test completed");
    }

    // Helper Methods
    private async Task<Guid> CreateProjectAsync(string projectName)
    {
        var request = new CreateProjectRequest(projectName);
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", request);
        response.Should().HaveStatusCode(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<Guid> CreateModelAsync(Guid projectId, string modelName)
    {
        var request = new CreateModelRequest(modelName, "1.0.0", "MIT", "{}");
        var response = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", request);
        response.Should().HaveStatusCode(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static List<CreateModelRequest> GenerateLargeModelRequests(int count)
    {
        var models = new List<CreateModelRequest>();
        for (int i = 1; i <= count; i++)
        {
            var largeMetadata = new
            {
                name = $"Large Model {i}",
                description = new string('A', 1000), // Large description
                architecture = "transformer",
                parameters = 175000000 + (i * 1000000),
                layers = Enumerable.Range(1, 100).Select(layer => new
                {
                    layerNumber = layer,
                    type = "attention",
                    parameters = Enumerable.Range(1, 50).ToArray()
                }).ToArray(),
                trainingData = new
                {
                    datasets = Enumerable.Range(1, 20).Select(d => $"Dataset {d}").ToArray(),
                    sampleCount = 10000000,
                    details = new string('B', 500)
                },
                performance = new
                {
                    accuracy = 0.95 + (i * 0.001),
                    benchmarks = Enumerable.Range(1, 30).Select(b => new
                    {
                        benchmark = $"Benchmark {b}",
                        score = 0.8 + (b * 0.01),
                        details = new string('C', 100)
                    }).ToArray()
                }
            };

            models.Add(new CreateModelRequest(
                $"LargeModel-{i}",
                $"1.{i}.0",
                "MIT",
                JsonSerializer.Serialize(largeMetadata)
            ));
        }
        return models;
    }

    private async Task CleanupTestDataAsync()
    {
        var tasks = _createdProjectIds.Select(async projectId =>
        {
            try
            {
                await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup project {ProjectId}", projectId);
            }
        });

        await Task.WhenAll(tasks);
        _createdProjectIds.Clear();
    }
}

public record ProjectStatistics(int ModelCount, DateTime CreatedAt, string ProjectName);

public record CreateProjectRequest(string ProjectName);

public record CreateModelRequest(
    string ModelName,
    string Version,
    string License,
    string? MetadataJson = null);