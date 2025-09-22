# Hartonomous Implementation Guide

## Project Consolidation and Architectural Refactoring

### Current State
The platform has fragmented model-related functionality across multiple projects that need consolidation into a unified ModelService following enterprise patterns.

### Target Architecture

#### Unified ModelService Structure
```
src/Services/Hartonomous.ModelService/
├── Abstractions/
│   ├── IModelProcessor.cs              # Generic model format processing
│   ├── ICircuitDiscoverer.cs          # Circuit discovery interface
│   ├── IAttributionAnalyzer.cs        # Attribution analysis interface
│   └── IAgentSynthesizer.cs           # Agent synthesis interface
├── Services/
│   ├── ModelProcessingService.cs       # GGUF/SafeTensors processing
│   ├── CircuitDiscoveryService.cs      # Mechanistic interpretability
│   ├── AttributionAnalysisService.cs   # Wanda algorithm
│   └── AgentSynthesisService.cs        # Agent creation
├── Repositories/
│   ├── ModelRepository.cs              # Generic model entity repository
│   ├── ComponentRepository.cs          # Component management
│   └── CircuitRepository.cs            # Circuit storage
├── Models/
│   ├── ModelFormat.cs                  # Model format abstractions
│   ├── Circuit.cs                      # Circuit domain models
│   └── Agent.cs                        # Agent domain models
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # DI configuration
└── ModelServiceOptions.cs              # Configuration
```

#### DataFabric Abstractions
```
src/Abstractions/Hartonomous.DataFabric.Abstractions/
├── IVectorService.cs                   # Vector operations interface
├── IGraphService.cs                    # Graph database interface
├── IModelDataService.cs                # Model data access interface
└── ServiceContracts/
    ├── ModelProcessingContracts.cs     # Processing DTOs
    ├── CircuitDiscoveryContracts.cs    # Discovery DTOs
    └── AgentSynthesisContracts.cs      # Synthesis DTOs
```

### Implementation Steps

#### Phase 1: Create Infrastructure Abstractions
1. **Create DataFabric.Abstractions project**
   - Extract all data interfaces from Core
   - Define unified service contracts
   - Establish consistent patterns

2. **Rename Infrastructure.Milvus to Infrastructure.SqlServer**
   - Update project file and namespace
   - Reflect actual SQL Server 2025 implementation
   - Update solution references

#### Phase 2: Consolidate ModelService
1. **Move ModelDistillationEngine** from fragmented project
2. **Move ModelQuery services** and repositories
3. **Move Core model services** (AgentDistillationService, etc.)
4. **Implement generic repository patterns**
5. **Create unified service interfaces**

#### Phase 3: DTO Consolidation
1. **Move all DTOs to Core/DTOs**
2. **Eliminate duplicates**
3. **Implement generic DTO patterns**
4. **Update all references**

#### Phase 4: Schema Unification
1. **Generate EF Core migration** for unified schema
2. **Replace fragmented SQL files**
3. **Validate database compatibility**

### Code Standards and Patterns

#### Generic Repository Pattern
```csharp
public abstract class ModelEntityRepository<T> : Repository<T>, IModelEntityRepository<T>
    where T : class, IModelEntity
{
    protected ModelEntityRepository(HartonomousDbContext context) : base(context) { }

    public virtual async Task<IEnumerable<T>> GetByUserAsync(string userId)
    {
        return await _dbSet.Where(e => e.UserId == userId).ToListAsync();
    }

    public virtual async Task<T> GetByIdAsync(Guid id, string userId)
    {
        return await _dbSet.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
    }
}
```

#### Generic Model Processor Interface
```csharp
public interface IModelProcessor<TFormat> where TFormat : IModelFormat
{
    Task<ModelProcessingResult> ProcessAsync(TFormat model, ProcessingOptions options);
    Task<bool> CanProcessAsync(byte[] modelData);
    Task<ModelMetadata> ExtractMetadataAsync(TFormat model);
}
```

#### Pipeline Composition Pattern
```csharp
public interface IModelPipeline
{
    IModelPipeline AddStage<TStage>() where TStage : IPipelineStage;
    Task<PipelineResult> ExecuteAsync(PipelineInput input);
}

public class ModelProcessingPipeline : IModelPipeline
{
    private readonly List<IPipelineStage> _stages = new();

    public IModelPipeline AddStage<TStage>() where TStage : IPipelineStage
    {
        _stages.Add(_serviceProvider.GetRequiredService<TStage>());
        return this;
    }

    public async Task<PipelineResult> ExecuteAsync(PipelineInput input)
    {
        var context = new PipelineContext(input);
        foreach (var stage in _stages)
        {
            context = await stage.ExecuteAsync(context);
        }
        return context.Result;
    }
}
```

#### Dependency Injection Configuration
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModelService(this IServiceCollection services, IConfiguration configuration)
    {
        // Register generic patterns
        services.AddScoped(typeof(IModelEntityRepository<>), typeof(ModelEntityRepository<>));
        services.AddScoped(typeof(IModelProcessor<>), typeof(ModelProcessor<>));

        // Register specific implementations
        services.AddScoped<ICircuitDiscoverer, CircuitDiscoveryService>();
        services.AddScoped<IAttributionAnalyzer, AttributionAnalysisService>();
        services.AddScoped<IAgentSynthesizer, AgentSynthesisService>();

        // Register pipeline
        services.AddScoped<IModelPipeline, ModelProcessingPipeline>();

        // Configure options
        services.Configure<ModelServiceOptions>(configuration.GetSection("ModelService"));

        return services;
    }
}
```

### Database Schema Patterns

#### User-Scoped Entities
```csharp
public abstract class UserScopedEntity : IModelEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ModelComponent : UserScopedEntity
{
    public Guid ModelId { get; set; }
    public string ComponentName { get; set; } = null!;
    public string ComponentType { get; set; } = null!;
    public double RelevanceScore { get; set; }
    public string EmbeddingVector { get; set; } = null!; // VECTOR type
}
```

#### Entity Framework Configuration
```csharp
public class ModelComponentConfiguration : IEntityTypeConfiguration<ModelComponent>
{
    public void Configure(EntityTypeBuilder<ModelComponent> builder)
    {
        builder.ToTable("ModelComponents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EmbeddingVector)
               .HasColumnType("VECTOR(1536)")
               .IsRequired();

        builder.HasIndex(e => new { e.ModelId, e.UserId });
        builder.HasIndex(e => e.ComponentType);

        // Vector index will be created via SQL
        builder.HasIndex(e => e.EmbeddingVector)
               .HasDatabaseName("IX_ModelComponents_Vector");
    }
}
```

### SQL Server 2025 Integration

#### Vector Operations Service
```csharp
public class SqlServerVectorService : IVectorService
{
    public async Task<IEnumerable<ComponentSimilarityResult>> FindSimilarComponentsAsync(
        string queryVector,
        double threshold,
        string userId)
    {
        const string sql = @"
            SELECT ComponentId, ComponentName,
                   VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector) AS Distance
            FROM ModelComponents
            WHERE UserId = @UserId
              AND VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector) < @Threshold
            ORDER BY Distance";

        return await _connection.QueryAsync<ComponentSimilarityResult>(sql,
            new { QueryVector = queryVector, UserId = userId, Threshold = threshold });
    }
}
```

#### SQL CLR Integration
```csharp
[Microsoft.SqlServer.Server.SqlFunction(DataAccess = DataAccessKind.Read)]
public static SqlString clr_ParseModelArchitecture(SqlBytes modelData, SqlString format)
{
    try
    {
        var processor = ModelProcessorFactory.Create(format.Value);
        var metadata = processor.ExtractMetadata(modelData.Value);
        return new SqlString(JsonSerializer.Serialize(metadata));
    }
    catch (Exception ex)
    {
        return new SqlString($"ERROR: {ex.Message}");
    }
}
```

### Testing Strategy

#### Unit Testing Patterns
```csharp
public class ModelProcessingServiceTests
{
    private readonly Mock<IModelRepository> _mockRepository;
    private readonly Mock<IVectorService> _mockVectorService;
    private readonly ModelProcessingService _service;

    [Test]
    public async Task ProcessModelAsync_ValidGGUF_ReturnsSuccess()
    {
        // Arrange
        var modelData = CreateValidGGUFData();
        var options = new ProcessingOptions { UserId = "test-user" };

        // Act
        var result = await _service.ProcessModelAsync(modelData, options);

        // Assert
        result.Success.Should().BeTrue();
        result.ComponentsExtracted.Should().BeGreaterThan(0);
    }
}
```

#### Integration Testing
```csharp
[TestClass]
public class ModelServiceIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task EndToEndModelProcessing_CompleteWorkflow_Success()
    {
        // Test complete pipeline: Ingestion -> Analysis -> Synthesis
        var model = await UploadTestModelAsync();
        var circuits = await DiscoverCircuitsAsync(model.Id);
        var agent = await SynthesizeAgentAsync(circuits.First().Id);

        agent.Should().NotBeNull();
        agent.Status.Should().Be(AgentStatus.Ready);
    }
}
```

### Performance Optimization

#### Caching Strategy
```csharp
public class CachedModelService : IModelService
{
    private readonly IModelService _inner;
    private readonly IMemoryCache _cache;

    public async Task<ModelMetadata> GetModelMetadataAsync(Guid modelId, string userId)
    {
        var cacheKey = $"model_metadata_{modelId}_{userId}";

        if (_cache.TryGetValue(cacheKey, out ModelMetadata cached))
            return cached;

        var metadata = await _inner.GetModelMetadataAsync(modelId, userId);
        _cache.Set(cacheKey, metadata, TimeSpan.FromMinutes(30));

        return metadata;
    }
}
```

#### Database Optimization
```sql
-- Optimized vector similarity query
WITH SimilarComponents AS (
    SELECT ComponentId, ComponentName,
           VECTOR_DISTANCE('cosine', EmbeddingVector, @QueryVector) AS Distance
    FROM ModelComponents WITH (INDEX(IX_ModelComponents_Vector))
    WHERE UserId = @UserId
)
SELECT TOP (@MaxResults) *
FROM SimilarComponents
WHERE Distance < @Threshold
ORDER BY Distance;
```

### Deployment Configuration

#### Production appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:hartonomous.database.windows.net;Database=Hartonomous;Authentication=Active Directory Default;"
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "[KeyVault:Azure-TenantId]",
    "ClientId": "[KeyVault:Azure-ClientId]"
  },
  "ModelService": {
    "MaxConcurrentProcessing": 5,
    "ModelStoragePath": "/app/models",
    "VectorDimensions": 1536
  },
  "Neo4j": {
    "Uri": "[KeyVault:Neo4j-Uri]",
    "Username": "[KeyVault:Neo4j-Username]",
    "Password": "[KeyVault:Neo4j-Password]"
  }
}
```

This implementation guide provides the concrete patterns and practices needed for the consolidation without requiring additional decision points.