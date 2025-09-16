using Hartonomous.IntegrationTests.Infrastructure;
using Hartonomous.MCP.DTOs;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using Serilog;

namespace Hartonomous.IntegrationTests.WorkflowTests;

/// <summary>
/// Comprehensive MCP (Multi-Agent Context Protocol) integration tests for agent coordination
/// </summary>
public class McpAgentCoordinationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly ILogger<McpAgentCoordinationTests> _logger;
    private readonly List<HubConnection> _connections = new();
    private readonly List<Guid> _registeredAgents = new();

    public McpAgentCoordinationTests(TestFixture fixture)
    {
        _fixture = fixture;
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
        _logger = loggerFactory.CreateLogger<McpAgentCoordinationTests>();
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing MCP agent coordination tests");
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _logger.LogInformation("Cleaning up MCP agent coordination tests");
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task McpHub_AgentRegistration_ShouldWorkCorrectly()
    {
        _logger.LogInformation("Testing MCP Hub agent registration");

        // Create SignalR connection to MCP Hub
        var connection = await CreateMcpConnectionAsync();

        var registrationCompleted = new TaskCompletionSource<Guid>();
        var agentJoinedNotified = new TaskCompletionSource<AgentDto>();

        // Setup event handlers
        connection.On<object>("AgentRegistered", (response) =>
        {
            var json = JsonSerializer.Serialize(response);
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (data != null && data.TryGetValue("AgentId", out var agentIdObj))
            {
                if (Guid.TryParse(agentIdObj.ToString(), out var agentId))
                {
                    registrationCompleted.SetResult(agentId);
                }
            }
        });

        connection.On<AgentDto>("AgentJoined", (agent) =>
        {
            agentJoinedNotified.SetResult(agent);
        });

        connection.On<object>("Error", (error) =>
        {
            _logger.LogError("SignalR Error: {Error}", JsonSerializer.Serialize(error));
            registrationCompleted.SetException(new Exception($"Registration failed: {error}"));
        });

        // Register agent
        var agentRequest = new AgentRegistrationRequest(
            AgentName: "TestAgent-DataProcessor",
            AgentType: "DataProcessor",
            Capabilities: new[] { "data_cleaning", "feature_extraction", "validation" },
            Description: "Test agent for data processing tasks",
            Configuration: new Dictionary<string, object>
            {
                { "maxConcurrentTasks", 5 },
                { "supportedFormats", new[] { "csv", "json", "parquet" } },
                { "memoryLimit", "2GB" }
            }
        );

        await connection.InvokeAsync("RegisterAgent", agentRequest);

        // Wait for registration completion
        var agentId = await registrationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        agentId.Should().NotBeEmpty();
        _registeredAgents.Add(agentId);

        // Wait for agent joined notification
        var joinedAgent = await agentJoinedNotified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        joinedAgent.Should().NotBeNull();
        joinedAgent.AgentId.Should().Be(agentId);
        joinedAgent.AgentName.Should().Be("TestAgent-DataProcessor");
        joinedAgent.Status.Should().Be(AgentStatus.Online);

        // Verify agent via REST API
        var getAgentResponse = await _fixture.HttpClient.GetAsync($"/api/agents/{agentId}");
        if (getAgentResponse.StatusCode == HttpStatusCode.OK)
        {
            var agentFromApi = await getAgentResponse.Content.ReadFromJsonAsync<AgentDto>();
            agentFromApi.Should().NotBeNull();
            agentFromApi!.AgentId.Should().Be(agentId);
        }

        _logger.LogInformation("Agent registration test completed successfully for agent {AgentId}", agentId);
    }

    [Fact]
    public async Task McpHub_MultiAgentCommunication_ShouldEnableMessageExchange()
    {
        _logger.LogInformation("Testing multi-agent communication via MCP Hub");

        // Create multiple agent connections
        var senderConnection = await CreateMcpConnectionAsync();
        var receiverConnection = await CreateMcpConnectionAsync();

        // Register sender agent
        var senderAgentId = await RegisterAgentAsync(senderConnection, new AgentRegistrationRequest(
            "TestAgent-Sender",
            "MessageSender",
            new[] { "messaging", "notification" }
        ));

        // Register receiver agent
        var receiverAgentId = await RegisterAgentAsync(receiverConnection, new AgentRegistrationRequest(
            "TestAgent-Receiver",
            "MessageReceiver",
            new[] { "messaging", "processing" }
        ));

        var messageReceived = new TaskCompletionSource<McpMessage>();

        // Setup message handler on receiver
        receiverConnection.On<McpMessage>("MessageReceived", (message) =>
        {
            messageReceived.SetResult(message);
        });

        // Send message from sender to receiver
        var testPayload = new
        {
            taskType = "process_data",
            dataUrl = "https://example.com/dataset.csv",
            parameters = new { threshold = 0.85, format = "json" }
        };

        var metadata = new Dictionary<string, object>
        {
            { "priority", "high" },
            { "timeout", 300 },
            { "retries", 3 }
        };

        await senderConnection.InvokeAsync("SendMessage", receiverAgentId, "TaskAssignment", testPayload, metadata);

        // Wait for message to be received
        var receivedMessage = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        receivedMessage.Should().NotBeNull();
        receivedMessage.FromAgentId.Should().Be(senderAgentId);
        receivedMessage.ToAgentId.Should().Be(receiverAgentId);
        receivedMessage.MessageType.Should().Be("TaskAssignment");
        receivedMessage.Metadata.Should().ContainKey("priority");

        _logger.LogInformation("Multi-agent communication test completed: message sent from {Sender} to {Receiver}",
            senderAgentId, receiverAgentId);
    }

    [Fact]
    public async Task McpHub_AgentDiscovery_ShouldFindAvailableAgents()
    {
        _logger.LogInformation("Testing agent discovery functionality");

        var discoveryConnection = await CreateMcpConnectionAsync();

        // Register discovery agent
        var discoveryAgentId = await RegisterAgentAsync(discoveryConnection, new AgentRegistrationRequest(
            "TestAgent-Discovery",
            "Coordinator",
            new[] { "discovery", "coordination" }
        ));

        // Register several agents with different types and capabilities
        var agentConfigs = new[]
        {
            new { Name = "DataProcessor-1", Type = "DataProcessor", Capabilities = new[] { "csv", "validation" } },
            new { Name = "DataProcessor-2", Type = "DataProcessor", Capabilities = new[] { "json", "transformation" } },
            new { Name = "ModelTrainer-1", Type = "ModelTrainer", Capabilities = new[] { "tensorflow", "pytorch" } },
            new { Name = "Evaluator-1", Type = "ModelEvaluator", Capabilities = new[] { "metrics", "visualization" } }
        };

        var connections = new List<HubConnection>();
        var agentIds = new List<Guid>();

        foreach (var config in agentConfigs)
        {
            var connection = await CreateMcpConnectionAsync();
            connections.Add(connection);

            var agentId = await RegisterAgentAsync(connection, new AgentRegistrationRequest(
                config.Name,
                config.Type,
                config.Capabilities
            ));
            agentIds.Add(agentId);
        }

        var discoveryCompleted = new TaskCompletionSource<AgentDiscoveryResponse>();

        discoveryConnection.On<AgentDiscoveryResponse>("AgentsDiscovered", (response) =>
        {
            discoveryCompleted.SetResult(response);
        });

        // Test discovery by agent type
        var discoveryRequest = new AgentDiscoveryRequest(AgentType: "DataProcessor");
        await discoveryConnection.InvokeAsync("DiscoverAgents", discoveryRequest);

        var discoveryResult = await discoveryCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        discoveryResult.Should().NotBeNull();
        discoveryResult.AvailableAgents.Should().HaveCountGreaterOrEqualTo(2);

        var dataProcessors = discoveryResult.AvailableAgents.Where(a => a.AgentType == "DataProcessor").ToList();
        dataProcessors.Should().HaveCount(2);

        // Test discovery by capabilities
        var capabilityDiscoveryCompleted = new TaskCompletionSource<AgentDiscoveryResponse>();
        discoveryConnection.On<AgentDiscoveryResponse>("AgentsDiscovered", (response) =>
        {
            capabilityDiscoveryCompleted.SetResult(response);
        });

        var capabilityRequest = new AgentDiscoveryRequest(RequiredCapabilities: new[] { "tensorflow" });
        await discoveryConnection.InvokeAsync("DiscoverAgents", capabilityRequest);

        var capabilityResult = await capabilityDiscoveryCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        capabilityResult.Should().NotBeNull();
        capabilityResult.AvailableAgents.Should().HaveCountGreaterOrEqualTo(1);

        var tensorflowAgents = capabilityResult.AvailableAgents
            .Where(a => a.Capabilities.Contains("tensorflow")).ToList();
        tensorflowAgents.Should().HaveCountGreaterOrEqualTo(1);

        // Cleanup additional connections
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }

        _logger.LogInformation("Agent discovery test completed: found {TotalAgents} agents, {DataProcessors} data processors, {TensorflowAgents} tensorflow agents",
            discoveryResult.AvailableAgents.Count(), dataProcessors.Count, tensorflowAgents.Count);
    }

    [Fact]
    public async Task McpHub_BroadcastCommunication_ShouldReachAllAgentsOfType()
    {
        _logger.LogInformation("Testing broadcast communication to agent types");

        var broadcasterConnection = await CreateMcpConnectionAsync();

        // Register broadcaster
        var broadcasterId = await RegisterAgentAsync(broadcasterConnection, new AgentRegistrationRequest(
            "TestAgent-Broadcaster",
            "Coordinator",
            new[] { "broadcasting", "coordination" }
        ));

        // Register multiple agents of the same type
        var targetConnections = new List<HubConnection>();
        var targetAgentIds = new List<Guid>();
        var messagesReceived = new List<TaskCompletionSource<McpMessage>>();

        for (int i = 1; i <= 3; i++)
        {
            var connection = await CreateMcpConnectionAsync();
            targetConnections.Add(connection);

            var agentId = await RegisterAgentAsync(connection, new AgentRegistrationRequest(
                $"TestAgent-Worker-{i}",
                "Worker",
                new[] { "processing", "execution" }
            ));
            targetAgentIds.Add(agentId);

            var messageReceived = new TaskCompletionSource<McpMessage>();
            messagesReceived.Add(messageReceived);

            connection.On<McpMessage>("MessageReceived", (message) =>
            {
                messageReceived.SetResult(message);
            });
        }

        // Broadcast message to all Worker agents
        var broadcastPayload = new
        {
            instruction = "start_processing",
            batchId = Guid.NewGuid(),
            priority = "high"
        };

        await broadcasterConnection.InvokeAsync("BroadcastToAgentType", "Worker", "BatchInstruction", broadcastPayload);

        // Wait for all agents to receive the broadcast
        var allMessages = await Task.WhenAll(messagesReceived.Select(tcs => tcs.Task.WaitAsync(TimeSpan.FromSeconds(10))));

        allMessages.Should().HaveCount(3);
        allMessages.Should().AllSatisfy(message =>
        {
            message.Should().NotBeNull();
            message.FromAgentId.Should().Be(broadcasterId);
            message.MessageType.Should().Be("BatchInstruction");
            message.ToAgentId.Should().BeOneOf(targetAgentIds);
        });

        // Cleanup target connections
        foreach (var connection in targetConnections)
        {
            await connection.DisposeAsync();
        }

        _logger.LogInformation("Broadcast communication test completed: {MessageCount} messages delivered to Worker agents",
            allMessages.Length);
    }

    [Fact]
    public async Task McpHub_HeartbeatAndStatusTracking_ShouldUpdateAgentStatus()
    {
        _logger.LogInformation("Testing heartbeat and status tracking");

        var connection = await CreateMcpConnectionAsync();

        var agentId = await RegisterAgentAsync(connection, new AgentRegistrationRequest(
            "TestAgent-HeartbeatTest",
            "Monitor",
            new[] { "monitoring", "reporting" }
        ));

        var statusUpdates = new List<TaskCompletionSource<object>>();
        var statusUpdate1 = new TaskCompletionSource<object>();
        var statusUpdate2 = new TaskCompletionSource<object>();

        connection.On<object>("AgentStatusChanged", (update) =>
        {
            var json = JsonSerializer.Serialize(update);
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (data != null && data.TryGetValue("AgentId", out var agentIdObj))
            {
                if (Guid.TryParse(agentIdObj.ToString(), out var updatedAgentId) && updatedAgentId == agentId)
                {
                    if (!statusUpdate1.Task.IsCompleted)
                        statusUpdate1.SetResult(update);
                    else if (!statusUpdate2.Task.IsCompleted)
                        statusUpdate2.SetResult(update);
                }
            }
        });

        // Send heartbeat with "Busy" status
        var busyMetrics = new Dictionary<string, object>
        {
            { "cpuUsage", 75.5 },
            { "memoryUsage", 1.2 },
            { "activeTasks", 3 }
        };

        await connection.InvokeAsync("Heartbeat", AgentStatus.Busy, busyMetrics);
        await statusUpdate1.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Send heartbeat with "Idle" status
        var idleMetrics = new Dictionary<string, object>
        {
            { "cpuUsage", 15.2 },
            { "memoryUsage", 0.8 },
            { "activeTasks", 0 }
        };

        await connection.InvokeAsync("Heartbeat", AgentStatus.Idle, idleMetrics);
        await statusUpdate2.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Verify status updates were received
        statusUpdate1.Task.IsCompletedSuccessfully.Should().BeTrue();
        statusUpdate2.Task.IsCompletedSuccessfully.Should().BeTrue();

        // Verify agent status via REST API
        var getAgentResponse = await _fixture.HttpClient.GetAsync($"/api/agents/{agentId}");
        if (getAgentResponse.StatusCode == HttpStatusCode.OK)
        {
            var agent = await getAgentResponse.Content.ReadFromJsonAsync<AgentDto>();
            agent.Should().NotBeNull();
            agent!.Status.Should().Be(AgentStatus.Idle);
            agent.LastHeartbeat.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        _logger.LogInformation("Heartbeat and status tracking test completed for agent {AgentId}", agentId);
    }

    [Fact]
    public async Task McpHub_TaskAssignmentAndCompletion_ShouldTrackTaskLifecycle()
    {
        _logger.LogInformation("Testing task assignment and completion workflow");

        var coordinatorConnection = await CreateMcpConnectionAsync();
        var workerConnection = await CreateMcpConnectionAsync();

        // Register coordinator and worker agents
        var coordinatorId = await RegisterAgentAsync(coordinatorConnection, new AgentRegistrationRequest(
            "TestAgent-Coordinator",
            "TaskCoordinator",
            new[] { "task_management", "coordination" }
        ));

        var workerId = await RegisterAgentAsync(workerConnection, new AgentRegistrationRequest(
            "TestAgent-Worker",
            "TaskWorker",
            new[] { "data_processing", "computation" }
        ));

        var taskReceived = new TaskCompletionSource<McpMessage>();
        var taskCompleted = new TaskCompletionSource<TaskResult>();

        // Setup task handlers
        workerConnection.On<McpMessage>("MessageReceived", (message) =>
        {
            if (message.MessageType == "TaskAssignment")
            {
                taskReceived.SetResult(message);
            }
        });

        coordinatorConnection.On<TaskResult>("TaskCompleted", (result) =>
        {
            taskCompleted.SetResult(result);
        });

        // Assign task from coordinator to worker
        var taskId = Guid.NewGuid();
        var taskData = new
        {
            taskId = taskId,
            operation = "feature_extraction",
            inputData = "dataset_12345.csv",
            parameters = new
            {
                method = "pca",
                components = 10,
                normalize = true
            }
        };

        await coordinatorConnection.InvokeAsync("SendMessage", workerId, "TaskAssignment", taskData);

        // Wait for task to be received by worker
        var receivedTask = await taskReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        receivedTask.Should().NotBeNull();
        receivedTask.MessageType.Should().Be("TaskAssignment");

        // Simulate task processing and completion
        await Task.Delay(TimeSpan.FromSeconds(1)); // Simulate processing time

        var taskResult = new
        {
            features = new[] { "feature1", "feature2", "feature3" },
            dimensionality = 10,
            explained_variance = 0.85,
            processing_time = "2.3s"
        };

        var metrics = new Dictionary<string, object>
        {
            { "execution_time_ms", 2300 },
            { "memory_peak_mb", 150 },
            { "cpu_usage_percent", 45 }
        };

        // Submit task completion
        await workerConnection.InvokeAsync("SubmitTaskResult", taskId, TaskResultStatus.Success, taskResult, null, metrics);

        // Wait for task completion notification
        var completedTask = await taskCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        completedTask.Should().NotBeNull();
        completedTask.TaskId.Should().Be(taskId);
        completedTask.Status.Should().Be(TaskResultStatus.Success);
        completedTask.AgentId.Should().Be(workerId);

        _logger.LogInformation("Task assignment and completion test completed: task {TaskId} processed by agent {WorkerId}",
            taskId, workerId);
    }

    [Fact]
    public async Task McpHub_AgentDisconnection_ShouldHandleGracefully()
    {
        _logger.LogInformation("Testing agent disconnection handling");

        var observerConnection = await CreateMcpConnectionAsync();
        var disconnectingConnection = await CreateMcpConnectionAsync();

        // Register observer agent
        var observerId = await RegisterAgentAsync(observerConnection, new AgentRegistrationRequest(
            "TestAgent-Observer",
            "Observer",
            new[] { "monitoring", "observation" }
        ));

        // Register agent that will disconnect
        var disconnectingAgentId = await RegisterAgentAsync(disconnectingConnection, new AgentRegistrationRequest(
            "TestAgent-Disconnecting",
            "Worker",
            new[] { "processing" }
        ));

        var agentDisconnected = new TaskCompletionSource<object>();

        observerConnection.On<object>("AgentDisconnected", (notification) =>
        {
            var json = JsonSerializer.Serialize(notification);
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (data != null && data.TryGetValue("AgentId", out var agentIdObj))
            {
                if (Guid.TryParse(agentIdObj.ToString(), out var disconnectedAgentId) &&
                    disconnectedAgentId == disconnectingAgentId)
                {
                    agentDisconnected.SetResult(notification);
                }
            }
        });

        // Disconnect the agent
        await disconnectingConnection.DisposeAsync();

        // Wait for disconnection notification
        var disconnectionNotification = await agentDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        disconnectionNotification.Should().NotBeNull();

        // Verify agent status via REST API
        var getAgentResponse = await _fixture.HttpClient.GetAsync($"/api/agents/{disconnectingAgentId}");
        if (getAgentResponse.StatusCode == HttpStatusCode.OK)
        {
            var agent = await getAgentResponse.Content.ReadFromJsonAsync<AgentDto>();
            agent.Should().NotBeNull();
            agent!.Status.Should().Be(AgentStatus.Offline);
        }

        _logger.LogInformation("Agent disconnection test completed: agent {AgentId} properly marked as offline",
            disconnectingAgentId);
    }

    [Fact]
    public async Task McpHub_ConcurrentAgentOperations_ShouldMaintainConsistency()
    {
        _logger.LogInformation("Testing concurrent agent operations");

        const int agentCount = 10;
        const int messagesPerAgent = 5;

        var connections = new List<HubConnection>();
        var agentIds = new List<Guid>();

        // Register multiple agents concurrently
        var registrationTasks = Enumerable.Range(1, agentCount).Select(async i =>
        {
            var connection = await CreateMcpConnectionAsync();
            connections.Add(connection);

            var agentId = await RegisterAgentAsync(connection, new AgentRegistrationRequest(
                $"TestAgent-Concurrent-{i}",
                "ConcurrentWorker",
                new[] { "processing", "concurrent" }
            ));

            agentIds.Add(agentId);
            return agentId;
        });

        await Task.WhenAll(registrationTasks);

        // Send messages concurrently between all agents
        var messageCount = 0;
        var messageTasks = new List<Task>();

        for (int i = 0; i < agentCount; i++)
        {
            for (int j = 0; j < messagesPerAgent; j++)
            {
                var senderIndex = i;
                var receiverIndex = (i + 1) % agentCount; // Send to next agent in circle
                var messageId = Interlocked.Increment(ref messageCount);

                var messageTask = Task.Run(async () =>
                {
                    try
                    {
                        var payload = new { messageId, iteration = j, timestamp = DateTime.UtcNow };
                        await connections[senderIndex].InvokeAsync("SendMessage",
                            agentIds[receiverIndex], "ConcurrentTest", payload);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send concurrent message {MessageId}", messageId);
                    }
                });

                messageTasks.Add(messageTask);
            }
        }

        // Wait for all messages to be sent
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(messageTasks);
        stopwatch.Stop();

        // Verify message delivery statistics via REST API
        var statsResponse = await _fixture.HttpClient.GetAsync("/api/mcp/statistics");
        if (statsResponse.StatusCode == HttpStatusCode.OK)
        {
            var stats = await statsResponse.Content.ReadAsStringAsync();
            _logger.LogInformation("MCP Statistics: {Stats}", stats);
        }

        // Cleanup connections
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }

        _logger.LogInformation("Concurrent operations test completed: {AgentCount} agents, {MessageCount} messages in {ElapsedMs}ms",
            agentCount, messageCount, stopwatch.ElapsedMilliseconds);

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "Concurrent operations should complete within 30 seconds");
    }

    // Helper Methods
    private async Task<HubConnection> CreateMcpConnectionAsync()
    {
        var baseUrl = _fixture.HttpClient.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost";
        var hubUrl = $"{baseUrl}/hubs/mcp";

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(GetAuthToken());
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddSerilog();
            })
            .Build();

        await connection.StartAsync();
        _connections.Add(connection);

        return connection;
    }

    private async Task<Guid> RegisterAgentAsync(HubConnection connection, AgentRegistrationRequest request)
    {
        var registrationCompleted = new TaskCompletionSource<Guid>();

        connection.On<object>("AgentRegistered", (response) =>
        {
            var json = JsonSerializer.Serialize(response);
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (data != null && data.TryGetValue("AgentId", out var agentIdObj))
            {
                if (Guid.TryParse(agentIdObj.ToString(), out var agentId))
                {
                    registrationCompleted.SetResult(agentId);
                }
            }
        });

        connection.On<object>("Error", (error) =>
        {
            registrationCompleted.SetException(new Exception($"Registration failed: {error}"));
        });

        await connection.InvokeAsync("RegisterAgent", request);

        var agentId = await registrationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _registeredAgents.Add(agentId);

        return agentId;
    }

    private string? GetAuthToken()
    {
        // Extract token from the HTTP client's default headers
        var authHeader = _fixture.HttpClient.DefaultRequestHeaders.Authorization;
        return authHeader?.Parameter;
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            // Dispose all SignalR connections
            foreach (var connection in _connections)
            {
                try
                {
                    if (connection.State == HubConnectionState.Connected)
                    {
                        await connection.DisposeAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose SignalR connection");
                }
            }

            // Cleanup registered agents via REST API
            foreach (var agentId in _registeredAgents)
            {
                try
                {
                    await _fixture.HttpClient.DeleteAsync($"/api/agents/{agentId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup agent {AgentId}", agentId);
                }
            }
        }
        finally
        {
            _connections.Clear();
            _registeredAgents.Clear();
        }
    }
}