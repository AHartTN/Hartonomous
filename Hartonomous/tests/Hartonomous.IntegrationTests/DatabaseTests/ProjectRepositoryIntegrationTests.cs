using Hartonomous.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.DatabaseTests;

/// <summary>
/// Integration tests for project repository operations with real SQL Server database
/// </summary>
[Collection("DatabaseTests")]
public class ProjectRepositoryIntegrationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly DatabaseTestHelper _dbHelper;

    public ProjectRepositoryIntegrationTests(TestFixture fixture)
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
    public async Task CreateProject_WithValidData_ShouldPersistToDatabase()
    {
        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();
        var userId = _fixture.TestUserId;

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);

        // Assert
        response.Should().BeSuccessful();
        var projectId = await response.Content.ReadFromJsonAsync<Guid>();
        projectId.Should().NotBeEmpty();

        // Verify database state
        var projectExists = await _dbHelper.ProjectExistsAsync(projectId);
        projectExists.Should().BeTrue();

        var storedProject = await _dbHelper.GetProjectAsync(projectId);
        storedProject.Should().NotBeNull();
        storedProject!.ProjectName.Should().Be(projectRequest.ProjectName);
        storedProject.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateMultipleProjects_ShouldAllPersistCorrectly()
    {
        // Arrange
        var projects = TestDataGenerator.GenerateMultipleProjects(5);
        var createdProjectIds = new List<Guid>();

        // Act
        foreach (var project in projects)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", project);
            response.Should().BeSuccessful();
            var projectId = await response.Content.ReadFromJsonAsync<Guid>();
            createdProjectIds.Add(projectId);
        }

        // Assert
        var userProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        userProjects.Should().HaveCount(5);

        foreach (var projectId in createdProjectIds)
        {
            userProjects.Should().Contain(p => p.ProjectId == projectId);
        }
    }

    [Fact]
    public async Task GetProjects_WhenUserHasProjects_ShouldReturnAllUserProjects()
    {
        // Arrange
        var projectRequests = TestDataGenerator.GenerateMultipleProjects(3);
        var createdProjects = new List<Guid>();

        // Create projects via HTTP API
        foreach (var project in projectRequests)
        {
            var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", project);
            var projectId = await response.Content.ReadFromJsonAsync<Guid>();
            createdProjects.Add(projectId);
        }

        // Act
        var getResponse = await _fixture.HttpClient.GetAsync("/api/projects");

        // Assert
        getResponse.Should().BeSuccessful();
        var returnedProjects = await getResponse.Content.ReadFromJsonAsync<List<ProjectDto>>();
        returnedProjects.Should().HaveCount(3);

        foreach (var projectId in createdProjects)
        {
            returnedProjects.Should().Contain(p => p.ProjectId == projectId);
        }
    }

    [Fact]
    public async Task GetProject_WithValidId_ShouldReturnProject()
    {
        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        var projectId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Act
        var getResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");

        // Assert
        getResponse.Should().BeSuccessful();
        var project = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        project.Should().NotBeNull();
        project!.ProjectId.Should().Be(projectId);
        project.ProjectName.Should().Be(projectRequest.ProjectName);
    }

    [Fact]
    public async Task GetProject_WithInvalidId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _fixture.HttpClient.GetAsync($"/api/projects/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProject_WithValidId_ShouldRemoveFromDatabase()
    {
        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        var projectId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Verify project exists
        var projectExists = await _dbHelper.ProjectExistsAsync(projectId);
        projectExists.Should().BeTrue();

        // Act
        var deleteResponse = await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");

        // Assert
        deleteResponse.Should().BeSuccessful();
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Verify project is deleted from database
        var projectStillExists = await _dbHelper.ProjectExistsAsync(projectId);
        projectStillExists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProject_WithInvalidId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _fixture.HttpClient.DeleteAsync($"/api/projects/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DatabaseTransactionRollback_OnError_ShouldMaintainConsistency()
    {
        // Arrange
        var validProject = TestDataGenerator.GenerateCreateProjectRequest();

        // Create a project first
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", validProject);
        var projectId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Count projects before attempting invalid operation
        var initialProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        var initialCount = initialProjects.Count;

        // Act - Try to create a project with invalid data that should fail
        var invalidProject = new { ProjectName = (string?)null };
        var invalidResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", invalidProject);

        // Assert
        invalidResponse.Should().HaveClientError();

        // Verify no additional projects were created
        var finalProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        finalProjects.Should().HaveCount(initialCount);

        // Verify original project still exists
        var originalExists = await _dbHelper.ProjectExistsAsync(projectId);
        originalExists.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentProjectCreation_ShouldHandleMultipleUsers()
    {
        // Arrange
        var projectRequests = TestDataGenerator.GenerateMultipleProjects(10);
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - Create multiple projects concurrently
        foreach (var project in projectRequests)
        {
            tasks.Add(_fixture.HttpClient.PostAsJsonAsync("/api/projects", project));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
        }

        // Verify all projects were created
        var userProjects = await _dbHelper.GetProjectsByUserAsync(_fixture.TestUserId);
        userProjects.Should().HaveCount(10);
    }

    [Fact]
    public async Task ProjectRepository_WithSpecialCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        var specialCharacterProject = new CreateProjectRequest(
            "Test Project with Special Characters: àáäâèéëêìíïîòóöôùúüûñç 中文 🚀");

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", specialCharacterProject);

        // Assert
        response.Should().BeSuccessful();
        var projectId = await response.Content.ReadFromJsonAsync<Guid>();

        var storedProject = await _dbHelper.GetProjectAsync(projectId);
        storedProject.Should().NotBeNull();
        storedProject!.ProjectName.Should().Be(specialCharacterProject.ProjectName);
    }

    [Fact]
    public async Task ProjectRepository_WithLongProjectName_ShouldHandleCorrectly()
    {
        // Arrange - Create a project name close to the 256 character limit
        var longName = new string('A', 250) + " Test";
        var longNameProject = new CreateProjectRequest(longName);

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", longNameProject);

        // Assert
        response.Should().BeSuccessful();
        var projectId = await response.Content.ReadFromJsonAsync<Guid>();

        var storedProject = await _dbHelper.GetProjectAsync(projectId);
        storedProject.Should().NotBeNull();
        storedProject!.ProjectName.Should().Be(longName);
    }
}

public record ProjectDto(Guid ProjectId, string ProjectName, DateTime CreatedAt);
public record CreateProjectRequest(string ProjectName);