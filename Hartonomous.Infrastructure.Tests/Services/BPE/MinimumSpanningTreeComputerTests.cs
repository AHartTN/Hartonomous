using Hartonomous.Infrastructure.Services.BPE;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hartonomous.Infrastructure.Tests.Services.BPE;

public class MinimumSpanningTreeComputerTests
{
    private readonly MinimumSpanningTreeComputer _computer;

    public MinimumSpanningTreeComputerTests()
    {
        _computer = new MinimumSpanningTreeComputer(NullLogger<MinimumSpanningTreeComputer>.Instance);
    }

    [Fact]
    public void ComputeMST_EmptyGraph_ReturnsEmptyMST()
    {
        // Arrange
        var graph = new Graph();

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert
        Assert.Empty(mst.Edges);
        Assert.Empty(mst.Vertices);
    }

    [Fact]
    public void ComputeMST_SingleEdge_ReturnsSameEdge()
    {
        // Arrange
        var graph = new Graph();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        graph.AddEdge(v1, v2, 10.0, 100);

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert
        Assert.Single(mst.Edges);
        Assert.Equal(2, mst.Vertices.Count);
    }

    [Fact]
    public void ComputeMST_Triangle_ReturnsMinimalSpanningTree()
    {
        // Arrange: Triangle with edges of weight 1, 2, 3
        var graph = new Graph();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();

        graph.AddEdge(v1, v2, 1.0, 10);  // Lightest
        graph.AddEdge(v2, v3, 2.0, 20);  // Middle
        graph.AddEdge(v1, v3, 3.0, 30);  // Heaviest (should be excluded)

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert: MST should have 2 edges (3 vertices - 1)
        Assert.Equal(2, mst.Edges.Count);
        Assert.Equal(3, mst.Vertices.Count);
        
        // MST should include edges with weight 1 and 2, excluding weight 3
        var weights = mst.Edges.Select(e => e.Weight).OrderBy(w => w).ToList();
        Assert.Equal(new[] { 1.0, 2.0 }, weights);
    }

    [Fact]
    public void ComputeMST_Square_ReturnsThreeEdges()
    {
        // Arrange: Square (4 vertices, 4 edges)
        var graph = new Graph();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        var v4 = Guid.NewGuid();

        graph.AddEdge(v1, v2, 1.0, 10);
        graph.AddEdge(v2, v3, 2.0, 20);
        graph.AddEdge(v3, v4, 3.0, 30);
        graph.AddEdge(v4, v1, 4.0, 40); // This should be excluded

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert: MST for 4 vertices has 3 edges
        Assert.Equal(3, mst.Edges.Count);
        Assert.Equal(4, mst.Vertices.Count);
    }

    [Fact]
    public void ComputeMST_CompleteGraph_SelectsMinimalEdges()
    {
        // Arrange: Complete graph K4 (6 edges)
        var graph = new Graph();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        var v4 = Guid.NewGuid();

        // Add all possible edges with different weights
        graph.AddEdge(v1, v2, 1.0, 10);
        graph.AddEdge(v1, v3, 2.0, 20);
        graph.AddEdge(v1, v4, 3.0, 30);
        graph.AddEdge(v2, v3, 4.0, 40);
        graph.AddEdge(v2, v4, 5.0, 50);
        graph.AddEdge(v3, v4, 6.0, 60);

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert: MST has 3 edges with smallest total weight
        Assert.Equal(3, mst.Edges.Count);
        
        // Should select edges with weights 1, 2, 3 (total = 6)
        var totalWeight = mst.Edges.Sum(e => e.Weight);
        Assert.Equal(6.0, totalWeight);
    }

    [Fact]
    public void ComputeMST_DisconnectedComponents_ReturnsForest()
    {
        // Arrange: Two separate triangles
        var graph = new Graph();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        var v4 = Guid.NewGuid();
        var v5 = Guid.NewGuid();
        var v6 = Guid.NewGuid();

        // First triangle
        graph.AddEdge(v1, v2, 1.0, 10);
        graph.AddEdge(v2, v3, 2.0, 20);
        graph.AddEdge(v1, v3, 3.0, 30);

        // Second triangle
        graph.AddEdge(v4, v5, 1.0, 10);
        graph.AddEdge(v5, v6, 2.0, 20);
        graph.AddEdge(v4, v6, 3.0, 30);

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert: MST has 4 edges (2 per triangle)
        Assert.Equal(4, mst.Edges.Count);
        Assert.Equal(6, mst.Vertices.Count);
    }

    [Fact]
    public void ComputeMST_PreservesHilbertDistance()
    {
        // Arrange
        var graph = new Graph();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        
        var expectedHilbert = 12345UL;
        graph.AddEdge(v1, v2, 1.0, expectedHilbert);

        // Act
        var mst = _computer.ComputeMST(graph);

        // Assert
        Assert.Single(mst.Edges);
        Assert.Equal(expectedHilbert, mst.Edges[0].HilbertDistance);
    }
}
