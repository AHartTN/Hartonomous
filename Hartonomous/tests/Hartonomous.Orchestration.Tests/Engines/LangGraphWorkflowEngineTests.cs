using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.DSL;
using Hartonomous.Orchestration.Engines;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hartonomous.Orchestration.Tests.Engines;

public class LangGraphWorkflowEngineTests
{
    private readonly Mock<IWorkflowRepository> _mockRepository;
    private readonly Mock<IWorkflowStateManager> _mockStateManager;
    private readonly Mock<IWorkflowDSLParser> _mockDslParser;
    private readonly Mock<ILogger<LangGraphWorkflowEngine>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly LangGraphWorkflowEngine _engine;

    private readonly Guid _testWorkflowId = Guid.NewGuid();
    private readonly Guid _testExecutionId = Guid.NewGuid();
    private readonly string _testUserId = "test-user-id";

    public LangGraphWorkflowEngineTests()
    {
        _mockRepository = new Mock<IWorkflowRepository>();
        _mockStateManager = new Mock<IWorkflowStateManager>();
        _mockDslParser = new Mock<IWorkflowDSLParser>();
        _mockLogger = new Mock<ILogger<LangGraphWorkflowEngine>>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _engine = new LangGraphWorkflowEngine(
            _mockRepository.Object,
            _mockStateManager.Object,
            _mockDslParser.Object,
            _mockLogger.Object,
            _mockServiceProvider.Object
        );
    }

    [Fact]
    public async Task StartWorkflowAsync_ValidWorkflow_ReturnsExecutionId()
    {
        // Arrange
        var workflowDefinition = new WorkflowDefinitionDto(
            _testWorkflowId,
            "Test Workflow",
            "Test Description",
            "{}",
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            _testUserId,
            1,
            WorkflowStatus.Active
        );

        var workflowGraph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Nodes = new Dictionary<string, WorkflowNode>
            {
                ["start"] = new WorkflowNode
                {
                    Id = "start",
                    Name = "Start",
                    Type = WorkflowNodeTypes.Start
                }
            },
            Edges = new List<WorkflowEdge>()
        };

        _mockRepository
            .Setup(r => r.GetWorkflowByIdAsync(_testWorkflowId, _testUserId))
            .ReturnsAsync(workflowDefinition);

        _mockRepository
            .Setup(r => r.StartWorkflowExecutionAsync(It.IsAny<StartWorkflowExecutionRequest>(), _testUserId))
            .ReturnsAsync(_testExecutionId);

        _mockStateManager
            .Setup(s => s.InitializeStateAsync(_testExecutionId, It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(true);

        _mockDslParser
            .Setup(p => p.ParseWorkflowAsync(It.IsAny<string>()))
            .ReturnsAsync(workflowGraph);

        var input = new Dictionary<string, object> { ["test"] = "value" };
        var configuration = new Dictionary<string, object> { ["config"] = "value" };

        // Act
        var result = await _engine.StartWorkflowAsync(_testWorkflowId, input, configuration, _testUserId, "test-execution");

        // Assert
        Assert.Equal(_testExecutionId, result);
        _mockRepository.Verify(r => r.StartWorkflowExecutionAsync(It.IsAny<StartWorkflowExecutionRequest>(), _testUserId), Times.Once);
        _mockStateManager.Verify(s => s.InitializeStateAsync(_testExecutionId, It.IsAny<Dictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public async Task StartWorkflowAsync_WorkflowNotFound_ThrowsException()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.GetWorkflowByIdAsync(_testWorkflowId, _testUserId))
            .ReturnsAsync((WorkflowDefinitionDto?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _engine.StartWorkflowAsync(_testWorkflowId, null, null, _testUserId));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task PauseWorkflowAsync_ValidExecution_ReturnsTrue()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Paused, null, _testUserId))
            .ReturnsAsync(true);

        _mockStateManager
            .Setup(s => s.UpdateStateAsync(_testExecutionId, It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(true);

        // Act
        var result = await _engine.PauseWorkflowAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Paused, null, _testUserId), Times.Once);
    }

    [Fact]
    public async Task ResumeWorkflowAsync_PausedExecution_ReturnsTrue()
    {
        // Arrange
        var execution = new WorkflowExecutionDto(
            _testExecutionId,
            _testWorkflowId,
            "Test Workflow",
            null,
            null,
            null,
            WorkflowExecutionStatus.Paused,
            DateTime.UtcNow,
            null,
            null,
            _testUserId,
            0,
            null,
            new List<NodeExecutionDto>()
        );

        var workflowDefinition = new WorkflowDefinitionDto(
            _testWorkflowId,
            "Test Workflow",
            "Test Description",
            "{}",
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            _testUserId,
            1,
            WorkflowStatus.Active
        );

        var state = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "currentNode",
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        var workflowGraph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Nodes = new Dictionary<string, WorkflowNode>(),
            Edges = new List<WorkflowEdge>()
        };

        _mockRepository
            .Setup(r => r.GetExecutionByIdAsync(_testExecutionId, _testUserId))
            .ReturnsAsync(execution);

        _mockRepository
            .Setup(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Running, null, _testUserId))
            .ReturnsAsync(true);

        _mockRepository
            .Setup(r => r.GetWorkflowByIdAsync(_testWorkflowId, _testUserId))
            .ReturnsAsync(workflowDefinition);

        _mockStateManager
            .Setup(s => s.GetCurrentStateAsync(_testExecutionId))
            .ReturnsAsync(state);

        _mockDslParser
            .Setup(p => p.ParseWorkflowAsync(It.IsAny<string>()))
            .ReturnsAsync(workflowGraph);

        // Act
        var result = await _engine.ResumeWorkflowAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Running, null, _testUserId), Times.Once);
    }

    [Fact]
    public async Task ResumeWorkflowAsync_NotPausedExecution_ReturnsFalse()
    {
        // Arrange
        var execution = new WorkflowExecutionDto(
            _testExecutionId,
            _testWorkflowId,
            "Test Workflow",
            null,
            null,
            null,
            WorkflowExecutionStatus.Running, // Not paused
            DateTime.UtcNow,
            null,
            null,
            _testUserId,
            0,
            null,
            new List<NodeExecutionDto>()
        );

        _mockRepository
            .Setup(r => r.GetExecutionByIdAsync(_testExecutionId, _testUserId))
            .ReturnsAsync(execution);

        // Act
        var result = await _engine.ResumeWorkflowAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CancelWorkflowAsync_ValidExecution_ReturnsTrue()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Cancelled, "Cancelled by user", _testUserId))
            .ReturnsAsync(true);

        _mockStateManager
            .Setup(s => s.UpdateStateAsync(_testExecutionId, It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(true);

        // Act
        var result = await _engine.CancelWorkflowAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Cancelled, "Cancelled by user", _testUserId), Times.Once);
    }

    [Fact]
    public async Task RetryWorkflowAsync_ValidExecution_ReturnsTrue()
    {
        // Arrange
        var execution = new WorkflowExecutionDto(
            _testExecutionId,
            _testWorkflowId,
            "Test Workflow",
            null,
            new Dictionary<string, object> { ["input"] = "value" },
            null,
            WorkflowExecutionStatus.Failed,
            DateTime.UtcNow,
            null,
            "Some error",
            _testUserId,
            0,
            null,
            new List<NodeExecutionDto>()
        );

        var workflowDefinition = new WorkflowDefinitionDto(
            _testWorkflowId,
            "Test Workflow",
            "Test Description",
            "{}",
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            _testUserId,
            1,
            WorkflowStatus.Active
        );

        var workflowGraph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Nodes = new Dictionary<string, WorkflowNode>(),
            Edges = new List<WorkflowEdge>()
        };

        _mockRepository
            .Setup(r => r.GetExecutionByIdAsync(_testExecutionId, _testUserId))
            .ReturnsAsync(execution);

        _mockRepository
            .Setup(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Running, null, _testUserId))
            .ReturnsAsync(true);

        _mockRepository
            .Setup(r => r.GetWorkflowByIdAsync(_testWorkflowId, _testUserId))
            .ReturnsAsync(workflowDefinition);

        _mockStateManager
            .Setup(s => s.InitializeStateAsync(_testExecutionId, It.IsAny<Dictionary<string, object>>()))
            .ReturnsAsync(true);

        _mockDslParser
            .Setup(p => p.ParseWorkflowAsync(It.IsAny<string>()))
            .ReturnsAsync(workflowGraph);

        // Act
        var result = await _engine.RetryWorkflowAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.True(result);
        _mockRepository.Verify(r => r.UpdateExecutionStatusAsync(_testExecutionId, WorkflowExecutionStatus.Running, null, _testUserId), Times.Once);
    }

    [Fact]
    public async Task GetExecutionStatusAsync_ValidExecution_ReturnsExecution()
    {
        // Arrange
        var execution = new WorkflowExecutionDto(
            _testExecutionId,
            _testWorkflowId,
            "Test Workflow",
            null,
            null,
            null,
            WorkflowExecutionStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            _testUserId,
            0,
            null,
            new List<NodeExecutionDto>()
        );

        _mockRepository
            .Setup(r => r.GetExecutionByIdAsync(_testExecutionId, _testUserId))
            .ReturnsAsync(execution);

        // Act
        var result = await _engine.GetExecutionStatusAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testExecutionId, result.ExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Running, result.Status);
    }

    [Fact]
    public async Task GetExecutionProgressAsync_ValidExecution_ReturnsProgress()
    {
        // Arrange
        var execution = new WorkflowExecutionDto(
            _testExecutionId,
            _testWorkflowId,
            "Test Workflow",
            null,
            null,
            null,
            WorkflowExecutionStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            _testUserId,
            0,
            null,
            new List<NodeExecutionDto>()
        );

        var state = new WorkflowStateDto(
            _testExecutionId,
            new Dictionary<string, object>(),
            "currentNode",
            new List<string> { "completed1" },
            new List<string> { "pending1", "pending2" },
            DateTime.UtcNow
        );

        _mockRepository
            .Setup(r => r.GetExecutionByIdAsync(_testExecutionId, _testUserId))
            .ReturnsAsync(execution);

        _mockStateManager
            .Setup(s => s.GetCurrentStateAsync(_testExecutionId))
            .ReturnsAsync(state);

        // Act
        var result = await _engine.GetExecutionProgressAsync(_testExecutionId, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testExecutionId, result.ExecutionId);
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_ValidDefinition_ReturnsValid()
    {
        // Arrange
        var workflowDefinition = "{}";
        var mockGraph = new WorkflowGraph();

        _mockDslParser
            .Setup(p => p.ParseWorkflowAsync(workflowDefinition))
            .ReturnsAsync(mockGraph);

        // Act
        var result = await _engine.ValidateWorkflowAsync(workflowDefinition);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateWorkflowAsync_InvalidDefinition_ReturnsInvalid()
    {
        // Arrange
        var workflowDefinition = "invalid";

        _mockDslParser
            .Setup(p => p.ParseWorkflowAsync(workflowDefinition))
            .ThrowsAsync(new ArgumentException("Invalid definition"));

        // Act
        var result = await _engine.ValidateWorkflowAsync(workflowDefinition);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("PARSE_ERROR", result.Errors[0].Code);
    }

    [Fact]
    public async Task ExecuteNodeAsync_ValidNode_ReturnsOutput()
    {
        // Arrange
        var nodeDefinition = @"{
            ""Id"": ""test-node"",
            ""Name"": ""Test Node"",
            ""Type"": ""action"",
            ""Configuration"": {},
            ""Input"": {},
            ""Dependencies"": []
        }";

        var input = new Dictionary<string, object> { ["test"] = "value" };

        // Act
        var result = await _engine.ExecuteNodeAsync(nodeDefinition, input, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("executedAt", result.Keys);
        Assert.Contains("nodeId", result.Keys);
        Assert.Contains("nodeType", result.Keys);
    }

    [Fact]
    public async Task ExecuteNodeAsync_InvalidNodeDefinition_ThrowsException()
    {
        // Arrange
        var invalidNodeDefinition = "invalid json";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _engine.ExecuteNodeAsync(invalidNodeDefinition, null, _testUserId));
    }
}