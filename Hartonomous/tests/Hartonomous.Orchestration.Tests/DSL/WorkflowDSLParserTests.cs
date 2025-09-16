using Hartonomous.Orchestration.DSL;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hartonomous.Orchestration.Tests.DSL;

public class WorkflowDSLParserTests
{
    private readonly Mock<ILogger<WorkflowDSLParser>> _mockLogger;
    private readonly WorkflowDSLParser _parser;

    public WorkflowDSLParserTests()
    {
        _mockLogger = new Mock<ILogger<WorkflowDSLParser>>();
        _parser = new WorkflowDSLParser(_mockLogger.Object);
    }

    [Fact]
    public async Task ParseWorkflowFromJsonAsync_ValidJson_ReturnsWorkflowGraph()
    {
        // Arrange
        var jsonContent = @"{
            ""name"": ""Test Workflow"",
            ""description"": ""A test workflow"",
            ""version"": ""1.0"",
            ""nodes"": {
                ""start"": {
                    ""name"": ""Start Node"",
                    ""type"": ""start""
                },
                ""action1"": {
                    ""name"": ""Action 1"",
                    ""type"": ""action"",
                    ""dependencies"": [""start""]
                }
            },
            ""edges"": [
                {
                    ""from"": ""start"",
                    ""to"": ""action1""
                }
            ]
        }";

        // Act
        var result = await _parser.ParseWorkflowFromJsonAsync(jsonContent);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Workflow", result.Name);
        Assert.Equal("A test workflow", result.Description);
        Assert.Equal("1.0", result.Version);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
    }

    [Fact]
    public async Task ParseWorkflowFromYamlAsync_ValidYaml_ReturnsWorkflowGraph()
    {
        // Arrange
        var yamlContent = @"
name: Test Workflow
description: A test workflow
version: '1.0'
nodes:
  start:
    name: Start Node
    type: start
  action1:
    name: Action 1
    type: action
    dependencies:
      - start
edges:
  - from: start
    to: action1
";

        // Act
        var result = await _parser.ParseWorkflowFromYamlAsync(yamlContent);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Workflow", result.Name);
        Assert.Equal("A test workflow", result.Description);
        Assert.Equal("1.0", result.Version);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
    }

    [Fact]
    public async Task ValidateDSLAsync_ValidWorkflow_ReturnsValid()
    {
        // Arrange
        var jsonContent = @"{
            ""name"": ""Valid Workflow"",
            ""nodes"": {
                ""start"": {
                    ""name"": ""Start"",
                    ""type"": ""start""
                }
            },
            ""edges"": []
        }";

        // Act
        var result = await _parser.ValidateDSLAsync(jsonContent);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateDSLAsync_MissingName_ReturnsInvalid()
    {
        // Arrange
        var jsonContent = @"{
            ""nodes"": {
                ""start"": {
                    ""name"": ""Start"",
                    ""type"": ""start""
                }
            }
        }";

        // Act
        var result = await _parser.ValidateDSLAsync(jsonContent);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MISSING_NAME");
    }

    [Fact]
    public async Task ValidateDSLAsync_CircularDependency_ReturnsInvalid()
    {
        // Arrange
        var jsonContent = @"{
            ""name"": ""Circular Workflow"",
            ""nodes"": {
                ""node1"": {
                    ""name"": ""Node 1"",
                    ""type"": ""action"",
                    ""dependencies"": [""node2""]
                },
                ""node2"": {
                    ""name"": ""Node 2"",
                    ""type"": ""action"",
                    ""dependencies"": [""node1""]
                }
            }
        }";

        // Act
        var result = await _parser.ValidateDSLAsync(jsonContent);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CIRCULAR_DEPENDENCY");
    }

    [Fact]
    public async Task ConvertToExecutableAsync_ValidGraph_ReturnsExecutableJson()
    {
        // Arrange
        var graph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Description = "Test Description",
            Version = "1.0",
            Nodes = new Dictionary<string, WorkflowNode>
            {
                ["start"] = new WorkflowNode
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = "start"
                }
            },
            Edges = new List<WorkflowEdge>()
        };

        // Act
        var result = await _parser.ConvertToExecutableAsync(graph);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Test Workflow", result);
        Assert.Contains("start", result);
    }

    [Fact]
    public async Task GenerateDSLAsync_ValidGraph_ReturnsYaml()
    {
        // Arrange
        var graph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Description = "Test Description",
            Version = "1.0",
            Nodes = new Dictionary<string, WorkflowNode>
            {
                ["start"] = new WorkflowNode
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = "start"
                }
            }
        };

        // Act
        var result = await _parser.GenerateDSLAsync(graph);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("name: Test Workflow", result);
        Assert.Contains("start:", result);
    }

    [Fact]
    public void GetSupportedVersion_ReturnsCorrectVersion()
    {
        // Act
        var version = _parser.GetSupportedVersion();

        // Assert
        Assert.Equal("1.0", version);
    }

    [Fact]
    public async Task ValidateDependenciesAsync_OrphanedNodes_ReturnsWarnings()
    {
        // Arrange
        var graph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Nodes = new Dictionary<string, WorkflowNode>
            {
                ["start"] = new WorkflowNode { Id = "start", Type = "start" },
                ["orphan"] = new WorkflowNode { Id = "orphan", Type = "action" }
            },
            Edges = new List<WorkflowEdge>()
        };

        // Act
        var result = await _parser.ValidateDependenciesAsync(graph);

        // Assert
        Assert.Contains(result.Warnings, w => w.Code == "ORPHANED_NODE");
    }

    [Fact]
    public async Task OptimizeGraphAsync_RemovesUnreachableNodes()
    {
        // Arrange
        var graph = new WorkflowGraph
        {
            Name = "Test Workflow",
            Nodes = new Dictionary<string, WorkflowNode>
            {
                ["start"] = new WorkflowNode { Id = "start", Type = "start" },
                ["reachable"] = new WorkflowNode { Id = "reachable", Type = "action" },
                ["unreachable"] = new WorkflowNode { Id = "unreachable", Type = "action" }
            },
            Edges = new List<WorkflowEdge>
            {
                new WorkflowEdge { From = "start", To = "reachable" }
            }
        };

        // Act
        var result = await _parser.OptimizeGraphAsync(graph);

        // Assert
        Assert.Equal(2, result.Nodes.Count); // start and reachable
        Assert.DoesNotContain("unreachable", result.Nodes.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid json")]
    [InlineData("{")]
    public async Task ParseWorkflowFromJsonAsync_InvalidJson_ThrowsException(string invalidJson)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _parser.ParseWorkflowFromJsonAsync(invalidJson));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid: yaml: content: [")]
    public async Task ParseWorkflowFromYamlAsync_InvalidYaml_ThrowsException(string invalidYaml)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _parser.ParseWorkflowFromYamlAsync(invalidYaml));
    }
}