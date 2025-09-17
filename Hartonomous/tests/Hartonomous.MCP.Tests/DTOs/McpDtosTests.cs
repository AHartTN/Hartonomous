using Hartonomous.Core.Shared.DTOs;

namespace Hartonomous.MCP.Tests.DTOs;

public class McpDtosTests
{
    [Fact]
    public void AgentRegistrationRequest_ShouldCreateWithRequiredProperties()
    {
        // Arrange & Act
        var request = new AgentRegistrationRequest(
            "TestAgent",
            "CodeGenerator",
            new[] { "generate-code", "analyze-code" },
            "Test description",
            new Dictionary<string, object> { { "key", "value" } }
        );

        // Assert
        Assert.Equal("TestAgent", request.AgentName);
        Assert.Equal("CodeGenerator", request.AgentType);
        Assert.Contains("generate-code", request.Capabilities);
        Assert.Contains("analyze-code", request.Capabilities);
        Assert.Equal("Test description", request.Description);
        Assert.NotNull(request.Configuration);
        Assert.Equal("value", request.Configuration["key"]);
    }

    [Fact]
    public void AgentDto_ShouldCreateWithAllProperties()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var registeredAt = DateTime.UtcNow;
        var lastHeartbeat = DateTime.UtcNow;
        var capabilities = new[] { "generate-code", "analyze-code" };

        // Act
        var agent = new AgentDto(
            agentId,
            "TestAgent",
            "CodeGenerator",
            "conn-123",
            capabilities,
            "Test description",
            new Dictionary<string, object> { { "key", "value" } },
            registeredAt,
            lastHeartbeat,
            AgentStatus.Online
        );

        // Assert
        Assert.Equal(agentId, agent.AgentId);
        Assert.Equal("TestAgent", agent.AgentName);
        Assert.Equal("CodeGenerator", agent.AgentType);
        Assert.Equal("conn-123", agent.ConnectionId);
        Assert.Equal(capabilities, agent.Capabilities);
        Assert.Equal("Test description", agent.Description);
        Assert.Equal(registeredAt, agent.RegisteredAt);
        Assert.Equal(lastHeartbeat, agent.LastHeartbeat);
        Assert.Equal(AgentStatus.Online, agent.Status);
    }

    [Fact]
    public void McpMessage_ShouldSetTimestampAutomatically()
    {
        // Arrange
        var fromAgentId = Guid.NewGuid();
        var toAgentId = Guid.NewGuid();
        var beforeCreation = DateTime.UtcNow;

        // Act
        var message = new McpMessage(
            Guid.NewGuid(),
            fromAgentId,
            toAgentId,
            "TestMessage",
            new { data = "test" }
        );

        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(message.Timestamp >= beforeCreation);
        Assert.True(message.Timestamp <= afterCreation);
    }

    [Fact]
    public void McpMessage_ShouldUseProvidedTimestamp()
    {
        // Arrange
        var specificTimestamp = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var message = new McpMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TestMessage",
            new { data = "test" },
            null,
            specificTimestamp
        );

        // Assert
        Assert.Equal(specificTimestamp, message.Timestamp);
    }

    [Fact]
    public void WorkflowDefinition_ShouldCreateWithSteps()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var steps = new[]
        {
            new WorkflowStep(Guid.NewGuid(), "Step1", "CodeGenerator", new { input = "test" }),
            new WorkflowStep(Guid.NewGuid(), "Step2", "CodeAnalyzer", new { input = "test2" })
        };

        // Act
        var workflow = new WorkflowDefinition(
            workflowId,
            "Test Workflow",
            "Test workflow description",
            steps,
            new Dictionary<string, object> { { "param1", "value1" } }
        );

        // Assert
        Assert.Equal(workflowId, workflow.WorkflowId);
        Assert.Equal("Test Workflow", workflow.WorkflowName);
        Assert.Equal("Test workflow description", workflow.Description);
        Assert.Equal(2, workflow.Steps.Count());
        Assert.NotNull(workflow.Parameters);
        Assert.Equal("value1", workflow.Parameters["param1"]);
    }

    [Fact]
    public void WorkflowStep_ShouldCreateWithDependencies()
    {
        // Arrange
        var stepId = Guid.NewGuid();
        var dependencies = new[] { "Step1", "Step2" };

        // Act
        var step = new WorkflowStep(
            stepId,
            "Step3",
            "CodeGenerator",
            new { input = "test" },
            dependencies,
            new Dictionary<string, object> { { "config", "value" } }
        );

        // Assert
        Assert.Equal(stepId, step.StepId);
        Assert.Equal("Step3", step.StepName);
        Assert.Equal("CodeGenerator", step.AgentType);
        Assert.Equal(dependencies, step.DependsOn);
        Assert.NotNull(step.Configuration);
        Assert.Equal("value", step.Configuration["config"]);
    }

    [Fact]
    public void TaskAssignment_ShouldCreateWithPriorityAndDueDate()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var dueDate = DateTime.UtcNow.AddHours(1);

        // Act
        var task = new TaskAssignment(
            taskId,
            agentId,
            "GenerateCode",
            new { code = "test" },
            5,
            dueDate,
            new Dictionary<string, object> { { "urgent", true } }
        );

        // Assert
        Assert.Equal(taskId, task.TaskId);
        Assert.Equal(agentId, task.AgentId);
        Assert.Equal("GenerateCode", task.TaskType);
        Assert.Equal(5, task.Priority);
        Assert.Equal(dueDate, task.DueDate);
        Assert.NotNull(task.Metadata);
        Assert.True((bool)task.Metadata["urgent"]);
    }

    [Theory]
    [InlineData(AgentStatus.Connecting)]
    [InlineData(AgentStatus.Online)]
    [InlineData(AgentStatus.Busy)]
    [InlineData(AgentStatus.Idle)]
    [InlineData(AgentStatus.Offline)]
    [InlineData(AgentStatus.Error)]
    public void AgentStatus_ShouldHaveAllValidValues(AgentStatus status)
    {
        // Arrange & Act
        var statusValue = (int)status;

        // Assert
        Assert.True(statusValue >= 0 && statusValue <= 5);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Pending)]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Failed)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public void WorkflowExecutionStatus_ShouldHaveAllValidValues(WorkflowExecutionStatus status)
    {
        // Arrange & Act
        var statusValue = (int)status;

        // Assert
        Assert.True(statusValue >= 0 && statusValue <= 4);
    }

    [Theory]
    [InlineData(TaskResultStatus.Success)]
    [InlineData(TaskResultStatus.Failed)]
    [InlineData(TaskResultStatus.Cancelled)]
    [InlineData(TaskResultStatus.Timeout)]
    public void TaskResultStatus_ShouldHaveAllValidValues(TaskResultStatus status)
    {
        // Arrange & Act
        var statusValue = (int)status;

        // Assert
        Assert.True(statusValue >= 0 && statusValue <= 3);
    }
}