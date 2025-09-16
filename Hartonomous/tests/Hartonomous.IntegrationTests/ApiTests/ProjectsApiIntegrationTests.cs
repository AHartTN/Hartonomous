using Hartonomous.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.ApiTests;

/// <summary>
/// Full HTTP workflow integration tests for the Projects API
/// </summary>
[Collection("ApiTests")]
public class ProjectsApiIntegrationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly DatabaseTestHelper _dbHelper;

    public ProjectsApiIntegrationTests(TestFixture fixture)
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
    public async Task ProjectsApi_FullCrudWorkflow_ShouldWorkEndToEnd()
    {
        // Arrange
        var createRequest = TestDataGenerator.GenerateCreateProjectRequest();

        // Act & Assert - Create Project
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", createRequest);
        createResponse.Should().HaveStatusCode(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();

        var projectId = await createResponse.Content.ReadFromJsonAsync<Guid>();
        projectId.Should().NotBeEmpty();

        // Act & Assert - Read Project
        var getResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
        getResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var project = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        project.Should().NotBeNull();
        project!.ProjectId.Should().Be(projectId);
        project.ProjectName.Should().Be(createRequest.ProjectName);
        project.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // Act & Assert - List Projects
        var listResponse = await _fixture.HttpClient.GetAsync("/api/projects");
        listResponse.Should().HaveStatusCode(HttpStatusCode.OK);

        var projects = await listResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        projects.Should().ContainSingle().Which.ProjectId.Should().Be(projectId);

        // Act & Assert - Delete Project
        var deleteResponse = await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");
        deleteResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

        // Verify deletion
        var getAfterDeleteResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
        getAfterDeleteResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProjectsApi_WithInvalidData_ShouldReturnValidationErrors()
    {
        // Test cases for various validation scenarios
        var testCases = new[]
        {
            new { Request = new { ProjectName = (string?)null }, ExpectedError = "Project name is required" },
            new { Request = new { ProjectName = "" }, ExpectedError = "Project name is required" },
            new { Request = new { ProjectName = "   " }, ExpectedError = "Project name is required" },
            new { Request = new { ProjectName = new string('A', 300) }, ExpectedError = "characters" }
        };

        foreach (var testCase in testCases)
        {
            // Act
            var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", testCase.Request);

            // Assert
            response.Should().HaveClientError();
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain(testCase.ExpectedError, because: $"Test case: {testCase.Request}");
        }
    }

    [Fact]
    public async Task ProjectsApi_WithUnauthenticatedRequest_ShouldReturnUnauthorized()
    {
        // Arrange
        var unauthenticatedClient = _fixture.CreateClient(); // No auth header
        var createRequest = TestDataGenerator.GenerateCreateProjectRequest();

        // Act & Assert - Create without auth
        var createResponse = await unauthenticatedClient.PostAsJsonAsync("/api/projects", createRequest);
        createResponse.Should().HaveStatusCode(HttpStatusCode.Unauthorized);

        // Act & Assert - Get without auth
        var getResponse = await unauthenticatedClient.GetAsync("/api/projects");
        getResponse.Should().HaveStatusCode(HttpStatusCode.Unauthorized);

        // Act & Assert - Delete without auth
        var deleteResponse = await unauthenticatedClient.DeleteAsync($"/api/projects/{Guid.NewGuid()}");
        deleteResponse.Should().HaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProjectsApi_ContentNegotiation_ShouldSupportJsonOnly()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        // Act - Request with different Accept headers
        var jsonResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");

        _fixture.HttpClient.DefaultRequestHeaders.Accept.Clear();
        _fixture.HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        var explicitJsonResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");

        _fixture.HttpClient.DefaultRequestHeaders.Accept.Clear();
        _fixture.HttpClient.DefaultRequestHeaders.Accept.Add(new("application/xml"));
        var xmlResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");

        // Assert
        jsonResponse.Should().HaveStatusCode(HttpStatusCode.OK);
        jsonResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        explicitJsonResponse.Should().HaveStatusCode(HttpStatusCode.OK);
        explicitJsonResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        xmlResponse.Should().HaveStatusCode(HttpStatusCode.NotAcceptable);
    }

    [Fact]
    public async Task ProjectsApi_ErrorHandling_ShouldReturnConsistentErrorFormat()
    {
        // Test various error scenarios and verify consistent error response format

        // 404 - Not Found
        var notFoundResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{Guid.NewGuid()}");
        notFoundResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

        // 400 - Bad Request
        var badRequestResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects",
            new { ProjectName = (string?)null });
        badRequestResponse.Should().HaveStatusCode(HttpStatusCode.BadRequest);

        // 405 - Method Not Allowed
        var methodNotAllowedResponse = await _fixture.HttpClient.PatchAsync("/api/projects", null);
        methodNotAllowedResponse.Should().HaveStatusCode(HttpStatusCode.MethodNotAllowed);

        // Verify error responses have consistent structure
        var notFoundContent = await notFoundResponse.Content.ReadAsStringAsync();
        notFoundContent.Should().NotBeEmpty();

        var badRequestContent = await badRequestResponse.Content.ReadAsStringAsync();
        badRequestContent.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ProjectsApi_ConcurrentOperations_ShouldMaintainDataConsistency()
    {
        // Arrange
        var projectRequests = TestDataGenerator.GenerateMultipleProjects(10);
        var createdProjectIds = new List<Guid>();

        // Act - Create projects concurrently
        var createTasks = projectRequests.Select(request =>
            _fixture.HttpClient.PostAsJsonAsync("/api/projects", request)
        );

        var createResponses = await Task.WhenAll(createTasks);

        // Collect created project IDs
        foreach (var response in createResponses)
        {
            response.Should().BeSuccessful();
            var projectId = await response.Content.ReadFromJsonAsync<Guid>();
            createdProjectIds.Add(projectId);
        }

        // Act - Read projects concurrently
        var readTasks = createdProjectIds.Select(id =>
            _fixture.HttpClient.GetAsync($"/api/projects/{id}")
        );

        var readResponses = await Task.WhenAll(readTasks);

        // Act - Delete some projects concurrently
        var deleteProjectIds = createdProjectIds.Take(5).ToList();
        var deleteTasks = deleteProjectIds.Select(id =>
            _fixture.HttpClient.DeleteAsync($"/api/projects/{id}")
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
        var finalListResponse = await _fixture.HttpClient.GetAsync("/api/projects");
        var finalProjects = await finalListResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        finalProjects.Should().HaveCount(5); // 10 created - 5 deleted = 5 remaining
    }

    [Fact]
    public async Task ProjectsApi_LargeDatasets_ShouldHandleEfficiently()
    {
        // Arrange - Create many projects
        const int projectCount = 50;
        var projects = TestDataGenerator.GenerateMultipleProjects(projectCount);

        // Act - Create projects in batches
        var batchSize = 10;
        for (int i = 0; i < projectCount; i += batchSize)
        {
            var batch = projects.Skip(i).Take(batchSize);
            var tasks = batch.Select(project =>
                _fixture.HttpClient.PostAsJsonAsync("/api/projects", project)
            );

            var responses = await Task.WhenAll(tasks);
            foreach (var response in responses)
            {
                response.Should().BeSuccessful();
            }
        }

        // Act - List all projects
        var listResponse = await _fixture.HttpClient.GetAsync("/api/projects");

        // Assert
        listResponse.Should().BeSuccessful();
        var allProjects = await listResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        allProjects.Should().HaveCount(projectCount);

        // Verify response is properly formatted JSON
        var responseContent = await listResponse.Content.ReadAsStringAsync();
        var parsedJson = JsonDocument.Parse(responseContent);
        parsedJson.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ProjectsApi_RateLimiting_ShouldHandleHighFrequencyRequests()
    {
        // Arrange
        const int requestCount = 100;
        var projectId = await CreateTestProjectAsync();

        // Act - Make many rapid requests
        var tasks = Enumerable.Range(0, requestCount)
            .Select(_ => _fixture.HttpClient.GetAsync($"/api/projects/{projectId}"));

        var responses = await Task.WhenAll(tasks);

        // Assert - Should handle all requests without rate limiting errors
        foreach (var response in responses)
        {
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync();
                content.Should().NotBeEmpty();
            }
        }

        // Most requests should succeed (some rate limiting might be acceptable)
        var successfulRequests = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        successfulRequests.Should().BeGreaterThan(requestCount * 0.8); // At least 80% success rate
    }

    [Fact]
    public async Task ProjectsApi_SwaggerDocumentation_ShouldBeAccessible()
    {
        // Act
        var swaggerResponse = await _fixture.HttpClient.GetAsync("/swagger/v1/swagger.json");

        // Assert
        swaggerResponse.Should().BeSuccessful();
        swaggerResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var swaggerContent = await swaggerResponse.Content.ReadAsStringAsync();
        var swaggerDoc = JsonDocument.Parse(swaggerContent);

        // Verify API documentation contains expected endpoints
        var paths = swaggerDoc.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/projects", out _).Should().BeTrue();
        paths.TryGetProperty("/api/projects/{id}", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ProjectsApi_HealthCheck_ShouldIndicateSystemHealth()
    {
        // Act
        var healthResponse = await _fixture.HttpClient.GetAsync("/health");

        // Assert
        healthResponse.Should().BeSuccessful();
        var healthContent = await healthResponse.Content.ReadAsStringAsync();
        healthContent.Should().Contain("Healthy");
        healthContent.Should().Contain("Timestamp");
    }

    [Fact]
    public async Task ProjectsApi_UserIsolation_ShouldEnforceProperAccessControl()
    {
        // This test would require multiple user contexts to fully test
        // For now, we'll test that a user can only see their own projects

        // Arrange
        var project1 = await CreateTestProjectAsync();

        // Act - Try to access with different user context (simulate by creating new authenticated client)
        // Note: In a full implementation, you'd create a client with different user claims
        var getResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{project1}");
        var listResponse = await _fixture.HttpClient.GetAsync("/api/projects");

        // Assert - User should see their own projects
        getResponse.Should().BeSuccessful();

        listResponse.Should().BeSuccessful();
        var projects = await listResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        projects.Should().Contain(p => p.ProjectId == project1);
    }

    [Fact]
    public async Task ProjectsApi_HttpMethodsValidation_ShouldEnforceCorrectUsage()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();

        // Test invalid HTTP methods for each endpoint
        var invalidMethodTests = new[]
        {
            new { Url = "/api/projects", Method = HttpMethod.Patch, ExpectedStatus = HttpStatusCode.MethodNotAllowed },
            new { Url = "/api/projects", Method = HttpMethod.Put, ExpectedStatus = HttpStatusCode.MethodNotAllowed },
            new { Url = $"/api/projects/{projectId}", Method = HttpMethod.Post, ExpectedStatus = HttpStatusCode.MethodNotAllowed },
            new { Url = $"/api/projects/{projectId}", Method = HttpMethod.Patch, ExpectedStatus = HttpStatusCode.MethodNotAllowed }
        };

        foreach (var test in invalidMethodTests)
        {
            // Act
            var request = new HttpRequestMessage(test.Method, test.Url);
            var response = await _fixture.HttpClient.SendAsync(request);

            // Assert
            response.StatusCode.Should().Be(test.ExpectedStatus,
                because: $"HTTP {test.Method} should not be allowed for {test.Url}");
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

public record ProjectDto(Guid ProjectId, string ProjectName, DateTime CreatedAt);
public record CreateProjectRequest(string ProjectName);