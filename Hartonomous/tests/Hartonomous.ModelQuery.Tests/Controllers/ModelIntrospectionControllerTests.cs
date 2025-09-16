using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Hartonomous.ModelQuery.Controllers;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;

namespace Hartonomous.ModelQuery.Tests.Controllers;

public class ModelIntrospectionControllerTests
{
    private readonly Mock<IModelIntrospectionService> _mockService;
    private readonly Mock<ILogger<ModelIntrospectionController>> _mockLogger;
    private readonly ModelIntrospectionController _controller;
    private readonly string _userId = "test-user-123";
    private readonly Guid _modelId = Guid.NewGuid();

    public ModelIntrospectionControllerTests()
    {
        _mockService = new Mock<IModelIntrospectionService>();
        _mockLogger = new Mock<ILogger<ModelIntrospectionController>>();
        _controller = new ModelIntrospectionController(_mockService.Object, _mockLogger.Object);

        // Setup user context
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }

    [Fact]
    public async Task AnalyzeModel_ValidModel_ReturnsOk()
    {
        // Arrange
        var introspection = new ModelIntrospectionDto(
            _modelId,
            "TestModel",
            1000,
            1000,
            5.0,
            new Dictionary<string, int> { ["Dense"] = 2 },
            new Dictionary<string, object>(),
            new List<string> { "fully_connected" },
            DateTime.UtcNow
        );

        _mockService.Setup(x => x.AnalyzeModelAsync(_modelId, _userId))
                   .ReturnsAsync(introspection);

        // Act
        var result = await _controller.AnalyzeModel(_modelId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedIntrospection = Assert.IsType<ModelIntrospectionDto>(okResult.Value);
        Assert.Equal(_modelId, returnedIntrospection.ModelId);
        Assert.Equal("TestModel", returnedIntrospection.ModelName);
    }

    [Fact]
    public async Task AnalyzeModel_ModelNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockService.Setup(x => x.AnalyzeModelAsync(_modelId, _userId))
                   .ReturnsAsync((ModelIntrospectionDto?)null);

        // Act
        var result = await _controller.AnalyzeModel(_modelId);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains(_modelId.ToString(), notFoundResult.Value?.ToString());
    }

    [Fact]
    public async Task AnalyzeModel_ServiceThrowsUnauthorized_ReturnsForbid()
    {
        // Arrange
        _mockService.Setup(x => x.AnalyzeModelAsync(_modelId, _userId))
                   .ThrowsAsync(new UnauthorizedAccessException());

        // Act
        var result = await _controller.AnalyzeModel(_modelId);

        // Assert
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task AnalyzeModel_ServiceThrowsException_ReturnsInternalServerError()
    {
        // Arrange
        _mockService.Setup(x => x.AnalyzeModelAsync(_modelId, _userId))
                   .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.AnalyzeModel(_modelId);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task SemanticSearch_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new SemanticSearchRequestDto("dense layer", "layers", 10, 0.7);
        var searchResults = new List<SemanticSearchResultDto>
        {
            new(Guid.NewGuid(), "layer", "Dense1", 0.9, new Dictionary<string, object>(), "Dense layer description")
        };

        _mockService.Setup(x => x.SemanticSearchAsync(request, _userId))
                   .ReturnsAsync(searchResults);

        // Act
        var result = await _controller.SemanticSearch(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedResults = Assert.IsAssignableFrom<IEnumerable<SemanticSearchResultDto>>(okResult.Value);
        Assert.Single(returnedResults);
    }

    [Fact]
    public async Task SemanticSearch_ServiceThrowsUnauthorized_ReturnsForbid()
    {
        // Arrange
        var request = new SemanticSearchRequestDto("test", "all", 10, 0.5);
        _mockService.Setup(x => x.SemanticSearchAsync(request, _userId))
                   .ThrowsAsync(new UnauthorizedAccessException());

        // Act
        var result = await _controller.SemanticSearch(request);

        // Assert
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetModelStatistics_ValidModel_ReturnsOk()
    {
        // Arrange
        var statistics = new Dictionary<string, object>
        {
            ["total_weights"] = 5,
            ["total_layers"] = 3,
            ["framework"] = "PyTorch"
        };

        _mockService.Setup(x => x.GetModelStatisticsAsync(_modelId, _userId))
                   .ReturnsAsync(statistics);

        // Act
        var result = await _controller.GetModelStatistics(_modelId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedStats = Assert.IsType<Dictionary<string, object>>(okResult.Value);
        Assert.Equal(5, returnedStats["total_weights"]);
        Assert.Equal("PyTorch", returnedStats["framework"]);
    }

    [Fact]
    public async Task GetModelCapabilities_ValidModel_ReturnsOk()
    {
        // Arrange
        var capabilities = new List<string> { "computer_vision", "fully_connected" };

        _mockService.Setup(x => x.GetModelCapabilitiesAsync(_modelId, _userId))
                   .ReturnsAsync(capabilities);

        // Act
        var result = await _controller.GetModelCapabilities(_modelId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedCapabilities = Assert.IsAssignableFrom<IEnumerable<string>>(okResult.Value);
        Assert.Contains("computer_vision", returnedCapabilities);
        Assert.Contains("fully_connected", returnedCapabilities);
    }

    [Fact]
    public async Task CompareModels_ValidModels_ReturnsOk()
    {
        // Arrange
        var modelBId = Guid.NewGuid();
        var request = new CompareModelsRequest(_modelId, modelBId, "architecture");
        var comparison = new ModelComparisonDto(
            _modelId,
            modelBId,
            "architecture",
            new Dictionary<string, object>(),
            new Dictionary<string, double>(),
            new List<string>(),
            new List<string>(),
            DateTime.UtcNow
        );

        _mockService.Setup(x => x.CompareModelsAsync(_modelId, modelBId, "architecture", _userId))
                   .ReturnsAsync(comparison);

        // Act
        var result = await _controller.CompareModels(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedComparison = Assert.IsType<ModelComparisonDto>(okResult.Value);
        Assert.Equal(_modelId, returnedComparison.ModelAId);
        Assert.Equal(modelBId, returnedComparison.ModelBId);
    }

    [Fact]
    public async Task CompareModels_ModelsNotFound_ReturnsNotFound()
    {
        // Arrange
        var modelBId = Guid.NewGuid();
        var request = new CompareModelsRequest(_modelId, modelBId, "architecture");

        _mockService.Setup(x => x.CompareModelsAsync(_modelId, modelBId, "architecture", _userId))
                   .ReturnsAsync((ModelComparisonDto?)null);

        // Act
        var result = await _controller.CompareModels(request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains("not found", notFoundResult.Value?.ToString());
    }
}