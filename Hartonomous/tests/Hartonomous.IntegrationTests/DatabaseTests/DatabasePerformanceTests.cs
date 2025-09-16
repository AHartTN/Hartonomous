using Hartonomous.IntegrationTests.Infrastructure;
using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Hartonomous.IntegrationTests.DatabaseTests;

/// <summary>
/// Performance tests for database operations to identify bottlenecks
/// </summary>
[Collection("DatabaseTests")]
public class DatabasePerformanceTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly DatabaseTestHelper _dbHelper;

    public DatabasePerformanceTests(TestFixture fixture)
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
    public async Task BulkProjectCreation_Performance_ShouldCompleteWithinReasonableTime()
    {
        // Arrange
        const int projectCount = 100;
        var projects = TestDataGenerator.GenerateMultipleProjects(projectCount);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = projects.Select(project =>
            _fixture.HttpClient.PostAsJsonAsync("/api/projects", project)
        );

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
        }

        // Performance assertions
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000); // 30 seconds max
        var averageTimePerProject = stopwatch.ElapsedMilliseconds / (double)projectCount;
        averageTimePerProject.Should().BeLessThan(300); // 300ms per project max

        // Verify all projects were created
        var userProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        userProjects.Should().HaveCount(projectCount);

        Console.WriteLine($"Created {projectCount} projects in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average time per project: {averageTimePerProject:F2}ms");
    }

    [Fact]
    public async Task BulkModelCreation_Performance_ShouldHandleLargeDatasets()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        const int modelCount = 50;
        var models = TestDataGenerator.GenerateMultipleModels(modelCount);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = models.Select(model =>
            _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", model)
        );

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
        }

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(25000); // 25 seconds max
        var averageTimePerModel = stopwatch.ElapsedMilliseconds / (double)modelCount;
        averageTimePerModel.Should().BeLessThan(500); // 500ms per model max

        var projectModels = await _dbHelper.GetModelsByProjectAsync(projectId);
        projectModels.Should().HaveCount(modelCount);

        Console.WriteLine($"Created {modelCount} models in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average time per model: {averageTimePerModel:F2}ms");
    }

    [Fact]
    public async Task ConcurrentReadOperations_Performance_ShouldScaleWell()
    {
        // Arrange
        var projectIds = await CreateMultipleProjectsAsync(10);
        const int concurrentReads = 50;
        var stopwatch = Stopwatch.StartNew();

        // Act - Perform concurrent read operations
        var readTasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < concurrentReads; i++)
        {
            var projectId = projectIds[i % projectIds.Count];
            readTasks.Add(_fixture.HttpClient.GetAsync($"/api/projects/{projectId}"));
        }

        var responses = await Task.WhenAll(readTasks);
        stopwatch.Stop();

        // Assert
        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
        }

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000); // 10 seconds max
        var averageTimePerRead = stopwatch.ElapsedMilliseconds / (double)concurrentReads;
        averageTimePerRead.Should().BeLessThan(200); // 200ms per read max

        Console.WriteLine($"Performed {concurrentReads} concurrent reads in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average time per read: {averageTimePerRead:F2}ms");
    }

    [Fact]
    public async Task LargeJsonMetadata_Performance_ShouldHandleEfficiently()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        // Create a very large JSON metadata payload
        var largeMetadata = new
        {
            description = string.Join(" ", Enumerable.Repeat("Large description text.", 1000)),
            training_samples = Enumerable.Range(1, 5000).Select(i => new {
                id = i,
                input = $"input_data_{i}_{string.Join("", Enumerable.Repeat("x", 100))}",
                output = $"output_data_{i}_{string.Join("", Enumerable.Repeat("y", 100))}",
                metadata = new {
                    timestamp = DateTime.UtcNow.AddDays(-i),
                    confidence = i * 0.0001,
                    tags = Enumerable.Range(1, 10).Select(j => $"tag_{i}_{j}").ToArray()
                }
            }).ToArray(),
            hyperparameters = Enumerable.Range(1, 500).ToDictionary(
                i => $"hyperparameter_{i}",
                i => new { value = i * 0.001, type = "float", description = $"Description for param {i}" }
            )
        };

        var modelRequest = new CreateModelRequest(
            ModelName: "Performance-Test-Model",
            Version: "1.0.0",
            License: "MIT",
            MetadataJson: System.Text.Json.JsonSerializer.Serialize(largeMetadata)
        );

        var stopwatch = Stopwatch.StartNew();

        // Act
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        var createTime = stopwatch.ElapsedMilliseconds;

        createResponse.Should().BeSuccessful();
        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Test retrieval performance
        stopwatch.Restart();
        var getResponse = await _fixture.HttpClient.GetAsync(
            $"/api/projects/{projectId}/models/{modelId}");
        var retrievalTime = stopwatch.ElapsedMilliseconds;

        // Assert
        getResponse.Should().BeSuccessful();
        var model = await getResponse.Content.ReadFromJsonAsync<ModelMetadataDto>();
        model.Should().NotBeNull();

        // Performance assertions
        createTime.Should().BeLessThan(5000); // 5 seconds max for creation
        retrievalTime.Should().BeLessThan(2000); // 2 seconds max for retrieval

        Console.WriteLine($"Large metadata JSON size: {modelRequest.MetadataJson!.Length} characters");
        Console.WriteLine($"Creation time: {createTime}ms");
        Console.WriteLine($"Retrieval time: {retrievalTime}ms");
    }

    [Fact]
    public async Task DatabaseConnectionPool_StressTest_ShouldHandleHighConcurrency()
    {
        // Arrange
        const int concurrentOperations = 100;
        var projects = TestDataGenerator.GenerateMultipleProjects(concurrentOperations);
        var stopwatch = Stopwatch.StartNew();

        // Act - Perform high number of concurrent database operations
        var tasks = new List<Task>();

        for (int i = 0; i < concurrentOperations; i++)
        {
            var project = projects[i];
            tasks.Add(Task.Run(async () =>
            {
                // Create project
                var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", project);
                createResponse.Should().BeSuccessful();
                var projectId = await createResponse.Content.ReadFromJsonAsync<Guid>();

                // Read project back
                var getResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
                getResponse.Should().BeSuccessful();

                // Create a model in the project
                var model = TestDataGenerator.GenerateCreateModelRequest();
                var modelResponse = await _fixture.HttpClient.PostAsJsonAsync(
                    $"/api/projects/{projectId}/models", model);
                modelResponse.Should().BeSuccessful();
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(60000); // 60 seconds max

        // Verify all operations completed successfully
        var userProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        userProjects.Should().HaveCount(concurrentOperations);

        var totalModels = 0;
        foreach (var project in userProjects)
        {
            var models = await _dbHelper.GetModelsByProjectAsync(project.ProjectId);
            totalModels += models.Count;
        }
        totalModels.Should().Be(concurrentOperations);

        Console.WriteLine($"Completed {concurrentOperations * 3} operations (create project, read project, create model) in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average time per operation set: {stopwatch.ElapsedMilliseconds / (double)concurrentOperations:F2}ms");
    }

    [Fact]
    public async Task QueryPerformance_WithIndexes_ShouldBeOptimal()
    {
        // Arrange - Create many projects and models to test query performance
        var projectIds = await CreateMultipleProjectsAsync(20);

        foreach (var projectId in projectIds)
        {
            var models = TestDataGenerator.GenerateMultipleModels(10);
            foreach (var model in models)
            {
                await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", model);
            }
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Perform various query operations
        var queryTasks = new List<Task<HttpResponseMessage>>();

        // Query all projects
        queryTasks.Add(_fixture.HttpClient.GetAsync("/api/projects"));

        // Query models for each project
        foreach (var projectId in projectIds)
        {
            queryTasks.Add(_fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models"));
        }

        var responses = await Task.WhenAll(queryTasks);
        stopwatch.Stop();

        // Assert
        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
        }

        // Query performance should be acceptable even with larger datasets
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(15000); // 15 seconds max
        var averageQueryTime = stopwatch.ElapsedMilliseconds / (double)queryTasks.Count;
        averageQueryTime.Should().BeLessThan(500); // 500ms per query max

        Console.WriteLine($"Executed {queryTasks.Count} queries in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average query time: {averageQueryTime:F2}ms");
    }

    [Fact]
    public async Task DatabaseCleanup_Performance_ShouldCompleteQuickly()
    {
        // Arrange - Create a significant amount of test data
        var projectIds = await CreateMultipleProjectsAsync(50);

        foreach (var projectId in projectIds)
        {
            var models = TestDataGenerator.GenerateMultipleModels(5);
            foreach (var model in models)
            {
                await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", model);
            }
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Perform cleanup
        await _fixture.CleanDatabaseAsync();
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000); // 10 seconds max

        // Verify cleanup was complete
        var remainingProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        remainingProjects.Should().BeEmpty();

        Console.WriteLine($"Database cleanup completed in {stopwatch.ElapsedMilliseconds}ms");
    }

    private async Task<Guid> CreateTestProjectAsync()
    {
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        response.Should().BeSuccessful();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<List<Guid>> CreateMultipleProjectsAsync(int count)
    {
        var projects = TestDataGenerator.GenerateMultipleProjects(count);
        var projectIds = new List<Guid>();

        foreach (var project in projects)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", project);
            response.Should().BeSuccessful();
            var projectId = await response.Content.ReadFromJsonAsync<Guid>();
            projectIds.Add(projectId);
        }

        return projectIds;
    }
}

public record CreateModelRequest(
    string ModelName,
    string Version,
    string License,
    string? MetadataJson = null);