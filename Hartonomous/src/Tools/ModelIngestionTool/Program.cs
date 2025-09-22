using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Hartonomous.ModelService.Services;
using Hartonomous.Infrastructure.SqlServer;
using Hartonomous.Infrastructure.Neo4j;

namespace ModelIngestionTool;

/// <summary>
/// Command-line tool for testing model ingestion
/// Usage: dotnet run -- "path/to/model.gguf" "model-name"
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: ModelIngestionTool <model-path> <model-name>");
            Console.WriteLine("Example: ModelIngestionTool C:\\models\\phi3-mini.gguf phi3-mini");
            return;
        }

        var modelPath = args[0];
        var modelName = args[1];
        var userId = Environment.UserName;

        Console.WriteLine($"Ingesting model: {modelName}");
        Console.WriteLine($"From: {modelPath}");
        Console.WriteLine($"User: {userId}");
        Console.WriteLine();

        try
        {
            // Setup configuration
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Hartonomous;Integrated Security=true;TrustServerCertificate=true;",
                    ["Neo4j:Uri"] = "bolt://localhost:7687",
                    ["Neo4j:Username"] = "neo4j",
                    ["Neo4j:Password"] = "password"
                })
                .Build();

            // Setup DI
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            // Add services
            services.AddTransient<GGUFParser>();
            services.AddTransient<ComponentExtractor>();
            services.AddTransient<ModelStorageService>();
            services.AddTransient<EmbeddingService>();
            services.AddTransient<GraphStorageService>();
            services.AddTransient<ModelIngestionService>();

            // Add infrastructure
            services.AddTransient<SqlServerVectorService>();
            services.AddTransient<Neo4jService>();

            using var serviceProvider = services.BuildServiceProvider();

            // Initialize vector service
            Console.WriteLine("Initializing vector service...");
            var vectorService = serviceProvider.GetRequiredService<SqlServerVectorService>();
            await vectorService.InitializeAsync();

            // Run ingestion
            Console.WriteLine("Starting ingestion...");
            var ingestionService = serviceProvider.GetRequiredService<ModelIngestionService>();

            var result = await ingestionService.IngestAsync(modelPath, modelName, userId);

            if (result.Success)
            {
                Console.WriteLine($"✅ Ingestion successful!");
                Console.WriteLine($"   Model ID: {result.ModelId}");
                Console.WriteLine($"   Components: {result.ComponentCount}");
                Console.WriteLine($"   Processing time: {result.ProcessingTimeMs}ms");
                Console.WriteLine();
                Console.WriteLine("You can now query the model using T-SQL:");
                Console.WriteLine($"   EXEC sp_QueryModelComponents @ModelId='{result.ModelId}', @UserId='{userId}'");
                Console.WriteLine($"   EXEC sp_GetModelAnalysis @ModelId='{result.ModelId}', @UserId='{userId}'");
            }
            else
            {
                Console.WriteLine($"❌ Ingestion failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}