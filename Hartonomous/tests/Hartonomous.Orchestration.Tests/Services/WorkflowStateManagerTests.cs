using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Hartonomous.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hartonomous.Orchestration.Tests.Services;

public class WorkflowStateManagerTests
{
    private readonly Mock<IWorkflowRepository> _mockRepository;
    private readonly Mock<ILogger<WorkflowStateManager>> _mockLogger;
    private readonly WorkflowStateManager _stateManager;
    private readonly Guid _testExecutionId = Guid.NewGuid();

    public WorkflowStateManagerTests()
    {
        _mockRepository = new Mock<IWorkflowRepository>();
        _mockLogger = new Mock<ILogger<WorkflowStateManager>>();
        _stateManager = new WorkflowStateManager(_mockRepository.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task InitializeStateAsync_ValidInput_ReturnsTrue()
    {
        // Arrange
        var initialState = new Dictionary<string, object>
        {
            ["test"] = "value"
        };

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(It.IsAny<Guid>(), It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.InitializeStateAsync(_testExecutionId, initialState);

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStateAsync_ValidUpdate_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object> { ["existing"] = "value" },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        var updateState = new Dictionary<string, object>
        {
            ["new"] = "value"
        };

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.UpdateStateAsync(_testExecutionId, updateState);

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()), Times.Once);
    }

    [Fact]
    public async Task GetCurrentStateAsync_StateExists_ReturnsState()
    {
        // Arrange
        var expectedState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object> { ["test"] = "value" },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(expectedState);

        // Act
        var result = await _stateManager.GetCurrentStateAsync(_testExecutionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testExecutionId, result.ExecutionId);
        Assert.Equal("node1", result.CurrentNode);
    }

    [Fact]
    public async Task SetVariableAsync_ValidVariable_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>
            {
                ["variables"] = new Dictionary<string, object>()
            },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.SetVariableAsync(_testExecutionId, "testVar", "testValue");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetVariableAsync_VariableExists_ReturnsValue()
    {
        // Arrange
        var variables = new Dictionary<string, object>
        {
            ["testVar"] = "testValue"
        };

        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>
            {
                ["variables"] = variables
            },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        // Act
        var result = await _stateManager.GetVariableAsync<string>(_testExecutionId, "testVar");

        // Assert
        Assert.Equal("testValue", result);
    }

    [Fact]
    public async Task GetVariableAsync_VariableNotExists_ReturnsDefault()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>
            {
                ["variables"] = new Dictionary<string, object>()
            },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        // Act
        var result = await _stateManager.GetVariableAsync<string>(_testExecutionId, "nonExistentVar");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateCurrentNodeAsync_ValidNode_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "oldNode",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.UpdateCurrentNodeAsync(_testExecutionId, "newNode");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task MarkNodeCompletedAsync_ValidNode_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "node1",
            new List<string>(),
            new List<string> { "node1" },
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.MarkNodeCompletedAsync(_testExecutionId, "node1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanProceedToNodeAsync_AllDependenciesCompleted_ReturnsTrue()
    {
        // Arrange
        var dependencies = new List<string> { "dep1", "dep2" };
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "node1",
            new List<string> { "dep1", "dep2" },
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        // Act
        var result = await _stateManager.CanProceedToNodeAsync(_testExecutionId, "targetNode", dependencies);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanProceedToNodeAsync_SomeDependenciesNotCompleted_ReturnsFalse()
    {
        // Arrange
        var dependencies = new List<string> { "dep1", "dep2", "dep3" };
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "node1",
            new List<string> { "dep1", "dep2" }, // dep3 not completed
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        // Act
        var result = await _stateManager.CanProceedToNodeAsync(_testExecutionId, "targetNode", dependencies);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateSnapshotAsync_ValidExecution_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object> { ["test"] = "value" },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.CreateSnapshotAsync(_testExecutionId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ClearStateAsync_ValidExecution_ReturnsTrue()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.ClearStateAsync(_testExecutionId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetStateHistoryAsync_ValidExecution_ReturnsHistory()
    {
        // Arrange
        var historyStates = new List<WorkflowStateDto>
        {
            new WorkflowStateDto(_testExecutionId, new Dictionary<string, object>(), "node1",
                new List<string>(), new List<string>(), DateTime.UtcNow.AddMinutes(-10)),
            new WorkflowStateDto(_testExecutionId, new Dictionary<string, object>(), "node2",
                new List<string> { "node1" }, new List<string>(), DateTime.UtcNow.AddMinutes(-5))
        };

        _mockRepository
            .Setup(r => r.GetWorkflowStateHistoryAsync(_testExecutionId, It.IsAny<int>()))
            .ReturnsAsync(historyStates);

        // Act
        var result = await _stateManager.GetStateHistoryAsync(_testExecutionId, 10);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("node1", result[0].CurrentNode);
        Assert.Equal("node2", result[1].CurrentNode);
    }

    [Fact]
    public async Task RemoveVariableAsync_ExistingVariable_ReturnsTrue()
    {
        // Arrange
        var variables = new Dictionary<string, object>
        {
            ["varToRemove"] = "value",
            ["keepThis"] = "anotherValue"
        };

        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>
            {
                ["variables"] = variables
            },
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.RemoveVariableAsync(_testExecutionId, "varToRemove");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AddPendingNodeAsync_ValidNode_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "node1",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.AddPendingNodeAsync(_testExecutionId, "pendingNode");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RemovePendingNodeAsync_ValidNode_ReturnsTrue()
    {
        // Arrange
        var existingState = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "node1",
            new List<string>(),
            new List<string> { "pendingNode" },
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetWorkflowStateAsync(_testExecutionId))
            .ReturnsAsync(existingState);

        _mockRepository
            .Setup(r => r.SaveWorkflowStateAsync(_testExecutionId, It.IsAny<WorkflowStateDto>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateManager.RemovePendingNodeAsync(_testExecutionId, "pendingNode");

        // Assert
        Assert.True(result);
    }
}