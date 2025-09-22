using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Hartonomous.ModelService.Services;
using Hartonomous.Infrastructure.SqlServer;
using Hartonomous.Infrastructure.Neo4j;
using Xunit;
using Xunit.Abstractions;

namespace Hartonomous.ModelService.IntegrationTests;

/// <summary>
/// Integration tests that work with real SQL Server and Neo4j
/// Tests the complete model ingestion pipeline
/// </summary>
public class ModelIngestionIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _connectionString;
    private readonly string _testUserId = "test-user-123";

    public ModelIngestionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Build configuration for local development
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Hartonomous;Integrated Security=true;TrustServerCertificate=true;",
                ["Neo4j:Uri"] = "bolt://localhost:7687",
                ["Neo4j:Username"] = "neo4j",
                ["Neo4j:Password"] = "password"
            })
            .Build();

        _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // Setup DI container
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddXUnit(output));

        // Add our services
        services.AddTransient<GGUFParser>();
        services.AddTransient<ComponentExtractor>();
        services.AddTransient<ModelStorageService>();
        services.AddTransient<EmbeddingService>();
        services.AddTransient<GraphStorageService>();
        services.AddTransient<ModelIngestionService>();

        // Add infrastructure services
        services.AddTransient<SqlServerVectorService>();
        services.AddTransient<Neo4jService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Can_Connect_To_SQL_Server()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand("SELECT @@VERSION", connection);
        var version = await command.ExecuteScalarAsync();

        _output.WriteLine($"SQL Server Version: {version}");
        Assert.NotNull(version);
    }

    [Fact]
    public async Task Can_Initialize_Vector_Tables()
    {
        var vectorService = _serviceProvider.GetRequiredService<SqlServerVectorService>();

        // This should create the ComponentEmbeddings table if it doesn't exist
        await vectorService.InitializeAsync();

        // Verify table exists
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand(@"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = 'ComponentEmbeddings'", connection);

        var count = (int)await command.ExecuteScalarAsync();
        Assert.Equal(1, count);

        _output.WriteLine("ComponentEmbeddings table verified");
    }

    [Fact]
    public async Task Can_Store_And_Query_Vector_Embeddings()
    {
        var vectorService = _serviceProvider.GetRequiredService<SqlServerVectorService>();
        await vectorService.InitializeAsync();

        var testComponentId = Guid.NewGuid();
        var testModelId = Guid.NewGuid();
        var testEmbedding = Enumerable.Range(0, 1536).Select(i => (float)(i / 1536.0)).ToArray();

        // Store embedding
        await vectorService.InsertEmbeddingAsync(
            testComponentId,
            testModelId,
            testEmbedding,
            "test_component",
            "Test component description",
            _testUserId);

        // Query similar embeddings
        var queryEmbedding = testEmbedding.Select(x => x * 0.99f).ToArray(); // Slightly different
        var results = await vectorService.FindSimilarComponentsAsync(
            queryEmbedding,
            0.5,
            10,
            _testUserId);

        Assert.NotEmpty(results);
        var result = results.First();
        Assert.Equal(testComponentId, result.ComponentId);

        _output.WriteLine($"Found similar component with distance: {result.Distance}");
    }

    [Fact]
    public async Task Can_Execute_Stored_Procedures()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Test sp_QueryModelComponents
        var command = new SqlCommand("sp_QueryModelComponents", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("@ComponentType", "attention");
        command.Parameters.AddWithValue("@MaxResults", 10);
        command.Parameters.AddWithValue("@UserId", _testUserId);

        using var reader = await command.ExecuteReaderAsync();
        var columnCount = reader.FieldCount;

        _output.WriteLine($"sp_QueryModelComponents returned {columnCount} columns");
        Assert.True(columnCount > 0);
    }

    [Fact]
    public async Task Can_Parse_Sample_GGUF_File()
    {
        // Create a minimal GGUF file for testing
        var testFilePath = Path.GetTempFileName();
        await CreateSampleGGUFFileAsync(testFilePath);

        try
        {
            var parser = _serviceProvider.GetRequiredService<GGUFParser>();
            var structure = await parser.ParseAsync(testFilePath);

            Assert.Equal(3u, structure.Version);
            Assert.True(structure.TensorCount > 0);
            Assert.NotEmpty(structure.Tensors);

            _output.WriteLine($"Parsed GGUF: {structure.TensorCount} tensors, {structure.MetadataCount} metadata entries");
        }
        finally
        {
            File.Delete(testFilePath);
        }
    }

    [Fact]
    public async Task Complete_Ingestion_Pipeline_Works()
    {
        // Create test GGUF file
        var testFilePath = Path.GetTempFileName();
        await CreateSampleGGUFFileAsync(testFilePath);

        try
        {
            var ingestionService = _serviceProvider.GetRequiredService<ModelIngestionService>();

            var result = await ingestionService.IngestAsync(
                testFilePath,
                "test-model",
                _testUserId);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(result.ComponentCount > 0);
            Assert.NotEqual(Guid.Empty, result.ModelId);

            _output.WriteLine($"Ingestion successful: {result.ComponentCount} components in {result.ProcessingTimeMs}ms");

            // Verify data was stored
            await VerifyModelInDatabaseAsync(result.ModelId);
        }
        finally
        {
            File.Delete(testFilePath);
        }
    }

    private async Task CreateSampleGGUFFileAsync(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(stream);

        // Write GGUF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3); // Version
        writer.Write((ulong)2); // Tensor count
        writer.Write((ulong)2); // Metadata count

        // Write metadata
        WriteString(writer, "general.architecture");
        writer.Write((uint)11); // STRING type
        WriteString(writer, "test_arch");

        WriteString(writer, "general.parameter_count");
        writer.Write((uint)6); // INT64 type
        writer.Write((long)1000000);

        // Write tensor definitions
        WriteString(writer, "test.embed.weight");
        writer.Write((uint)2); // 2 dimensions
        writer.Write((long)1000); // vocab size
        writer.Write((long)512);  // embed dim
        writer.Write((uint)0); // F32 type
        writer.Write((ulong)0); // offset

        WriteString(writer, "test.output.weight");
        writer.Write((uint)2); // 2 dimensions
        writer.Write((long)512);  // embed dim
        writer.Write((long)1000); // vocab size
        writer.Write((uint)0); // F32 type
        writer.Write((ulong)2048000); // offset
    }

    private void WriteString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }

    private async Task VerifyModelInDatabaseAsync(Guid modelId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check foundation model
        var modelCommand = new SqlCommand(@"
            SELECT COUNT(*) FROM FoundationModels
            WHERE ModelId = @ModelId AND UserId = @UserId", connection);
        modelCommand.Parameters.AddWithValue("@ModelId", modelId);
        modelCommand.Parameters.AddWithValue("@UserId", _testUserId);

        var modelCount = (int)await modelCommand.ExecuteScalarAsync();
        Assert.Equal(1, modelCount);

        // Check components
        var componentCommand = new SqlCommand(@"
            SELECT COUNT(*) FROM ModelComponents
            WHERE ModelId = @ModelId AND UserId = @UserId", connection);
        componentCommand.Parameters.AddWithValue("@ModelId", modelId);
        componentCommand.Parameters.AddWithValue("@UserId", _testUserId);

        var componentCount = (int)await componentCommand.ExecuteScalarAsync();
        Assert.True(componentCount > 0);

        _output.WriteLine($"Verified model in database: {componentCount} components");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

public static class LoggingExtensions
{
    public static ILoggingBuilder AddXUnit(this ILoggingBuilder builder, ITestOutputHelper output)
    {
        builder.AddProvider(new XUnitLoggerProvider(output));
        return builder;
    }
}

public class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XUnitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

public class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XUnitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _output.WriteLine($"[{logLevel}] {_categoryName}: {formatter(state, exception)}");
    }
}