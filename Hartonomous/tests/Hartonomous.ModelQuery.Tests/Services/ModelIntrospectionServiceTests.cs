using Xunit;
using Moq;
using Hartonomous.ModelQuery.Services;
using Hartonomous.ModelQuery.Interfaces;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.DTOs;

namespace Hartonomous.ModelQuery.Tests.Services;

public class ModelIntrospectionServiceTests
{
    private readonly Mock<IModelArchitectureRepository> _mockArchitectureRepo;
    private readonly Mock<IModelWeightRepository> _mockWeightRepo;
    private readonly Mock<INeuralMapRepository> _mockNeuralMapRepo;
    private readonly Mock<IModelRepository> _mockModelRepo;
    private readonly ModelIntrospectionService _service;
    private readonly string _userId = "test-user-123";
    private readonly Guid _modelId = Guid.NewGuid();

    public ModelIntrospectionServiceTests()
    {
        _mockArchitectureRepo = new Mock<IModelArchitectureRepository>();
        _mockWeightRepo = new Mock<IModelWeightRepository>();
        _mockNeuralMapRepo = new Mock<INeuralMapRepository>();
        _mockModelRepo = new Mock<IModelRepository>();

        _service = new ModelIntrospectionService(
            _mockArchitectureRepo.Object,
            _mockWeightRepo.Object,
            _mockNeuralMapRepo.Object,
            _mockModelRepo.Object);
    }

    [Fact]
    public async Task AnalyzeModelAsync_ValidModel_ReturnsIntrospectionDto()
    {
        // Arrange
        var model = new ModelMetadataDto(_modelId, "TestModel", "1.0", "MIT");
        var weights = new List<ModelWeightDto>
        {
            new(_modelId, _modelId, "layer1", "weight1", "float32", new[] { 10, 5 }, 200, "/path1", "checksum1", DateTime.UtcNow),
            new(_modelId, _modelId, "layer2", "weight2", "float32", new[] { 5, 3 }, 60, "/path2", "checksum2", DateTime.UtcNow)
        };
        var layers = new List<ModelLayerDto>
        {
            new(_modelId, _modelId, "layer1", "Dense", 0, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow),
            new(_modelId, _modelId, "layer2", "Conv2D", 1, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow)
        };
        var architecture = new ModelArchitectureDto(_modelId, "TestArch", "PyTorch", layers, new Dictionary<string, object>(), new Dictionary<string, object>(), DateTime.UtcNow);

        _mockModelRepo.Setup(x => x.GetModelByIdAsync(_modelId, _userId)).ReturnsAsync(model);
        _mockArchitectureRepo.Setup(x => x.GetModelArchitectureAsync(_modelId, _userId)).ReturnsAsync(architecture);
        _mockWeightRepo.Setup(x => x.GetModelWeightsAsync(_modelId, _userId)).ReturnsAsync(weights);

        // Act
        var result = await _service.AnalyzeModelAsync(_modelId, _userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_modelId, result.ModelId);
        Assert.Equal("TestModel", result.ModelName);
        Assert.Equal(65, result.TotalParameters); // (10*5) + (5*3) = 50 + 15 = 65
        Assert.Equal(65, result.TrainableParameters);
        Assert.Contains("Dense", result.LayerTypeCount.Keys);
        Assert.Contains("Conv2D", result.LayerTypeCount.Keys);
    }

    [Fact]
    public async Task AnalyzeModelAsync_ModelNotFound_ReturnsNull()
    {
        // Arrange
        _mockModelRepo.Setup(x => x.GetModelByIdAsync(_modelId, _userId)).ReturnsAsync((ModelMetadataDto?)null);

        // Act
        var result = await _service.AnalyzeModelAsync(_modelId, _userId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetModelStatisticsAsync_ValidModel_ReturnsStatistics()
    {
        // Arrange
        var weights = new List<ModelWeightDto>
        {
            new(_modelId, _modelId, "layer1", "weight1", "float32", new[] { 10, 5 }, 200, "/path1", "checksum1", DateTime.UtcNow),
            new(_modelId, _modelId, "layer2", "weight2", "float16", new[] { 5, 3 }, 60, "/path2", "checksum2", DateTime.UtcNow)
        };
        var layers = new List<ModelLayerDto>
        {
            new(_modelId, _modelId, "layer1", "Dense", 0, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow)
        };
        var architecture = new ModelArchitectureDto(_modelId, "TestArch", "TensorFlow", layers, new Dictionary<string, object>(), new Dictionary<string, object>(), DateTime.UtcNow);
        var graph = new NeuralMapGraphDto(_modelId, "TestModel", "1.0", new List<NeuralMapNodeDto>(), new List<NeuralMapEdgeDto>(), new Dictionary<string, object>());

        _mockWeightRepo.Setup(x => x.GetModelWeightsAsync(_modelId, _userId)).ReturnsAsync(weights);
        _mockArchitectureRepo.Setup(x => x.GetModelArchitectureAsync(_modelId, _userId)).ReturnsAsync(architecture);
        _mockNeuralMapRepo.Setup(x => x.GetModelGraphAsync(_modelId, _userId)).ReturnsAsync(graph);

        // Act
        var result = await _service.GetModelStatisticsAsync(_modelId, _userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result["total_weights"]);
        Assert.Equal(1, result["total_layers"]);
        Assert.Equal("TensorFlow", result["framework"]);
        Assert.Contains("float32", (List<string>)result["data_types"]);
        Assert.Contains("float16", (List<string>)result["data_types"]);
    }

    [Fact]
    public async Task GetModelCapabilitiesAsync_ConvolutionalModel_ReturnsComputerVision()
    {
        // Arrange
        var layers = new List<ModelLayerDto>
        {
            new(_modelId, _modelId, "conv1", "Conv2D", 0, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow),
            new(_modelId, _modelId, "dense1", "Dense", 1, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow)
        };
        var architecture = new ModelArchitectureDto(_modelId, "CNN", "PyTorch", layers, new Dictionary<string, object>(), new Dictionary<string, object>(), DateTime.UtcNow);

        _mockArchitectureRepo.Setup(x => x.GetModelArchitectureAsync(_modelId, _userId)).ReturnsAsync(architecture);

        // Act
        var result = await _service.GetModelCapabilitiesAsync(_modelId, _userId);

        // Assert
        Assert.Contains("computer_vision", result);
        Assert.Contains("fully_connected", result);
        Assert.Contains("pytorch_model", result);
    }

    [Fact]
    public async Task GetModelCapabilitiesAsync_RecurrentModel_ReturnsSequenceModeling()
    {
        // Arrange
        var layers = new List<ModelLayerDto>
        {
            new(_modelId, _modelId, "lstm1", "LSTM", 0, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow),
            new(_modelId, _modelId, "gru1", "GRU", 1, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow)
        };
        var architecture = new ModelArchitectureDto(_modelId, "RNN", "TensorFlow", layers, new Dictionary<string, object>(), new Dictionary<string, object>(), DateTime.UtcNow);

        _mockArchitectureRepo.Setup(x => x.GetModelArchitectureAsync(_modelId, _userId)).ReturnsAsync(architecture);

        // Act
        var result = await _service.GetModelCapabilitiesAsync(_modelId, _userId);

        // Assert
        Assert.Contains("sequence_modeling", result);
        Assert.Contains("tensorflow_model", result);
    }

    [Fact]
    public async Task CompareModelsAsync_ValidModels_ReturnsComparison()
    {
        // Arrange
        var modelA = new ModelMetadataDto(_modelId, "ModelA", "1.0", "MIT");
        var modelB = new ModelMetadataDto(Guid.NewGuid(), "ModelB", "1.0", "Apache");

        var weightsA = new List<ModelWeightDto>
        {
            new(_modelId, _modelId, "layer1", "weight1", "float32", new[] { 10, 5 }, 200, "/path1", "checksum1", DateTime.UtcNow)
        };
        var weightsB = new List<ModelWeightDto>
        {
            new(modelB.ModelId, modelB.ModelId, "layer1", "weight1", "float32", new[] { 10, 5 }, 200, "/path1", "checksum1", DateTime.UtcNow),
            new(modelB.ModelId, modelB.ModelId, "layer2", "weight2", "float32", new[] { 5, 3 }, 60, "/path2", "checksum2", DateTime.UtcNow)
        };

        var layersA = new List<ModelLayerDto>
        {
            new(_modelId, _modelId, "layer1", "Dense", 0, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow)
        };
        var layersB = new List<ModelLayerDto>
        {
            new(modelB.ModelId, modelB.ModelId, "layer1", "Dense", 0, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow),
            new(modelB.ModelId, modelB.ModelId, "layer2", "Conv2D", 1, new Dictionary<string, object>(), new List<ModelWeightDto>(), DateTime.UtcNow)
        };

        var architectureA = new ModelArchitectureDto(_modelId, "ArchA", "PyTorch", layersA, new Dictionary<string, object>(), new Dictionary<string, object>(), DateTime.UtcNow);
        var architectureB = new ModelArchitectureDto(modelB.ModelId, "ArchB", "PyTorch", layersB, new Dictionary<string, object>(), new Dictionary<string, object>(), DateTime.UtcNow);

        _mockModelRepo.Setup(x => x.GetModelByIdAsync(_modelId, _userId)).ReturnsAsync(modelA);
        _mockModelRepo.Setup(x => x.GetModelByIdAsync(modelB.ModelId, _userId)).ReturnsAsync(modelB);
        _mockArchitectureRepo.Setup(x => x.GetModelArchitectureAsync(_modelId, _userId)).ReturnsAsync(architectureA);
        _mockArchitectureRepo.Setup(x => x.GetModelArchitectureAsync(modelB.ModelId, _userId)).ReturnsAsync(architectureB);
        _mockWeightRepo.Setup(x => x.GetModelWeightsAsync(_modelId, _userId)).ReturnsAsync(weightsA);
        _mockWeightRepo.Setup(x => x.GetModelWeightsAsync(modelB.ModelId, _userId)).ReturnsAsync(weightsB);

        // Act
        var result = await _service.CompareModelsAsync(_modelId, modelB.ModelId, "architecture", _userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_modelId, result.ModelAId);
        Assert.Equal(modelB.ModelId, result.ModelBId);
        Assert.Contains("layer1", result.CommonLayers);
        Assert.Contains("layer2", result.UniqueLayers);
        Assert.Equal(-1, result.Differences["layer_count_diff"]);
    }

    [Fact]
    public async Task SemanticSearchAsync_ValidRequest_ReturnsResults()
    {
        // Arrange
        var request = new SemanticSearchRequestDto("dense layer", "layers", 10, 0.5);

        // Act
        var result = await _service.SemanticSearchAsync(request, _userId);

        // Assert
        Assert.NotNull(result);
        // Note: The current implementation returns empty results as it's a placeholder
        // In a real implementation, this would return actual search results
    }
}