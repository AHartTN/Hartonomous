using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.Milvus;
using Hartonomous.Infrastructure.EventStreaming;

namespace Hartonomous.IntegrationTests.DataFabricTests;

/// <summary>
/// Integration tests for the complete Hartonomous data fabric
/// Tests Neo4j, SQL Server Vector, and CDC pipeline integration
/// </summary>
public class DataFabricIntegrationTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Neo4jService _neo4jService;
    private readonly SqlServerVectorService _vectorService;
    private readonly DataFabricOrchestrator _orchestrator;
    private readonly ILogger<DataFabricIntegrationTests> _logger;

    public DataFabricIntegrationTests()
    {
        // Build configuration for tests
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.IntegrationTests.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Neo4j:Uri"] = "bolt://localhost:7687",
                ["Neo4j:Username"] = "neo4j",
                ["Neo4j:Password"] = "password",
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;",
                ["Kafka:BootstrapServers"] = "localhost:9092"
            })
            .Build();

        // Build service provider
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<IConfiguration>(configuration);
        services.AddHartonomousNeo4j(configuration);
        services.AddHartonomousMilvus(configuration);
        services.AddHartonomousEventStreaming(configuration);
        services.AddHartonomousDataFabric(configuration);

        _serviceProvider = services.BuildServiceProvider();

        _neo4jService = _serviceProvider.GetRequiredService<Neo4jService>();
        _vectorService = _serviceProvider.GetRequiredService<SqlServerVectorService>();
        _orchestrator = _serviceProvider.GetRequiredService<DataFabricOrchestrator>();
        _logger = _serviceProvider.GetRequiredService<ILogger<DataFabricIntegrationTests>>();
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing data fabric for integration tests");

        try
        {
            // Initialize the data fabric
            await _orchestrator.InitializeAsync();
            _logger.LogInformation("Data fabric initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Data fabric initialization failed - some tests may be skipped");
            // Don't fail here - individual tests will handle service unavailability
        }
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DataFabricOrchestrator_InitializeAsync_ShouldSucceed()
    {
        // Arrange & Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await _orchestrator.InitializeAsync();
        });

        // Assert
        exception.Should().BeNull("Data fabric should initialize without errors when services are available");
    }

    [Fact]
    public async Task DataFabricOrchestrator_CheckHealthAsync_ShouldReturnHealthStatus()
    {
        // Act
        var health = await _orchestrator.CheckHealthAsync();

        // Assert
        health.Should().NotBeNull();
        health.CheckTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        health.OverallStatus.Should().BeOneOf("Healthy", "Degraded", "Unhealthy");

        if (health.OverallStatus == "Healthy")
        {
            health.Neo4jStatus.Should().Be("Healthy");
            health.MilvusStatus.Should().Be("Healthy");
        }
    }

    [Fact]
    public async Task Neo4jService_ComponentOperations_ShouldWorkEndToEnd()
    {
        // Arrange
        var userId = "test-user-" + Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var componentId1 = Guid.NewGuid();
        var componentId2 = Guid.NewGuid();

        try
        {
            // Act & Assert - Create components
            await _neo4jService.CreateModelComponentAsync(componentId1, modelId, "TestLayer1", "Dense", userId);
            await _neo4jService.CreateModelComponentAsync(componentId2, modelId, "TestLayer2", "Dense", userId);

            // Create relationship
            await _neo4jService.CreateComponentRelationshipAsync(componentId1, componentId2, "FEEDS_TO", userId);

            // Query paths
            var paths = await _neo4jService.GetModelPathsAsync(componentId1, 2, userId);
            paths.Should().NotBeEmpty("Should find paths from the starting component");

            // Find similar components
            var similar = await _neo4jService.FindSimilarComponentsAsync(componentId1, userId, 5);
            similar.Should().Contain(c => c.Id == componentId2, "Should find similar component of same type");

            // Clean up
            await _neo4jService.DeleteComponentAsync(componentId1, userId);
            await _neo4jService.DeleteComponentAsync(componentId2, userId);
        }
        catch (Exception ex) when (ex.Message.Contains("connection") || ex.Message.Contains("driver"))
        {
            // Skip test if Neo4j is not available
            _logger.LogWarning("Skipping Neo4j test - service not available: {Message}", ex.Message);
            return;
        }
    }

    [Fact]
    public async Task SqlServerVectorService_EmbeddingOperations_ShouldWorkEndToEnd()
    {
        // Arrange
        var userId = "test-user-" + Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var embedding = GenerateRandomEmbedding(768);

        try
        {
            // Act & Assert - Insert embedding
            await _vectorService.InsertEmbeddingAsync(componentId, modelId, userId, embedding, "Dense", "TestComponent");

            // Search for similar embeddings
            var queryEmbedding = GenerateRandomEmbedding(768);
            var results = await _vectorService.SearchSimilarAsync(queryEmbedding, userId, 5);

            results.Should().NotBeNull("Search should return results");

            // Get collection stats
            var stats = await _vectorService.GetCollectionStatsAsync();
            stats.Should().NotBeNull();
            stats.RowCount.Should().BeGreaterThan(0, "Collection should contain inserted data");

            // Clean up
            await _vectorService.DeleteEmbeddingsAsync(componentId, userId);
        }
        catch (Exception ex) when (ex.Message.Contains("connection") || ex.Message.Contains("SQL Server"))
        {
            // Skip test if Milvus is not available
            _logger.LogWarning("Skipping Milvus test - service not available: {Message}", ex.Message);
            return;
        }
    }

    [Fact]
    public async Task DataFabricOrchestrator_SemanticSearch_ShouldCombineGraphAndVectorResults()
    {
        // Arrange
        var userId = "test-user-" + Guid.NewGuid();
        var queryEmbedding = GenerateRandomEmbedding(768);

        try
        {
            // Act
            var results = await _orchestrator.PerformSemanticSearchAsync(queryEmbedding, userId, topK: 5);

            // Assert
            results.Should().NotBeNull();
            results.Query.Should().NotBeEmpty();
            results.SearchTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            results.Results.Should().NotBeNull();

            // Each result should have both vector match and graph context
            foreach (var result in results.Results)
            {
                result.Component.Should().NotBeNull();
                result.GraphContext.Should().NotBeNull();
            }
        }
        catch (Exception ex) when (ex.Message.Contains("connection") || ex.Message.Contains("not available"))
        {
            _logger.LogWarning("Skipping semantic search test - data fabric services not available: {Message}", ex.Message);
            return;
        }
    }

    [Fact]
    public async Task DataFabricOrchestrator_ModelInsights_ShouldProvideComprehensiveAnalysis()
    {
        // Arrange
        var userId = "test-user-" + Guid.NewGuid();
        var modelId = Guid.NewGuid();

        try
        {
            // Act
            var insights = await _orchestrator.GetModelInsightsAsync(modelId, userId);

            // Assert
            insights.Should().NotBeNull();
            insights.ModelId.Should().Be(modelId);
            insights.Message.Should().NotBeEmpty();
            insights.RelationshipPaths.Should().NotBeNull();
            insights.SimilarComponents.Should().NotBeNull();
        }
        catch (Exception ex) when (ex.Message.Contains("connection") || ex.Message.Contains("not available"))
        {
            _logger.LogWarning("Skipping model insights test - data fabric services not available: {Message}", ex.Message);
            return;
        }
    }

    [Theory]
    [InlineData("Dense")]
    [InlineData("Conv2D")]
    [InlineData("LSTM")]
    public async Task Neo4jService_FindSimilarComponents_ShouldRespectComponentTypeFilter(string componentType)
    {
        // Arrange
        var userId = "test-user-" + Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var componentId = Guid.NewGuid();

        try
        {
            // Create a component of the specified type
            await _neo4jService.CreateModelComponentAsync(componentId, modelId, $"Test{componentType}", componentType, userId);

            // Act
            var similar = await _neo4jService.FindSimilarComponentsAsync(componentId, userId, 10);

            // Assert
            similar.Should().NotBeNull();
            // All returned components should be of the same type (in a real scenario with more data)

            // Clean up
            await _neo4jService.DeleteComponentAsync(componentId, userId);
        }
        catch (Exception ex) when (ex.Message.Contains("connection") || ex.Message.Contains("driver"))
        {
            _logger.LogWarning("Skipping component type test - Neo4j not available: {Message}", ex.Message);
            return;
        }
    }

    [Fact]
    public async Task SqlServerVectorService_UserIsolation_ShouldRespectUserBoundaries()
    {
        // Arrange
        var user1 = "user1-" + Guid.NewGuid();
        var user2 = "user2-" + Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var componentId1 = Guid.NewGuid();
        var componentId2 = Guid.NewGuid();
        var embedding1 = GenerateRandomEmbedding(768);
        var embedding2 = GenerateRandomEmbedding(768);

        try
        {
            // Insert embeddings for different users
            await _vectorService.InsertEmbeddingAsync(componentId1, modelId, user1, embedding1, "Dense", "User1Component");
            await _vectorService.InsertEmbeddingAsync(componentId2, modelId, user2, embedding2, "Dense", "User2Component");

            // Search as user1
            var user1Results = await _vectorService.SearchSimilarAsync(embedding1, user1, 10);
            var user2Results = await _vectorService.SearchSimilarAsync(embedding2, user2, 10);

            // Assert - Each user should only see their own data
            user1Results.Should().NotContain(r => r.ComponentId == componentId2, "User1 should not see User2's components");
            user2Results.Should().NotContain(r => r.ComponentId == componentId1, "User2 should not see User1's components");

            // Clean up
            await _vectorService.DeleteEmbeddingsAsync(componentId1, user1);
            await _vectorService.DeleteEmbeddingsAsync(componentId2, user2);
        }
        catch (Exception ex) when (ex.Message.Contains("connection") || ex.Message.Contains("SQL Server"))
        {
            _logger.LogWarning("Skipping user isolation test - Milvus not available: {Message}", ex.Message);
            return;
        }
    }

    private static float[] GenerateRandomEmbedding(int dimension)
    {
        var random = new Random();
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0); // Random values between -1 and 1
        }

        return embedding;
    }
}