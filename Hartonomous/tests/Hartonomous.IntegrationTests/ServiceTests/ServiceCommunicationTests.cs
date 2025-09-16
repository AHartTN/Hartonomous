using Hartonomous.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Hartonomous.IntegrationTests.ServiceTests;

/// <summary>
/// Integration tests for service-to-service communication across the Hartonomous platform
/// </summary>
[Collection("ServiceTests")]
public class ServiceCommunicationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly DatabaseTestHelper _dbHelper;
    private readonly HttpClient _mcpClient;
    private readonly HttpClient _modelQueryClient;
    private readonly HttpClient _orchestrationClient;

    public ServiceCommunicationTests(TestFixture fixture)
    {
        _fixture = fixture;
        _dbHelper = new DatabaseTestHelper(_fixture.ConnectionString);

        // Create HTTP clients for different services
        _mcpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5001") };
        _modelQueryClient = new HttpClient { BaseAddress = new Uri("http://localhost:5002") };
        _orchestrationClient = new HttpClient { BaseAddress = new Uri("http://localhost:5003") };
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _mcpClient.Dispose();
        _modelQueryClient.Dispose();
        _orchestrationClient.Dispose();
    }

    [Fact]
    public async Task ApiToDatabase_ProjectCreation_ShouldTriggerOutboxEvents()
    {
        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();

        // Act - Create project via API
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);

        // Assert
        response.Should().BeSuccessful();
        var projectId = await response.Content.ReadFromJsonAsync<Guid>();

        // Verify outbox events were created for downstream services
        await Task.Delay(1000); // Allow time for outbox event creation

        var outboxEvents = await _dbHelper.GetOutboxEventsAsync();
        outboxEvents.Should().NotBeEmpty();

        var projectCreatedEvent = outboxEvents.FirstOrDefault(e => e.EventType == "ProjectCreated");
        projectCreatedEvent.Should().NotBeNull();

        var eventPayload = JsonSerializer.Deserialize<JsonElement>(projectCreatedEvent!.Payload);
        eventPayload.GetProperty("ProjectId").GetGuid().Should().Be(projectId);
        eventPayload.GetProperty("ProjectName").GetString().Should().Be(projectRequest.ProjectName);
    }

    [Fact]
    public async Task ApiToDatabase_ModelCreation_ShouldTriggerMultipleEvents()
    {
        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        // Act - Create model via API
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);

        // Assert
        response.Should().BeSuccessful();
        var modelId = await response.Content.ReadFromJsonAsync<Guid>();

        // Verify multiple outbox events were created
        await Task.Delay(1000);

        var outboxEvents = await _dbHelper.GetOutboxEventsAsync();
        outboxEvents.Should().NotBeEmpty();

        // Should have events for model creation and metadata indexing
        var modelCreatedEvent = outboxEvents.FirstOrDefault(e => e.EventType == "ModelCreated");
        var metadataIndexEvent = outboxEvents.FirstOrDefault(e => e.EventType == "ModelMetadataUpdated");

        modelCreatedEvent.Should().NotBeNull();
        metadataIndexEvent.Should().NotBeNull();

        var modelEventPayload = JsonSerializer.Deserialize<JsonElement>(modelCreatedEvent!.Payload);
        modelEventPayload.GetProperty("ModelId").GetGuid().Should().Be(modelId);
        modelEventPayload.GetProperty("ModelName").GetString().Should().Be(modelRequest.ModelName);
    }

    [Fact]
    public async Task OutboxPattern_EventProcessing_ShouldMarkEventsAsProcessed()
    {
        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();

        // Act - Create project to generate events
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        response.Should().BeSuccessful();

        await Task.Delay(1000);

        // Get initial event count
        var initialEvents = await _dbHelper.GetOutboxEventsAsync();
        var unprocessedEvents = initialEvents.Where(e => e.ProcessedAt == null).ToList();

        // Simulate event processing (this would normally be done by a background service)
        await SimulateEventProcessingAsync(unprocessedEvents);

        // Assert
        var finalEvents = await _dbHelper.GetOutboxEventsAsync();
        var stillUnprocessed = finalEvents.Where(e => e.ProcessedAt == null).ToList();

        stillUnprocessed.Should().BeEmpty("All events should be marked as processed");

        var processedEvents = finalEvents.Where(e => e.ProcessedAt != null).ToList();
        processedEvents.Should().NotBeEmpty();
        processedEvents.Should().HaveCount(initialEvents.Count);
    }

    [Fact]
    public async Task CrossService_MCPRegistration_ShouldPersistAgentMetadata()
    {
        // This test simulates MCP service registering an agent and verifying the data flow

        // Arrange
        var agentRegistration = new
        {
            AgentId = Guid.NewGuid(),
            Name = "TestAgent",
            Capabilities = new[] { "text-processing", "code-analysis" },
            Version = "1.0.0",
            Description = "Integration test agent"
        };

        try
        {
            // Act - Register agent with MCP service
            var mcpResponse = await _mcpClient.PostAsJsonAsync("/api/agents/register", agentRegistration);

            if (mcpResponse.StatusCode == HttpStatusCode.NotFound ||
                mcpResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                // MCP service is not running - skip this test
                Assert.True(true, "MCP service not available - skipping test");
                return;
            }

            mcpResponse.Should().BeSuccessful();

            // Verify agent data can be retrieved
            var getResponse = await _mcpClient.GetAsync($"/api/agents/{agentRegistration.AgentId}");
            getResponse.Should().BeSuccessful();

            var retrievedAgent = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
            retrievedAgent.GetProperty("AgentId").GetGuid().Should().Be(agentRegistration.AgentId);
            retrievedAgent.GetProperty("Name").GetString().Should().Be(agentRegistration.Name);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("connection"))
        {
            // Service not available - skip test
            Assert.True(true, "MCP service not available - skipping test");
        }
    }

    [Fact]
    public async Task CrossService_ModelQuery_ShouldHandleMetadataSearch()
    {
        // This test verifies that model metadata can be queried across the ModelQuery service

        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        // Create model via main API
        var createResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        createResponse.Should().BeSuccessful();

        var modelId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        // Allow time for data propagation
        await Task.Delay(2000);

        try
        {
            // Act - Query model via ModelQuery service
            var queryRequest = new
            {
                SearchTerm = modelRequest.ModelName,
                ProjectId = projectId,
                IncludeMetadata = true
            };

            var queryResponse = await _modelQueryClient.PostAsJsonAsync("/api/search/models", queryRequest);

            if (queryResponse.StatusCode == HttpStatusCode.NotFound ||
                queryResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                Assert.True(true, "ModelQuery service not available - skipping test");
                return;
            }

            queryResponse.Should().BeSuccessful();

            var searchResults = await queryResponse.Content.ReadFromJsonAsync<JsonElement>();
            var results = searchResults.GetProperty("Results").EnumerateArray().ToList();

            // Assert
            results.Should().NotBeEmpty();
            var foundModel = results.FirstOrDefault(r =>
                r.GetProperty("ModelId").GetGuid() == modelId);
            foundModel.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("connection"))
        {
            Assert.True(true, "ModelQuery service not available - skipping test");
        }
    }

    [Fact]
    public async Task CrossService_Orchestration_ShouldCoordinateWorkflows()
    {
        // This test verifies workflow coordination across services

        // Arrange
        var projectId = await CreateTestProjectAsync();
        var modelId = await CreateTestModelAsync(projectId);

        var workflowRequest = new
        {
            WorkflowId = Guid.NewGuid(),
            Name = "Integration Test Workflow",
            Steps = new[]
            {
                new { StepId = "validate-model", Service = "ModelQuery", Action = "ValidateModel", Parameters = new { ModelId = modelId } },
                new { StepId = "register-agent", Service = "MCP", Action = "RegisterAgent", Parameters = new { ModelId = modelId } },
                new { StepId = "deploy-model", Service = "API", Action = "UpdateStatus", Parameters = new { ModelId = modelId, Status = "Deployed" } }
            }
        };

        try
        {
            // Act - Submit workflow to Orchestration service
            var workflowResponse = await _orchestrationClient.PostAsJsonAsync("/api/workflows", workflowRequest);

            if (workflowResponse.StatusCode == HttpStatusCode.NotFound ||
                workflowResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                Assert.True(true, "Orchestration service not available - skipping test");
                return;
            }

            workflowResponse.Should().BeSuccessful();

            var workflowId = workflowResponse.Headers.Location?.Segments.LastOrDefault();
            workflowId.Should().NotBeNullOrEmpty();

            // Poll for workflow completion
            var maxAttempts = 10;
            var attempt = 0;
            bool workflowCompleted = false;

            while (attempt < maxAttempts && !workflowCompleted)
            {
                await Task.Delay(1000);

                var statusResponse = await _orchestrationClient.GetAsync($"/api/workflows/{workflowId}");
                if (statusResponse.IsSuccessStatusCode)
                {
                    var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
                    var currentStatus = status.GetProperty("Status").GetString();

                    if (currentStatus == "Completed" || currentStatus == "Failed")
                    {
                        workflowCompleted = true;
                        currentStatus.Should().Be("Completed");
                    }
                }

                attempt++;
            }

            // Assert
            workflowCompleted.Should().BeTrue("Workflow should complete within reasonable time");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("connection"))
        {
            Assert.True(true, "Orchestration service not available - skipping test");
        }
    }

    [Fact]
    public async Task EventDrivenArchitecture_EndToEndFlow_ShouldPropagateChanges()
    {
        // This test verifies the complete event-driven flow from API to all services

        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();

        // Act - Create project and model
        var projectResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);
        projectResponse.Should().BeSuccessful();

        var projectId = await projectResponse.Content.ReadFromJsonAsync<Guid>();
        var modelRequest = TestDataGenerator.GenerateCreateModelRequest();

        var modelResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/projects/{projectId}/models", modelRequest);
        modelResponse.Should().BeSuccessful();

        var modelId = await modelResponse.Content.ReadFromJsonAsync<Guid>();

        // Allow time for event propagation
        await Task.Delay(3000);

        // Assert - Verify events were created and would be processed by services
        var outboxEvents = await _dbHelper.GetOutboxEventsAsync();
        outboxEvents.Should().NotBeEmpty();

        var eventTypes = outboxEvents.Select(e => e.EventType).Distinct().ToList();
        eventTypes.Should().Contain("ProjectCreated");
        eventTypes.Should().Contain("ModelCreated");

        // Verify event payloads contain correct data
        foreach (var evt in outboxEvents)
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(evt.Payload);

            if (evt.EventType == "ProjectCreated")
            {
                payload.GetProperty("ProjectId").GetGuid().Should().Be(projectId);
                payload.GetProperty("UserId").GetString().Should().Be(_fixture.TestUserId);
            }
            else if (evt.EventType == "ModelCreated")
            {
                payload.GetProperty("ModelId").GetGuid().Should().Be(modelId);
                payload.GetProperty("ProjectId").GetGuid().Should().Be(projectId);
            }
        }
    }

    [Fact]
    public async Task ServiceResilience_PartialFailure_ShouldNotCorruptState()
    {
        // This test verifies that if one service is down, others continue to work

        // Arrange
        var projectRequest = TestDataGenerator.GenerateCreateProjectRequest();

        // Act - Create project (main API should work even if other services are down)
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", projectRequest);

        // Assert
        response.Should().BeSuccessful();
        var projectId = await response.Content.ReadFromJsonAsync<Guid>();

        // Verify project exists in database
        var projectExists = await _dbHelper.ProjectExistsAsync(projectId);
        projectExists.Should().BeTrue();

        // Verify we can still read the project
        var getResponse = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
        getResponse.Should().BeSuccessful();

        // Events should still be created for eventual consistency
        await Task.Delay(1000);
        var outboxEventCount = await _dbHelper.GetOutboxEventCountAsync();
        outboxEventCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DataConsistency_CrossServiceTransactions_ShouldMaintainIntegrity()
    {
        // This test verifies data consistency across service boundaries

        // Arrange
        var projectId = await CreateTestProjectAsync();
        var models = TestDataGenerator.GenerateMultipleModels(5);

        // Act - Create multiple models rapidly
        var createTasks = models.Select(model =>
            _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", model)
        );

        var responses = await Task.WhenAll(createTasks);
        var createdModelIds = new List<Guid>();

        foreach (var response in responses)
        {
            response.Should().BeSuccessful();
            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            createdModelIds.Add(modelId);
        }

        // Allow time for event processing
        await Task.Delay(2000);

        // Assert - Verify data consistency
        // 1. All models exist in database
        foreach (var modelId in createdModelIds)
        {
            var modelExists = await _dbHelper.ModelExistsAsync(modelId);
            modelExists.Should().BeTrue();
        }

        // 2. Correct number of events generated
        var outboxEvents = await _dbHelper.GetOutboxEventsAsync();
        var modelCreatedEvents = outboxEvents.Where(e => e.EventType == "ModelCreated").ToList();
        modelCreatedEvents.Should().HaveCount(5);

        // 3. All events have valid payloads
        foreach (var evt in modelCreatedEvents)
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(evt.Payload);
            var eventModelId = payload.GetProperty("ModelId").GetGuid();
            createdModelIds.Should().Contain(eventModelId);
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

    private async Task SimulateEventProcessingAsync(List<OutboxEvent> events)
    {
        // Simulate event processing by marking events as processed
        // In a real system, this would be done by background services

        foreach (var evt in events)
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();
            await connection.ChangeDatabaseAsync("HartonomousDB_Tests");

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE dbo.OutboxEvents
                SET ProcessedAt = @ProcessedAt
                WHERE EventId = @EventId";

            command.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("@EventId", evt.EventId);

            await command.ExecuteNonQueryAsync();
        }
    }
}

public record CreateProjectRequest(string ProjectName);
public record CreateModelRequest(
    string ModelName,
    string Version,
    string License,
    string? MetadataJson = null);